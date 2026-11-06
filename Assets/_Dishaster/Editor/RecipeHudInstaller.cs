using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// RecipeHud prefab'ı: Bilmiyorum'un tarif kartı (sol üst), herkesin gördüğü "tarif bozuldu" uyarısı ve dökme
    /// göstergesi (alt orta).
    /// Game sahnesine koyar; Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class RecipeHudInstaller
    {
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterRecipeHud.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/RecipeHud.prefab";

        static readonly Color PanelColor = new Color(0.08f, 0.07f, 0.06f, 0.82f);
        static readonly Color FailColor = new Color(0.55f, 0.08f, 0.06f, 0.92f);
        static readonly Color TextColor = new Color(0.96f, 0.94f, 0.90f);

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

            RectTransform recipePanel = AddPanel(canvas, "RecipePanel", PanelColor, new Vector2(0f, 1f), new Vector2(270f, -250f), new Vector2(500f, 460f));
            hud.recipePanel = recipePanel.gameObject;
            hud.recipeText = DishasterSceneBuilder.AddText(recipePanel, "RecipeText", "", 26f, TextColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 420f));
            hud.recipeText.alignment = TextAlignmentOptions.TopLeft;

            RectTransform failPanel = AddPanel(canvas, "FailPanel", FailColor, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(980f, 130f));
            hud.failPanel = failPanel.gameObject;
            hud.failText = DishasterSceneBuilder.AddText(failPanel, "FailText", "", 40f, TextColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940f, 120f));

            // Dökme göstergesi: alt ortada, nişangahın altında.
            RectTransform pourPanel = AddPanel(canvas, "PourPanel", PanelColor, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(620f, 170f));
            hud.pourPanel = pourPanel.gameObject;
            hud.pourText = DishasterSceneBuilder.AddText(pourPanel, "PourText", "", 32f, TextColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(590f, 160f));

            PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, PrefabPath);
            Object.DestroyImmediate(canvas.gameObject);
        }

        static RectTransform AddPanel(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            DishasterSceneBuilder.SetRect(image.rectTransform, anchor, position, size);
            return image.rectTransform;
        }
    }
}
