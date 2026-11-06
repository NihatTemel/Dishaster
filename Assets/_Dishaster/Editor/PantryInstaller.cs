using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Pantry prefab'ını (odanın güneybatı köşesinde kiler, kasalar, içlerinde ürünler, güney duvarında un, şeker ve
    /// kabartma tozu rafı) ve ürün prefab'larını
    /// oluşturur, Game sahnesine koyar. Version değişince kendiliğinden yeniden kurar. Her ürün dilim dilim
    /// mesh'lerden oluşuyor: kesilince uçtan bir parça kapanıyor, kalan yerde iç rengi görünüyor.
    /// </summary>
    [InitializeOnLoad]
    public static class PantryInstaller
    {
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterPantry.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/Pantry.prefab";
        const string MeshFolder = "Assets/_Dishaster/Meshes";

        const int ItemsPerCrate = 5;

        // Oda x=-9 ve z=-7'de bitiyor. Kuzey duvarı z=-1, doğu duvarında z=-3.4 ile -1 arası giriş.
        const float RoomWest = -9f;
        const float RoomSouth = -7f;
        const float PantryEast = -5f;
        const float PantryNorth = -1f;
        const float DoorSouth = -3.4f;
        const float WallHeight = 3f;
        const float WallThickness = 0.2f;

        const float StandDepth = 0.8f;
        const float StandHeight = 0.55f;
        const float CrateDepth = 0.5f;
        const float CrateLength = 0.7f;
        const float CrateRim = 0.1f;
        const float CrateTilt = -15f;
        const float CrateSpacing = 0.85f;
        const float FirstCrateZ = -6.3f;

        // Güney duvarı boyunca raf; kasaların standıyla çakışmıyor.
        static readonly Vector3 ShelfCenter = new Vector3(-6.65f, 0f, -6.72f);
        static readonly Vector3 ShelfSize = new Vector3(2.6f, 0.9f, 0.5f);

        const int MeshSides = 14;
        const int MeshRings = 4;

        internal class Food
        {
            public string name;
            public Color peel;
            public Color flesh;
            public float length;
            public float radius;
            public int slices;
            public float arcDegrees;
            public bool lengthwiseInCrate;
            public Color? stem;
            public Func<float, float> profile;
        }

        internal static float Round(float t) => Mathf.Sqrt(Mathf.Max(0f, 1f - Square(2f * t - 1f)));

        static float Square(float x) => x * x;

        static readonly Food[] Foods =
        {
            new Food
            {
                name = "Banana", peel = new Color(0.93f, 0.80f, 0.22f), flesh = new Color(0.96f, 0.92f, 0.72f),
                length = 0.22f, radius = 0.022f, slices = 8, arcDegrees = 70f, lengthwiseInCrate = true,
                stem = new Color(0.35f, 0.28f, 0.12f),
                profile = t => Mathf.Max(0.3f, Mathf.Sqrt(1f - Mathf.Pow(Mathf.Abs(2f * t - 1f), 4f))),
            },
            new Food
            {
                name = "Avocado", peel = new Color(0.16f, 0.26f, 0.12f), flesh = new Color(0.75f, 0.80f, 0.45f),
                length = 0.13f, radius = 0.042f, slices = 5,
                profile = t => Round(t) * (0.62f + 0.38f * t),
            },
            new Food
            {
                name = "Tomato", peel = new Color(0.80f, 0.14f, 0.10f), flesh = new Color(0.85f, 0.25f, 0.18f),
                length = 0.09f, radius = 0.045f, slices = 5,
                stem = new Color(0.15f, 0.40f, 0.12f),
                profile = Round,
            },
            new Food
            {
                name = "Cucumber", peel = new Color(0.14f, 0.36f, 0.14f), flesh = new Color(0.78f, 0.88f, 0.62f),
                length = 0.24f, radius = 0.025f, slices = 9, lengthwiseInCrate = true,
                profile = t => Mathf.Sqrt(1f - Mathf.Pow(Mathf.Abs(2f * t - 1f), 6f)),
            },
            // Domatesle aynı boyda ve yakın parlaklıkta: renk olmadan ayırt edilemesinler diye.
            new Food
            {
                name = "Apple", peel = new Color(0.22f, 0.52f, 0.16f), flesh = new Color(0.94f, 0.92f, 0.72f),
                length = 0.09f, radius = 0.045f, slices = 5,
                stem = new Color(0.25f, 0.16f, 0.08f),
                profile = Round,
            },
            new Food
            {
                name = "Lemon", peel = new Color(0.95f, 0.85f, 0.20f), flesh = new Color(0.95f, 0.92f, 0.55f),
                length = 0.11f, radius = 0.033f, slices = 5,
                profile = Round,
            },
        };

        static PantryInstaller()
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

        [MenuItem("Tools/Dishaster/4 Install Pantry", priority = 4)]
        static void Install()
        {
            BuildPrefab();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde kiler yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity);
        }

        // ── KİLER ───────────────────────────────────────────────────────────

        static void BuildPrefab()
        {
            Directory.CreateDirectory(KitchenCounterInstaller.ItemsFolder);
            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();

            var root = new GameObject("Pantry");
            Material wall = DishasterSceneBuilder.GetMaterial("M_Wall", new Color(0.78f, 0.74f, 0.66f));

            float width = PantryEast - RoomWest;
            AddSolid(root.transform, "WallNorth", new Vector3((RoomWest + PantryEast) / 2f, WallHeight / 2f, PantryNorth), new Vector3(width, WallHeight, WallThickness), wall).AddComponent<LowVisionOutline>().type = LowVisionOutlineType.Wall;
            AddSolid(root.transform, "WallEast", new Vector3(PantryEast, WallHeight / 2f, (RoomSouth + DoorSouth) / 2f), new Vector3(WallThickness, WallHeight, DoorSouth - RoomSouth), wall).AddComponent<LowVisionOutline>().type = LowVisionOutlineType.Wall;

            // Duvarlar oda lambalarını kesiyor; kilerin kendi ışığı var.
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3((RoomWest + PantryEast) / 2f, WallHeight - 0.25f, (RoomSouth + PantryNorth) / 2f);
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.95f, 0.86f);

            float standLength = PantryNorth - RoomSouth - 0.2f;
            float standZ = RoomSouth + standLength / 2f;
            float standX = RoomWest + StandDepth / 2f;
            AddSolid(root.transform, "Stand", new Vector3(standX, StandHeight / 2f, standZ), new Vector3(StandDepth, StandHeight, standLength), DishasterSceneBuilder.GetMaterial("M_TableLeg", new Color(0.36f, 0.24f, 0.15f)));

            for (int i = 0; i < Foods.Length; i++)
                BuildCrate(root.transform, Foods[i], new Vector3(standX, StandHeight, FirstCrateZ + CrateSpacing * i));

            BuildShelf(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        // Sırtı kaldırılmış kasa: içi odaya doğru eğik, dışarıdan rahat görünüyor.
        static void BuildCrate(Transform parent, Food food, Vector3 position)
        {
            GameObject foodPrefab = BuildFood(food);
            Material wood = DishasterSceneBuilder.GetMaterial("M_Crate", new Color(0.62f, 0.45f, 0.26f));

            var crate = new GameObject("Crate_" + food.name);
            crate.transform.SetParent(parent, false);
            crate.transform.localPosition = position + Vector3.up * (Mathf.Sin(-CrateTilt * Mathf.Deg2Rad) * CrateDepth / 2f);
            crate.transform.localRotation = Quaternion.Euler(0f, 0f, CrateTilt);

            KitchenCounterInstaller.AddPart(crate.transform, PrimitiveType.Cube, "Floor", new Vector3(0f, 0.01f, 0f), new Vector3(CrateDepth, 0.02f, CrateLength), wood);

            float rimY = CrateRim / 2f;
            KitchenCounterInstaller.AddPart(crate.transform, PrimitiveType.Cube, "Back", new Vector3(-CrateDepth / 2f, rimY, 0f), new Vector3(0.02f, CrateRim, CrateLength), wood);
            KitchenCounterInstaller.AddPart(crate.transform, PrimitiveType.Cube, "Front", new Vector3(CrateDepth / 2f, rimY * 0.6f, 0f), new Vector3(0.02f, CrateRim * 0.6f, CrateLength), wood);
            KitchenCounterInstaller.AddPart(crate.transform, PrimitiveType.Cube, "SideA", new Vector3(0f, rimY, -CrateLength / 2f), new Vector3(CrateDepth, CrateRim, 0.02f), wood);
            KitchenCounterInstaller.AddPart(crate.transform, PrimitiveType.Cube, "SideB", new Vector3(0f, rimY, CrateLength / 2f), new Vector3(CrateDepth, CrateRim, 0.02f), wood);

            for (int i = 0; i < ItemsPerCrate; i++)
            {
                Vector3 spot = food.lengthwiseInCrate
                    ? new Vector3(-0.16f + 0.08f * i, 0.02f, 0f)
                    : i < 3
                        ? new Vector3(-0.1f, 0.02f, -0.2f + 0.2f * i)
                        : new Vector3(0.1f, 0.02f, -0.1f + 0.2f * (i - 3));

                KitchenCounterInstaller.PlaceItem(crate.transform, foodPrefab, spot, food.lengthwiseInCrate ? 90f : 25f * i);
            }
        }

        // ── RAF: dökülebilen kuru malzemeler ─────────────────────────────────

        static void BuildShelf(Transform parent)
        {
            AddSolid(parent, "Shelf", ShelfCenter + Vector3.up * (ShelfSize.y / 2f), ShelfSize, DishasterSceneBuilder.GetMaterial("M_TableLeg", new Color(0.36f, 0.24f, 0.15f)));

            float top = ShelfSize.y;
            KitchenCounterInstaller.PlaceItem(parent, BuildFlourBag(), new Vector3(ShelfCenter.x - 0.8f, top, ShelfCenter.z), 0f);
            KitchenCounterInstaller.PlaceItem(parent, BuildSugarJar(), new Vector3(ShelfCenter.x, top, ShelfCenter.z), 0f);
            KitchenCounterInstaller.PlaceItem(parent, BuildBakingPowder(), new Vector3(ShelfCenter.x + 0.8f, top, ShelfCenter.z), 0f);
        }

        static GameObject BuildFlourBag()
        {
            var root = new GameObject("FlourBag");
            Material paper = DishasterSceneBuilder.GetMaterial("M_FlourBag", new Color(0.88f, 0.84f, 0.74f));
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.11f, 0f), new Vector3(0.14f, 0.22f, 0.09f), paper);
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cube, "Fold", new Vector3(0f, 0.23f, 0f), new Vector3(0.14f, 0.02f, 0.03f), paper);
            return KitchenCounterInstaller.SaveSourceItem(root, Ingredient.Flour, 2000f, 150f, 0.12f, 0.24f, 0.06f);
        }

        static GameObject BuildSugarJar()
        {
            var root = new GameObject("SugarJar");
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cylinder, "Body", new Vector3(0f, 0.08f, 0f), new Vector3(0.1f, 0.08f, 0.1f), DishasterSceneBuilder.GetMaterial("M_SugarJar", new Color(0.92f, 0.93f, 0.95f)));
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cylinder, "Lid", new Vector3(0f, 0.165f, 0f), new Vector3(0.104f, 0.008f, 0.104f), DishasterSceneBuilder.GetMaterial("M_SugarLid", new Color(0.55f, 0.35f, 0.25f)));
            return KitchenCounterInstaller.SaveSourceItem(root, Ingredient.Sugar, 1000f, 120f, 0.09f, 0.17f, 0.05f);
        }

        // Küçük kutu, yavaş akıyor: 100 ml'nin %20'si ancak dikkatle tutuyor.
        static GameObject BuildBakingPowder()
        {
            var root = new GameObject("BakingPowder");
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.06f, 0f), new Vector3(0.07f, 0.12f, 0.04f), DishasterSceneBuilder.GetMaterial("M_BakingPowder", new Color(0.85f, 0.25f, 0.20f)));
            return KitchenCounterInstaller.SaveSourceItem(root, Ingredient.BakingPowder, 400f, 80f, 0.07f, 0.12f, 0.04f);
        }

        static GameObject AddSolid(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
        }

        // ── ÜRÜN ────────────────────────────────────────────────────────────

        internal static GameObject BuildFood(Food food)
        {
            Material peel = DishasterSceneBuilder.GetMaterial("M_" + food.name, food.peel);
            Material flesh = DishasterSceneBuilder.GetMaterial("M_" + food.name + "Flesh", food.flesh);
            var materials = new[] { peel, flesh };

            Mesh[] meshes = BuildMeshes(food);

            var root = new GameObject(food.name);
            var segments = new GameObject[food.slices];
            var sliceScales = new float[food.slices];

            float halfArc = food.arcDegrees * Mathf.Deg2Rad / 2f;
            float arcRadius = halfArc > 0f ? food.length / (halfArc * 2f) : 0f;
            float middleOffset = arcRadius * (1f + Mathf.Cos(halfArc)) / 2f;
            float segmentLength = food.length / food.slices;
            float middleRadius = SegmentRadius(food, food.slices / 2);

            for (int i = 0; i < food.slices; i++)
            {
                // Düz ürünlerde de aynı hesap: yay yoksa x ekseninde dizili.
                float t = (i + 0.5f) / food.slices;
                float angle = Mathf.Lerp(halfArc, -halfArc, t);
                Vector3 position = halfArc > 0f
                    ? new Vector3(arcRadius * Mathf.Sin(angle), food.radius, middleOffset - arcRadius * Mathf.Cos(angle))
                    : new Vector3(food.length / 2f - food.length * t, food.radius, 0f);
                Quaternion along = Quaternion.FromToRotation(Vector3.up, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));

                var segment = new GameObject("Segment" + i);
                segment.transform.SetParent(root.transform, false);
                segment.transform.localPosition = position;
                segment.transform.localRotation = along;
                segment.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                segment.AddComponent<MeshRenderer>().sharedMaterials = materials;
                segments[i] = segment;
                sliceScales[i] = Mathf.Max(0.3f, SegmentRadius(food, i) / middleRadius);

                if (i == food.slices - 1 && food.stem.HasValue)
                    KitchenCounterInstaller.AddPart(segment.transform, PrimitiveType.Cube, "Stem", Vector3.down * (segmentLength * 0.9f), new Vector3(0.012f, 0.03f, 0.012f), DishasterSceneBuilder.GetMaterial("M_" + food.name + "Stem", food.stem.Value));
            }

            GameObject prefab = KitchenCounterInstaller.SaveItem(root, KitchenItemKind.Generic, Mathf.Max(food.length / 2f, food.radius) + 0.03f, food.radius * 2f, 0.03f);

            var item = prefab.GetComponent<KitchenItem>();
            item.slicePrefab = BuildSlice(food, meshes[food.slices / 2], materials).GetComponent<KitchenItem>();
            item.sliceScales = sliceScales;

            // Parçalar prefab'ın içinde: kayıttan sonra aynı isimlerle bulunuyor.
            item.segments = new GameObject[food.slices];
            for (int i = 0; i < food.slices; i++)
                item.segments[i] = prefab.transform.Find("Segment" + i).gameObject;

            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
        }

        static GameObject BuildSlice(Food food, Mesh mesh, Material[] materials)
        {
            float thickness = food.length / food.slices;

            var root = new GameObject(food.name + "Slice");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * (thickness / 2f);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = materials;

            return KitchenCounterInstaller.SaveItem(root, KitchenItemKind.Generic, Mathf.Max(food.radius, 0.03f) + 0.01f, thickness, 0.03f);
        }

        static float SegmentRadius(Food food, int index) => food.radius * food.profile((index + 0.5f) / food.slices);

        // ── MESH ────────────────────────────────────────────────────────────

        /// <summary>Tek asset içinde dilim başına bir mesh. Ekseni y; alt mesh 0 kabuk, 1 iç (kesik yüzler).</summary>
        static Mesh[] BuildMeshes(Food food)
        {
            string path = $"{MeshFolder}/{food.name}.asset";
            var meshes = new Mesh[food.slices];

            for (int i = 0; i < food.slices; i++)
            {
                meshes[i] = BuildSegmentMesh(food, i);

                if (i == 0) AssetDatabase.CreateAsset(meshes[i], path);
                else AssetDatabase.AddObjectToAsset(meshes[i], path);
            }

            AssetDatabase.SaveAssets();
            return meshes;
        }

        static Mesh BuildSegmentMesh(Food food, int index)
        {
            float thickness = food.length / food.slices;
            float tNear = (float)index / food.slices;
            float tFar = (float)(index + 1) / food.slices;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var side = new List<int>();
            var caps = new List<int>();

            // Kabuk. y arttıkça ürünün başına (t küçülen) doğru gidiyor; kesim baştan başlıyor.
            for (int ring = 0; ring <= MeshRings; ring++)
            {
                float blend = (float)ring / MeshRings;
                float t = Mathf.Lerp(tFar, tNear, blend);
                float y = (blend - 0.5f) * thickness;
                float radius = food.radius * food.profile(t);

                const float Step = 0.02f;
                float slope = food.radius * (food.profile(Mathf.Clamp01(t - Step)) - food.profile(Mathf.Clamp01(t + Step))) / (Step * 2f * food.length);

                for (int s = 0; s <= MeshSides; s++)
                {
                    float angle = 2f * Mathf.PI * s / MeshSides;
                    var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices.Add(radial * radius + Vector3.up * y);
                    normals.Add((radial - Vector3.up * slope).normalized);
                }
            }

            int perRing = MeshSides + 1;
            for (int ring = 0; ring < MeshRings; ring++)
            {
                for (int s = 0; s < MeshSides; s++)
                {
                    int a = ring * perRing + s;
                    int b = a + 1;
                    int c = a + perRing + 1;
                    int d = a + perRing;
                    side.AddRange(new[] { a, d, c, a, c, b });
                }
            }

            AddCap(food, tNear, thickness / 2f, Vector3.up, vertices, normals, caps);
            AddCap(food, tFar, -thickness / 2f, Vector3.down, vertices, normals, caps);

            var mesh = new Mesh { name = $"{food.name}_{index}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(side, 0);
            mesh.SetTriangles(caps, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddCap(Food food, float t, float y, Vector3 normal, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            float radius = food.radius * food.profile(t);
            int center = vertices.Count;

            vertices.Add(Vector3.up * y);
            normals.Add(normal);

            for (int s = 0; s <= MeshSides; s++)
            {
                float angle = 2f * Mathf.PI * s / MeshSides;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
                normals.Add(normal);
            }

            for (int s = 0; s < MeshSides; s++)
            {
                int a = center + 1 + s;
                int b = a + 1;

                if (normal.y > 0f) triangles.AddRange(new[] { center, b, a });
                else triangles.AddRange(new[] { center, a, b });
            }
        }
    }
}
