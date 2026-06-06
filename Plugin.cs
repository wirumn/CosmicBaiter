using System;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CosmicBaiter;

public sealed class Plugin : IDalamudPlugin
{
    private const uint RefinedCosmicMayflyId = 52250;
    private const uint FisherJobId = 18;
    private const uint MinerJobId = 16;
    private const uint BotanistJobId = 17;

    // Auto-loop tuning (milliseconds).
    private const long MinNodeOpenMs = 1500;      // ignore Gathering-window flickers shorter than this
    private const long ReportDelayMs = 1500;      // wait this long after the last node's window closes before reporting
    private const long MissionGoneMs = 1000;      // mission id must read 0 this long before we trust it's really over (flicker guard)
    private const long InitiateCooldownMs = 3000; // minimum gap between InitiateMission attempts
    private const int  MaxInitiateAttempts = 3;   // give up + disable after this many failed starts

    private uint _lastMissionId = 0;
    private uint _lastJobId = 0;

    // Auto-loop state trackers.
    private long _missionZeroSinceTick = 0;
    private long _lastInitiateTick = 0;
    private bool _reportRequested = false;
    private int _initiateAttempts = 0;

    // Node-completion tracking (counts Gathering-window open -> close cycles).
    private bool _gatheringWasOpen = false;
    private long _gatheringOpenedTick = 0;
    private long _gatheringClosedTick = 0;
    private int _nodesGathered = 0;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem = new("CosmicBaiter");
    public ConfigWindow ConfigWindow { get; init; }

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Initialize the service locator pattern
        pluginInterface.Create<Services>();

        this.Configuration = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(Services.PluginInterface);

        this.ConfigWindow = new ConfigWindow(this.Configuration);
        this.WindowSystem.AddWindow(this.ConfigWindow);

