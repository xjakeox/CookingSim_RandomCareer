using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CookingSim_RandomCareer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "cookingsim_randomcareer";
        public const string PluginName = "CookingSim_RandomCareer";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RandomCareer;
        internal static ConfigEntry<int> Seed;
        internal static ConfigEntry<int> MaxStartFame;
        internal static ConfigEntry<string> ExcludedPackages;
        internal static ConfigEntry<bool> DiscoveryLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            RandomCareer = Config.Bind("General", "RandomCareer", false,
                "When on, starting a new Career gives 3 random starting recipes instead of the usual three. Loading a save is never affected. Set by the \"Random start\" row in the Career panel; this setting only decides on its own if that row could not be added.");
            Seed = Config.Bind("Random", "Seed", 0,
                "0 = a new roll every Career. Any other number always rolls the same three recipes (for the same DLC set). The seed used is written to the log.");
            MaxStartFame = Config.Bind("Random", "MaxStartFame", 1,
                "Highest recipe fame level allowed in the starting pick (0 = only the 5 fame-0 recipes).");
            ExcludedPackages = Config.Bind("Random", "ExcludedPackages", "",
                "Comma-separated recipe packages never picked at start, e.g. FoodNetwork when playing CSMP with someone who doesn't own it. " +
                "DLC is detected automatically: FoodNetwork recipes are only in the pool if you own that DLC. " +
                "Packages: Classic, American, Chinese, Oktoberfest, Halloween, FoodNetwork, Christmas, Valentines2022, BBQ.");
            DiscoveryLogging = Config.Bind("Debug", "DiscoveryLogging", false,
                "Log recipe/product state at each step of a new Career start (Step A discovery).");

            _harmony = new Harmony(PluginGuid);
            ApplyPatches(typeof(RandomCareerPatches));
            ApplyPatches(typeof(CareerPanelToggle));
            if (DiscoveryLogging.Value)
                ApplyPatches(typeof(DiscoveryPatches));

            Log.LogInfo($"{PluginName} {PluginVersion} loaded (RandomCareer={RandomCareer.Value})");
        }

        // Each nested [HarmonyPatch] class is applied on its own so a game update that
        // renames one target only disables that patch.
        private void ApplyPatches(Type container)
        {
            foreach (Type patch in container.GetNestedTypes(AccessTools.all))
            {
                try
                {
                    _harmony.CreateClassProcessor(patch).Patch();
                }
                catch (Exception e)
                {
                    Log.LogError($"Failed to apply {container.Name}.{patch.Name}: {e}");
                }
            }
        }
    }
}
