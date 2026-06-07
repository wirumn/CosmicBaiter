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

        // Number of gathering nodes to fully gather before the mission is reported.
        public int NodesToGather { get; set; } = 2;

        // Learned WKSMissionUnit RowIds, captured the first time a mission runs on
        // each job (the two missions share a name, so they can't be told apart by name).
        public uint MinerMissionId { get; set; } = 0;
        public uint BotanistMissionId { get; set; } = 0;

        public bool AutoLoopCrafterMissions { get; set; } = false;
        public int MinCrafterTimeSeconds { get; set; } = 100;
        public uint WeaverMissionId { get; set; } = 0;
        public uint LeatherworkerMissionId { get; set; } = 0;
        public uint GoldsmithMissionId { get; set; } = 0;
        public uint ArmorerMissionId { get; set; } = 0;
        public uint BlacksmithMissionId { get; set; } = 0;
        public uint CarpenterMissionId { get; set; } = 0;
        public uint AlchemistMissionId { get; set; } = 0;
        public uint CulinarianMissionId { get; set; } = 0;

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
