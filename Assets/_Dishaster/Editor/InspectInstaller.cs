using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// İncele prefab'ını (InspectView + "[F] İncele" ipucu) oluşturur ve Game sahnesine koyar.
    /// Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class InspectInstaller
    {
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterInspect.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/Inspect.prefab";

        static readonly Color PanelColor = new Color(UiStyle.Ink.r, UiStyle.Ink.g, UiStyle.Ink.b, 0.82f);
        static readonly Color TextColor = UiStyle.Cream;

        static InspectInstaller()
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

        [MenuItem("Tools/Dishaster/9 Install Inspect", priority = 9)]
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
            canvas.name = "Inspect";
            Object.DestroyImmediate(canvas.GetComponent<GraphicRaycaster>());

            var inspect = canvas.gameObject.AddComponent<InspectView>();

            // Nişangahın hemen altında.
            TMP_Text prompt = DishasterSceneBuilder.AddText(canvas, "Prompt", "[F] İncele", 30f, TextColor,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(400f, 50f), true);
            var promptShadow = prompt.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            promptShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            promptShadow.effectDistance = new Vector2(1.5f, -1.5f);
            inspect.prompt = prompt.gameObject;

            Image panel = UiStyle.Card(canvas, "Help", PanelColor, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(900f, 64f));
            DishasterSceneBuilder.AddText(panel.transform, "HelpText", "[F] Bırak     Fare: etrafında dön     Tekerlek: yaklaş / uzaklaş", 26f, TextColor,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 60f));
            inspect.help = panel.gameObject;

            PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, PrefabPath);
            Object.DestroyImmediate(canvas.gameObject);
        }
    }
}