        Services.CommandManager.AddHandler("/cosmicbaiter", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "Opens config. Subcommands: auto (toggle auto loop), stop, reset (clear learned mission IDs)."
        });

        Services.PluginInterface.UiBuilder.Draw += DrawUI;
        Services.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

        Services.Framework.Update += OnFrameworkUpdate;
        Services.Log.Information("CosmicBaiter loaded using service locator structure.");
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnFrameworkUpdate;
        
        Services.PluginInterface.UiBuilder.Draw -= DrawUI;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;

        Services.CommandManager.RemoveHandler("/cosmicbaiter");

        this.WindowSystem.RemoveAllWindows();
        this.ConfigWindow.Dispose();

        Services.Log.Information("CosmicBaiter disposed.");
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "auto":
                this.Configuration.AutoLoopMissions = !this.Configuration.AutoLoopMissions;
                this.Configuration.Save();
                ResetLoopTrackers();
                var state = this.Configuration.AutoLoopMissions ? "ENABLED" : "DISABLED";
                Services.Chat.Print($"[CosmicBaiter] Auto mission loop {state}.");
                Services.Log.Information($"[AutoLoop] {state}.");
                break;

            case "stop":
                this.Configuration.AutoLoopMissions = false;
                this.Configuration.Save();
                ResetLoopTrackers();
                Services.Chat.Print("[CosmicBaiter] Auto mission loop DISABLED.");
                break;

            case "reset":
                this.Configuration.MinerMissionId = 0;
                this.Configuration.BotanistMissionId = 0;
                this.Configuration.Save();
                Services.Chat.Print("[CosmicBaiter] Learned mission IDs cleared. Start each mission once to re-learn.");
                break;

            default:
                this.ConfigWindow.IsOpen = true;
                break;
        }
    }

    private void DrawUI()
    {
        this.WindowSystem.Draw();
    }

    private void DrawConfigUI()
    {
        this.ConfigWindow.IsOpen = true;
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        var localPlayer = Services.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        uint currentJobId = localPlayer.ClassJob.RowId;
        var wksManager = WKSManager.Instance();
        if (wksManager == null)
        {
            _lastMissionId = 0;
            _lastJobId = 0;
            ResetLoopTrackers();
            return;
        }

        uint currentMissionId = wksManager->State.CurrentMission.MissionUnitRowId;

        if (this.Configuration.AutoLoopMissions)
            HandleAutoLoop(wksManager, currentJobId);

        if (currentMissionId == _lastMissionId && currentJobId == _lastJobId)
            return;

        _lastMissionId = currentMissionId;
        _lastJobId = currentJobId;

        if (currentMissionId != 0)
        {
            if (currentJobId == FisherJobId)
            {
                EquipBaitIfNecessary();
            }

            if (currentJobId == FisherJobId || currentJobId == 16 /* Miner */ || currentJobId == 17 /* Botanist */)
            {
                if (this.Configuration.AutoOpenMissionProgress)
                {
                    OpenMissionProgressWindow();
                }
            }
        }
    }

    private unsafe void OpenMissionProgressWindow()
    {
        var addonInfo = Services.GameGui.GetAddonByName("WKSHud", 1);
        if (addonInfo.Address != nint.Zero)
        {
            var addon = (AtkUnitBase*)addonInfo.Address;
            if (addon->IsVisible)
            {
                var button = addon->GetComponentButtonById(7u);
                if (button != null && button->IsEnabled)
                {
                    var ownerNode = button->AtkComponentBase.OwnerNode;
                    if (ownerNode != null)
                    {
                        var atkEvent = ownerNode->AtkResNode.AtkEventManager.Event;
                        if (atkEvent != null)
                        {
                            Services.Log.Debug("Auto-opening Mission in Progress window via ReceiveEvent...");
                            addon->ReceiveEvent(atkEvent->State.EventType, (int)atkEvent->Param, atkEvent, null);
                        }
                    }
                }
            }
        }
    }

    private unsafe void EquipBaitIfNecessary()
    {
        if (!this.Configuration.AutoEquipBait) return;

        var currentBait = UIState.Instance()->PlayerState.FishingBait;
        if (currentBait != RefinedCosmicMayflyId)
        {
            Services.Log.Information($"Mission active and currently Fisher. Bait is {currentBait}, changing to Refined Cosmic Mayfly ({RefinedCosmicMayflyId}).");
            GameMain.ExecuteCommand(701, 4, (int)RefinedCosmicMayflyId, 0, 0);
        }
    }

    private void ResetLoopTrackers()
    {
        _missionZeroSinceTick = 0;
        _lastInitiateTick = 0;
        _reportRequested = false;
        _initiateAttempts = 0;
        _gatheringWasOpen = false;
        _gatheringOpenedTick = 0;
        _gatheringClosedTick = 0;
        _nodesGathered = 0;
    }

    private unsafe bool IsGatheringWindowOpen()
    {
        var info = Services.GameGui.GetAddonByName("Gathering", 1);
        if (info.Address == nint.Zero)
            return false;
        return ((AtkUnitBase*)info.Address)->IsVisible;
    }

    // Drives the start -> watch -> report -> repeat loop for Miner & Botanist.
    // Gathering itself is done externally; this only manages the mission lifecycle.
    private unsafe void HandleAutoLoop(WKSManager* mgr, uint jobId)
    {
        if (jobId != MinerJobId && jobId != BotanistJobId)
            return;
        if (!mgr->IsLoaded)
            return;

        var module = mgr->MissionModule;
        if (module == null)
            return;

        long now = Environment.TickCount64;
        ref var mission = ref mgr->State.CurrentMission;
        ushort missionId = mission.MissionUnitRowId;

        uint learnedId = jobId == MinerJobId
            ? this.Configuration.MinerMissionId
            : this.Configuration.BotanistMissionId;

        if (missionId != 0)
        {
            // A mission is active: reset the "gone" / retry counters.
            _missionZeroSinceTick = 0;
            _initiateAttempts = 0;

            // Learn the per-job mission id the first time we see one running.
            if (learnedId == 0)
            {
                if (jobId == MinerJobId) this.Configuration.MinerMissionId = missionId;
                else this.Configuration.BotanistMissionId = missionId;
                this.Configuration.Save();
                Services.Log.Information($"[AutoLoop] Learned mission {missionId} for job {jobId}.");
            }

            // Wait for the game to clear the mission after we asked to report.
            if (_reportRequested)
                return;

            // Count node completions by watching the Gathering window open -> close.
            bool open = IsGatheringWindowOpen();
            if (open && !_gatheringWasOpen)
            {
                _gatheringWasOpen = true;
                _gatheringOpenedTick = now;
            }
            else if (!open && _gatheringWasOpen)
            {
                _gatheringWasOpen = false;
                // Ignore brief flickers; only count a real gathering session.
                if (now - _gatheringOpenedTick >= MinNodeOpenMs)
                {
                    _nodesGathered++;
                    _gatheringClosedTick = now;
                    Services.Log.Information($"[AutoLoop] Node {_nodesGathered}/{this.Configuration.NodesToGather} gathered.");
                }
            }

            bool allNodesDone = _nodesGathered >= this.Configuration.NodesToGather;
            bool settled = _gatheringClosedTick != 0 && now - _gatheringClosedTick >= ReportDelayMs;

            if (allNodesDone && !open && settled)
            {
                Services.Log.Information($"[AutoLoop] Reporting mission {missionId} ({_nodesGathered} nodes gathered).");
                module->ReportMission();
                _reportRequested = true;
            }
        }
        else
        {
            // No active mission: clear per-mission tracking for the next one.
            _reportRequested = false;
            _gatheringWasOpen = false;
            _gatheringOpenedTick = 0;
            _gatheringClosedTick = 0;
            _nodesGathered = 0;

            if (learnedId == 0)
                return; // nothing learned yet for this job: start one manually first.

            // Flicker guard: the id briefly drops to 0 mid-mission, so only act once
            // it has stayed 0 for a sustained window.
            if (_missionZeroSinceTick == 0)
                _missionZeroSinceTick = now;
            if (now - _missionZeroSinceTick < MissionGoneMs)
                return;

            if (now - _lastInitiateTick < InitiateCooldownMs)
                return;

            if (_initiateAttempts >= MaxInitiateAttempts)
            {
                Services.Log.Warning("[AutoLoop] InitiateMission failed repeatedly; disabling auto loop.");
                Services.Chat.Print("[CosmicBaiter] Could not start a mission; auto loop disabled.");
                this.Configuration.AutoLoopMissions = false;
                this.Configuration.Save();
                return;
            }

            Services.Log.Information($"[AutoLoop] Initiating mission {learnedId} for job {jobId} (attempt {_initiateAttempts + 1}).");
            module->InitiateMission((ushort)learnedId);
            _lastInitiateTick = now;
            _initiateAttempts++;
        }
    }
}
