// ============================================================
//  Job Suitability Tracker — Mod para Oxygen Not Included
//  Compatible con juego base (VANILLA_ID) y Space Out (EXPANSION1_ID)
//  Versión 1.49
//
//  QUÉ HACE:
//    Añade debajo de los contadores del logro "Idoneidad Laboral"
//    (ExosuitCycles) una lista de todos los duplicantes con ☑ o ☐
//    indicando si completaron alguna tarea con traje en el ciclo actual.
//
//  CÓMO RASTREA:
//    Lee directamente el campo dupesCompleteChoresInSuits de
//    ColonyAchievementTracker — la misma fuente que usa el juego para
//    su contador oficial. Esto garantiza paridad exacta sin necesidad
//    de interceptar chores, workers ni eventos de finalización.
//
//  PANEL:
//    AchievementWidget.ShowProgress(ColonyAchievementStatus achievement)
// ============================================================

using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace JobSuitabilityTracker
{
    // ================================================================
    //  CONFIGURACIÓN
    // ================================================================
    public static class Config
    {
        public const string AchievementID = "ExosuitCycles";
    }

    // ================================================================
    //  ENTRADA DEL MOD
    // ================================================================
    public class JobSuitabilityTrackerMod : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            Debug.Log("[JST] Job Suitability Tracker v1.49 cargado.");
            TryPatchShowProgress(harmony);
        }

        private static void TryPatchShowProgress(Harmony harmony)
        {
            try
            {
                var widgetType = AccessTools.TypeByName("AchievementWidget");
                if (widgetType == null) { Debug.LogWarning("[JST] AchievementWidget no encontrado."); return; }

                var statusType = AccessTools.TypeByName("ColonyAchievementStatus");
                var target = statusType != null
                    ? AccessTools.Method(widgetType, "ShowProgress", new[] { statusType })
                    : AccessTools.Method(widgetType, "ShowProgress");

                if (target == null) { Debug.LogWarning("[JST] ShowProgress no encontrado."); return; }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(ShowProgressPostfix), nameof(ShowProgressPostfix.Postfix)));
                Debug.Log("[JST] Parche → AchievementWidget.ShowProgress");
            }
            catch (Exception e) { Debug.LogWarning($"[JST] Error ShowProgress: {e.Message}"); }
        }
    }

    // ================================================================
    //  HELPERS DE REFLEXIÓN
    // ================================================================
    internal static class ReflectionHelper
    {
        private const BindingFlags DECLARED =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // Traversa la jerarquía de herencia para encontrar propiedades y
        // campos privados en clases padre (DeclaredOnly requerido para
        // que Type.GetField encuentre privados de clases base).
        public static object GetMemberValue(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            while (t != null && t != typeof(object))
            {
                var prop = t.GetProperty(name, DECLARED);
                if (prop != null) try { return prop.GetValue(obj); } catch { }

                var field = t.GetField(name, DECLARED);
                if (field != null) try { return field.GetValue(obj); } catch { }

                t = t.BaseType;
            }
            return null;
        }

        public static object Invoke(object obj, string method, params object[] args)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            while (t != null && t != typeof(object))
            {
                var m = t.GetMethod(method, DECLARED);
                if (m != null) try { return m.Invoke(obj, args); } catch { }
                t = t.BaseType;
            }
            return null;
        }
    }

    // ================================================================
    //  POSTFIX — AchievementWidget.ShowProgress
    // ================================================================
    public static class ShowProgressPostfix
    {
        public static void Postfix(object __instance, object achievement)
        {
            if (__instance == null || achievement == null) return;
            try
            {
                if (!IsTargetAchievement(achievement)) return;
                var widget = __instance as MonoBehaviour;
                if (widget == null) return;
                InjectOrRefresh(widget.gameObject);
            }
            catch (Exception e) { Debug.LogWarning($"[JST] Postfix: {e.Message}"); }
        }

        // ── ¿Es el logro ExosuitCycles? ──────────────────────────────
        private static bool IsTargetAchievement(object status)
        {
            var mAch = ReflectionHelper.GetMemberValue(status, "m_achievement");
            if (mAch != null)
            {
                var id = ReflectionHelper.GetMemberValue(mAch, "Id") as string
                      ?? ReflectionHelper.GetMemberValue(mAch, "id") as string;
                if (id != null) return id == Config.AchievementID;
            }
            // Fallback: escanear todos los campos no-primitivos buscando el Id
            foreach (var f in status.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.FieldType.IsValueType || f.FieldType == typeof(string)) continue;
                var val = f.GetValue(status);
                if (val == null) continue;
                var id = ReflectionHelper.GetMemberValue(val, "Id") as string
                      ?? ReflectionHelper.GetMemberValue(val, "id") as string;
                if (id == Config.AchievementID) return true;
            }
            return false;
        }

        // ── Lee dupesCompleteChoresInSuits directamente del juego ────
        // SaveGame.Instance.ColonyAchievementTracker.dupesCompleteChoresInSuits
        // es Dictionary<int, List<int>> (ciclo → lista de InstanceIDs de dupes).
        // Es la misma fuente que usa DupesCompleteChoreInExoSuitForCycles
        // para su GetNumberOfDupesForCycle, garantizando paridad exacta.
        private static HashSet<int> GetSuitedDupeIdsForCurrentCycle()
        {
            var result = new HashSet<int>();
            try
            {
                if (GameClock.Instance == null) return result;
                int cycle = 0;
                try { cycle = (int)ReflectionHelper.Invoke(GameClock.Instance, "GetCycle"); } catch { }

                // 1. Obtener SaveGame.Instance
                var saveGameType = AccessTools.TypeByName("SaveGame");
                if (saveGameType == null) return result;

                object saveGame = null;
                foreach (var memberName in new[] { "Instance", "instance" })
                {
                    saveGame =
                        saveGameType.GetProperty(memberName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?.GetValue(null)
                        ?? saveGameType.GetField(memberName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            ?.GetValue(null);
                    if (saveGame != null) break;
                }
                if (saveGame == null) return result;

                // 2. Obtener ColonyAchievementTracker (propiedad de SaveGame que
                //    devuelve GetComponent<ColonyAchievementTracker>())
                var tracker = ReflectionHelper.GetMemberValue(saveGame, "ColonyAchievementTracker");
                if (tracker == null) return result;

                // 3. Leer el campo público dupesCompleteChoresInSuits
                var field = tracker.GetType().GetField("dupesCompleteChoresInSuits",
                    BindingFlags.Public | BindingFlags.Instance);
                if (field == null) return result;

                var dict = field.GetValue(tracker) as IDictionary;
                if (dict == null || !dict.Contains(cycle)) return result;

                // 4. Extraer los InstanceIDs del ciclo actual
                var list = dict[cycle] as IEnumerable;
                if (list == null) return result;

                foreach (var item in list)
                    if (item is int id) result.Add(id);
            }
            catch (Exception e) { Debug.LogWarning($"[JST] GetSuitedDupeIds: {e.Message}"); }
            return result;
        }

        // ── Estado por duplicante ─────────────────────────────────────
        private static List<(string Name, bool Done)> GetDuplicantStatus()
        {
            var result    = new List<(string, bool)>();
            var suitedIds = GetSuitedDupeIdsForCurrentCycle();

            foreach (var minion in Components.MinionIdentities.Items)
            {
                if (minion == null) continue;
                var kpid = minion.GetComponent<KPrefabID>();
                if (kpid == null) continue;

                bool done = suitedIds.Contains(kpid.InstanceID);
                result.Add((minion.GetProperName(), done));
            }

            result.Sort((a, b) =>
                a.Item2 != b.Item2
                    ? (a.Item2 ? -1 : 1)
                    : string.Compare(a.Item1, b.Item1, StringComparison.Ordinal));
            return result;
        }

        // ── Inyección / refresco del panel ───────────────────────────
        private static void InjectOrRefresh(GameObject root)
        {
            const string PANEL = "JST_Panel";

            Transform existing = root.transform.Find(PANEL);
            if (existing == null)
                foreach (Transform child in root.transform)
                {
                    existing = child.Find(PANEL);
                    if (existing != null) break;
                }

            var list      = GetDuplicantStatus();
            int doneCount = list.Count(x => x.Done);
            int total     = Components.MinionIdentities.Items.Count;
            string header = $"Completado con traje ({doneCount}/{total}):";

            if (existing != null)
            {
                UpdateHeader(existing.gameObject, header);
                RefreshRows(existing.gameObject, list);
                return;
            }

            // Primera vez: crear el panel bajo el contenedor adecuado
            Transform content = root.transform;
            foreach (var n in new[] { "Requirements", "Content", "Body",
                                      "ScrollContent", "Inner", "Container", "Details" })
            {
                var t = root.transform.Find(n);
                if (t != null) { content = t; break; }
            }

            var panel = new GameObject(PANEL);
            panel.transform.SetParent(content, false);

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing               = 2f;
            vlg.childAlignment        = TextAnchor.UpperLeft;
            vlg.childControlHeight    = false;
            vlg.childControlWidth     = true;
            vlg.childForceExpandWidth = true;
            vlg.padding               = new RectOffset(4, 0, 4, 0);

            panel.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            AddRow(panel.transform, "JST_Header", header,
                   new Color(0.75f, 0.75f, 0.75f), 11f);

            RefreshRows(panel, list);
        }

        private static void UpdateHeader(GameObject panel, string text)
        {
            if (panel.transform.childCount == 0) return;
            var tmp = panel.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp != null) tmp.text = text;
        }

        private static void RefreshRows(GameObject panel,
                                         List<(string Name, bool Done)> list)
        {
            for (int i = panel.transform.childCount - 1; i > 0; i--)
                UnityEngine.Object.Destroy(panel.transform.GetChild(i).gameObject);

            if (list.Count == 0)
            {
                AddRow(panel.transform, "JST_Empty", "  (sin duplicantes)",
                       Color.gray, 10.5f);
                return;
            }

            foreach (var (name, done) in list)
            {
                var    color = done
                    ? new Color(0.30f, 0.88f, 0.30f)
                    : new Color(0.90f, 0.40f, 0.40f);
                string mark  = done ? "☑" : "☐";
                AddRow(panel.transform, $"JST_{name}",
                       $"  {mark}  {name}", color, 10.5f);
            }
        }

        private static void AddRow(Transform parent, string goName,
                                    string text, Color color, float size)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 16f;

            var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text               = text;
            tmp.color              = color;
            tmp.fontSize           = size;
            tmp.overflowMode       = TMPro.TextOverflowModes.Ellipsis;
            tmp.enableWordWrapping = false;
        }
    }
}
