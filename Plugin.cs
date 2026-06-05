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

    private uint _lastMissionId = 0;
    private uint _lastJobId = 0;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Initialize the service locator pattern
        pluginInterface.Create<Services>();

        Services.Framework.Update += OnFrameworkUpdate;
        Services.Log.Information("CosmicBaiter loaded using service locator structure.");
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnFrameworkUpdate;
        Services.Log.Information("CosmicBaiter disposed.");
    }

    private long _lastBaitChangeTime = 0;

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        // Require local player to be valid
        var localPlayer = Services.ObjectTable.LocalPlayer;
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

        // If the state changes (we change jobs, or mission starts/ends)
        if (currentMissionId != _lastMissionId || currentJobId != _lastJobId)
        {
            // Only update our memory if we successfully handled the state change
            if (HandleStateChange(currentMissionId, currentJobId))
            {
                _lastMissionId = currentMissionId;
                _lastJobId = currentJobId;
            }
        }
    }

    private unsafe bool HandleStateChange(uint missionId, uint jobId)
    {
        if (missionId == 0 || jobId != FisherJobId)
            return true; // Nothing to do, state change handled

        var currentBait = UIState.Instance()->PlayerState.FishingBait;
        if (currentBait == RefinedCosmicMayflyId)
            return true; // Already equipped, handled

        // Don't have the item? We can't handle it, but we should mark it as handled so we don't spam.
        if (InventoryManager.Instance()->GetInventoryItemCount(RefinedCosmicMayflyId) <= 0)
        {
             Services.Log.Warning("You don't have any Refined Cosmic Mayfly! Cannot auto-equip bait.");
             return true; 
        }

        // We need to equip it, check if we are allowed to right now.
        var status = ActionManager.Instance()->GetActionStatus(ActionType.Item, RefinedCosmicMayflyId);
        if (status != 0)
        {
            // We can't use it yet (e.g. animation lock, teleporting, casting).
            // Return false so we try again next frame!
            return false;
        }

        // We can use it! But don't spam if we just tried.
        if (Environment.TickCount64 < _lastBaitChangeTime + 2000)
            return false; // Wait for the server to process previous request

        Services.Log.Information($"Mission active and currently Fisher. Bait is {currentBait}, changing to Refined Cosmic Mayfly ({RefinedCosmicMayflyId}).");
        GameMain.ExecuteCommand(701, 4, (int)RefinedCosmicMayflyId, 0, 0);
        _lastBaitChangeTime = Environment.TickCount64;

        return false; // Return false so we verify it actually changed next frame!
    }

    private void ResetState()
    {
        // Reset tracking so that if WKSManager unloads and reloads, we re-evaluate
        _lastMissionId = 0;
        _lastJobId = 0;
    }
}
