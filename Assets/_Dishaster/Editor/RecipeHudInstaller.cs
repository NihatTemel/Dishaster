using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// RecipeHud prefab'ı: Bilmiyorum'un tarif kartı (sol üst) ve herkesin gördüğü sonuç şeridi (üst orta). İkisi de
    /// içeriğe göre uzuyor: yazılar kaydırılıyor, kutudan taşmıyor. Kartta her adım bir satır (daire + yazı); satırlar
    /// oyunda kalıptan çoğaltılıyor.
    /// Game sahnesine koyar; Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class RecipeHudInstaller
    {
        const string Version = "5";
        const string VersionFile = "ProjectSettings/DishasterRecipeHud.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/RecipeHud.prefab";

        const float CardWidth = 520f;
        const float ResultWidth = 700f;
        const float BadgeSize = 36f;

        static readonly Color PanelColor = new Color(UiStyle.Cream.r, UiStyle.Cream.g, UiStyle.Cream.b, 0.97f);

        static RecipeHudInstaller()
        {
            EditorApplication.delayCall += AutoInstall;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) AutoInstall();
            };
        }

        static void AutoInstall()
        {
            if (File.Exists(VersionFile) && File.ReadAllText(VersionFile) == Version) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

            Install();
        }

        [MenuItem("Tools/Dishaster/8 Install Recipe HUD", priority = 8)]
        static void Install()
        {
            BuildPrefab();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity);
        }

        static void BuildPrefab()
        {
            Transform canvas = DishasterSceneBuilder.AddCanvas();
            canvas.name = "RecipeHud";
            // Çizim ekranında da tarif görünsün.
            canvas.GetComponent<Canvas>().sortingOrder = 10;
            Object.DestroyImmediate(canvas.GetComponent<GraphicRaycaster>());

            var hud = canvas.gameObject.AddComponent<RecipeHud>();
            BuildCard(canvas, hud);
            BuildResult(canvas, hud);

            PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, PrefabPath);
            Object.DestroyImmediate(canvas.gameObject);
        }

        // ── TARİF KARTI ─────────────────────────────────────────────────────

        static void BuildCard(Transform canvas, RecipeHud hud)
        {
            Image card = UiStyle.Card(canvas, "RecipePanel", PanelColor, new Vector2(0f, 1f), Vector2.zero, new Vector2(CardWidth, 300f));
            RectTransform rect = card.rectTransform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(36f, -36f);
            UiStyle.Column(card.gameObject, new RectOffset(30, 30, 26, 28), 6f, true);
            hud.recipePanel = card.gameObject;

            // Üstte ince hardal şerit (kartın "etiketi").
            Image tab = UiStyle.Card(card.transform, "Tab", UiStyle.Mustard, new Vector2(0f, 1f), Vector2.zero, new Vector2(120f, 8f), false);
            tab.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            tab.rectTransform.pivot = new Vector2(0f, 1f);
            tab.rectTransform.anchoredPosition = new Vector2(30f, -12f);

            hud.titleText = Text(card.transform, "Title", 36f, UiStyle.Ink, true);
            hud.titleText.characterSpacing = 4f;
            UiStyle.Fit(hud.titleText, 0.7f, false);
            hud.subtitleText = Text(card.transform, "Subtitle", 21f, UiStyle.InkSoft, false);

            Image divider = UiStyle.Card(card.transform, "Divider", new Color(UiStyle.Ink.r, UiStyle.Ink.g, UiStyle.Ink.b, 0.12f), new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, 3f), false);
            UiStyle.Fixed(divider.gameObject, 0f, 3f);

            var steps = new GameObject("Steps", typeof(RectTransform));
            steps.transform.SetParent(card.transform, false);
            VerticalLayoutGroup column = UiStyle.Column(steps, new RectOffset(0, 0, 10, 0), 14f, false);
            column.childAlignment = TextAnchor.UpperLeft;
            hud.stepsRoot = (RectTransform)steps.transform;

            hud.stepTemplate = BuildStepRow(steps.transform);
        }

        /// <summary>Adım satırı: solda daire (numara ya da tik), sağda kaydırılan yazı.</summary>
        static GameObject BuildStepRow(Transform parent)
        {
            var row = new GameObject("StepTemplate", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var line = row.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 14f;
            line.childAlignment = TextAnchor.UpperLeft;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;

            var badge = new GameObject("Badge").AddComponent<Image>();
            badge.transform.SetParent(row.transform, false);
            badge.sprite = UiStyle.Circle;
            badge.color = UiStyle.CreamDark;
            badge.raycastTarget = false;
            UiStyle.Fixed(badge.gameObject, BadgeSize, BadgeSize);

            TMP_Text number = Text(badge.transform, "Number", 21f, UiStyle.Ink, true);
            number.alignment = TextAlignmentOptions.Center;
            UiStyle.Stretch(number.rectTransform);

            var check = new GameObject("Check").AddComponent<Image>();
            check.transform.SetParent(badge.transform, false);
            check.sprite = UiStyle.Check;
            check.color = UiStyle.Cream;
            check.raycastTarget = false;
            UiStyle.Stretch(check.rectTransform);
            check.rectTransform.offsetMin = new Vector2(6f, 6f);
            check.rectTransform.offsetMax = new Vector2(-6f, -6f);
            check.gameObject.SetActive(false);

            TMP_Text label = Text(row.transform, "Label", 25f, UiStyle.Ink, false);
            label.margin = new Vector4(0f, 4f, 0f, 0f);
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            return row;
        }

        // ── SONUÇ ŞERİDİ ────────────────────────────────────────────────────

        static void BuildResult(Transform canvas, RecipeHud hud)
        {
            Image panel = UiStyle.Card(canvas, "ResultPanel", UiStyle.Terracotta, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(ResultWidth, 140f));
            RectTransform rect = panel.rectTransform;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -48f);
            VerticalLayoutGroup column = UiStyle.Column(panel.gameObject, new RectOffset(40, 40, 24, 26), 4f, true);
            column.childAlignment = TextAnchor.UpperCenter;
            hud.resultPanel = panel.gameObject;
            hud.resultBackground = panel;

            hud.resultTitle = Text(panel.transform, "Title", 42f, UiStyle.Cream, true);
            hud.resultTitle.alignment = TextAlignmentOptions.Center;
            hud.resultTitle.characterSpacing = 6f;
            UiStyle.Fit(hud.resultTitle, 0.7f, false);

            hud.resultText = Text(panel.transform, "Text", 26f, UiStyle.Cream, false);
            hud.resultText.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>Düzende yazı: genişlik düzenden, yükseklik yazıdan (satırlar kaydırılıyor).</summary>
        static TMP_Text Text(Transform parent, string name, float size, Color color, bool heading)
        {
            TMP_Text text = DishasterSceneBuilder.AddText(parent, name, "", size, color, new Vector2(0f, 1f), Vector2.zero, new Vector2(100f, size * 1.3f), heading);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }
    }
}
