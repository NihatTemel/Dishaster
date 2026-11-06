using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// LowVisionRole prefab'ını (el, tahta bariyeri) oluşturur ve Game sahnesine koyar.
    /// Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class LowVisionInstaller
    {
        const string Version = "5";
        const string VersionFile = "ProjectSettings/DishasterLowVision.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/LowVisionRole.prefab";
        const string ShaderPath = "Assets/_Dishaster/Shaders/LowVision.shader";
        const string MaskShaderPath = "Assets/_Dishaster/Shaders/LowVisionOutlineMask.shader";

        // Tahta kuzey duvarında (z=7); tezgah ve kiler bu çizginin güneyinde.
        const float BarrierZ = 2.5f;
        const float RoomWidth = 18f;
        const float RoomHeight = 3f;
        const int IgnoreRaycastLayer = 2;

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
            role.boardBarrier = BuildBarrier(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        // Hareketi her oyuncu kendi makinesinde yapıyor: sadece görmeyende açılınca diğerleri içinden geçiyor.
        static Collider BuildBarrier(Transform parent)
        {
            var barrier = new GameObject("BoardBarrier") { layer = IgnoreRaycastLayer };
            barrier.transform.SetParent(parent, false);
            barrier.transform.localPosition = new Vector3(0f, RoomHeight / 2f, BarrierZ);

            var collider = barrier.AddComponent<BoxCollider>();
            collider.size = new Vector3(RoomWidth, RoomHeight, 0.2f);
            collider.enabled = false;
            return collider;
        }

        static Transform BuildHand(Transform parent)
        {
            var hand = new GameObject("Hand");
            hand.transform.SetParent(parent, false);
            hand.transform.localPosition = new Vector3(0f, 1f, -5f);

            Material skin = DishasterSceneBuilder.GetMaterial("M_Hand", new Color(0.85f, 0.70f, 0.58f));

            AddPart(hand.transform, "Palm", Vector3.zero, new Vector3(0.09f, 0.03f, 0.10f), skin);
            for (int i = 0; i < 4; i++)
                AddPart(hand.transform, "Finger" + (i + 1), new Vector3(-0.033f + 0.022f * i, 0f, 0.09f), new Vector3(0.017f, 0.017f, 0.08f), skin);
            AddPart(hand.transform, "Thumb", new Vector3(-0.058f, 0f, 0.015f), new Vector3(0.05f, 0.018f, 0.018f), skin);

            // Sahibinde hemen, diğerlerinde ilk konum mesajıyla açılıyor.
            hand.SetActive(false);
            return hand.transform;
        }

        static void AddPart(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<BoxCollider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
