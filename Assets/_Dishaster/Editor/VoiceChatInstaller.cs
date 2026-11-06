using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// VoiceChat prefab'ını (sesli sohbet + "Konuşuyorsun" göstergesi) oluşturur ve Game sahnesine koyar.
    /// Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class VoiceChatInstaller
    {
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterVoiceChat.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/VoiceChat.prefab";

        static readonly Color TextColor = new Color(0.96f, 0.94f, 0.90f);

        static VoiceChatInstaller()
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

        [MenuItem("Tools/Dishaster/10 Install Voice Chat", priority = 10)]
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
            canvas.name = "VoiceChat";
            Object.DestroyImmediate(canvas.GetComponent<GraphicRaycaster>());

            var voice = canvas.gameObject.AddComponent<VoiceChat>();

            // Sağ alt köşe: kendi mikrofonunun durumu.
            TMP_Text status = DishasterSceneBuilder.AddText(canvas, "Status", "", 26f, TextColor,
                new Vector2(1f, 0f), new Vector2(-170f, 45f), new Vector2(300f, 50f));
            status.alignment = TextAlignmentOptions.Right;
            voice.status = status;

            PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, PrefabPath);
            Object.DestroyImmediate(canvas.gameObject);
        }
    }
}
