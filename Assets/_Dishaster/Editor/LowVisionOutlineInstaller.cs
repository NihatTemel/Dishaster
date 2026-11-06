using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Görmeyenin sürekli hattını gördüğü objeleri işaretler: oda ve kiler duvarları Wall, tezgahın gövdesi Table
    /// (üstündeki tahta ve eşyalar değil: onlar sadece elle açılıyor).
    /// Kurucular yeni kurulumda kendileri ekliyor; bu, mevcut prefab'lara ve Game sahnesine bir kez uyguluyor.
    /// Version değişince kendiliğinden yeniden uygular.
    /// </summary>
    [InitializeOnLoad]
    public static class LowVisionOutlineInstaller
    {
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterLowVisionOutline.version";
        const string GamePath = "Assets/_Dishaster/Scenes/Game.unity";
        const string CounterPath = "Assets/_Dishaster/Prefabs/Counter.prefab";
        const string PantryPath = "Assets/_Dishaster/Prefabs/Pantry.prefab";

        static readonly string[] RoomWalls = { "WallNorth", "WallSouth", "WallEast", "WallWest", "Door" };
        static readonly string[] PantryWalls = { "WallNorth", "WallEast" };
        static readonly string[] CounterBody = { "Top", "Cabinet" };

        static LowVisionOutlineInstaller()
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

        [MenuItem("Tools/Dishaster/6 Install Low Vision Outlines", priority = 6)]
        static void Install()
        {
            if (!File.Exists(GamePath) || !File.Exists(CounterPath) || !File.Exists(PantryPath)) return;

            PatchPrefab(CounterPath, MarkCounter);
            PatchPrefab(PantryPath, root => MarkChildren(root.transform, PantryWalls, LowVisionOutlineType.Wall));

            if (PatchGameScene())
            {
                File.WriteAllText(VersionFile, Version);
                Debug.Log("[Dishaster] Görmeyen için duvar ve tezgah hatları kuruldu.");
            }
        }

        /// <summary>Sahne kurucu oda duvarlarını yaratınca çağırıyor.</summary>
        public static void MarkRoomWalls(Transform room)
        {
            MarkChildren(room, RoomWalls, LowVisionOutlineType.Wall);
        }

        public static void MarkCounter(GameObject counter)
        {
            // Eski kurulumda tüm tezgah (tahta dahil) işaretliydi.
            if (counter.TryGetComponent(out LowVisionOutline whole)) Object.DestroyImmediate(whole, true);

            MarkChildren(counter.transform, CounterBody, LowVisionOutlineType.Table);
        }

        static void MarkChildren(Transform parent, string[] names, LowVisionOutlineType type)
        {
            foreach (string name in names)
            {
                Transform child = parent.Find(name);
                if (child != null) Mark(child.gameObject, type);
            }
        }

        static void Mark(GameObject target, LowVisionOutlineType type)
        {
            if (!target.TryGetComponent(out LowVisionOutline outline)) outline = target.AddComponent<LowVisionOutline>();
            outline.type = type;
        }

        static void PatchPrefab(string path, System.Action<GameObject> patch)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                patch(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static bool PatchGameScene()
        {
            Scene scene = SceneManager.GetSceneByPath(GamePath);
            bool wasOpen = scene.isLoaded;

            if (wasOpen && scene.isDirty)
            {
                Debug.LogWarning("[Dishaster] Game sahnesinde kaydedilmemiş değişiklik var; duvar hatları eklenmedi. " +
                                 "Sahneyi kaydedince Tools/Dishaster/6 Install Low Vision Outlines'ı çalıştır.");
                return false;
            }

            if (!wasOpen) scene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);

            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Room")
                    MarkRoomWalls(root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            return true;
        }
    }
}
