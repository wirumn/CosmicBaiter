using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;

namespace CosmicBaiter;

public sealed class Plugin : IDalamudPlugin
{
    private const uint RefinedCosmicMayflyId = 52250;
    private const uint FisherJobId = 18;

    private readonly IPluginLog _pluginLog;
    private readonly IFramework _framework;
    private readonly IObjectTable _objectTable;

    private uint _lastMissionId = 0;
    private uint _lastJobId = 0;

    public Plugin(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog, IFramework framework, IObjectTable objectTable)
    {
        _pluginLog = pluginLog;
        _framework = framework;
        _objectTable = objectTable;

        _framework.Update += OnFrameworkUpdate;
        _pluginLog.Information("CosmicBaiter loaded.");
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _pluginLog.Information("CosmicBaiter disposed.");
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        // Require local player to be valid
        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        uint currentJobId = localPlayer.ClassJob.RowId;
        
        // Safety check to ensure WKSManager is present
        var wksManager = WKSManager.Instance();
        if (wksManager == null)
        {
            ResetState();
            return;
        }

        uint currentMissionId = wksManager->State.CurrentMission.MissionUnitRowId;

        // If nothing has changed, do nothing
        if (currentMissionId == _lastMissionId && currentJobId == _lastJobId)
            return;

        // State has changed, record the new state
        _lastMissionId = currentMissionId;
        _lastJobId = currentJobId;

        // Only act if we have an active mission and we are a Fisher
        if (currentMissionId != 0 && currentJobId == FisherJobId)
        {
            EquipBaitIfNecessary();
        }
    }

    private unsafe void EquipBaitIfNecessary()
    {
        var currentBait = UIState.Instance()->PlayerState.FishingBait;
        if (currentBait != RefinedCosmicMayflyId)
        {
            _pluginLog.Information($"Mission active and currently Fisher. Bait is {currentBait}, changing to Refined Cosmic Mayfly ({RefinedCosmicMayflyId}).");
            
            // Item execution command: 701, ActionType 4 (Item), ItemID
            GameMain.ExecuteCommand(701, 4, (int)RefinedCosmicMayflyId, 0, 0);
        }
    }

    private void ResetState()
    {
        // Reset tracking so that if WKSManager unloads and reloads, we re-evaluate
        _lastMissionId = 0;
        _lastJobId = 0;
    }
}
