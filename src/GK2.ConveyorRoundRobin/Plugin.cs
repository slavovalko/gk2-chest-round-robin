using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2.ConveyorRoundRobin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "gk2.conveyorroundrobin";
        public const string PluginName = "GK2 Conveyor Round Robin";
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> LogHandOuts;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Hand out conveyor chest items round-robin. Set to false for vanilla behaviour.");
            LogHandOuts = Config.Bind("Debug", "LogHandOuts", false,
                "Write a log line for every item a managed chest hands out.");

            Harmony harmony = new Harmony(PluginGuid);
            try
            {
                harmony.PatchAll(typeof(ChestRoundRobin));
                Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Logger.LogError($"{PluginName} {PluginVersion} failed to patch, game left unmodified:{Environment.NewLine}{e}");
            }
        }
    }
}
