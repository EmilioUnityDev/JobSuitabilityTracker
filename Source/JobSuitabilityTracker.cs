// ============================================================
//  Job Suitability Tracker — Mod para Oxygen Not Included
//  Compatible con juego base (VANILLA_ID) y Space Out (EXPANSION1_ID)
//  Versión 1.62
//
//  QUÉ HACE:
//    Añade en el modo expandido del logro "Idoneidad Laboral" (ExosuitCycles)
//    una lista de todos los duplicantes con ■ o □ indicando si completaron
//    alguna tarea con traje en el ciclo actual.
//
//  CÓMO RASTREA:
//    Lee directamente dupesCompleteChoresInSuits de ColonyAchievementTracker.
//
//  PARCHES:
//    1. AchievementWidget.ShowProgress  → registra el progressParent del
//       widget ExosuitCycles y crea el panel si ya está expandido.
//    2. AchievementWidget.ExpandAchievement → inyecta/refresca el panel
//       cuando el usuario despliega el widget (ShowProgress no se vuelve
//       a llamar al expandir, sólo ExpandAchievement).
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
            Debug.Log("[JST] Job Suitability Tracker v1.62 cargado.");
            TryPatchShowProgress(harmony);
            TryPatchExpandAchievement(harmony);
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
                    typeof(AchievementPatches), nameof(AchievementPatches.ShowProgressPostfix)));
                Debug.Log("[JST] Parche → AchievementWidget.ShowProgress");
            }
            catch (Exception e) { Debug.LogWarning($"[JST] Error ShowProgress: {e.Message}"); }
        }

        // ExpandAchievement es private — AccessTools la encuentra igualmente.
        private static void TryPatchExpandAchievement(Harmony harmony)
        {
            try
            {
                var widgetType = AccessTools.TypeByName("AchievementWidget");
                if (widgetType == null) return;

                var target = AccessTools.Method(widgetType, "ExpandAchievement");
                if (target == null) { Debug.LogWarning("[JST] ExpandAchievement no encontrado."); return; }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(AchievementPatches), nameof(AchievementPatches.ExpandAchievementPostfix)));
                Debug.Log("[JST] Parche → AchievementWidget.ExpandAchievement");
            }
            catch (Exception e) { Debug.LogWarning($"[JST] Error ExpandAchievement: {e.Message}"); }
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
    //  POSTFIXES — ShowProgress y ExpandAchievement
    // ================================================================
    public static class AchievementPatches
    {
        // progressParents de widgets identificados como ExosuitCycles.
        // Se rellena en ShowProgressPostfix y se consulta en
        // ExpandAchievementPostfix (que no recibe el achievement).
        private static readonly HashSet<RectTransform> _exosuitParents =
            new HashSet<RectTransform>();

        // ── ShowProgress postfix ──────────────────────────────────────
        // Llamado cuando el juego puebla el widget (al mostrar la pantalla
        // de logros). progressParent suele estar inactivo (colapsado).
        public static void ShowProgressPostfix(object __instance, object achievement)
        {
            if (__instance == null || achievement == null) return;
            try
            {
                if (!IsTargetAchievement(achievement)) return;

                var progressParent =
                    ReflectionHelper.GetMemberValue(__instance, "progressParent") as RectTransform;
                if (progressParent == null) return;

                // Registrar este progressParent como ExosuitCycles
                _exosuitParents.Add(progressParent);

                // Si ya está expandido al cargar, inyectar ahora
                if (progressParent.gameObject.activeSelf)
                    InjectOrRefresh(progressParent);
            }
            catch (Exception e) { Debug.LogWarning($"[JST] ShowProgress: {e.Message}"); }
        }

        // ── ExpandAchievement postfix ─────────────────────────────────
        // Llamado DESPUÉS de que el juego haya hecho SetActive(toggle).
        // Si el resultado es active=true → el usuario acaba de expandir.
        public static void ExpandAchievementPostfix(object __instance)
        {
            try
            {
                var progressParent =
                    ReflectionHelper.GetMemberValue(__instance, "progressParent") as RectTransform;
                if (progressParent == null) return;

                // Sólo actuar en widgets ExosuitCycles registrados
                if (!_exosuitParents.Contains(progressParent)) return;

                if (progressParent.gameObject.activeSelf)
                    InjectOrRefresh(progressParent);   // expandido → mostrar
                else
                {
                    // Contraído → ocultar panel si existe
                    var existing = progressParent.Find("JST_Panel");
                    if (existing != null) existing.gameObject.SetActive(false);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[JST] ExpandAchievement: {e.Message}"); }
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

        // ── Lee dupesCompleteChoresInSuits del juego ──────────────────
        private static HashSet<int> GetSuitedDupeIdsForCurrentCycle()
        {
            var result = new HashSet<int>();
            try
            {
                if (GameClock.Instance == null) return result;
                int cycle = 0;
                try { cycle = (int)ReflectionHelper.Invoke(GameClock.Instance, "GetCycle"); } catch { }

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

                var tracker = ReflectionHelper.GetMemberValue(saveGame, "ColonyAchievementTracker");
                if (tracker == null) return result;

                var field = tracker.GetType().GetField("dupesCompleteChoresInSuits",
                    BindingFlags.Public | BindingFlags.Instance);
                if (field == null) return result;

                var dict = field.GetValue(tracker) as IDictionary;
                if (dict == null || !dict.Contains(cycle)) return result;

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
        private static void InjectOrRefresh(RectTransform progressParent)
        {
            const string PANEL = "JST_Panel";

            Transform existing = progressParent.Find(PANEL);

            // Guard: si el panel existe pero no tiene VLG (creación fallida anterior),
            // lo eliminamos para recrearlo limpiamente.
            if (existing != null && existing.GetComponent<VerticalLayoutGroup>() == null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
                existing = null;
            }

            var list      = GetDuplicantStatus();
            int doneCount = list.Count(x => x.Done);
            int total     = Components.MinionIdentities.Items.Count;
            string header = $"Completado con traje ({doneCount}/{total}):";

            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                UpdateHeader(existing.gameObject, header);
                RefreshRows(existing.gameObject, list);
                return;
            }

            // Primera vez: forzar rebuild para que los world corners sean válidos
            // (puede llamarse justo después de SetActive(true) en ExpandAchievement)
            LayoutRebuilder.ForceRebuildLayoutImmediate(progressParent);

            int leftMargin = MeasureDescLeftMargin(progressParent);
            Debug.Log($"[JST] Panel creado → leftMargin={leftMargin}");

            var panel = new GameObject(PANEL);
            panel.transform.SetParent(progressParent, false);

            // El VLG de progressParent usa el patrón ancla-fija (0,1)→(0,1)
            // y controla el ancho a través de sizeDelta.x, NO a través de stretch-anchor.
            // Copiamos ese patrón exactamente: ancla en top-left y sizeDelta.x = ancho
            // del contenedor (ya conocido tras ForceRebuildLayoutImmediate).
            var panelRt = panel.AddComponent<RectTransform>();
            float containerWidth = progressParent.rect.width;
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot     = new Vector2(0f, 1f);
            panelRt.sizeDelta = new Vector2(containerWidth, 0f); // altura la fija ContentSizeFitter
            Debug.Log($"[JST] panelRt: top-left anchor, sizeDelta.x={containerWidth}");

            // flexibleWidth=1 para que el VLG padre lo expanda si sobra espacio
            var panelLe = panel.AddComponent<LayoutElement>();
            panelLe.flexibleWidth = 1f;

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 0f;
            vlg.childAlignment         = TextAnchor.UpperLeft;
            vlg.childControlHeight     = true;
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding                = new RectOffset(leftMargin, 0, 2, 0);

            panel.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            AddRow(panel.transform, "JST_Header", header,
                   new Color(0.75f, 0.75f, 0.75f), 10.5f);

            RefreshRows(panel, list);

            // Segundo rebuild: hace que el VLG padre posicione y dimensione
            // correctamente el panel recién añadido.
            LayoutRebuilder.ForceRebuildLayoutImmediate(progressParent);
        }

        // Mide el offset del texto de la primera fila de requisito
        // en el espacio local del HIJO (RequirementPrefab), NO de progressParent.
        //
        // Razón: progressParent tiene un VLG con padding.left=P que desplaza
        // a todos sus hijos P píxeles a la derecha. Si midiéramos desde
        // progressParent obtendríamos P+T (P=padding VLG, T=offset texto en fila),
        // y al aplicarlo como padding.left de nuestro panel (que el VLG ya desplaza P)
        // el texto quedaría en P+(P+T)=2P+T en lugar de P+T. Midiendo dentro del
        // hijo obtenemos T directamente, que es el padding correcto.
        private static int MeasureDescLeftMargin(RectTransform progressParent)
        {
            const int FALLBACK = 22;
            try
            {
                foreach (Transform child in progressParent)
                {
                    if (child.name.StartsWith("JST")) continue;

                    var childRt = child as RectTransform ?? child.GetComponent<RectTransform>();
                    if (childRt == null) continue;

                    Component textComp =
                        (Component)child.GetComponentInChildren<TMPro.TextMeshProUGUI>(true)
                        ?? child.GetComponentInChildren<Text>(true);
                    if (textComp == null) continue;

                    var textRt = textComp.GetComponent<RectTransform>();
                    if (textRt == null) continue;

                    var corners = new Vector3[4];
                    textRt.GetWorldCorners(corners);

                    // Convertir la esquina superior-izquierda del texto al espacio
                    // local del HIJO (RequirementPrefab), no de progressParent.
                    Vector3 localInChild = childRt.InverseTransformPoint(corners[1]);
                    float leftEdgeInChild = localInChild.x - childRt.rect.xMin;

                    Debug.Log($"[JST] MeasureDesc: leftEdgeInChild={leftEdgeInChild:F1} "
                            + $"(hijo='{child.name}', childRect.xMin={childRt.rect.xMin:F1})");

                    if (leftEdgeInChild > 0f && leftEdgeInChild < 300f)
                        return Mathf.RoundToInt(leftEdgeInChild);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[JST] MeasureDesc: {e.Message}"); }
            return FALLBACK;
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
                AddRow(panel.transform, "JST_Empty", "(sin duplicantes)",
                       Color.gray, 10.5f);
                return;
            }

            foreach (var (name, done) in list)
            {
                var    color = done
                    ? new Color(0.30f, 0.88f, 0.30f)
                    : new Color(0.90f, 0.40f, 0.40f);
                string mark  = done ? "■" : "□";
                AddRow(panel.transform, $"JST_{name}",
                       $"{mark} {name}", color, 10.5f);
            }
        }

        private static void AddRow(Transform parent, string goName,
                                    string text, Color color, float size)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = size + 2f;

            var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text               = text;
            tmp.color              = color;
            tmp.fontSize           = size;
            tmp.overflowMode       = TMPro.TextOverflowModes.Ellipsis;
            tmp.enableWordWrapping = false;
        }
    }
}
