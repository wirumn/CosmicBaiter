using ImGuiNET;
using System;
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
            this.Flags = (Dalamud.Bindings.ImGui.ImGuiWindowFlags)(ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
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
        }
    }
}
