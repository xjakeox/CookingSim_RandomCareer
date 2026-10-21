using System;
using System.Collections.Generic;
using System.Linq;
using CS.Quests;
using HarmonyLib;
using RecipeMenu;
using WIUtils;

namespace CookingSim_RandomCareer
{
    // Step B: a new Career started while RandomCareer is on replaces the default save's
    // three starting recipes (1024, 1043, 1079) with three random ones.
    internal static class RandomCareerPatches
    {
        private static readonly AccessTools.FieldRef<RecipesManager, List<RecipesManager.RecipeAvailability>> AvailableRecipes =
            AccessTools.FieldRefAccess<RecipesManager, List<RecipesManager.RecipeAvailability>>("availableRecipes");

        // Set by the Career panel's Play for the next start. Null when the panel row isn't
        // available, in which case the RandomCareer config decides.
        internal static bool? Pending;

        // True from StartBaseCareer until SkipTutorial consumes it. Never persisted, so loading
        // a save (which doesn't go through StartBaseCareer) stays vanilla.
        private static bool _activeRun;

        // P1: GUIGameModesWindow.BaseCareer is the only caller, i.e. a fresh Career from the menu.
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartBaseCareer))]
        private static class StartBaseCareerPatch
        {
            private static void Prefix(ref bool tutorial)
            {
                _activeRun = Pending ?? Plugin.RandomCareer.Value;
                Pending = null;
                if (!_activeRun)
                    return;
                if (tutorial)
                    Plugin.Log.LogInfo("Random Career: tutorial forced off");
                // The tutorial teaches the fixed starter recipes, so random mode always skips it.
                tutorial = false;
            }
        }

        // P2: runs after the default save, ResetProducts and PrepareDay, right before StartDay.
        [HarmonyPatch(typeof(FirstPlayTutorial), nameof(FirstPlayTutorial.SkipTutorial))]
        private static class SkipTutorialPatch
        {
            private static bool Prefix(FirstPlayTutorial __instance)
            {
                if (!_activeRun)
                    return true;
                _activeRun = false;

                try
                {
                    if (!Roll())
                        return true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Random Career roll failed, falling back to vanilla starters: {e}");
                    return true;
                }

                // The rest of vanilla SkipTutorial. Unlocks happen before SetFirstPlayFinished,
                // as in vanilla, so they don't count towards the "dishes unlocked" achievements.
                QuestsPanel.Me?.Hide();
                __instance.SetFirstPlayFinished(finished: true);
                return false;
            }
        }

        private static bool Roll()
        {
            RecipesManager rm = TemporalSingleton<RecipesManager>.Me;
            List<Recipe> pool = BuildPool(rm);
            if (pool.Count < 3)
            {
                Plugin.Log.LogError($"Random Career: only {pool.Count} recipes in the pool (MaxStartFame={Plugin.MaxStartFame.Value}), keeping vanilla starters");
                return false;
            }

            int seed = Plugin.Seed.Value != 0 ? Plugin.Seed.Value : Environment.TickCount & int.MaxValue;
            var rng = new Random(seed);
            // Partial Fisher-Yates over an id-sorted pool, so a seed always gives the same picks.
            for (int i = 0; i < 3; i++)
            {
                int j = rng.Next(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            List<Recipe> picks = pool.Take(3).ToList();

            // Clear the default save's starters. RefreshRecipesLists rebuilds known/locked from
            // PlayerStats and only refills availableRecipes when it is empty.
            PlayerStats stats = PlayerStats.Me;
            stats.knownRecipes.Clear();
            stats.upgradedRecipes.Clear();
            stats.thisDayRecipes?.Clear();
            AvailableRecipes(rm).Clear();
            rm.RefreshRecipesLists(withDailyRecipes: false);

            // UnlockRecipe(payCP: false) also unlocks each recipe's products.
            foreach (Recipe r in picks)
                rm.UnlockRecipe(r, payCP: false);

            ResetMenuItems(picks);

            // Same order as BaseCareerCoroutine: rebuild the product list and re-hide the props
            // of anything only the vanilla starters needed.
            TemporalSingleton<ProductsManager>.Me.ResetProducts();
            StandsStartStateManager.Me?.RefreshState();

            List<int> products = TemporalSingleton<ProductsManager>.Me.GetAvailableProductsIDs();
            Plugin.Log.LogInfo($"Random Career: content [{string.Join(", ", Singleton<GameManager>.Me.CurrentContentIDs)}], pool by package: " +
                               string.Join(", ", pool.GroupBy(r => r.RecipePackageType).Select(g => $"{g.Key} {g.Count()}")));
            Plugin.Log.LogInfo($"Random Career: seed {seed}, pool {pool.Count}, picked {string.Join(", ", picks.Select(Describe))}");
            Plugin.Log.LogInfo($"Random Career: known [{string.Join(", ", rm.knownRecipes.Select(r => r.Id))}], " +
                               $"{products?.Count ?? 0} products available: [{string.Join(", ", products ?? new List<int>())}]");
            return true;
        }

        // Halloween event recipes are priced at 999 CP; they aren't meant to be bought.
        private const int EventOnlyCost = 999;

        // Base-tier recipes (nothing upgrades into them) up to MaxStartFame, minus excluded packages
        // and event-only recipes. rm.Recipes only holds content the player owns ("base", plus
        // "foodnetwork" with that DLC), so DLC needs no check here. Sorted by id so the seed is
        // reproducible.
        private static List<Recipe> BuildPool(RecipesManager rm)
        {
            var excluded = new HashSet<string>(
                Plugin.ExcludedPackages.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            var upgradeTargets = new HashSet<int>(rm.Recipes.Where(r => r.UpgradeID != -1).Select(r => r.UpgradeID));
            return rm.Recipes
                .Where(r => !upgradeTargets.Contains(r.Id)
                            && r.UnlockFame <= Plugin.MaxStartFame.Value
                            && r.UnlockCost < EventOnlyCost
                            && !excluded.Contains(r.RecipePackageType.ToString()))
                .OrderBy(r => r.Id)
                .ToList();
        }

        // The default save also seeds the custom-menu manager with the vanilla starters. Left alone,
        // 3 stale + 3 new would reach the 5-dish threshold and unlock menu selection on day 1.
        private static void ResetMenuItems(List<Recipe> picks)
        {
            MenuItemsManager mim = Game.CurrentClassicGameService?.MenuService?.MenuItemsManager;
            if (mim == null)
                return;
            var ids = new HashSet<int>(picks.Select(r => r.Id));
            Traverse t = Traverse.Create(mim);
            t.Property("AvailableRecipes").SetValue(ids.ToList());
            t.Property("CurrentMenu").SetValue((mim.CurrentMenu ?? new List<int>()).Where(ids.Contains).ToList());
            t.Property("LastNonContestMenu").SetValue((mim.LastNonContestMenu ?? new List<int>()).Where(ids.Contains).ToList());
        }

        private static string Describe(Recipe r)
        {
            string name;
            try { name = Localization.Get(r); }
            catch { name = r.name; }
            return $"{r.Id} {name} (fame {r.UnlockFame}, {r.RecipePackageType})";
        }
    }
}
