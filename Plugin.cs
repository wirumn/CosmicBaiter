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
        var localPlayer = Services.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        uint currentJobId = localPlayer.ClassJob.RowId;
        var wksManager = WKSManager.Instance();
        if (wksManager == null)
        {
            _lastMissionId = 0;
            _lastJobId = 0;
            return;
        }

        uint currentMissionId = wksManager->State.CurrentMission.MissionUnitRowId;

        if (currentMissionId == _lastMissionId && currentJobId == _lastJobId)
            return;

        _lastMissionId = currentMissionId;
        _lastJobId = currentJobId;

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
            if (InventoryManager.Instance()->GetInventoryItemCount(RefinedCosmicMayflyId) <= 0)
            {
                Services.Log.Warning("You don't have any Refined Cosmic Mayfly! Cannot auto-equip bait.");
                return;
            }

            Services.Log.Information($"Mission active and currently Fisher. Bait is {currentBait}, changing to Refined Cosmic Mayfly ({RefinedCosmicMayflyId}).");
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
