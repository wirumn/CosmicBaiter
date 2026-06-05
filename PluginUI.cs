using ImGuiNET;
using System;
using System.Numerics;

namespace CosmicBaiter
{
    public class PluginUI : IDisposable
    {
        private Configuration Configuration;

        private bool visible = false;
        public bool Visible
        {
            get { return this.visible; }
            set { this.visible = value; }
        }

        public PluginUI(Configuration configuration)
        {
            this.Configuration = configuration;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            DrawMainWindow();
        }

        public void DrawMainWindow()
        {
            if (!Visible)
            {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(300, 150), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(300, 150), new Vector2(float.MaxValue, float.MaxValue));
            if (ImGui.Begin("CosmicBaiter Configuration", ref this.visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
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
            ImGui.End();
        }
    }
}
