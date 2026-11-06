using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Mutfak eşyası prefab'larını, onları taşıyan Counter prefab'ını oluşturur ve Game sahnesine koyar.
    /// Version değişince kendiliğinden yeniden kurar. Eşyaların origin'i tabanında, böylece bir yüzeye koymak
    /// için y'si yüzey yüksekliği olması yetiyor. Ürünler kilerde (bkz. PantryInstaller).
    /// </summary>
    [InitializeOnLoad]
    public static class KitchenCounterInstaller
    {
        const string Version = "5";
        const string VersionFile = "ProjectSettings/DishasterKitchenCounter.version";
        const string OldTablePath = "Assets/_Dishaster/Prefabs/Table.prefab";
        const string CounterPath = "Assets/_Dishaster/Prefabs/Counter.prefab";
        internal const string ItemsFolder = "Assets/_Dishaster/Prefabs/Items";
        const string OldCupPath = ItemsFolder + "/Cup.prefab";
        const string MeshFolder = "Assets/_Dishaster/Meshes";
        const string GlassShaderPath = "Assets/_Dishaster/Shaders/Glass.shader";

        const float CounterWidth = 1.8f;
        const float CounterDepth = 0.9f;
        const float CounterHeight = 0.8f;
        const float TopThickness = 0.05f;
        const float CabinetInset = 0.03f;

        static readonly Vector2 BoardSize = new Vector2(0.5f, 0.3f);
        const float BoardHeight = 0.02f;
        const float BoardZ = -0.2f;

        // Trigger olduğu için oyuncuyu durdurmuyor; el ışını için. İnce eşyalarda da tutulabilsin.
        const float MinColliderSize = 0.03f;

        static KitchenCounterInstaller()
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

        [MenuItem("Tools/Dishaster/3 Install Kitchen Counter", priority = 3)]
        static void Install()
        {
            // Eski masa taşınıyor: GUID aynı kaldığı için sahnedeki kopya tezgaha dönüşüyor.
            if (File.Exists(OldTablePath) && !File.Exists(CounterPath))
                AssetDatabase.MoveAsset(OldTablePath, CounterPath);

            BuildCounterPrefab();

            // Kahve fincanının yerini 1 L'lik ölçü bardağı aldı.
            if (File.Exists(OldCupPath)) AssetDatabase.DeleteAsset(OldCupPath);

            if (GameScenePrefabs.EnsurePlaced(CounterPath, Vector3.zero, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde tezgah yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(CounterPath)) BuildCounterPrefab();
            return GameScenePrefabs.EnsurePlaced(CounterPath, Vector3.zero, Quaternion.identity);
        }

        // ── TEZGAH ──────────────────────────────────────────────────────────

        static void BuildCounterPrefab()
        {
            Directory.CreateDirectory(ItemsFolder);
            AssetDatabase.Refresh();

            var root = new GameObject("Counter");

            // Oyuncu tezgahın içinden geçmesin diye tek kutu.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, CounterHeight / 2f, 0f);
            collider.size = new Vector3(CounterWidth, CounterHeight, CounterDepth);

            Material stone = DishasterSceneBuilder.GetMaterial("M_Worktop", new Color(0.72f, 0.71f, 0.68f));
            Material cabinet = DishasterSceneBuilder.GetMaterial("M_Cabinet", new Color(0.42f, 0.40f, 0.38f));

            float cabinetHeight = CounterHeight - TopThickness;
            AddPart(root.transform, PrimitiveType.Cube, "Top", new Vector3(0f, CounterHeight - TopThickness / 2f, 0f), new Vector3(CounterWidth, TopThickness, CounterDepth), stone);
            AddPart(root.transform, PrimitiveType.Cube, "Cabinet", new Vector3(0f, cabinetHeight / 2f, 0f), new Vector3(CounterWidth - CabinetInset * 2f, cabinetHeight, CounterDepth - CabinetInset * 2f), cabinet);

            var board = new GameObject("CuttingBoard");
            board.transform.SetParent(root.transform, false);
            board.transform.localPosition = new Vector3(0f, CounterHeight, BoardZ);
            AddPart(board.transform, PrimitiveType.Cube, "Wood", new Vector3(0f, BoardHeight / 2f, 0f), new Vector3(BoardSize.x, BoardHeight, BoardSize.y), DishasterSceneBuilder.GetMaterial("M_CuttingBoard", new Color(0.50f, 0.33f, 0.18f)));

            var counter = root.AddComponent<KitchenCounter>();
            LowVisionOutlineInstaller.MarkCounter(root);
            counter.topHeight = CounterHeight;
            counter.topSize = new Vector2(CounterWidth, CounterDepth);
            counter.cuttingBoard = board.transform;
            counter.boardSize = BoardSize;
            counter.boardHeight = BoardHeight;

            PlaceItem(root.transform, BuildMeasuringCup(), new Vector3(-0.62f, CounterHeight, 0.25f), 0f);
            PlaceItem(root.transform, BuildBowl(), new Vector3(-0.5f, CounterHeight, -0.15f), 0f);
            PlaceItem(root.transform, BuildKnife(), new Vector3(0.62f, CounterHeight, 0.18f), 60f);
            PlaceItem(root.transform, BuildSaltShaker(), new Vector3(0.66f, CounterHeight, -0.22f), 0f);

            PrefabUtility.SaveAsPrefabAsset(root, CounterPath);
            Object.DestroyImmediate(root);
        }

        internal static void PlaceItem(Transform parent, GameObject itemPrefab, Vector3 position, float yaw)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ── EŞYALAR ─────────────────────────────────────────────────────────

        // 1 L: iç çapı 11 cm, yüksekliği 11 cm. Cam saydam, diğerleri seviyeyi dışarıdan görüyor.
        static GameObject BuildMeasuringCup()
        {
            var root = new GameObject("MeasuringCup");
            Material plastic = DishasterSceneBuilder.GetMaterial("M_CupHandle", new Color(0.30f, 0.32f, 0.36f));

            AddPart(root.transform, PrimitiveType.Cylinder, "Glass", new Vector3(0f, 0.062f, 0f), new Vector3(0.12f, 0.058f, 0.12f), GetGlassMaterial());
            AddPart(root.transform, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.003f, 0f), new Vector3(0.124f, 0.003f, 0.124f), plastic);
            AddPart(root.transform, PrimitiveType.Cube, "Handle", new Vector3(0.075f, 0.07f, 0f), new Vector3(0.02f, 0.08f, 0.016f), plastic);
            Transform fill = AddPart(root.transform, PrimitiveType.Cylinder, "Fill", new Vector3(0f, 0.06f, 0f), new Vector3(0.11f, 0.055f, 0.11f), FillMaterial());

            AddCupMarks(root.transform, CupFillBottom, CupFillTop, 1000f);

            KitchenContainer container = AddContainer(root, 1000f, true, true, 200f);
            container.displayName = "Ölçü bardağı";
            container.fill = fill;
            container.fillBottom = CupFillBottom;
            container.fillTop = CupFillTop;
            container.fillRadiusBottom = 0.054f;
            container.fillRadiusTop = 0.054f;

            return SaveItem(root, KitchenItemKind.Generic, 0.1f, 0.12f, 0.06f);
        }

        const float CupFillBottom = 0.006f;
        const float CupFillTop = 0.116f;
        const float CupMarkRadius = 0.0606f;

        // Camın çevresinde üç sütun (her yönden biri okunuyor): her 100 ml'de koyu çizgi ve rakam, 500 ve 1000'de uzun çizgi.
        // Seviye içerikle aynı ölçekte (fillBottom..fillTop = 0..kapasite), çizgiyle dolum tam denk geliyor.
        static void AddCupMarks(Transform parent, float bottom, float top, float capacity)
        {
            Material mark = DishasterSceneBuilder.GetMaterial("M_CupMark", new Color(0.12f, 0.22f, 0.55f));
            mark.color = new Color(0.08f, 0.08f, 0.10f);
            EditorUtility.SetDirty(mark);
            float[] columns = { 0f, 120f, 240f };

            foreach (float column in columns)
            {
                for (int ml = 100; ml <= capacity; ml += 100)
                {
                    float y = bottom + (top - bottom) * ml / capacity;
                    float length = ml % 500 == 0 ? 0.03f : 0.018f;

                    Transform tick = AddPart(parent, PrimitiveType.Cube, $"Mark{column}_{ml}", OnGlass(column, y), new Vector3(length, 0.0026f, 0.0015f), mark);
                    tick.localRotation = FacingOut(column);

                    AddMarkLabel(parent, ml.ToString(), column - 30f, y, mark.color);
                }
            }
        }

        static Vector3 OnGlass(float angle, float y) => Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, CupMarkRadius) + Vector3.up * y;

        // Yazının önü içe bakıyor: dışarıdan okunuyor.
        static Quaternion FacingOut(float angle) => Quaternion.Euler(0f, angle + 180f, 0f);

        static void AddMarkLabel(Transform parent, string text, float angle, float y, Color color)
        {
            var label = new GameObject("Label" + text).AddComponent<TextMeshPro>();
            label.transform.SetParent(parent, false);
            label.transform.localPosition = OnGlass(angle, y);
            label.transform.localRotation = FacingOut(angle);
            label.rectTransform.sizeDelta = new Vector2(0.04f, 0.012f);
            label.text = text;
            label.fontSize = 0.1f;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.ForceMeshUpdate();
        }

        // Hamur bu kasede hazırlanıyor. Ağzı geniş, içi görünüyor; içerik sadece yüzeyi olarak çiziliyor.
        static GameObject BuildBowl()
        {
            var root = new GameObject("MixingBowl");

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = BuildBowlMesh();
            body.AddComponent<MeshRenderer>().sharedMaterial = DishasterSceneBuilder.GetMaterial("M_Bowl", new Color(0.78f, 0.82f, 0.86f));

            Transform fill = AddPart(root.transform, PrimitiveType.Cylinder, "Fill", new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.001f, 0.2f), FillMaterial());

            KitchenContainer container = AddContainer(root, 2500f, false, true, 0f);
            container.displayName = "Kase";
            container.isMixingBowl = true;
            container.fill = fill;
            container.fillIsDisc = true;
            container.fillBottom = 0.014f;
            container.fillTop = 0.095f;
            container.fillRadiusBottom = 0.058f;
            container.fillRadiusTop = 0.118f;

            return SaveItem(root, KitchenItemKind.Generic, 0.14f, 0.1f, 0.1f);
        }

        // Profil (yarıçap, yükseklik): dıştan alttan ağza, içten ağızdan dibe. Her parça kendi kenarlarıyla.
        static Mesh BuildBowlMesh()
        {
            Vector2[] profile =
            {
                new Vector2(0f, 0f), new Vector2(0.062f, 0f), new Vector2(0.13f, 0.1f), new Vector2(0.122f, 0.1f),
                new Vector2(0.056f, 0.012f), new Vector2(0f, 0.012f),
            };

            const int sides = 28;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int segment = 0; segment < profile.Length - 1; segment++)
            {
                Vector2 a = profile[segment];
                Vector2 b = profile[segment + 1];
                int start = vertices.Count;

                for (int side = 0; side <= sides; side++)
                {
                    float angle = 2f * Mathf.PI * side / sides;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);
                    vertices.Add(new Vector3(a.x * cos, a.y, a.x * sin));
                    vertices.Add(new Vector3(b.x * cos, b.y, b.x * sin));
                }

                for (int side = 0; side < sides; side++)
                {
                    int i = start + side * 2;
                    triangles.AddRange(new[] { i, i + 1, i + 3, i, i + 3, i + 2 });
                }
            }

            var mesh = new Mesh { name = "Bowl" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Directory.CreateDirectory(MeshFolder);
            string path = MeshFolder + "/Bowl.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material GetGlassMaterial()
        {
            const string path = "Assets/_Dishaster/Materials/M_Glass.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(GlassShaderPath));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = new Color(0.85f, 0.92f, 1f, 0.22f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>İçerik rengi oyunda malzemeye göre veriliyor.</summary>
        internal static Material FillMaterial() => DishasterSceneBuilder.GetMaterial("M_Fill", Color.white);

        /// <summary>Kap bileşeni; dökülebiliyorsa ağzından akan ince silindir de ekleniyor (dökerken açılıyor).</summary>
        internal static KitchenContainer AddContainer(GameObject root, float capacity, bool canPour, bool canReceive, float pourRate)
        {
            var container = root.AddComponent<KitchenContainer>();
            container.capacity = capacity;
            container.canPour = canPour;
            container.canReceive = canReceive;
            container.pourRate = pourRate;

            if (canPour)
            {
                Transform stream = AddPart(root.transform, PrimitiveType.Cylinder, "Stream", Vector3.zero, new Vector3(0.012f, 0.15f, 0.012f), DishasterSceneBuilder.GetMaterial("M_Stream", Color.white));
                stream.gameObject.SetActive(false);
                container.stream = stream;
            }

            return container;
        }

        /// <summary>İçi dolu, dökülebilen kap (süt kutusu, un torbası).</summary>
        internal static GameObject SaveSourceItem(GameObject root, Ingredient ingredient, float amount, float pourRate, float grabRadius, float height, float holdForward)
        {
            KitchenContainer container = AddContainer(root, amount, true, false, pourRate);
            container.startIngredient = ingredient;
            container.startAmount = amount;
            return SaveItem(root, KitchenItemKind.Generic, grabRadius, height, holdForward);
        }

        static GameObject BuildKnife()
        {
            var root = new GameObject("Knife");
            AddPart(root.transform, PrimitiveType.Cube, "Blade", new Vector3(0f, 0.007f, 0.07f), new Vector3(0.04f, 0.014f, 0.18f), DishasterSceneBuilder.GetMaterial("M_KnifeBlade", new Color(0.75f, 0.76f, 0.78f)));
            AddPart(root.transform, PrimitiveType.Cube, "Handle", new Vector3(0f, 0.015f, -0.08f), new Vector3(0.035f, 0.03f, 0.12f), DishasterSceneBuilder.GetMaterial("M_KnifeHandle", new Color(0.12f, 0.10f, 0.09f)));

            GameObject prefab = SaveItem(root, KitchenItemKind.Knife, 0.16f, 0.03f, 0.09f);

            // Elde tutulunca bakılan noktaya ağzının ortası denk geliyor.
            prefab.GetComponent<KitchenItem>().reachBack = 0.16f;
            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
        }

        static GameObject BuildSaltShaker()
        {
            var root = new GameObject("SaltShaker");
            AddPart(root.transform, PrimitiveType.Cylinder, "Body", new Vector3(0f, 0.06f, 0f), new Vector3(0.07f, 0.06f, 0.07f), DishasterSceneBuilder.GetMaterial("M_SaltBody", new Color(0.92f, 0.92f, 0.90f)));
            AddPart(root.transform, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 0.135f, 0f), new Vector3(0.07f, 0.015f, 0.07f), DishasterSceneBuilder.GetMaterial("M_SaltCap", new Color(0.45f, 0.46f, 0.48f)));
            return SaveItem(root, KitchenItemKind.Generic, 0.08f, 0.15f, 0.04f);
        }

        /// <summary>KitchenItem ve el ışını için görünen hacme oturan trigger ekleyip prefab olarak kaydeder.</summary>
        internal static GameObject SaveItem(GameObject root, KitchenItemKind kind, float grabRadius, float height, float holdForward)
        {
            // Kap ya da malzeme bileşeni eklendiyse KitchenItem zaten gelmiş (RequireComponent).
            if (!root.TryGetComponent(out KitchenItem item)) item = root.AddComponent<KitchenItem>();
            item.kind = kind;
            item.grabRadius = grabRadius;
            item.height = height;
            item.holdForward = holdForward;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer part in renderers)
                bounds.Encapsulate(part.bounds);

            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = bounds.center;
            trigger.size = Vector3.Max(bounds.size, Vector3.one * MinColliderSize);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ItemsFolder}/{root.name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // Görsel parça: çarpışma yok, onu üstündeki kutu yapıyor.
        internal static Transform AddPart(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part.transform;
        }
    }
}
