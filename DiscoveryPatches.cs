using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HarmonyLib;
using WIUtils;

namespace CookingSim_RandomCareer
{
    // Step A: read-only logging of where the vanilla starting recipes and products come from.
    // Nothing here changes game state.
    internal static class DiscoveryPatches
    {
        private static readonly AccessTools.FieldRef<RecipesManager, List<RecipesManager.RecipeAvailability>> AvailableRecipes =
            AccessTools.FieldRefAccess<RecipesManager, List<RecipesManager.RecipeAvailability>>("availableRecipes");

        private static readonly AccessTools.FieldRef<FirstPlayTutorial, List<int>> BasicRecipes =
            AccessTools.FieldRefAccess<FirstPlayTutorial, List<int>>("basicRecipes");

        // Set by StartBaseCareer so per-unlock logging stays quiet outside a career start.
        private static bool _inCareerStart;

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartBaseCareer))]
        private static class StartBaseCareerPatch
        {
            private static void Prefix(string kitchenId, int levelStart, bool tutorial, bool startOrSkipTutorial, bool applySave, bool timeConstrained)
            {
                _inCareerStart = true;
                Plugin.Log.LogInfo($"[Discovery] StartBaseCareer kitchen={kitchenId} levelStart={levelStart} tutorial={tutorial} " +
                                   $"startOrSkipTutorial={startOrSkipTutorial} applySave={applySave} timeConstrained={timeConstrained}");
            }
        }

        [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.AfterLoad))]
        private static class AfterLoadPatch
        {
            private static void Postfix(PlayerStats __instance)
            {
                if (!_inCareerStart)
                    return;
                Plugin.Log.LogInfo($"[Discovery] PlayerStats.AfterLoad: save knownRecipes=[{Ids(__instance.knownRecipes)}] " +
                                   $"upgradedRecipes=[{Ids(__instance.upgradedRecipes)}] fameLevel={__instance.fameLevel}");
                DumpState("after default save (AfterLoad)");
            }
        }

        [HarmonyPatch(typeof(StandardProductsManager), nameof(StandardProductsManager.ResetProducts))]
        private static class ResetProductsPatch
        {
            private static void Postfix()
            {
                if (_inCareerStart)
                    DumpState("after ResetProducts");
            }
        }

        [HarmonyPatch(typeof(RecipesManager), nameof(RecipesManager.UnlockRecipe))]
        private static class UnlockRecipePatch
        {
            private static void Postfix(Recipe r, bool payCP)
            {
                if (!_inCareerStart)
                    return;
                Plugin.Log.LogInfo($"[Discovery] UnlockRecipe {Describe(r)} payCP={payCP} from {Callers()}");
            }
        }

        [HarmonyPatch(typeof(FirstPlayTutorial), nameof(FirstPlayTutorial.SkipTutorial))]
        private static class SkipTutorialPatch
        {
            private static void Prefix(FirstPlayTutorial __instance)
            {
                List<int> basic = BasicRecipes(__instance);
                Plugin.Log.LogInfo($"[Discovery] SkipTutorial basicRecipes=[{Names(basic)}]");
                DumpState("before SkipTutorial");
                DumpPool();
            }

            private static void Postfix()
            {
                DumpState("after SkipTutorial");
                _inCareerStart = false;
            }
        }

        [HarmonyPatch(typeof(FirstPlayTutorial), nameof(FirstPlayTutorial.StartTutorial))]
        private static class StartTutorialPatch
        {
            private static void Prefix(FirstPlayTutorial __instance, bool fromSave)
            {
                Plugin.Log.LogInfo($"[Discovery] StartTutorial fromSave={fromSave} basicRecipes=[{Names(BasicRecipes(__instance))}]");
                DumpState("before StartTutorial");
                DumpPool();
                _inCareerStart = false;
            }
        }

        private static void DumpState(string label)
        {
            var log = Plugin.Log;
            RecipesManager rm = TemporalSingleton<RecipesManager>.Me;
            if (rm == null)
            {
                log.LogInfo($"[Discovery] --- {label}: RecipesManager is null");
                return;
            }

            List<RecipesManager.RecipeAvailability> available = AvailableRecipes(rm);
            log.LogInfo($"[Discovery] --- {label}");
            log.LogInfo($"[Discovery]   RecipesManager.knownRecipes ({rm.knownRecipes.Count}): {Describe(rm.knownRecipes)}");
            log.LogInfo($"[Discovery]   upgradedRecipes ({rm.upgradedRecipes.Count}): {Describe(rm.upgradedRecipes)}");
            log.LogInfo($"[Discovery]   lockedRecipes.Count={rm.lockedRecipes.Count}");
            log.LogInfo($"[Discovery]   availableRecipes ({available?.Count ?? -1}): [{string.Join(", ", available?.Select(a => a.RecipeId) ?? Enumerable.Empty<int>())}]");

            if (TemporalSingleton<ProductsManager>.Me is StandardProductsManager pm)
            {
                List<int> ids = pm.GetAvailableProductsIDs();
                log.LogInfo($"[Discovery]   availableProducts ({ids?.Count ?? -1}): [{Ids(ids)}]");
            }
            else
            {
                log.LogInfo($"[Discovery]   ProductsManager is {TemporalSingleton<ProductsManager>.Me?.GetType().Name ?? "null"}");
            }
        }

        // Full recipe table plus the candidate starting pool from plan section 4.3 step 1.
        private static void DumpPool()
        {
            var log = Plugin.Log;
            RecipesManager rm = TemporalSingleton<RecipesManager>.Me;
            if (rm?.Recipes == null)
                return;

            var upgradeTargets = new HashSet<int>(rm.Recipes.Where(r => r.UpgradeID != -1).Select(r => r.UpgradeID));
            var sceneProducts = new HashSet<int>();
            ProductsManager pm = TemporalSingleton<ProductsManager>.Me;
            if (pm?.ProductsInScene != null)
                foreach (Product p in pm.ProductsInScene)
                    if (p != null && p.group != null)
                        sceneProducts.Add(p.group.id);

            log.LogInfo($"[Discovery] === Recipes ({rm.Recipes.Count}), scene product ids: {sceneProducts.Count}");
            log.LogInfo("[Discovery] id | name | fame | cost | upgradeID | baseTier | package | baseGame | content | neededProducts | missingInScene");
            foreach (Recipe r in rm.Recipes.OrderBy(r => r.UnlockFame).ThenBy(r => r.Id))
            {
                List<int> needed = SafeNeeded(r);
                int missing = needed?.Count(id => !sceneProducts.Contains(id)) ?? -1;
                log.LogInfo($"[Discovery] {r.Id} | {r.name} | {r.UnlockFame} | {SafeCost(r)} | {r.UpgradeID} | {!upgradeTargets.Contains(r.Id)} | " +
                            $"{r.RecipePackageType} | {r.BaseGameRecipe} | {SafeContent(r)} | {needed?.Count ?? -1} | {missing}");
            }

            List<Recipe> baseTier = rm.Recipes.Where(r => !upgradeTargets.Contains(r.Id)).ToList();
            log.LogInfo($"[Discovery] base-tier fame distribution: " +
                        string.Join(", ", baseTier.GroupBy(r => r.UnlockFame).OrderBy(g => g.Key).Select(g => $"fame {g.Key}: {g.Count()}")));
            log.LogInfo($"[Discovery] base-tier package distribution: " +
                        string.Join(", ", baseTier.GroupBy(r => r.RecipePackageType).Select(g => $"{g.Key}: {g.Count()}")));
            List<Recipe> pool = baseTier.Where(r => r.UnlockFame == 0).ToList();
            log.LogInfo($"[Discovery] candidate pool (base tier, fame 0): {pool.Count}: {Describe(pool)}");
        }

        private static List<int> SafeNeeded(Recipe r)
        {
            try { return r.GetRecipeNeededProducts(true); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Discovery] GetRecipeNeededProducts({r.Id}) threw {e.GetType().Name}"); return null; }
        }

        private static string SafeCost(Recipe r)
        {
            try { return r.UnlockCost.ToString(); }
            catch { return "?"; }
        }

        private static string SafeContent(Recipe r)
        {
            try { return r.GetContentID(); }
            catch { return "?"; }
        }

        private static string Describe(Recipe r) => r == null ? "null" : $"{r.Id}:{r.name}";

        private static string Describe(IEnumerable<Recipe> recipes) => "[" + string.Join(", ", recipes.Select(Describe)) + "]";

        private static string Ids(IEnumerable<int> ids) => ids == null ? "null" : string.Join(", ", ids);

        private static string Names(IEnumerable<int> ids)
        {
            if (ids == null)
                return "null";
            List<Recipe> all = TemporalSingleton<RecipesManager>.Me?.Recipes;
            return string.Join(", ", ids.Select(id => all?.FirstOrDefault(r => r.Id == id) is Recipe r ? Describe(r) : id.ToString()));
        }

        private static string Callers()
        {
            var sb = new StringBuilder();
            StackFrame[] frames = new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>();
            foreach (StackFrame f in frames.Take(4))
            {
                var m = f.GetMethod();
                if (sb.Length > 0)
                    sb.Append(" <- ");
                sb.Append(m?.DeclaringType?.Name).Append('.').Append(m?.Name);
            }
            return sb.ToString();
        }
    }
}
