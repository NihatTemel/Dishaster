using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// LowVisionRole prefab'ını (elin hedefi; tutulan eşyalar ona bağlanıyor) oluşturur ve Game sahnesine koyar.
    /// Eski tahta bariyeri yok: duvar tahtası kalktı (defter), mutfakta görmeyeni engelleyen bir taraf kalmadı.
    /// Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class LowVisionInstaller
    {
        const string Version = "7";
        const string VersionFile = "ProjectSettings/DishasterLowVision.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/LowVisionRole.prefab";
        const string ShaderPath = "Assets/_Dishaster/Shaders/LowVision.shader";
        const string MaskShaderPath = "Assets/_Dishaster/Shaders/LowVisionOutlineMask.shader";


        static LowVisionInstaller()
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

        [MenuItem("Tools/Dishaster/5 Install Low Vision", priority = 5)]
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
            var root = new GameObject("LowVisionRole");
            var role = root.AddComponent<LowVisionRole>();
            role.effectShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            role.outlineMaskShader = AssetDatabase.LoadAssetAtPath<Shader>(MaskShaderPath);
            role.hand = BuildHand(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        // Görünen el karakterin kendisi (IK); bu sadece hedef. Sahibinde hemen, diğerlerinde ilk konum mesajıyla açılıyor.
        static Transform BuildHand(Transform parent)
        {
            var hand = new GameObject("Hand");
            hand.transform.SetParent(parent, false);
            hand.transform.localPosition = new Vector3(0f, 1f, -1.9f);
            hand.SetActive(false);
            return hand.transform;
        }
    }
}
