using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static Dishaster.EditorTools.KitchenLayout;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Kitchen prefab'ı: art'ın oda parçalarıyla 10 x 6 m mutfak (zemin, duvarlar, köşeler, arka duvarda evyenin
    /// üstünde pencere, ön duvarda kapı, tavan), arka duvar boyunca tezgah sırası (dolaplar, ocak ve davlumbaz,
    /// evye, üst dolaplar, raf), süs eşyaları (ocakta tencere ve tava, tezgahta tabak), ışıklar ve yansıma probu.
    /// Buzdolabı, ada (iş tezgahı) ve kiler ayrı prefab'lar (oyun eşyaları onlarda). Game sahnesine koyar;
    /// Version değişince kendiliğinden yeniden kurar. Eski kutu odanın üretilmiş malzemelerini de siler.
    /// </summary>
    [InitializeOnLoad]
    public static class KitchenInstaller
    {
        const string Version = "1";
        const string VersionFile = "ProjectSettings/DishasterKitchen.version";
        public const string PrefabPath = "Assets/_Dishaster/Prefabs/Kitchen.prefab";
        const string CeilingMeshPath = "Assets/_Dishaster/Meshes/KitchenCeiling.asset";
        const string CeilingMaterialPath = "Assets/ART/3D/Room/Materials/M_Room_Wall_Krem.mat";
        public const string SkyPath = "Assets/_Dishaster/Materials/M_Sky.mat";

        // Alt dolaplar adaçayı, üst dolaplar ve cihazlar krem (art paleti).
        const MobilyaRengi.Renk BaseColor = MobilyaRengi.Renk.Adacayi;
        const MobilyaRengi.Renk UpperColor = MobilyaRengi.Renk.Krem;

        static readonly Color LightColor = new Color(1f, 0.9f, 0.78f);
        static readonly Color SunColor = new Color(1f, 0.93f, 0.8f);

        static KitchenInstaller()
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

        [MenuItem("Tools/Dishaster/14 Install Kitchen Room", priority = 14)]
        static void Install()
        {
            if (!BuildPrefab()) return;

            DeleteObsoleteAssets();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath) && !BuildPrefab()) return false;
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity);
        }

        /// <summary>Menü ve lobi arka planı da bu odayı kullanıyor.</summary>
        public static GameObject EnsurePrefab()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>Pencereden görünen gökyüzü (Game, menü ve lobi sahneleri).</summary>
        public static Material Sky()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(material, SkyPath);
            }

            material.SetFloat("_SunSize", 0.03f);
            material.SetFloat("_AtmosphereThickness", 0.8f);
            material.SetColor("_SkyTint", new Color(0.55f, 0.62f, 0.72f));
            material.SetColor("_GroundColor", new Color(0.42f, 0.45f, 0.36f));
            material.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Kapalı mutfağın ortam ışığı ve gökyüzü (sahne ayarı; her sahne kurulurken çağrılıyor).</summary>
        public static void ApplyRenderSettings()
        {
            RenderSettings.skybox = Sky();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.60f, 0.57f);
            RenderSettings.ambientEquatorColor = new Color(0.47f, 0.44f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.25f, 0.22f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        static bool BuildPrefab()
        {
            string[] required =
            {
                RoomArt + "PF_Wall_100", RoomArt + "PF_Wall_050", RoomArt + "PF_Wall_Corner", RoomArt + "PF_Wall_Window_200",
                RoomArt + "PF_Wall_Door_150", RoomArt + "PF_Floor_100", FurnitureArt + "PF_Dolap_100", FurnitureArt + "PF_Dolap_050",
                FurnitureArt + "PF_Evye_100", FurnitureArt + "PF_Ocak_100", FurnitureArt + "PF_Davlumbaz_100",
                FurnitureArt + "PF_Duvar_Dolabi_100", FurnitureArt + "PF_Duvar_Rafi_150",
            };
            if (!HasArt(required))
            {
                Debug.LogWarning("[Dishaster] Mutfak kurulamadı: oda/mobilya art'ı eksik. Önce 'Dishaster > Art > Mobilyalari kur', " +
                                 "sonra 'Oda parcalarini kur' çalıştırılmalı; ardından Tools/Dishaster/14 Install Kitchen Room.");
                return false;
            }

            var root = new GameObject("Kitchen");
            BuildFloor(root.transform);
            BuildCeiling(root.transform);
            BuildWalls(root.transform);
            BuildBackRun(root.transform);
            BuildLights(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return true;
        }

        // ── ZEMİN VE TAVAN ──────────────────────────────────────────────────

        static void BuildFloor(Transform parent)
        {
            var floor = new GameObject("Floor").transform;
            floor.SetParent(parent, false);

            // Karonun kendi kutusu: her biri hücresinin köşesine, üst yüzü y=0'da.
            GameObject tile = Art(RoomArt + "PF_Floor_100");
            GameObject probe = Instantiate(tile, floor);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds bounds = MeshBounds(probe.transform, probe.transform);
            Object.DestroyImmediate(probe);

            int columns = Mathf.RoundToInt(HalfWidth * 2f);
            int rows = Mathf.RoundToInt(HalfDepth * 2f);
            for (int x = 0; x < columns; x++)
            {
                for (int z = 0; z < rows; z++)
                {
                    GameObject piece = Instantiate(tile, floor);
                    piece.name = $"Floor_{x}_{z}";
                    piece.transform.localPosition = new Vector3(-HalfWidth + x - bounds.min.x, -bounds.max.y, -HalfDepth + z - bounds.min.z);
                }
            }

            var ground = floor.gameObject.AddComponent<BoxCollider>();
            ground.center = new Vector3(0f, -0.1f, 0f);
            ground.size = new Vector3(HalfWidth * 2f + 0.4f, 0.2f, HalfDepth * 2f + 0.4f);
        }

        static void BuildCeiling(Transform parent)
        {
            var ceiling = new GameObject("Ceiling");
            ceiling.transform.SetParent(parent, false);
            ceiling.transform.localPosition = new Vector3(0f, WallHeight, 0f);
            ceiling.AddComponent<MeshFilter>().sharedMesh = CeilingMesh();

            var meshRenderer = ceiling.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CeilingMaterialPath);
            // Tek yüzlü: güneş tavandan sızmasın diye gölgesi iki yüzlü.
            meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

            var box = ceiling.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.1f, 0f);
            box.size = new Vector3(HalfWidth * 2f + 0.4f, 0.2f, HalfDepth * 2f + 0.4f);
        }

        // Aşağı bakan düz yüzey; UV metre (duvar boyası dokusu metrede bir tekrarlıyor).
        static Mesh CeilingMesh()
        {
            float w = HalfWidth + 0.2f;
            float d = HalfDepth + 0.2f;
            var mesh = new Mesh { name = "KitchenCeiling" };
            mesh.vertices = new[] { new Vector3(-w, 0f, -d), new Vector3(w, 0f, -d), new Vector3(w, 0f, d), new Vector3(-w, 0f, d) };
            mesh.uv = new[] { new Vector2(-w, -d), new Vector2(w, -d), new Vector2(w, d), new Vector2(-w, d) };
            mesh.normals = new[] { Vector3.down, Vector3.down, Vector3.down, Vector3.down };
            mesh.triangles = new[] { 0, 2, 3, 0, 1, 2 };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            Directory.CreateDirectory(Path.GetDirectoryName(CeilingMeshPath));
            AssetDatabase.DeleteAsset(CeilingMeshPath);
            AssetDatabase.CreateAsset(mesh, CeilingMeshPath);
            return mesh;
        }

        // ── DUVARLAR ────────────────────────────────────────────────────────

        static void BuildWalls(Transform parent)
        {
            var walls = new GameObject("Walls").transform;
            walls.SetParent(parent, false);

            GameObject full = Art(RoomArt + "PF_Wall_100");
            GameObject half = Art(RoomArt + "PF_Wall_050");
            GameObject window = Art(RoomArt + "PF_Wall_Window_200");
            GameObject door = Art(RoomArt + "PF_Wall_Door_150");

            // Kuzey: pencere evyenin üstünde (3.5-5.5). Güney: kapı x=-1..0.5 (doğma alanının arkası).
            Run(walls, Side.North, (full, 1f), (full, 1f), (full, 1f), (half, 0.5f), (window, 2f), (full, 1f), (full, 1f), (full, 1f), (full, 1f), (half, 0.5f));
            Run(walls, Side.South, (full, 1f), (full, 1f), (full, 1f), (full, 1f), (half, 0.5f), (door, 1.5f), (full, 1f), (full, 1f), (full, 1f), (full, 1f));
            Run(walls, Side.East, (full, 1f), (full, 1f), (full, 1f), (full, 1f), (full, 1f), (full, 1f));
            Run(walls, Side.West, (full, 1f), (full, 1f), (full, 1f), (full, 1f), (full, 1f), (full, 1f));

            // Köşeler: iki duvarın arkada bıraktığı boşluk, dış köşede.
            GameObject corner = Art(RoomArt + "PF_Wall_Corner");
            GameObject probe = Instantiate(corner, walls);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds bounds = MeshBounds(probe.transform, probe.transform);
            Object.DestroyImmediate(probe);

            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f;
                float sz = i < 2 ? -1f : 1f;
                // Dış karenin en küçük köşesi: x'te duvarın arkası ya da iç yüz, z'de aynı.
                float minX = sx > 0f ? HalfWidth : -HalfWidth - bounds.size.x;
                float minZ = sz > 0f ? HalfDepth : -HalfDepth - bounds.size.z;

                GameObject piece = Instantiate(corner, walls);
                piece.name = "Corner" + (i + 1);
                piece.transform.localPosition = new Vector3(minX - bounds.min.x, -bounds.min.y, minZ - bounds.min.z);
                AddBox(piece);
            }
        }

        static void Run(Transform parent, Side side, params (GameObject prefab, float width)[] pieces)
        {
            WallFrame frame = Frame(side);
            float cursor = 0f;
            for (int i = 0; i < pieces.Length; i++)
            {
                GameObject piece = PlaceAlong(parent, pieces[i].prefab, frame, cursor, pieces[i].width);
                cursor += pieces[i].width;
                if (piece == null) continue;

                piece.name = $"{side}_{i + 1:00}_{pieces[i].prefab.name.Replace("PF_", "")}";
                AddBox(piece);
                Outline(piece, LowVisionOutlineType.Wall);
                foreach (MeshRenderer meshRenderer in piece.GetComponentsInChildren<MeshRenderer>(true))
                    meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            }
        }

        // ── ARKA SIRA ───────────────────────────────────────────────────────

        static void BuildBackRun(Transform parent)
        {
            var run = new GameObject("BackCounters").transform;
            run.SetParent(parent, false);
            WallFrame frame = Frame(Side.North);

            GameObject cabinet = Art(FurnitureArt + "PF_Dolap_100");
            GameObject small = Art(FurnitureArt + "PF_Dolap_050");

            // 0-1 buzdolabı (ayrı prefab, FridgeInstaller).
            Base(run, cabinet, frame, 1f, 1f, MutfakModulu.TezgahTipi.Duz, MutfakModulu.OnYuzTipi.IkiKapak);
            GameObject stove = Appliance(run, Art(FurnitureArt + "PF_Ocak_100"), frame, 2f, 1f, LowVisionOutlineType.Table);
            Appliance(run, Art(FurnitureArt + "PF_Davlumbaz_100"), frame, 2f, 1f, LowVisionOutlineType.Wall);
            Base(run, cabinet, frame, 3f, 1f, MutfakModulu.TezgahTipi.Duz, MutfakModulu.OnYuzTipi.IkiCekmece);
            GameObject sink = PlaceAlong(run, Art(FurnitureArt + "PF_Evye_100"), frame, 4f, 1f);
            if (sink != null && sink.TryGetComponent(out MutfakModulu sinkModule))
            {
                sinkModule.onYuz = MutfakModulu.OnYuzTipi.IkiKapak;
                sinkModule.Uygula();
            }

            Finish(sink, BaseColor, LowVisionOutlineType.Table, "Sink");

            GameObject right = Base(run, cabinet, frame, 5f, 1f, MutfakModulu.TezgahTipi.Duz, MutfakModulu.OnYuzTipi.IkiKapak);
            Base(run, small, frame, 6f, 0.5f, MutfakModulu.TezgahTipi.SagCikinti, MutfakModulu.OnYuzTipi.SagMenteseKapak);

            // Üst dolaplar: pencerenin iki yanında (davlumbazın solu, pencerenin sağı).
            GameObject upper = Art(FurnitureArt + "PF_Duvar_Dolabi_100");
            Appliance(run, upper, frame, 1f, 1f, LowVisionOutlineType.Wall, UpperColor);
            Appliance(run, upper, frame, 5.5f, 1f, LowVisionOutlineType.Wall, UpperColor);

            // Boş duvarda raf: kaseler.
            GameObject shelf = PlaceAlong(run, Art(FurnitureArt + "PF_Duvar_Rafi_150"), frame, 7f, 1.5f);
            if (shelf != null)
            {
                shelf.name = "Shelf";
                if (shelf.TryGetComponent(out RafModulu rack))
                {
                    rack.katSayisi = 2;
                    rack.ilkKatYuksekligi = 1.35f;
                    rack.katArasi = 0.45f;
                    rack.Uygula();
                }

                AddBox(shelf);
                Outline(shelf, LowVisionOutlineType.Wall);
            }

            var decor = new GameObject("Decor").transform;
            decor.SetParent(parent, false);
            DecorateStove(decor, stove, frame);
            DecorateCounter(decor, right, frame);
            DecorateShelf(decor, shelf, frame);
        }

        static GameObject Base(Transform parent, GameObject prefab, WallFrame frame, float cursor, float width, MutfakModulu.TezgahTipi top, MutfakModulu.OnYuzTipi front)
        {
            GameObject instance = PlaceAlong(parent, prefab, frame, cursor, width);
            Module(instance, top, front);
            Finish(instance, BaseColor, LowVisionOutlineType.Table, null);
            return instance;
        }

        static GameObject Appliance(Transform parent, GameObject prefab, WallFrame frame, float cursor, float width, LowVisionOutlineType outline, MobilyaRengi.Renk color = UpperColor)
        {
            GameObject instance = PlaceAlong(parent, prefab, frame, cursor, width);
            Finish(instance, color, outline, null);
            return instance;
        }

        static void Finish(GameObject instance, MobilyaRengi.Renk color, LowVisionOutlineType outline, string name)
        {
            if (instance == null) return;
            if (name != null) instance.name = name;
            Paint(instance, color);
            AddBox(instance);
            Outline(instance, outline);
        }

        // ── SÜS (çarpışmasız: el ışını arkasındakine değiyor) ───────────────

        static void DecorateStove(Transform parent, GameObject stove, WallFrame frame)
        {
            if (stove == null) return;

            Transform left = FindDeep(stove.transform, "Goz_Sol");
            Transform right = FindDeep(stove.transform, "Goz_Sag");

            GameObject pot = Art(ToolsArt + "PF_Pot_Copper_01");
            if (left != null && pot != null && SurfaceBelow(stove, left.position + Vector3.up * 0.6f, 1f, out Vector3 potBase))
            {
                GameObject instance = Instantiate(pot, parent);
                instance.transform.SetPositionAndRotation(potBase, frame.rotation * Quaternion.Euler(0f, 90f, 0f));

                // Kapak tencerenin pivotundan 20,9 cm yukarıda (ARACLAR.md).
                GameObject lid = Instantiate(Art(ToolsArt + "PF_Pot_Copper_Lid_01"), parent);
                if (lid != null) lid.transform.SetPositionAndRotation(potBase + Vector3.up * 0.209f, instance.transform.rotation);
            }

            // Tavanın sapı sağa (evye tarafı), biraz öne dönük: tencereye çarpmıyor.
            GameObject pan = Art(ToolsArt + "PF_Pan_Copper_01");
            if (right != null && pan != null && SurfaceBelow(stove, right.position + Vector3.up * 0.6f, 1f, out Vector3 panBase))
            {
                GameObject instance = Instantiate(pan, parent);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Bounds bounds = MeshBounds(instance.transform, instance.transform);
                // Sap kökün +X'indeyse 180° çevir: art'ın -X'i çerçevede sağa bakıyor.
                Quaternion flip = bounds.center.x > 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                instance.transform.SetPositionAndRotation(panBase, frame.rotation * Quaternion.Euler(0f, -25f, 0f) * flip);
            }
        }

        static void DecorateCounter(Transform parent, GameObject cabinet, WallFrame frame)
        {
            if (cabinet == null) return;

            Vector3 plateSpot = Point(frame, 5.45f, 1.5f, 0.38f);
            GameObject plate = Art(ToolsArt + "PF_Plate_Serving_01");
            if (plate != null && SurfaceBelow(cabinet, plateSpot, 1f, out Vector3 plateBase))
                Instantiate(plate, parent).transform.SetPositionAndRotation(plateBase, frame.rotation);

            Vector3 spoonSpot = Point(frame, 5.85f, 1.5f, 0.42f);
            GameObject spoon = Art(ToolsArt + "PF_Spoon_Wooden_01");
            if (spoon != null && SurfaceBelow(cabinet, spoonSpot, 1f, out Vector3 spoonBase))
                Instantiate(spoon, parent).transform.SetPositionAndRotation(spoonBase + Vector3.up * 0.01f, frame.rotation * Quaternion.Euler(0f, 75f, 0f));
        }

        static void DecorateShelf(Transform parent, GameObject shelf, WallFrame frame)
        {
            GameObject bowl = Art(ToolsArt + "PF_Bowl_Soup_01");
            if (shelf == null || bowl == null) return;

            // Raf duvardan 10 cm önde, 35 cm derin: ortası 27,5 cm. Üst kat yukarıdan, alt kat üst katın altından.
            float[] cursors = { 7.4f, 7.85f, 8.3f };
            for (int i = 0; i < cursors.Length; i++)
            {
                Vector3 above = Point(frame, cursors[i], 2.7f, 0.275f);
                if (!SurfaceBelow(shelf, above, 2f, out Vector3 top)) continue;

                Vector3 spot = top;
                if (i == 1 && SurfaceBelow(shelf, new Vector3(above.x, top.y - 0.14f, above.z), 1.5f, out Vector3 lower)) spot = lower;
                Instantiate(bowl, parent).transform.SetPositionAndRotation(spot, frame.rotation * Quaternion.Euler(0f, 40f * i, 0f));
            }
        }

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        // ── IŞIK ────────────────────────────────────────────────────────────

        static void BuildLights(Transform parent)
        {
            var lights = new GameObject("Lights").transform;
            lights.SetParent(parent, false);

            // Güneş pencereden giriyor: evyenin önüne sıcak bir leke, gölgeler yumuşak.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(lights, false);
            sun.transform.rotation = Quaternion.Euler(35f, 195f, 0f);
            sun.type = LightType.Directional;
            sun.color = SunColor;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;

            Vector3[] points = { new Vector3(-2.6f, 2.45f, 1f), new Vector3(1.6f, 2.45f, 0.8f), new Vector3(0f, 2.45f, -1.7f) };
            for (int i = 0; i < points.Length; i++)
            {
                var light = new GameObject("Fill" + (i + 1)).AddComponent<Light>();
                light.transform.SetParent(lights, false);
                light.transform.localPosition = points[i];
                light.type = LightType.Point;
                light.range = 7f;
                light.intensity = 0.95f;
                light.color = LightColor;
                light.shadows = LightShadows.None;
            }

            // Bakır, çelik ve cam için (MOBILYA.md): oyun başında bir kez çekiliyor, fırın gerekmiyor.
            var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(lights, false);
            probe.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.size = new Vector3(HalfWidth * 2f, WallHeight, HalfDepth * 2f);
            probe.boxProjection = true;
            probe.resolution = 128;
        }

        // ── ESKİ ÜRETİLEN MALZEMELER ────────────────────────────────────────

        // Kutu oda, kutu tezgah, kutu buzdolabı ve kiler, çizilmiş ürünler: art geldi, hiçbiri kullanılmıyor.
        static readonly string[] Obsolete =
        {
            "Materials/M_Apple", "Materials/M_AppleFlesh", "Materials/M_AppleStem", "Materials/M_Avocado", "Materials/M_AvocadoFlesh",
            "Materials/M_BakingPowder", "Materials/M_Banana", "Materials/M_BananaFlesh", "Materials/M_BananaSlice", "Materials/M_BananaStem",
            "Materials/M_BerryBox", "Materials/M_Bowl", "Materials/M_Cabinet", "Materials/M_Ceiling", "Materials/M_Crate",
            "Materials/M_Cucumber", "Materials/M_CucumberFlesh", "Materials/M_Cup", "Materials/M_CupDrink", "Materials/M_CuttingBoard",
            "Materials/M_Door", "Materials/M_Egg", "Materials/M_EggTray", "Materials/M_Floor", "Materials/M_FlourBag",
            "Materials/M_Fridge", "Materials/M_FridgeHandle", "Materials/M_FridgeInside", "Materials/M_KnifeBlade", "Materials/M_KnifeHandle",
            "Materials/M_Lamp", "Materials/M_Lemon", "Materials/M_LemonFlesh", "Materials/M_MilkCarton", "Materials/M_MilkLabel",
            "Materials/M_SaltBody", "Materials/M_SaltCap", "Materials/M_Strawberry", "Materials/M_StrawberryFlesh", "Materials/M_StrawberryStem",
            "Materials/M_SugarJar", "Materials/M_SugarLid", "Materials/M_TableLeg", "Materials/M_TableTop", "Materials/M_Tomato",
            "Materials/M_TomatoFlesh", "Materials/M_TomatoStem", "Materials/M_Wall", "Materials/M_Worktop",
            "Meshes/Apple", "Meshes/Avocado", "Meshes/Banana", "Meshes/Bowl", "Meshes/Cucumber", "Meshes/Lemon", "Meshes/Strawberry", "Meshes/Tomato",
            "Prefabs/Items/Apple", "Prefabs/Items/AppleSlice", "Prefabs/Items/Avocado", "Prefabs/Items/AvocadoSlice", "Prefabs/Items/Banana",
            "Prefabs/Items/BananaSlice", "Prefabs/Items/Cucumber", "Prefabs/Items/CucumberSlice", "Prefabs/Items/Lemon", "Prefabs/Items/LemonSlice",
            "Prefabs/Items/Tomato", "Prefabs/Items/TomatoSlice", "Prefabs/Items/Strawberry", "Prefabs/Items/StrawberrySlice",
            "Prefabs/Items/Egg", "Prefabs/Items/FlourBag", "Prefabs/Items/SugarJar", "Prefabs/Items/BakingPowder", "Prefabs/Items/SaltShaker",
            "Prefabs/FoodShowcase",
        };

        internal static void DeleteObsoleteAssets()
        {
            foreach (string path in Obsolete)
            {
                string folder = path.StartsWith("Materials/") ? ".mat" : path.StartsWith("Meshes/") ? ".asset" : ".prefab";
                string full = "Assets/_Dishaster/" + path + folder;
                if (File.Exists(full)) AssetDatabase.DeleteAsset(full);
            }
        }
    }
}
