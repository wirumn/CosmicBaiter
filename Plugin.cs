using System;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
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
    private const long MissionGoneMs = 3500;      // mission id must read 0 this long before we trust it's really over (flicker guard)
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

    // Crafter tracking.
    private bool _isActuallyCrafting = false;
    private long _craftingStartedTick = 0;
    private bool _recipeWasOpen = false;
    private long _recipeSettledTick = 0;
    private bool _craftClicked = false;

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

        if (this.Configuration.AutoLoopCrafterMissions)
            HandleCrafterAutoLoop(wksManager, currentJobId);

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
        _recipeWasOpen = false;
        _recipeSettledTick = 0;
        _craftClicked = false;
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
        _recipeWasOpen = false;
        _recipeSettledTick = 0;
        _craftClicked = false;

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

    private bool IsCrafterJob(uint jobId)
    {
        return jobId is >= 8 and <= 15;
    }

    private uint GetLearnedCrafterMissionId(uint jobId)
    {
        return jobId switch
        {
            8 => this.Configuration.CarpenterMissionId,
            9 => this.Configuration.BlacksmithMissionId,
            10 => this.Configuration.ArmorerMissionId,
            11 => this.Configuration.GoldsmithMissionId,
            12 => this.Configuration.LeatherworkerMissionId,
            13 => this.Configuration.WeaverMissionId,
            14 => this.Configuration.AlchemistMissionId,
            15 => this.Configuration.CulinarianMissionId,
            _ => 0
        };
    }

    private void SetLearnedCrafterMissionId(uint jobId, uint missionId)
    {
        switch (jobId)
        {
            case 8: this.Configuration.CarpenterMissionId = missionId; break;
            case 9: this.Configuration.BlacksmithMissionId = missionId; break;
            case 10: this.Configuration.ArmorerMissionId = missionId; break;
            case 11: this.Configuration.GoldsmithMissionId = missionId; break;
            case 12: this.Configuration.LeatherworkerMissionId = missionId; break;
            case 13: this.Configuration.WeaverMissionId = missionId; break;
            case 14: this.Configuration.AlchemistMissionId = missionId; break;
            case 15: this.Configuration.CulinarianMissionId = missionId; break;
        }
        this.Configuration.Save();
    }

    private unsafe long GetCrafterTimeRemaining()
    {
        var c = UIState.Instance()->MassivePcContentTodo.Director;
        if (c != null)
        {
            var todo = c->MassivePcContentTodos[1];
            if (todo[1].Enabled)
            {
                var t = todo[1];
                long rem = t.EndTimestamp - Framework.GetServerTime();
                return rem > 0 ? rem : 0;
            }
        }
        return 0;
    }

    private unsafe void ClickSynthesizeButton()
    {
        var addonInfo = Services.GameGui.GetAddonByName("WKSRecipeNotebook", 1);
        if (addonInfo.Address != nint.Zero)
        {
            var addon = (AtkUnitBase*)addonInfo.Address;
            if (addon->IsVisible)
            {
                var button = addon->GetComponentButtonById(50u);
                if (button != null && button->IsEnabled)
                {
                    var ownerNode = button->AtkComponentBase.OwnerNode;
                    if (ownerNode != null)
                    {
                        var atkEvent = ownerNode->AtkResNode.AtkEventManager.Event;
                        if (atkEvent != null)
                        {
                            Services.Log.Information("[AutoLoop] Clicking Synthesize on WKSRecipeNotebook...");
                            addon->ReceiveEvent(atkEvent->State.EventType, (int)atkEvent->Param, atkEvent, null);
                        }
                    }
                }
            }
        }
    }

    private unsafe bool IsRecipeNoteOpen()
    {
        var addonInfo = Services.GameGui.GetAddonByName("WKSRecipeNotebook", 1);
        if (addonInfo.Address == nint.Zero) return false;
        return ((AtkUnitBase*)addonInfo.Address)->IsVisible;
    }

    private unsafe void HandleCrafterAutoLoop(WKSManager* mgr, uint jobId)
    {
        if (!IsCrafterJob(jobId))
            return;
        if (!mgr->IsLoaded)
            return;

        var module = mgr->MissionModule;
        if (module == null)
            return;

        long now = Environment.TickCount64;
        ref var mission = ref mgr->State.CurrentMission;
        ushort missionId = mission.MissionUnitRowId;

        uint learnedId = GetLearnedCrafterMissionId(jobId);

        if (missionId != 0)
        {
            _missionZeroSinceTick = 0;
            _initiateAttempts = 0;

            if (learnedId == 0)
            {
                SetLearnedCrafterMissionId(jobId, missionId);
                Services.Log.Information($"[AutoLoop] Learned crafter mission {missionId} for job {jobId}.");
            }

            if (_reportRequested)
                return;

            bool isCrafting = Services.Condition[ConditionFlag.Crafting];
            if (isCrafting && !_isActuallyCrafting)
            {
                _isActuallyCrafting = true;
                _craftingStartedTick = now;
            }
            else if (!isCrafting && _isActuallyCrafting)
            {
                _isActuallyCrafting = false;
                if (now - _craftingStartedTick > 3000)
                {
                    Services.Log.Information("[AutoLoop] Crafting state ended cleanly. Re-opening WKSRecipeNotebook...");
                    AgentRecipeNote.Instance()->Show();
                }
                else
                {
                    Services.Log.Information("[AutoLoop] Crafting state flicker ignored.");
                }
            }

            bool recipeOpen = IsRecipeNoteOpen();
            
            if (recipeOpen && !_recipeWasOpen)
            {
                _recipeWasOpen = true;
                _recipeSettledTick = now;
                _craftClicked = false;
                Services.Log.Information("[AutoLoop] WKSRecipeNotebook opened.");
            }
            else if (!recipeOpen && _recipeWasOpen)
            {
                _recipeWasOpen = false;
                _recipeSettledTick = 0;
                _craftClicked = false;
                Services.Log.Information("[AutoLoop] WKSRecipeNotebook closed.");
            }

            long waited = now - _recipeSettledTick;
            long threshold = _craftClicked ? 1500 : 1000;

            if (_recipeWasOpen && _recipeSettledTick != 0 && waited >= threshold)
            {
                // Removed ConditionFlag.Crafting check because the game puts you in a crafting stance
                // instantly when the mission initiates, preventing the first craft.
                // If the recipe notebook is open, we are guaranteed not to be mid-synthesis.
                {
                    long timeRem = GetCrafterTimeRemaining();
                    
                    if (timeRem <= 0)
                    {
                        // Time read failed or director not ready. Wait before taking action.
                        return;
                    }
                    
                    if (timeRem > this.Configuration.MinCrafterTimeSeconds)
                    {
                        Services.Log.Information($"[AutoLoop] Attempting Synthesize... (Time left: {timeRem}s)");
                        ClickSynthesizeButton();
                        _recipeSettledTick = now; // wait retry interval
                        _craftClicked = true;
                    }
                    else
                    {
                        Services.Log.Information($"[AutoLoop] Not enough time left ({timeRem}s). Reporting crafter mission {missionId}.");
                        module->ReportMission();
                        _reportRequested = true;
                        _recipeSettledTick = 0; // stop trying
                        _craftClicked = false;
                    }
                }
            }
        }
        else
        {
            _reportRequested = false;
            _recipeWasOpen = false;
            _recipeSettledTick = 0;
            _craftClicked = false;

            if (learnedId == 0) return;

            if (_missionZeroSinceTick == 0)
                _missionZeroSinceTick = now;
            if (now - _missionZeroSinceTick < MissionGoneMs)
                return;

            if (now - _lastInitiateTick < InitiateCooldownMs)
                return;

            if (_initiateAttempts >= MaxInitiateAttempts)
            {
                Services.Log.Warning("[AutoLoop] InitiateMission failed repeatedly; disabling crafter auto loop.");
                Services.Chat.Print("[CosmicBaiter] Could not start crafter mission; auto loop disabled.");
                this.Configuration.AutoLoopCrafterMissions = false;
                this.Configuration.Save();
                return;
            }

            Services.Log.Information($"[AutoLoop] Initiating crafter mission {learnedId} for job {jobId} (attempt {_initiateAttempts + 1}).");
            module->InitiateMission((ushort)learnedId);
            _lastInitiateTick = now;
            _initiateAttempts++;
        }
    }
}










