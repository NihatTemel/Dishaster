using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// FoodShowcase prefab'ı: tezgahın sağındaki küçük masada art'ın 8 yiyeceği (Assets/ART/3D/Fruit_Vegetables,
    /// bkz. YIYECEKLER.md) yan yana. Hepsi mutfak eşyası: alınıyor, tahtada bıçakla dilim dilim kesiliyor, dilimleri
    /// kaseye atılabiliyor. Her yiyecek için Prefabs/Items altında Art_&lt;Ad&gt; (art prefab'ı iç içe, Whole/Slices) ve her
    /// dilim için Art_&lt;Ad&gt;_Slice_NN kuruluyor; art güncellenince yeniden kurulum yeter. Version değişince yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class FoodShowcaseInstaller
    {
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterFoodShowcase.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/FoodShowcase.prefab";
        const string FoodFolder = "Assets/ART/3D/Fruit_Vegetables/Prefabs";

        static readonly (string prefab, Ingredient ingredient)[] Foods =
        {
            ("PF_Strawberry_01", Ingredient.Strawberry), ("PF_Tomato_01", Ingredient.Tomato), ("PF_Onion_01", Ingredient.Onion),
            ("PF_Pepper_01", Ingredient.Pepper), ("PF_Mushroom_01", Ingredient.Mushroom), ("PF_Carrot_01", Ingredient.Carrot),
            ("PF_Cheese_Block_01", Ingredient.Cheese), ("PF_Bread_Loaf_01", Ingredient.Bread),
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
            foreach ((string food, Ingredient ingredient) in Foods)
            {
                GameObject prefab = BuildFoodItem(food, ingredient);
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

        // ── YİYECEK EŞYASI ──────────────────────────────────────────────────

        /// <summary>
        /// Art yiyeceğinden mutfak eşyası: kök KitchenItem (art prefab'ı iç içe), segmentler Slice_01..N (üstten),
        /// her kesimde sıradaki dilimin kendi eşya prefab'ı çıkıyor. Dilimler malzeme olarak kaseye giriyor.
        /// </summary>
        static GameObject BuildFoodItem(string food, Ingredient ingredient)
        {
            var art = AssetDatabase.LoadAssetAtPath<GameObject>($"{FoodFolder}/{food}.prefab");
            if (art == null) return null;

            string id = food.Replace("PF_", "").Replace("_01", "");

            // Dilimler: art'taki yerleri ve yönleriyle, tabanı eşyanın kökünde.
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(art);
            Transform tempSlices = temp.transform.Find("Slices");
            if (tempSlices == null || temp.transform.Find("Whole") == null)
            {
                Debug.LogWarning($"[Dishaster] {food}: Whole/Slices yok (YIYECEKLER.md yapısı bekleniyor).");
                Object.DestroyImmediate(temp);
                return null;
            }

            tempSlices.gameObject.SetActive(true);
            var sliceNames = new System.Collections.Generic.List<string>();
            var slicePrefabs = new System.Collections.Generic.List<KitchenItem>();
            foreach (Transform slice in Sorted(tempSlices))
            {
                var filter = slice.GetComponentInChildren<MeshFilter>(true);
                var meshRenderer = slice.GetComponentInChildren<MeshRenderer>(true);
                if (filter == null || meshRenderer == null) continue;

                Bounds bounds = meshRenderer.bounds;
                var root = new GameObject($"Art_{id}_{slice.name}");
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                body.AddComponent<MeshRenderer>().sharedMaterials = meshRenderer.sharedMaterials;
                body.transform.SetPositionAndRotation(filter.transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), filter.transform.rotation);
                body.transform.localScale = filter.transform.lossyScale;

                var piece = root.AddComponent<KitchenIngredientItem>();
                piece.ingredient = ingredient;
                piece.amount = 1f;

                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.015f;
                GameObject saved = KitchenCounterInstaller.SaveItem(root, KitchenItemKind.Generic, Mathf.Max(radius, 0.04f), Mathf.Max(bounds.size.y, 0.01f), 0.03f);
                slicePrefabs.Add(saved.GetComponent<KitchenItem>());
                sliceNames.Add(slice.name);
            }

            Object.DestroyImmediate(temp);

            // Bütün hal.
            var wrapper = new GameObject($"Art_{id}");
            var nested = (GameObject)PrefabUtility.InstantiatePrefab(art);
            nested.transform.SetParent(wrapper.transform, false);
            Bounds whole = WorldBounds(nested);
            float grab = Mathf.Max(whole.extents.x, whole.extents.z) + 0.03f;
            GameObject item = KitchenCounterInstaller.SaveItem(wrapper, KitchenItemKind.Generic, grab, whole.size.y, 0.03f);

            // Kaydedilen prefab'ın içindeki objelere bağla (aynı isimlerle).
            var kitchenItem = item.GetComponent<KitchenItem>();
            Transform artRoot = item.transform.GetChild(0);
            kitchenItem.whole = artRoot.Find("Whole").gameObject;
            kitchenItem.slicesRoot = artRoot.Find("Slices").gameObject;
            kitchenItem.segments = sliceNames.Select(name => kitchenItem.slicesRoot.transform.Find(name).gameObject).ToArray();
            kitchenItem.slicePrefabs = slicePrefabs.ToArray();
            PrefabUtility.SavePrefabAsset(item);
            return item;
        }

        static System.Collections.Generic.IEnumerable<Transform> Sorted(Transform parent)
        {
            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in parent) children.Add(child);
            return children.OrderBy(child => child.name, System.StringComparer.Ordinal);
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
