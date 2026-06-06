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
    private const long ScoreSettleMs = 2000;      // score must be unchanged this long before reporting (= done gathering)
    private const long MissionGoneMs = 1000;      // mission id must read 0 this long before we trust it's really over (flicker guard)
    private const long InitiateCooldownMs = 3000; // minimum gap between InitiateMission attempts
    private const int  MaxInitiateAttempts = 3;   // give up + disable after this many failed starts

    private uint _lastMissionId = 0;
    private uint _lastJobId = 0;

    // Auto-loop state trackers.
    private long _missionZeroSinceTick = 0;
    private long _lastScoreChangeTick = 0;
    private long _lastInitiateTick = 0;
    private ushort _lastScore = 0;
    private bool _reportRequested = false;
    private int _initiateAttempts = 0;

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
        _lastScoreChangeTick = 0;
        _lastInitiateTick = 0;
        _lastScore = 0;
        _reportRequested = false;
        _initiateAttempts = 0;
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
        ushort score = mission.Score;
        var rank = mission.Rank;

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

            if (rank == WKSMissionModule.MissionRank.Failed)
            {
                Services.Log.Warning("[AutoLoop] Mission failed; abandoning.");
                module->AbandonMission();
                ResetLoopTrackers();
                return;
            }

            // Wait for the game to clear the mission after we asked to report.
            if (_reportRequested)
                return;

            // Track score changes so we can tell when gathering has stopped.
            if (score != _lastScore)
            {
                _lastScore = score;
                _lastScoreChangeTick = now;
            }

            bool rankReached = rank != WKSMissionModule.MissionRank.Failed
                               && (int)rank >= this.Configuration.MinReportRank;
            bool scoreSettled = _lastScoreChangeTick != 0
                                && now - _lastScoreChangeTick >= ScoreSettleMs;

            if (rankReached && scoreSettled)
            {
                Services.Log.Information($"[AutoLoop] Reporting mission {missionId} (rank {rank}, score {score}).");
                module->ReportMission();
                _reportRequested = true;
            }
        }
        else
        {
            // No active mission.
            _reportRequested = false;
            _lastScore = 0;
            _lastScoreChangeTick = 0;

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
