using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace CosmicBaiter
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public bool AutoEquipBait { get; set; } = true;
        public bool AutoOpenMissionProgress { get; set; } = true;

        // Auto start/report loop for Miner & Botanist stellar missions.
        public bool AutoLoopMissions { get; set; } = false;

        // Minimum rank required before reporting. None=0, Bronze=1, Silver=2, Gold=3.
        // Default Gold: the mission only reaches Gold once both nodes are gathered,
        // so this doubles as the "both nodes done" gate.
        public int MinReportRank { get; set; } = 3;

        // Learned WKSMissionUnit RowIds, captured the first time a mission runs on
        // each job (the two missions share a name, so they can't be told apart by name).
        public uint MinerMissionId { get; set; } = 0;
        public uint BotanistMissionId { get; set; } = 0;

        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }
}
