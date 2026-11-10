using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static Dishaster.EditorTools.KitchenLayout;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Fridge prefab'ı: art'ın buzdolabı (PF_Buzdolabi_100) arka duvarın sol ucunda; iki kapağı (sağdan menteşeli)
    /// görmeyenin eliyle açılıp kapanıyor (KitchenDoor). Raflarda art'ın süt kutuları, çilekler ve peynir. Gövdenin
    /// içi oyuk (el raflara değiyor), raf yükseklikleri mesh'ten. Game sahnesine koyar; Version değişince
    /// kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class FridgeInstaller
    {
        const string Version = "3";
        const string VersionFile = "ProjectSettings/DishasterFridge.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/Fridge.prefab";
        const float Width = 1f;

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
            if (!BuildPrefab(out Vector3 position, out Quaternion rotation)) return;
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, position, rotation, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!BuildPrefab(out Vector3 position, out Quaternion rotation, onlyIfMissing: true)) return false;
            return GameScenePrefabs.EnsurePlaced(PrefabPath, position, rotation);
        }

        /// <summary>Kök art'ın pivotunda; yer ve dönüş arka duvarın çerçevesinden (uzanma yönü ölçülüyor).</summary>
        static bool BuildPrefab(out Vector3 position, out Quaternion rotation, bool onlyIfMissing = false)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            GameObject art = Art(FurnitureArt + "PF_Buzdolabi_100");
            if (art == null) return false;

            var scratch = new GameObject("FridgeScratch");
            GameObject probe = PlaceAlong(scratch.transform, art, Frame(Side.North), FridgeCursor, Width);
            position = probe.transform.position;
            rotation = probe.transform.rotation;
            Object.DestroyImmediate(scratch);

            if (onlyIfMissing && File.Exists(PrefabPath)) return true;

            Directory.CreateDirectory(KitchenCounterInstaller.ItemsFolder);

            var root = new GameObject("Fridge");
            GameObject body = Instantiate(art, root.transform);
            body.name = "Body";
            Paint(body, MobilyaRengi.Renk.Krem);

            // Kapaklar: menteşe kapağın kendi pivotunda (sağ kenar), 0 → -100° (MOBILYA.md).
            var doors = new List<Transform>();
            foreach (string doorName in new[] { "Kapak_Alt", "Kapak_Ust" })
            {
                Transform door = body.transform.Find(doorName);
                if (door == null) continue;

                var panel = door.gameObject.AddComponent<BoxCollider>();
                Bounds bounds = MeshBounds(door, door);
                panel.center = bounds.center;
                panel.size = bounds.size;

                var kitchenDoor = door.gameObject.AddComponent<KitchenDoor>();
                kitchenDoor.hinge = door;
                kitchenDoor.panel = panel;
                kitchenDoor.openAngle = -100f;
                doors.Add(door);
            }

            // Gövde ve raflar kendi şekilleriyle: içi oyuk, el raflara değiyor, oyuncu içinden geçmiyor.
            foreach (Transform part in body.transform)
                if (!doors.Contains(part))
                    AddMeshColliders(part.gameObject);

            Outline(body, LowVisionOutlineType.Table);

            // İçi aydınlık: kapak açılınca raflar görünsün.
            var light = new GameObject("InsideLight").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            Bounds whole = MeshBounds(body.transform, root.transform);
            light.transform.localPosition = new Vector3(whole.center.x, whole.max.y - 0.35f, whole.center.z + 0.1f);
            light.type = LightType.Point;
            light.range = 1.2f;
            light.intensity = 0.7f;
            light.color = new Color(0.92f, 0.96f, 1f);
            light.shadows = LightShadows.None;

            FillShelves(root.transform, body);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return true;
        }

        // Raflar alttan üste: alt rafta çilekler, ortada süt kutuları, üstte peynir. Yükseklik rafın üst yüzü.
        static void FillShelves(Transform root, GameObject body)
        {
            var shelves = new List<Bounds>();
            foreach (string shelfName in new[] { "Raf_1", "Raf_2", "Raf_3" })
            {
                Transform shelf = body.transform.Find(shelfName);
                if (shelf != null) shelves.Add(MeshBounds(shelf, root));
            }

            if (shelves.Count == 0)
            {
                Debug.LogWarning("[Dishaster] Buzdolabında raf bulunamadı (Raf_1..3); eşyalar konmadı.");
                return;
            }

            shelves.Sort((a, b) => a.max.y.CompareTo(b.max.y));
            Bounds bottom = shelves[0];
            Bounds middle = shelves[Mathf.Min(1, shelves.Count - 1)];
            Bounds top = shelves[shelves.Count - 1];

            // Süt: ortada iki kutu.
            GameObject milk = BuildMilkCarton();
            if (milk != null)
            {
                for (int i = 0; i < 2; i++)
                    KitchenCounterInstaller.PlaceItem(root, milk, OnShelf(middle, -0.2f + 0.4f * i, 0f), 0f);
            }

            // Çilek: altta altı tane, iki sıra.
            GameObject strawberry = FoodShowcaseInstaller.FoodItem("PF_Strawberry_01");
            if (strawberry != null)
            {
                for (int i = 0; i < 6; i++)
                    KitchenCounterInstaller.PlaceItem(root, strawberry, OnShelf(bottom, -0.3f + 0.3f * (i % 3), i < 3 ? -0.2f : 0.15f), 35f * i);
            }

            GameObject cheese = FoodShowcaseInstaller.FoodItem("PF_Cheese_Block_01");
            if (cheese != null && top.max.y > middle.max.y + 0.05f)
                KitchenCounterInstaller.PlaceItem(root, cheese, OnShelf(top, 0f, 0f), 10f);
        }

        // Rafın üstünde bir nokta: x ve z rafın yarı genişliği/derinliği oranında (-0.5..0.5).
        static Vector3 OnShelf(Bounds shelf, float x, float z)
        {
            return new Vector3(shelf.center.x + shelf.size.x * x, shelf.max.y + 0.002f, shelf.center.z + shelf.size.z * z);
        }

        static GameObject BuildMilkCarton()
        {
            GameObject art = Art(PantryArt + "PF_MilkCarton_01");
            if (art == null) return null;

            GameObject root = WrapOnBase("MilkCarton", art, Quaternion.identity);
            Bounds bounds = MeshBounds(root.transform, root.transform);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            return KitchenCounterInstaller.SaveSourceItem(root, Ingredient.Milk, 1000f, 150f, radius + 0.03f, bounds.size.y, radius + 0.02f);
        }
    }
}
