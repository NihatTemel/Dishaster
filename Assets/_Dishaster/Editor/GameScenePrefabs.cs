using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dishaster.EditorTools
{
    /// <summary>Prefab'ları Game sahnesine koyan ortak adım. Sahne kapalıysa açıp kaydedip kapatıyor.</summary>
    internal static class GameScenePrefabs
    {
        const string GamePath = "Assets/_Dishaster/Scenes/Game.unity";

        /// <summary>
        /// Prefab sahnede yoksa ekler; moveExisting ise varolanı da verilen yere taşır.
        /// Sahnede kaydedilmemiş değişiklik varsa dokunmaz, false döner.
        /// </summary>
        public static bool EnsurePlaced(string prefabPath, Vector3 position, Quaternion rotation, bool moveExisting = false)
        {
            Scene scene = SceneManager.GetSceneByPath(GamePath);
            bool wasOpen = scene.isLoaded;
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

            if (wasOpen && scene.isDirty)
            {
                Debug.LogWarning($"[Dishaster] Game sahnesinde kaydedilmemiş değişiklik var; {prefabName} eklenmedi. " +
                                 "Sahneyi kaydedince Tools/Dishaster menüsünden tekrar çalıştır.");
                return false;
            }

            if (!wasOpen) scene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            GameObject existing = FindInstance(scene, prefab);
            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetPositionAndRotation(position, rotation);
                EditorSceneManager.SaveScene(scene);
            }
            else if (moveExisting)
            {
                existing.transform.SetPositionAndRotation(position, rotation);
                existing.name = prefabName;
                EditorSceneManager.SaveScene(scene);
            }

            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);

            Debug.Log($"[Dishaster] {prefabName} Game sahnesinde hazır.");
            return true;
        }

        /// <summary>Prefab'ın sahnedeki kopyasını kaldırır. Kaydedilmemiş değişiklik varsa dokunmaz, false döner.</summary>
        public static bool Remove(string prefabPath)
        {
            Scene scene = SceneManager.GetSceneByPath(GamePath);
            bool wasOpen = scene.isLoaded;
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

            if (wasOpen && scene.isDirty)
            {
                Debug.LogWarning($"[Dishaster] Game sahnesinde kaydedilmemiş değişiklik var; {prefabName} kaldırılmadı. " +
                                 "Sahneyi kaydedince Tools/Dishaster menüsünden tekrar çalıştır.");
                return false;
            }

            if (!wasOpen) scene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);

            GameObject existing = FindInstance(scene, AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
                EditorSceneManager.SaveScene(scene);
            }

            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            return true;
        }

        static GameObject FindInstance(Scene scene, GameObject prefab)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == prefab)
                        return child.gameObject;

            return null;
        }
    }
}
