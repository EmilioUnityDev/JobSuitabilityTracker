// ============================================================
//  Job Suitability Tracker — Mod para Oxygen Not Included
//  Compatible con juego base (VANILLA_ID) y Space Out (EXPANSION1_ID)
//  Versión 1.36
//
//  QUÉ HACE:
//    Añade debajo de los contadores del logro "Idoneidad Laboral"
//    (ExosuitCycles) una lista de todos los duplicantes con ☑ o ☐
//    indicando si completaron alguna tarea con traje en el ciclo actual.
//
//  CÓMO RASTREA (doble fuente):
//    1) SuitChoreTracker (tiempo real): parcheamos ChoreDriver.SetChore y
//       ChoreDriver.StopChore. Cuando un chore pasa a inactivo con
//       context.chore.isComplete==true y el dupe lleva HasAirtightSuit,
//       lo registramos. Se limpia automáticamente al detectar nuevo ciclo.
//    2) HasAirtightSuit (fallback): dupes que llevan traje ahora mismo casi
//       siempre han completado al menos una tarea este ciclo.
//    ☑ = rastreado por tracker O lleva traje actualmente.
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
    //  TRACKER DE TAREAS CON TRAJE
    //  Registra qué dupes completaron una tarea con traje en el ciclo
    //  actual. Se limpia automáticamente al detectar un nuevo ciclo.
    // ================================================================
    public static class SuitChoreTracker
    {
        private static int          _trackedCycle = -1;
        private static HashSet<int> _done         = new HashSet<int>();

        public static void Record(int instanceId)
        {
            int c = CurrentCycle();
            if (_trackedCycle != c) { _done.Clear(); _trackedCycle = c; }
            _done.Add(instanceId);
        }

        public static bool HasDone(int instanceId)
        {
            if (_trackedCycle != CurrentCycle()) return false;
            return _done.Contains(instanceId);
        }

        public static int CountThisCycle()
        {
            if (_trackedCycle != CurrentCycle()) return 0;
            return _done.Count;
        }

        private static int CurrentCycle()
        {
            if (GameClock.Instance == null) return 0;
            try { return (int)ReflectionHelper.Invoke(GameClock.Instance, "GetCycle"); }
            catch { return 0; }
        }
    }

    // ================================================================
    //  ENTRADA DEL MOD
    // ================================================================
    public class JobSuitabilityTrackerMod : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            Debug.Log("[JST] Job Suitability Tracker v1.36 cargado.");
            TryPatchShowProgress(harmony);
            TryPatchChoreEnd(harmony);
        }

        // ── Panel de logros ───────────────────────────────────────────
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

        // ── Finalización de tareas (dos hooks complementarios) ────────
        // SetChore(Context): cubre la transición A→B (chore A completado,
        //   driver.context aún tiene chore A con isComplete=True).
        // StopChore(): cubre paradas explícitas sin sustitución inmediata.
        private static void TryPatchChoreEnd(Harmony harmony)
        {
            try
            {
                var driverType = AccessTools.TypeByName("ChoreDriver");
                if (driverType == null) { Debug.LogWarning("[JST] ChoreDriver no encontrado."); return; }

                var setChore = AccessTools.Method(driverType, "SetChore");
                if (setChore != null)
                {
                    harmony.Patch(setChore, prefix: new HarmonyMethod(
                        typeof(ChoreEndPrefix), nameof(ChoreEndPrefix.Prefix)));
                    Debug.Log("[JST] Parche → ChoreDriver.SetChore");
                }

                var stopChore = AccessTools.Method(driverType, "StopChore");
                if (stopChore != null)
                {
                    harmony.Patch(stopChore, prefix: new HarmonyMethod(
                        typeof(ChoreEndPrefix), nameof(ChoreEndPrefix.Prefix)));
                    Debug.Log("[JST] Parche → ChoreDriver.StopChore");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[JST] Error ChoreDriver: {e.Message}"); }
        }
    }

    // ================================================================
    //  HELPERS DE REFLEXIÓN
    // ================================================================
    internal static class ReflectionHelper
    {
        private const BindingFlags ALL =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static object GetMemberValue(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            return t.GetProperty(name, ALL)?.GetValue(obj)
                ?? t.GetField(name, ALL)?.GetValue(obj);
        }

        public static object Invoke(object obj, string method, params object[] args)
        {
            if (obj == null) return null;
            var m = obj.GetType().GetMethod(method, ALL);
            return m?.Invoke(obj, args);
        }
    }

    // ================================================================
    //  PREFIX COMPARTIDO — ChoreDriver.SetChore / StopChore
    //  Lee el contexto ANTERIOR del driver antes de que se sobreescriba.
    //  Si context.chore.isComplete==true y el dupe lleva traje → registra.
    // ================================================================
    public static class ChoreEndPrefix
    {
        private static readonly Tag TagSuit = TagManager.Create("HasAirtightSuit");

        public static void Prefix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                var ctx   = ReflectionHelper.GetMemberValue(__instance, "context");
                var chore = ctx != null ? ReflectionHelper.GetMemberValue(ctx, "chore") : null;
                if (chore == null) return;

                var ic = ReflectionHelper.GetMemberValue(chore, "isComplete");
                if (!(ic is bool b && b)) return;

                var driverComp = __instance as Component;
                if (driverComp == null) return;
                var kpid = driverComp.GetComponent<KPrefabID>();
                if (kpid == null) return;
                if (!kpid.HasTag(TagSuit)) return;

                SuitChoreTracker.Record(kpid.InstanceID);
            }
            catch { }
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
                InjectOrRefresh(widget.gameObject, achievement);
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

        // ── Recuento oficial del juego para el ciclo actual ───────────
        private static int GetGameCount(object status)
        {
            var mAch = ReflectionHelper.GetMemberValue(status, "m_achievement");
            if (mAch == null) return -1;

            var reqs = ReflectionHelper.GetMemberValue(mAch, "Requirements") as IEnumerable
                    ?? ReflectionHelper.GetMemberValue(mAch, "requirements") as IEnumerable;
            if (reqs == null) return -1;

            int currentCycle = 0;
            if (GameClock.Instance != null)
                try { currentCycle = (int)ReflectionHelper.Invoke(GameClock.Instance, "GetCycle"); } catch { }

            foreach (var req in reqs)
            {
                if (req == null) continue;
                var m = req.GetType().GetMethod("GetNumberOfDupesForCycle",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) continue;
                try { return Convert.ToInt32(m.Invoke(req, new object[] { currentCycle })); }
                catch { }
            }
            return -1;
        }

        // ── Estado por duplicante ─────────────────────────────────────
        // done = rastreado por SuitChoreTracker  OR  lleva HasAirtightSuit
        // El tracker captura completaciones en tiempo real desde que se cargó
        // el mod. HasAirtightSuit cubre dupes que completaron antes del arranque
        // y aún llevan el traje puesto.
        private static readonly Tag TagSuit = TagManager.Create("HasAirtightSuit");

        private static List<(string Name, bool Done)> GetDuplicantStatus()
        {
            var result = new List<(string, bool)>();
            foreach (var minion in Components.MinionIdentities.Items)
            {
                if (minion == null) continue;
                var kpid = minion.GetComponent<KPrefabID>();
                if (kpid == null) continue;

                bool done = SuitChoreTracker.HasDone(kpid.InstanceID)
                         || kpid.HasTag(TagSuit);
                result.Add((minion.GetProperName(), done));
            }

            result.Sort((a, b) =>
                a.Item2 != b.Item2
                    ? (a.Item2 ? -1 : 1)
                    : string.Compare(a.Item1, b.Item1, StringComparison.Ordinal));
            return result;
        }

        // ── Inyección / refresco del panel ───────────────────────────
        private static void InjectOrRefresh(GameObject root, object status)
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
            int gameCount = GetGameCount(status);

            string header = gameCount >= 0 && gameCount != doneCount
                ? $"Completado con traje ({doneCount}/{total}) [juego:{gameCount}]:"
                : $"Completado con traje ({doneCount}/{total}):";

            if (existing != null)
            {
                UpdateHeader(existing.gameObject, header);
                RefreshRows(existing.gameObject, list);
                return;
            }

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
            vlg.padding = new RectOffset(4, 0, 4, 0);

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
                var color = done
                    ? new Color(0.30f, 0.88f, 0.30f)
                    : new Color(0.90f, 0.40f, 0.40f);
                string mark = done ? "☑" : "☐";
                AddRow(panel.transform, $"JST_{name}",
                       $"  {mark}  {name}", color, 10.5f);
            }
        }

        // Usamos TextMeshProUGUI en lugar de LocText para evitar que
        // LocText.Awake() llame a StringKey..ctor en contexto no inicializado.
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
