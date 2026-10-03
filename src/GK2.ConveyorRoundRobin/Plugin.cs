using BepInEx;

namespace GK2.ConveyorRoundRobin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "gk2.conveyorroundrobin";
        public const string PluginName = "GK2 Conveyor Round Robin";
        public const string PluginVersion = "0.1.0";

        private void Awake()
        {
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }
    }
}
