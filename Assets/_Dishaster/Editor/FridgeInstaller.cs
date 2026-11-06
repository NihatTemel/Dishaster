using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Fridge prefab'ı: doğu duvarında, kapağı açılıp kapanan buzdolabı. İçinde süt kutuları, yumurtalar ve
    /// çilekler. Game sahnesine koyar; Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class FridgeInstaller
    {
        const string Version = "1";
        const string VersionFile = "ProjectSettings/DishasterFridge.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/Fridge.prefab";

        // Doğu duvarı x=9; önü odaya (batıya) bakıyor. Tahta bariyerinin (z=2.5) güneyinde, görmeyen ulaşabiliyor.
        static readonly Vector3 Position = new Vector3(8.62f, 0f, -2f);
        static readonly Quaternion Rotation = Quaternion.Euler(0f, -90f, 0f);

        // Yerel: önü +z.
        const float Width = 0.75f;
        const float Depth = 0.7f;
        const float Height = 1.8f;
        const float Wall = 0.04f;
        const float Plinth = 0.1f;
        const float DoorThickness = 0.05f;
        static readonly float[] ShelfHeights = { 0.5f, 0.95f, 1.4f };

        static readonly PantryInstaller.Food Strawberry = new PantryInstaller.Food
        {
            name = "Strawberry", peel = new Color(0.82f, 0.10f, 0.12f), flesh = new Color(0.95f, 0.55f, 0.55f),
            length = 0.045f, radius = 0.019f, slices = 4,
            stem = new Color(0.20f, 0.45f, 0.15f),
            profile = t => PantryInstaller.Round(t) * (0.5f + 0.5f * t),
        };

        static FridgeInstaller()
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

        [MenuItem("Tools/Dishaster/7 Install Fridge", priority = 7)]
        static void Install()
        {
            BuildPrefab();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Position, Rotation, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Position, Rotation);
        }

        static void BuildPrefab()
        {
            Directory.CreateDirectory(KitchenCounterInstaller.ItemsFolder);

            var root = new GameObject("Fridge");
            Material shell = DishasterSceneBuilder.GetMaterial("M_Fridge", new Color(0.90f, 0.91f, 0.92f));
            Material inside = DishasterSceneBuilder.GetMaterial("M_FridgeInside", new Color(0.96f, 0.97f, 0.98f));

            // Gövde parçaları katı: oyuncu içinden geçmiyor, el ışını raflara değiyor.
            AddSolid(root.transform, "Back", new Vector3(0f, Height / 2f, -Depth / 2f + Wall / 2f), new Vector3(Width, Height, Wall), shell);
            AddSolid(root.transform, "SideLeft", new Vector3(-Width / 2f + Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Depth), shell);
            AddSolid(root.transform, "SideRight", new Vector3(Width / 2f - Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Depth), shell);
            AddSolid(root.transform, "Top", new Vector3(0f, Height - Wall / 2f, 0f), new Vector3(Width, Wall, Depth), shell);
            AddSolid(root.transform, "Bottom", new Vector3(0f, Plinth / 2f, 0f), new Vector3(Width, Plinth, Depth), shell);

            Vector3 shelfSize = new Vector3(Width - Wall * 2f, 0.02f, Depth - Wall - 0.02f);
            for (int i = 0; i < ShelfHeights.Length; i++)
                AddSolid(root.transform, "Shelf" + (i + 1), new Vector3(0f, ShelfHeights[i] - 0.01f, Wall / 2f - 0.01f), shelfSize, inside);

            BuildDoor(root.transform, shell);
            FillShelves(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        // Menteşe sağda (önden bakınca), kapak dışa açılıyor.
        static void BuildDoor(Transform parent, Material shell)
        {
            var hinge = new GameObject("DoorHinge");
            hinge.transform.SetParent(parent, false);
            hinge.transform.localPosition = new Vector3(-Width / 2f, 0f, Depth / 2f);

            GameObject panel = AddSolid(hinge.transform, "Door", new Vector3(Width / 2f, Height / 2f, DoorThickness / 2f), new Vector3(Width, Height, DoorThickness), shell);
            KitchenCounterInstaller.AddPart(hinge.transform, PrimitiveType.Cube, "Handle", new Vector3(Width - 0.06f, 1.1f, DoorThickness + 0.025f), new Vector3(0.025f, 0.32f, 0.03f), DishasterSceneBuilder.GetMaterial("M_FridgeHandle", new Color(0.35f, 0.36f, 0.38f)));

            var door = hinge.AddComponent<KitchenDoor>();
            door.hinge = hinge.transform;
            door.panel = panel.GetComponent<Collider>();
            door.openAngle = -100f;
        }

        static void FillShelves(Transform parent)
        {
            // Orta raf: iki süt kutusu.
            GameObject milk = BuildMilkCarton();
            KitchenCounterInstaller.PlaceItem(parent, milk, new Vector3(-0.15f, ShelfHeights[1], 0.08f), 0f);
            KitchenCounterInstaller.PlaceItem(parent, milk, new Vector3(0.05f, ShelfHeights[1], 0.08f), 0f);

            // Alt raf: solda yumurta kabı (2x3), sağda çilek kutusu.
            float shelf = ShelfHeights[0];
            KitchenCounterInstaller.AddPart(parent, PrimitiveType.Cube, "EggTray", new Vector3(-0.17f, shelf + 0.01f, 0.05f), new Vector3(0.13f, 0.02f, 0.19f), DishasterSceneBuilder.GetMaterial("M_EggTray", new Color(0.70f, 0.62f, 0.50f)));
            GameObject egg = BuildEgg();
            for (int i = 0; i < 6; i++)
                KitchenCounterInstaller.PlaceItem(parent, egg, new Vector3(-0.2f + 0.06f * (i % 2), shelf + 0.02f, -0.01f + 0.06f * (i / 2)), 0f);

            Material box = DishasterSceneBuilder.GetMaterial("M_BerryBox", new Color(0.30f, 0.55f, 0.35f));
            KitchenCounterInstaller.AddPart(parent, PrimitiveType.Cube, "BerryBoxFloor", new Vector3(0.15f, shelf + 0.005f, 0.05f), new Vector3(0.22f, 0.01f, 0.18f), box);
            KitchenCounterInstaller.AddPart(parent, PrimitiveType.Cube, "BerryBoxFront", new Vector3(0.15f, shelf + 0.025f, 0.14f), new Vector3(0.22f, 0.04f, 0.008f), box);
            KitchenCounterInstaller.AddPart(parent, PrimitiveType.Cube, "BerryBoxBack", new Vector3(0.15f, shelf + 0.025f, -0.04f), new Vector3(0.22f, 0.04f, 0.008f), box);

            GameObject strawberry = PantryInstaller.BuildFood(Strawberry);
            for (int i = 0; i < 6; i++)
                KitchenCounterInstaller.PlaceItem(parent, strawberry, new Vector3(0.08f + 0.07f * (i % 3), shelf + 0.01f, 0.01f + 0.08f * (i / 3)), 30f * i);
        }

        static GameObject BuildMilkCarton()
        {
            var root = new GameObject("MilkCarton");
            Material carton = DishasterSceneBuilder.GetMaterial("M_MilkCarton", new Color(0.95f, 0.95f, 0.97f));
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.1f, 0f), new Vector3(0.075f, 0.2f, 0.075f), carton);
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cube, "Label", new Vector3(0f, 0.1f, 0.038f), new Vector3(0.06f, 0.08f, 0.002f), DishasterSceneBuilder.GetMaterial("M_MilkLabel", new Color(0.25f, 0.45f, 0.80f)));
            Transform gable = KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Cube, "Gable", new Vector3(0f, 0.215f, 0f), new Vector3(0.053f, 0.053f, 0.075f), carton);
            gable.localRotation = Quaternion.Euler(0f, 0f, 45f);
            return KitchenCounterInstaller.SaveSourceItem(root, Ingredient.Milk, 1000f, 150f, 0.08f, 0.24f, 0.04f);
        }

        static GameObject BuildEgg()
        {
            var root = new GameObject("Egg");
            KitchenCounterInstaller.AddPart(root.transform, PrimitiveType.Sphere, "Shell", new Vector3(0f, 0.03f, 0f), new Vector3(0.045f, 0.06f, 0.045f), DishasterSceneBuilder.GetMaterial("M_Egg", new Color(0.93f, 0.86f, 0.74f)));

            var ingredient = root.AddComponent<KitchenIngredientItem>();
            ingredient.ingredient = Ingredient.Egg;
            ingredient.amount = 1f;
            return KitchenCounterInstaller.SaveItem(root, KitchenItemKind.Generic, 0.05f, 0.06f, 0.03f);
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
    }
}
