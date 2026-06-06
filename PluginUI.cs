using System;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using Dalamud.Interface.Windowing;

namespace CosmicBaiter
{
    public class ConfigWindow : Window, IDisposable
    {
        private Configuration Configuration;

        public ConfigWindow(Configuration configuration)
            : base("CosmicBaiter Configuration")
        {
            this.Configuration = configuration;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(300, 150),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
            this.Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            var autoEquip = this.Configuration.AutoEquipBait;
            if (ImGui.Checkbox("Auto-Equip Bait", ref autoEquip))
            {
                this.Configuration.AutoEquipBait = autoEquip;
                this.Configuration.Save();
            }
            ImGui.TextWrapped("Automatically equips Refined Cosmic Mayfly when starting a Fisher stellar mission.");

            ImGui.Spacing();

            var autoPop = this.Configuration.AutoOpenMissionProgress;
            if (ImGui.Checkbox("Auto-Open Mission in Progress Window", ref autoPop))
            {
                this.Configuration.AutoOpenMissionProgress = autoPop;
                this.Configuration.Save();
            }
            ImGui.TextWrapped("Automatically opens the 'Mission in Progress' window when a mission starts.");

            ImGui.Separator();

            var autoLoop = this.Configuration.AutoLoopMissions;
            if (ImGui.Checkbox("Auto Start + Report Missions (Miner/Botanist)", ref autoLoop))
            {
                this.Configuration.AutoLoopMissions = autoLoop;
                this.Configuration.Save();
            }
            ImGui.TextWrapped("Automatically starts the mission, waits while you gather, then reports it once " +
                              "the rank below is reached. You handle the gathering. Toggle with /cosmicbaiter auto.");

            int nodes = this.Configuration.NodesToGather;
            if (ImGui.InputInt("Nodes to gather before reporting", ref nodes))
            {
                this.Configuration.NodesToGather = Math.Clamp(nodes, 1, 10);
                this.Configuration.Save();
            }
            ImGui.TextWrapped("Reports only after this many gathering nodes have been fully gathered " +
                              "(the Gathering window opening and closing). Rank/score are ignored.");

            ImGui.Spacing();
            ImGui.Text($"Learned mission IDs - Miner: {this.Configuration.MinerMissionId}, " +
                       $"Botanist: {this.Configuration.BotanistMissionId}");
            if (ImGui.Button("Reset learned IDs"))
            {
                this.Configuration.MinerMissionId = 0;
                this.Configuration.BotanistMissionId = 0;
                this.Configuration.Save();
            }
            ImGui.TextWrapped("Start the correct mission once on each job to capture its ID, then enable auto.");
        }
    }
}
