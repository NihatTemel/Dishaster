using System.IO;
using UnityEditor;
using UnityEngine;
using static Dishaster.EditorTools.KitchenLayout;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Pantry prefab'ı: doğu duvarı boyunca kiler. Üç alt dolap (art), tezgahlarında art'ın sebze kasaları, her
    /// kasada beşer yiyecek (domates, soğan, biber, mantar, havuç; art'ın kesilebilen yiyecekleri), üstte duvar
    /// rafında baharat kavanozları. Game sahnesine koyar; Version değişince kendiliğinden yeniden kurar. Tezgah,
    /// kasa tabanı ve raf yükseklikleri mesh'ten ölçülüyor.
    /// </summary>
    [InitializeOnLoad]
    public static class PantryInstaller
    {
        const string Version = "3";
        const string VersionFile = "ProjectSettings/DishasterPantry.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/Pantry.prefab";

        const int ItemsPerCrate = 5;

        // Kasaların yiyecekleri (FoodShowcaseInstaller'daki art yiyecekleri). Çilek ve peynir buzdolabında, ekmek adada.
        static readonly string[] CrateFoods = { "PF_Tomato_01", "PF_Onion_01", "PF_Pepper_01", "PF_Mushroom_01", "PF_Carrot_01" };

        static readonly string[] Spices = { "Salt", "BlackPepper", "ChiliFlakes", "Cumin", "Cinnamon", "Thyme" };

        // Doğu duvarı boyunca (kuzey köşeden güneye, metre): üç dolap 0-3, kasalar onların üstünde, raf 0.75-2.25.
        const float CrateFirst = 0.2f;
        const float CrateStep = 0.58f;
        const float ShelfCursor = 0.75f;
        const float ShelfWidth = 1.5f;
        // Kasa tezgahın ortasında (75 cm derin), raf duvardan 10 cm önde ve 35 cm derin.
        const float CrateInset = 0.37f;
        const float ShelfInset = 0.275f;

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
            if (!BuildPrefab()) return;
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde kiler yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath) && !BuildPrefab()) return false;
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity);
        }

        static bool BuildPrefab()
        {
            GameObject cabinet = Art(FurnitureArt + "PF_Dolap_100");
            GameObject crate = Art(FurnitureArt + "Parcalar/PF_VeggieCrate_01");
            GameObject shelf = Art(FurnitureArt + "PF_Duvar_Rafi_150");
            if (cabinet == null || crate == null || shelf == null)
            {
                Debug.LogWarning("[Dishaster] Kiler kurulamadı: dolap, kasa ya da raf art'ı yok.");
                return false;
            }

            Directory.CreateDirectory(KitchenCounterInstaller.ItemsFolder);
            AssetDatabase.Refresh();

            var root = new GameObject("Pantry");
            WallFrame frame = Frame(Side.East);

            // Dolaplar: sol uç kuzey duvarına dayalı (düz), sağ uç boşlukta (çıkıntılı).
            var counters = new GameObject("Counters").transform;
            counters.SetParent(root.transform, false);
            MutfakModulu.OnYuzTipi[] fronts = { MutfakModulu.OnYuzTipi.IkiKapak, MutfakModulu.OnYuzTipi.IkiCekmece, MutfakModulu.OnYuzTipi.IkiKapak };
            GameObject[] cabinets = new GameObject[fronts.Length];
            for (int i = 0; i < fronts.Length; i++)
            {
                GameObject instance = PlaceAlong(counters, cabinet, frame, i, 1f);
                Module(instance, i == fronts.Length - 1 ? MutfakModulu.TezgahTipi.SagCikinti : MutfakModulu.TezgahTipi.Duz, fronts[i]);
                Paint(instance, MobilyaRengi.Renk.Adacayi);
                AddBox(instance);
                Outline(instance, LowVisionOutlineType.Table);
                cabinets[i] = instance;
            }

            var crates = new GameObject("Crates").transform;
            crates.SetParent(root.transform, false);
            for (int i = 0; i < CrateFoods.Length; i++)
            {
                float cursor = CrateFirst + CrateStep * i;
                GameObject under = cabinets[Mathf.Clamp(Mathf.FloorToInt(cursor + 0.2f), 0, cabinets.Length - 1)];
                BuildCrate(crates, crate, CrateFoods[i], frame, cursor + 0.2f, under);
            }

            BuildSpiceShelf(root.transform, shelf, frame);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return true;
        }

        // ── KASA ────────────────────────────────────────────────────────────

        // Kasa tezgahın üstünde, uzun kenarı duvardan odaya; yiyecekler tabanında (taban yüksekliği ölçülüyor).
        static void BuildCrate(Transform parent, GameObject cratePrefab, string food, WallFrame frame, float cursor, GameObject cabinet)
        {
            GameObject foodPrefab = FoodShowcaseInstaller.FoodItem(food);

            Vector3 above = Point(frame, cursor, 1.6f, CrateInset);
            float top = SurfaceBelow(cabinet, above, 1.6f, out Vector3 surface) ? surface.y : 0.9f;

            GameObject crate = Instantiate(cratePrefab, parent);
            crate.name = "Crate_" + food.Replace("PF_", "").Replace("_01", "");
            // Art'ta uzun kenar x (60 cm): çerçevenin "odaya doğru"suna çevir.
            crate.transform.rotation = Quaternion.LookRotation(frame.along, Vector3.up);
            crate.transform.position = new Vector3(above.x, top, above.z);
            AddMeshColliders(crate);

            if (foodPrefab == null) return;

            // Yiyeceğin yerdeki izi: uzunsa (havuç) tek sıra yan yana, yuvarlaksa 3 + 2.
            GameObject probe = Instantiate(foodPrefab, null);
            Bounds footprint = MeshBounds(probe.transform, probe.transform);
            Object.DestroyImmediate(probe);
            float longSide = Mathf.Max(footprint.size.x, footprint.size.z);
            float shortSide = Mathf.Min(footprint.size.x, footprint.size.z);
            bool longFood = longSide > 0.2f;
            // Uzun yiyeceğin uzun ekseni kasanın kısa kenarına (40 cm) paralel.
            float alongYaw = footprint.size.x > footprint.size.z ? -90f : 0f;

            Vector3 into = frame.into;
            Vector3 along = frame.along;
            Vector3 center = new Vector3(above.x, 0f, above.z);
            for (int i = 0; i < ItemsPerCrate; i++)
            {
                Vector3 offset;
                float yaw;
                if (longFood)
                {
                    float step = Mathf.Min(0.11f, (0.5f - shortSide) / (ItemsPerCrate - 1));
                    offset = into * (step * (i - (ItemsPerCrate - 1) / 2f));
                    yaw = Quaternion.LookRotation(along).eulerAngles.y + alongYaw + (i % 2 == 0 ? 6f : -6f);
                }
                else
                {
                    float step = Mathf.Clamp(longSide + 0.02f, 0.1f, 0.17f);
                    offset = i < 3
                        ? into * (step * (i - 1)) - along * 0.085f
                        : into * (step * (i - 3.5f)) + along * 0.085f;
                    yaw = 37f * i;
                }

                Vector3 spot = center + offset;
                float floor = SurfaceBelow(crate, new Vector3(spot.x, top + 0.5f, spot.z), 0.6f, out Vector3 bottom) ? bottom.y : top + 0.02f;
                KitchenCounterInstaller.PlaceItem(parent, foodPrefab, new Vector3(spot.x, floor + 0.002f, spot.z), yaw);
            }
        }

        // ── BAHARAT RAFI ────────────────────────────────────────────────────

        static void BuildSpiceShelf(Transform parent, GameObject shelfPrefab, WallFrame frame)
        {
            GameObject shelf = PlaceAlong(parent, shelfPrefab, frame, ShelfCursor, ShelfWidth);
            shelf.name = "SpiceShelf";
            if (shelf.TryGetComponent(out RafModulu rack))
            {
                rack.katSayisi = 2;
                rack.ilkKatYuksekligi = 1.3f;
                rack.katArasi = 0.45f;
                rack.Uygula();
            }

            AddBox(shelf);
            Outline(shelf, LowVisionOutlineType.Wall);

            // Üç kavanoz üst katta, üçü alt katta. Üst kat yukarıdan, alt kat üst katın altından ölçülüyor.
            for (int i = 0; i < Spices.Length; i++)
            {
                GameObject jar = BuildSpiceJar(Spices[i]);
                if (jar == null) continue;

                float cursor = ShelfCursor + 0.3f + 0.45f * (i % 3);
                Vector3 above = Point(frame, cursor, 2.7f, ShelfInset);
                if (!SurfaceBelow(shelf, above, 2f, out Vector3 spot)) continue;
                if (i >= 3 && SurfaceBelow(shelf, new Vector3(above.x, spot.y - 0.14f, above.z), 1.5f, out Vector3 lower)) spot = lower;

                KitchenCounterInstaller.PlaceItem(parent, jar, spot, Quaternion.LookRotation(frame.into).eulerAngles.y + 15f * i);
            }
        }

        // Kavanoz eşya: alınıp adaya konabiliyor (içi tariflerde yok, şimdilik süs ama elde tutuluyor).
        static GameObject BuildSpiceJar(string spice)
        {
            GameObject art = Art(PantryArt + "PF_SpiceJar_" + spice);
            if (art == null) return null;

            GameObject root = WrapOnBase("SpiceJar_" + spice, art, Quaternion.identity);
            Bounds bounds = MeshBounds(root.transform, root.transform);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            return KitchenCounterInstaller.SaveItem(root, KitchenItemKind.Generic, radius + 0.02f, bounds.size.y, radius + 0.02f);
        }
    }
}
