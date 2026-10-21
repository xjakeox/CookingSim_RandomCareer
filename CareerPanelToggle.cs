using System.Collections.Generic;
using System.Linq;
using System.Text;
using CS.GUI;
using CS.GUI.GameModes;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CookingSim_RandomCareer
{
    // Step D: a "Random start  Off/On" row in the Career panel's Settings section, cloned from
    // the recipe time-limit ("Relaxed mode") control. It loads from and saves to the RandomCareer config.
    internal static class CareerPanelToggle
    {
        // Short enough to fit on one line like "Relaxed mode".
        private const string Label = "Random start";
        private const float RowSpacing = 12f;

        private static readonly AccessTools.FieldRef<GUICareerGameMode, GUISettingsArrows> TutorialSelection =
            AccessTools.FieldRefAccess<GUICareerGameMode, GUISettingsArrows>("tutorialSelection");
        private static readonly AccessTools.FieldRef<GUICareerGameMode, GUISettingsArrows> TimeConstraintSelection =
            AccessTools.FieldRefAccess<GUICareerGameMode, GUISettingsArrows>("timeConstraintSelection");
        private static readonly AccessTools.FieldRef<GUICareerGameMode, GameObject> TutorialSelectionParent =
            AccessTools.FieldRefAccess<GUICareerGameMode, GameObject>("tutorialSelectionParent");
        private static readonly AccessTools.FieldRef<GUIGameMode, List<Selectable>> Selectables =
            AccessTools.FieldRefAccess<GUIGameMode, List<Selectable>>("selectables");
        private static readonly AccessTools.FieldRef<GUISettingsArrows, Transform> BarsParent =
            AccessTools.FieldRefAccess<GUISettingsArrows, Transform>("barsParent");
        private static readonly AccessTools.FieldRef<GUISettingsArrows, Text> ValueText =
            AccessTools.FieldRefAccess<GUISettingsArrows, Text>("valueText");

        private static GameObject _row;
        private static GUISettingsArrows _arrows;
        private static RectTransform _original;
        private static float _loggedHeight = -1f;
        // Hide() only saves the row's value after Show() has loaded it.
        private static bool _shown;

        [HarmonyPatch(typeof(GUICareerGameMode), nameof(GUICareerGameMode.Init))]
        private static class InitPatch
        {
            private static void Postfix(GUICareerGameMode __instance)
            {
                GUISettingsArrows time = TimeConstraintSelection(__instance);
                GUISettingsArrows tutorial = TutorialSelection(__instance);
                if (time == null)
                {
                    Plugin.Log.LogWarning("Career panel: no time-limit row to clone; set RandomCareer in the config instead");
                    return;
                }

                // The panel's sections are placed by hand (no layout group), so clone only the
                // arrows control into the "Settings" section and move it below the original.
                Transform section = time.transform.parent;
                _row = Object.Instantiate(time.gameObject, section);
                _row.name = "RandomCareerSelection";
                _row.transform.SetSiblingIndex(time.transform.GetSiblingIndex() + 1);
                _original = (RectTransform)time.transform;
                _arrows = _row.GetComponent<GUISettingsArrows>();
                // Existing loc keys, so the value reads Off/On in every language.
                _arrows.TextValues = new[] { "setting_off", "setting_on" };

                if (!Relabel(_row, _arrows) || Plugin.DiscoveryLogging.Value)
                {
                    Transform container = tutorial != null ? CommonParent(tutorial.transform, time.transform) : section;
                    Plugin.Log.LogInfo("Career panel hierarchy:\n" + Dump(container, 0));
                }

                List<Selectable> list = Selectables(__instance);
                int index = list.IndexOf(time);
                list.Insert(index >= 0 ? index + 1 : list.Count, _arrows);
                Plugin.Log.LogInfo($"Career panel: added '{Label}' row in {section.name}");
            }
        }

        [HarmonyPatch(typeof(GUICareerGameMode), nameof(GUICareerGameMode.Show))]
        private static class ShowPatch
        {
            private static void Postfix(GUICareerGameMode __instance)
            {
                if (_arrows == null)
                    return;
                GameObject tutorialParent = TutorialSelectionParent(__instance);
                if (tutorialParent != null)
                    _row.SetActive(tutorialParent.activeSelf);
                PlaceBelowOriginal();
                _arrows.Value = Plugin.RandomCareer.Value ? 1 : 0;
                _arrows.UpdateValue();
                _shown = true;
            }
        }

        [HarmonyPatch(typeof(GUICareerGameMode), nameof(GUICareerGameMode.Hide))]
        private static class HidePatch
        {
            private static void Postfix()
            {
                if (_arrows != null && _shown)
                    Plugin.RandomCareer.Value = _arrows.Value == 1;
                _shown = false;
            }
        }

        [HarmonyPatch(typeof(GUICareerGameMode), "Play")]
        private static class PlayPatch
        {
            private static void Prefix()
            {
                if (_arrows == null)
                    return;
                Plugin.RandomCareer.Value = _arrows.Value == 1;
                // A hidden row (other kitchens) never starts a random run.
                RandomCareerPatches.Pending = Plugin.RandomCareer.Value && _row.activeInHierarchy;
            }
        }

        // The random start relies on the base kitchen's default save and FirstPlayTutorial, so the
        // row follows the tutorial row, which vanilla only shows for kitchen 0.
        [HarmonyPatch(typeof(GUICareerGameMode), "SetTutorialSelectionActivity")]
        private static class KitchenPatch
        {
            private static void Postfix(int selectedKitchenId)
            {
                if (_row != null)
                    _row.SetActive(selectedKitchenId == 0);
            }
        }

        private static bool Relabel(GameObject row, GUISettingsArrows arrows)
        {
            Text valueText = ValueText(arrows);
            bool found = false;
            foreach (AutoLocalizedTextMeshProUGUI tmp in row.GetComponentsInChildren<AutoLocalizedTextMeshProUGUI>(true))
            {
                tmp.LocalizeText = false;
                tmp.text = Label;
                found = true;
            }
            foreach (AutoLocalizedText text in row.GetComponentsInChildren<AutoLocalizedText>(true))
            {
                if (text == valueText)
                    continue;
                text.LocalizeText = false;
                text.text = Label;
                found = true;
            }
            if (!found)
                Plugin.Log.LogWarning("Career panel: no label found in the cloned row");
            return found;
        }

        private static Transform CommonParent(Transform a, Transform b)
        {
            var ancestors = new HashSet<Transform>();
            for (Transform t = a.parent; t != null; t = t.parent)
                ancestors.Add(t);
            Transform p = b.parent;
            while (p != null && !ancestors.Contains(p))
                p = p.parent;
            return p ?? b.parent;
        }

        // Runs on Show: during Init the menu canvas is inactive and unsized, so stretched rects
        // measure near zero. Always offsets from the original, so repeated Shows don't drift.
        private static void PlaceBelowOriginal()
        {
            if (_original == null)
                return;
            float height = Height(_original);
            var rect = (RectTransform)_row.transform;
            rect.anchoredPosition = _original.anchoredPosition - new Vector2(0f, height + RowSpacing);
            if (!Mathf.Approximately(height, _loggedHeight))
            {
                _loggedHeight = height;
                Plugin.Log.LogInfo($"Career panel: original row height {height:F1}, original at {_original.anchoredPosition}, new row at {rect.anchoredPosition}");
            }
        }

        // The control's own rect plus the value bars hanging below it, in the control's local
        // units. Only those two: the highlight frame and the selection animation enlarge the
        // control while it is selected (measured 110 vs 63), which made the row jump between opens.
        private static float Height(RectTransform control)
        {
            Rect own = control.rect;
            float top = own.yMax, bottom = own.yMin;
            if (BarsParent(control.GetComponent<GUISettingsArrows>()) is RectTransform bars)
            {
                var corners = new Vector3[4];
                bars.GetWorldCorners(corners);
                foreach (Vector3 c in corners)
                {
                    float y = control.InverseTransformPoint(c).y;
                    top = Mathf.Max(top, y);
                    bottom = Mathf.Min(bottom, y);
                }
            }
            return top - bottom;
        }

        private static string Dump(Transform t, int depth)
        {
            var sb = new StringBuilder();
            string components = string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inactive)").Append(" [").Append(components).Append("]\n");
            if (depth < 4)
                foreach (Transform child in t)
                    sb.Append(Dump(child, depth + 1));
            return sb.ToString();
        }
    }
}
