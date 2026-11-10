using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Counter prefab'ı: iş tezgahı art'ın adası (PF_Ada_200, 2 x 1,5 m, önü oyunculara), üstünde art'ın kesme
    /// tahtası, bıçağı, karıştırma kasesi, ekmek ve 1 L ölçü bardağı. Eşya prefab'larını kurar, Game sahnesine koyar;
    /// Version değişince kendiliğinden yeniden kurar. Eşyaların origin'i tabanında, böylece bir yüzeye koymak için
    /// y'si yüzey yüksekliği olması yetiyor. Tezgah ve tahta yükseklikleri mesh'ten ölçülüyor.
    /// </summary>
    [InitializeOnLoad]
    public static class KitchenCounterInstaller
    {
        const string Version = "6";
        const string VersionFile = "ProjectSettings/DishasterKitchenCounter.version";
        const string OldTablePath = "Assets/_Dishaster/Prefabs/Table.prefab";
        const string CounterPath = "Assets/_Dishaster/Prefabs/Counter.prefab";
        internal const string ItemsFolder = "Assets/_Dishaster/Prefabs/Items";
        const string OldCupPath = ItemsFolder + "/Cup.prefab";
        const string GlassShaderPath = "Assets/_Dishaster/Shaders/Glass.shader";

        // Adanın üstünde (kökü adanın ortası; -z oyuncular tarafı). Tahta öne-sağa, kase ve bardak sola.
        static readonly Vector2 BoardSpot = new Vector2(0.32f, -0.3f);
        static readonly Vector2 CupSpot = new Vector2(-0.78f, -0.38f);
        static readonly Vector2 BowlSpot = new Vector2(-0.4f, -0.2f);
        static readonly Vector2 KnifeSpot = new Vector2(0.32f, 0.2f);
        static readonly Vector2 BreadSpot = new Vector2(-0.45f, 0.38f);

        // Art'ın aletleri gerçeğin ~1,5 katı (ARACLAR.md): ölçü bardağı da aynı ölçekte, 1 L yine 1 L.
        const float CupScale = 1.5f;

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

            bool built = BuildCounterPrefab();

            // Kahve fincanının yerini 1 L'lik ölçü bardağı aldı.
            if (File.Exists(OldCupPath)) AssetDatabase.DeleteAsset(OldCupPath);

            if (!built) return;
            if (GameScenePrefabs.EnsurePlaced(CounterPath, KitchenLayout.IslandCenter, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde tezgah yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(CounterPath)) BuildCounterPrefab();
            return GameScenePrefabs.EnsurePlaced(CounterPath, KitchenLayout.IslandCenter, Quaternion.identity);
        }

        // ── TEZGAH ──────────────────────────────────────────────────────────

        static bool BuildCounterPrefab()
        {
            GameObject island = KitchenLayout.Art(KitchenLayout.FurnitureArt + "PF_Ada_200");
            GameObject boardArt = KitchenLayout.Art(KitchenLayout.ToolsArt + "PF_CuttingBoard_01");
            if (island == null || boardArt == null)
            {
                Debug.LogWarning("[Dishaster] Tezgah kurulamadı: ada ya da kesme tahtası art'ı yok.");
                return false;
            }

            Directory.CreateDirectory(ItemsFolder);
            AssetDatabase.Refresh();

            var root = new GameObject("Counter");

            // Ada: önü (dolaplı sıra) oyunculara (-z), ortası kökte.
            GameObject body = KitchenLayout.Instantiate(island, root.transform);
            body.name = "Island";
            body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Bounds bounds = KitchenLayout.MeshBounds(body.transform, root.transform);
            body.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            KitchenLayout.Paint(body, MobilyaRengi.Renk.Adacayi);
            bounds = KitchenLayout.MeshBounds(body.transform, root.transform);

            // Oyuncu adanın içinden geçmesin diye tek kutu; görmeyen gövdenin hatlarını uzaktan görüyor.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            KitchenLayout.Outline(body, LowVisionOutlineType.Table);

            float top = KitchenLayout.SurfaceBelow(body, root.transform.position + Vector3.up * 2f, 3f, out Vector3 surface)
                ? surface.y - root.transform.position.y
                : bounds.max.y;

            // Kesme tahtası: tabanı tezgahta, üst yüzü kesilen yer.
            var board = new GameObject("CuttingBoard");
            board.transform.SetParent(root.transform, false);
            board.transform.localPosition = new Vector3(BoardSpot.x, top, BoardSpot.y);
            GameObject boardModel = KitchenLayout.Instantiate(boardArt, board.transform);
            Bounds boardBounds = KitchenLayout.MeshBounds(boardModel.transform, board.transform);
            boardModel.transform.localPosition = new Vector3(-boardBounds.center.x, -boardBounds.min.y, -boardBounds.center.z);
            boardBounds = KitchenLayout.MeshBounds(boardModel.transform, board.transform);

            var counter = root.AddComponent<KitchenCounter>();
            counter.topHeight = top;
            counter.topSize = new Vector2(bounds.size.x - 0.04f, bounds.size.z - 0.04f);
            counter.cuttingBoard = board.transform;
            counter.boardSize = new Vector2(boardBounds.size.x, boardBounds.size.z);
            counter.boardHeight = boardBounds.max.y;

            PlaceItem(root.transform, BuildMeasuringCup(), new Vector3(CupSpot.x, top, CupSpot.y), 30f);
            PlaceItem(root.transform, BuildBowl(), new Vector3(BowlSpot.x, top, BowlSpot.y), 0f);
            GameObject knife = BuildKnife();
            if (knife != null) PlaceItem(root.transform, knife, new Vector3(KnifeSpot.x, top, KnifeSpot.y), 90f);
            GameObject bread = FoodShowcaseInstaller.FoodItem("PF_Bread_Loaf_01");
            if (bread != null) PlaceItem(root.transform, bread, new Vector3(BreadSpot.x, top, BreadSpot.y), 15f);

            PrefabUtility.SaveAsPrefabAsset(root, CounterPath);
            Object.DestroyImmediate(root);
            return true;
        }

        internal static void PlaceItem(Transform parent, GameObject itemPrefab, Vector3 position, float yaw)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ── EŞYALAR ─────────────────────────────────────────────────────────

        // 1 L (art ölçeğinde 1,5 kat: iç çapı 16,5 cm). Cam saydam, diğerleri seviyeyi dışarıdan görüyor.
        static GameObject BuildMeasuringCup()
        {
            const float S = CupScale;
            var root = new GameObject("MeasuringCup");
            Material plastic = DishasterSceneBuilder.GetMaterial("M_CupHandle", new Color(0.30f, 0.32f, 0.36f));

            AddPart(root.transform, PrimitiveType.Cylinder, "Glass", new Vector3(0f, 0.062f, 0f) * S, new Vector3(0.12f, 0.058f, 0.12f) * S, GetGlassMaterial());
            AddPart(root.transform, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.003f, 0f) * S, new Vector3(0.124f, 0.003f, 0.124f) * S, plastic);
            AddPart(root.transform, PrimitiveType.Cube, "Handle", new Vector3(0.075f, 0.07f, 0f) * S, new Vector3(0.02f, 0.08f, 0.016f) * S, plastic);
            AddPart(root.transform, PrimitiveType.Cube, "HandleTop", new Vector3(0.068f, 0.108f, 0f) * S, new Vector3(0.016f, 0.008f, 0.016f) * S, plastic);
            AddPart(root.transform, PrimitiveType.Cube, "HandleBottom", new Vector3(0.068f, 0.032f, 0f) * S, new Vector3(0.016f, 0.008f, 0.016f) * S, plastic);
            Transform fill = AddPart(root.transform, PrimitiveType.Cylinder, "Fill", new Vector3(0f, 0.06f, 0f) * S, new Vector3(0.11f, 0.055f, 0.11f) * S, FillMaterial());

            AddCupMarks(root.transform, CupFillBottom, CupFillTop, 1000f);

            KitchenContainer container = AddContainer(root, 1000f, true, true, 200f);
            container.displayName = "Ölçü bardağı";
            container.fill = fill;
            container.fillBottom = CupFillBottom;
            container.fillTop = CupFillTop;
            container.fillRadiusBottom = 0.054f * S;
            container.fillRadiusTop = 0.054f * S;

            return SaveItem(root, KitchenItemKind.Generic, 0.1f * S, 0.12f * S, 0.06f * S);
        }

        const float CupFillBottom = 0.006f * CupScale;
        const float CupFillTop = 0.116f * CupScale;
        const float CupMarkRadius = 0.0606f * CupScale;

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
                    float length = (ml % 500 == 0 ? 0.03f : 0.018f) * CupScale;

                    Transform tick = AddPart(parent, PrimitiveType.Cube, $"Mark{column}_{ml}", OnGlass(column, y), new Vector3(length, 0.0026f * CupScale, 0.0015f), mark);
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
            label.rectTransform.sizeDelta = new Vector2(0.04f, 0.012f) * CupScale;
            label.text = text;
            label.fontSize = 0.1f * CupScale;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.ForceMeshUpdate();
        }

        // Hamur bu kasede hazırlanıyor (art'ın karıştırma kasesi). Ağzı geniş, içi görünüyor; içerik sadece yüzeyi
        // olarak çiziliyor: seviye ve yarıçap kasenin kutusundan (iç profil yaklaşık).
        static GameObject BuildBowl()
        {
            GameObject art = KitchenLayout.Art(KitchenLayout.ToolsArt + "PF_MixingBowl_01");
            GameObject root = art != null ? KitchenLayout.WrapOnBase("MixingBowl", art, Quaternion.identity) : new GameObject("MixingBowl");
            Bounds bounds = KitchenLayout.MeshBounds(root.transform, root.transform);
            float rim = Mathf.Max(bounds.extents.x, bounds.extents.z);
            float height = bounds.size.y;

            Transform fill = AddPart(root.transform, PrimitiveType.Cylinder, "Fill", new Vector3(0f, height * 0.3f, 0f), new Vector3(rim, 0.001f, rim), FillMaterial());

            KitchenContainer container = AddContainer(root, 2500f, false, true, 0f);
            container.displayName = "Kase";
            container.isMixingBowl = true;
            container.fill = fill;
            container.fillIsDisc = true;
            container.fillBottom = height * 0.12f;
            container.fillTop = height * 0.9f;
            container.fillRadiusBottom = rim * 0.45f;
            container.fillRadiusTop = rim * 0.9f;

            return SaveItem(root, KitchenItemKind.Generic, rim + 0.02f, height, rim);
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

        // Art'ın bıçağı: pivotu sapın ortası, uzun ekseni x. Eşyanın +z'si ağız (tutuş ve kesme buna göre), ağız aşağı.
        static GameObject BuildKnife()
        {
            GameObject art = KitchenLayout.Art(KitchenLayout.ToolsArt + "PF_Knife_Chef_01");
            if (art == null) return null;

            // Ağız sapın hangi yanında: pivot sapta, kutunun büyük kısmı ağız tarafında.
            GameObject probe = KitchenLayout.Instantiate(art, null);
            Bounds artBounds = KitchenLayout.MeshBounds(probe.transform, probe.transform);
            Object.DestroyImmediate(probe);
            Quaternion bladeForward = Quaternion.Euler(0f, artBounds.center.x >= 0f ? -90f : 90f, 0f);

            GameObject root = KitchenLayout.WrapOnBase("Knife", art, bladeForward);
            Transform model = root.transform.GetChild(0);

            // El sapı tutuyor (HandGoals): sap art'ın pivotunda.
            var handle = new GameObject("Handle").transform;
            handle.SetParent(root.transform, false);
            handle.position = model.position;

            Bounds bounds = KitchenLayout.MeshBounds(root.transform, root.transform);
            float length = bounds.size.z;
            float handleZ = handle.localPosition.z;
            float bladeStart = handleZ + length * 0.18f;
            float bladeMiddle = (bladeStart + bounds.max.z) / 2f;

            // Avuç sapta: kök avucun önünde -sap kadar; bakılan noktaya ağzın ortası geliyor.
            float holdForward = -handleZ;
            GameObject prefab = SaveItem(root, KitchenItemKind.Knife, length * 0.45f, bounds.size.y, holdForward);
            prefab.GetComponent<KitchenItem>().reachBack = bladeMiddle + holdForward;
            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
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
