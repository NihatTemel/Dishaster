using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// FoodShowcase prefab'ı: tezgahın sağındaki küçük masada art'ın 8 yiyeceği (Assets/ART/3D/Fruit_Vegetables,
    /// bkz. YIYECEKLER.md) yan yana. Şimdilik sadece görmek için: alınmıyor, kesilmiyor. Yiyecek prefab'ları iç içe
    /// (nested) duruyor; art kurulumu güncelleyince burada da güncel görünüyor. Version değişince yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class FoodShowcaseInstaller
    {
        const string Version = "1";
        const string VersionFile = "ProjectSettings/DishasterFoodShowcase.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/FoodShowcase.prefab";
        const string FoodFolder = "Assets/ART/3D/Fruit_Vegetables/Prefabs";

        static readonly string[] Foods =
        {
            "PF_Strawberry_01", "PF_Tomato_01", "PF_Onion_01", "PF_Pepper_01",
            "PF_Mushroom_01", "PF_Carrot_01", "PF_Cheese_Block_01", "PF_Bread_Loaf_01",
        };

        // Tezgahın (orijinde, 1.8 m) sağında; doğuşta (z=-5) oyuncuların önünde.
        static readonly Vector3 Position = new Vector3(2.7f, 0f, 0f);

        const float TableHeight = 0.8f;
        const float TableDepth = 0.5f;
        const float TopThickness = 0.04f;
        const float Gap = 0.06f;
        const float Margin = 0.1f;

        static FoodShowcaseInstaller()
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

        [MenuItem("Tools/Dishaster/12 Install Food Showcase", priority = 12)]
        static void Install()
        {
            // Yiyecekler eksikse tamam sayılmıyor: bir sonraki açılışta yeniden deneniyor.
            bool complete = BuildPrefab();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Position, Quaternion.identity, true) && complete)
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Position, Quaternion.identity);
        }

        static bool BuildPrefab()
        {
            var root = new GameObject("FoodShowcase");
            var foods = new GameObject("Foods").transform;
            foods.SetParent(root.transform, false);

            // Önce hepsi konup genişlikleri ölçülüyor; masa toplam boya göre.
            bool complete = true;
            float x = 0f;
            foreach (string food in Foods)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FoodFolder}/{food}.prefab");
                if (prefab == null)
                {
                    complete = false;
                    Debug.LogWarning($"[Dishaster] {food} bulunamadı ({FoodFolder}). Art'ın 'Dishaster > Art > Yiyecekleri kur' menüsü çalıştırıldı mı?");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(foods, false);

                Bounds bounds = WorldBounds(instance);
                float halfWidth = bounds.size.x / 2f;
                float centerOffset = bounds.center.x - instance.transform.position.x;

                // Pivot tabanın ortasında (YIYECEKLER.md): masanın üstüne oturuyor.
                instance.transform.localPosition = new Vector3(x + halfWidth - centerOffset, 0f, 0f);
                x += halfWidth * 2f + Gap;
            }

            float width = Mathf.Max(0.6f, x - Gap) + Margin * 2f;
            foods.localPosition = new Vector3(-width / 2f + Margin, TableHeight, 0f);

            BuildTable(root.transform, width);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return complete;
        }

        static Bounds WorldBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void BuildTable(Transform parent, float width)
        {
            Material top = DishasterSceneBuilder.GetMaterial("M_TableTop", new Color(0.72f, 0.56f, 0.40f));
            Material leg = DishasterSceneBuilder.GetMaterial("M_TableLeg", new Color(0.35f, 0.26f, 0.19f));

            AddBlock(parent, "Top", new Vector3(0f, TableHeight - TopThickness / 2f, 0f), new Vector3(width, TopThickness, TableDepth), top);

            float legX = width / 2f - 0.05f;
            float legZ = TableDepth / 2f - 0.05f;
            float legHeight = TableHeight - TopThickness;
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f;
                float sz = i < 2 ? -1f : 1f;
                AddBlock(parent, "Leg" + (i + 1), new Vector3(sx * legX, legHeight / 2f, sz * legZ), new Vector3(0.05f, legHeight, 0.05f), leg);
            }
        }

        static void AddBlock(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
