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

    private bool _hasLoggedIntent = false;

    private unsafe bool HandleStateChange(uint missionId, uint jobId)
    {
        if (missionId == 0 || jobId != FisherJobId)
        {
            _hasLoggedIntent = false;
            return true; // Nothing to do, state change handled
        }

        var currentBait = UIState.Instance()->PlayerState.FishingBait;
        if (currentBait == RefinedCosmicMayflyId)
        {
            if (_hasLoggedIntent)
            {
                Services.Log.Information("Successfully changed bait to Refined Cosmic Mayfly.");
                _hasLoggedIntent = false;
            }
            return true; // Already equipped, handled
        }

        // Don't have the item? We can't handle it, but we should mark it as handled so we don't spam.
        if (InventoryManager.Instance()->GetInventoryItemCount(RefinedCosmicMayflyId) <= 0)
        {
             if (!_hasLoggedIntent)
             {
                 Services.Log.Warning("You don't have any Refined Cosmic Mayfly! Cannot auto-equip bait.");
                 _hasLoggedIntent = true;
             }
             return true; 
        }

        if (!_hasLoggedIntent)
        {
            Services.Log.Information($"Mission active and currently Fisher. Bait is {currentBait}, attempting to change to Refined Cosmic Mayfly ({RefinedCosmicMayflyId})...");
            _hasLoggedIntent = true;
        }

        // If the player's line is already in the water, they are actively fishing and CANNOT change bait.
        // We just wait patiently until they reel in.
        var isFishing = Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Fishing];
        if (isFishing)
            return false;

        // We aggressively spam the equip command every 500ms. 
        // If the player is in an animation lock (e.g., Chumming), the client will drop it.
        // By spamming it rapidly, we ensure it slips in the EXACT MILLISECOND the animation lock ends!
        if (Environment.TickCount64 < _lastBaitChangeTime + 500)
            return false;

        GameMain.ExecuteCommand(701, 4, (int)RefinedCosmicMayflyId, 0, 0);
        ActionManager.Instance()->UseAction(ActionType.Item, RefinedCosmicMayflyId); // Also attempt ActionManager just in case it queues better
        
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
