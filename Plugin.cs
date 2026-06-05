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

    private uint _lastMissionId = 0;
    private uint _lastJobId = 0;

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
            HelpMessage = "Opens the CosmicBaiter configuration window."
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
        this.ConfigWindow.IsOpen = true;
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

            if (this.Configuration.AutoOpenMissionProgress)
            {
                OpenMissionProgressWindow();
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
                var values = stackalloc AtkValue[1];
                values[0].SetInt(0);
                addon->FireCallback(1, values);
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

    private void ResetState()
    {
        // Reset tracking so that if WKSManager unloads and reloads, we re-evaluate
        _lastMissionId = 0;
        _lastJobId = 0;
    }
}
