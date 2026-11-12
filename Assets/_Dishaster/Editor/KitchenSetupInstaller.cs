using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Dishaster.EditorTools.KitchenLayout;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Özgür'ün mutfak düzeninin (Game sahnesi) üstüne yapılan ayarlar. Kiler, tezgah ve buzdolabı prefab'ları
    /// yeniden kurulmuyor (sahnedeki düzen bozulmasın); yerinde değiştiriliyor:
    /// - Ada: üstünde sadece kase, bıçak, ölçü bardağı ve kesme tahtası. Ekmek buzdolabının üst rafına geçiyor
    ///   (Counter ve Fridge prefab'ları), adadaki süsler (Art_Ekler'de, eşya olmayanlar) kalkıyor.
    /// - Baharat rafı: tek kutu çarpıştırıcı kavanozları içine alıyordu (el kavanoza değemiyordu, alınamıyordu);
    ///   yerine her katın kendi tahtası.
    /// - Kasalar: ön sıranın açık ucuna bir kasa daha, içinde çilekler (buzdolabından taşındı); her kasaya KitchenCrate
    ///   (içerik her oyunda karışıyor); üstlerine çıkılmasın diye kasaları saran görünmez bir duvar (Ignore Raycast: el
    ///   ışını içinden geçiyor, eşyalar alınıyor). Doğma noktası o duvarın dibindeyse öne alınıyor.
    /// - Kesme tahtası: kendi kutusu (el tahtanın üstüne değsin).
    /// - Tabaklar: duvardaki yığınlar (Kitchen/Decor) alınıp bırakılabilen tabak oluyor (KitchenItem + KitchenPlate);
    ///   yığından sadece en üstteki alınıyor (KitchenCounter).
    /// Sürüm değişince bir kez çalışıyor; Game sahnesinde kaydedilmemiş değişiklik varsa dokunmuyor.
    /// </summary>
    [InitializeOnLoad]
    public static class KitchenSetupInstaller
    {
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterKitchenSetup.version";
        const string GamePath = "Assets/_Dishaster/Scenes/Game.unity";
        const string CounterPath = "Assets/_Dishaster/Prefabs/Counter.prefab";
        const string FridgePath = "Assets/_Dishaster/Prefabs/Fridge.prefab";
        const string BreadPath = KitchenCounterInstaller.ItemsFolder + "/Art_Bread_Loaf.prefab";
        const string StrawberryPath = KitchenCounterInstaller.ItemsFolder + "/Art_Strawberry.prefab";
        const string PlatePrefix = "PF_Plate_Serving_01";

        const string ExtraCrateName = "Crate_Extra";
        const string NoClimbName = "NoClimb";
        const int IgnoreRaycastLayer = 2;
        // Oyuncunun basamak yüksekliğinden (0,3 m) ve zıplamasından çok yüksek.
        const float NoClimbHeight = 2.2f;
        // Doğma noktası kasa alanına bundan yakınsa öne alınıyor (kapsül yarıçapı 0,3 m + pay).
        const float SpawnClearance = 0.45f;

        static KitchenSetupInstaller()
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

        [MenuItem("Tools/Dishaster/9 Kitchen Setup", priority = 9)]
        static void Install()
        {
            Scene scene = SceneManager.GetSceneByPath(GamePath);
            bool wasOpen = scene.isLoaded;
            if (wasOpen && scene.isDirty)
            {
                Debug.LogWarning("[Dishaster] Game sahnesinde kaydedilmemiş değişiklik var; mutfak ayarları yapılmadı. " +
                                 "Sahneyi kaydedince Tools/Dishaster/9 Kitchen Setup ile tekrar çalıştır.");
                return;
            }

            MoveBreadToFridge();
            RemoveStrawberriesFromFridge();
            AddCuttingBoardCollider();

            if (!wasOpen) scene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);
            ClearIsland(scene);
            FixSpiceShelf(scene);
            PutStrawberriesInCrate(scene);
            SetupCrates(scene);
            SetupPlates(scene);
            RestItemsOnBase(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);

            File.WriteAllText(VersionFile, Version);
            Debug.Log("[Dishaster] Mutfak ayarları yapıldı: ada, ekmek buzdolabında, baharat rafı, kasalar (çilekler kasada), tabaklar.");
        }

        // ── EKMEK ───────────────────────────────────────────────────────────

        static void MoveBreadToFridge()
        {
            var bread = AssetDatabase.LoadAssetAtPath<GameObject>(BreadPath);
            if (bread == null)
            {
                Debug.LogWarning("[Dishaster] Ekmek eşyası yok (" + BreadPath + "); ekmek taşınmadı.");
                return;
            }

            GameObject counter = PrefabUtility.LoadPrefabContents(CounterPath);
            try
            {
                KitchenItem[] breads = counter.GetComponentsInChildren<KitchenItem>(true).Where(IsBread).ToArray();
                foreach (KitchenItem item in breads)
                    Object.DestroyImmediate(item.gameObject);
                if (breads.Length > 0) PrefabUtility.SaveAsPrefabAsset(counter, CounterPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(counter);
            }

            GameObject fridge = PrefabUtility.LoadPrefabContents(FridgePath);
            try
            {
                if (!fridge.GetComponentsInChildren<KitchenItem>(true).Any(IsBread) && PlaceBread(fridge.transform, bread))
                    PrefabUtility.SaveAsPrefabAsset(fridge, FridgePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fridge);
            }
        }

        static bool IsBread(KitchenItem item) => item.name.StartsWith("Art_Bread");

        /// <summary>
        /// Üst rafta en geniş boşluğun ortası (peynirin yanı); kısa kenarı rafın genişliği boyunca, uzun kenarı
        /// derinliğine. Raf ve eşyalar buzdolabının kökünün uzayında ölçülüyor.
        /// </summary>
        static bool PlaceBread(Transform fridge, GameObject bread)
        {
            Transform[] shelves = fridge.GetComponentsInChildren<Transform>(true).Where(part => part.name.StartsWith("Raf_")).ToArray();
            if (shelves.Length == 0)
            {
                Debug.LogWarning("[Dishaster] Buzdolabında raf yok (Raf_1..3); ekmek konmadı.");
                return false;
            }

            Bounds shelf = shelves.Select(part => MeshBounds(part, fridge)).OrderBy(bounds => bounds.max.y).Last();
            bool widthAlongX = shelf.size.x >= shelf.size.z;
            float start = widthAlongX ? shelf.min.x : shelf.min.z;
            float end = widthAlongX ? shelf.max.x : shelf.max.z;

            // Raftaki eşyaların kapladığı aralıklar; en geniş boşluk.
            var taken = new List<(float from, float to)>();
            foreach (KitchenItem item in fridge.GetComponentsInChildren<KitchenItem>(true))
            {
                Bounds bounds = MeshBounds(item.transform, fridge);
                if (Mathf.Abs(bounds.min.y - shelf.max.y) > 0.06f) continue;
                taken.Add(widthAlongX ? (bounds.min.x, bounds.max.x) : (bounds.min.z, bounds.max.z));
            }

            taken.Sort((a, b) => a.from.CompareTo(b.from));
            float gapFrom = start;
            float gapTo = start;
            float cursor = start;
            foreach ((float from, float to) in taken)
            {
                if (from - cursor > gapTo - gapFrom)
                {
                    gapFrom = cursor;
                    gapTo = from;
                }

                cursor = Mathf.Max(cursor, to);
            }

            if (end - cursor > gapTo - gapFrom)
            {
                gapFrom = cursor;
                gapTo = end;
            }

            Bounds footprint = MeshBounds(bread.transform, bread.transform);
            bool shortAlongX = footprint.size.x <= footprint.size.z;
            float yaw = widthAlongX == shortAlongX ? 0f : 90f;
            if (gapTo - gapFrom < Mathf.Min(footprint.size.x, footprint.size.z))
                Debug.LogWarning("[Dishaster] Buzdolabının üst rafında ekmeğe yer dar; yine de en geniş boşluğa kondu.");

            float across = (gapFrom + gapTo) / 2f;
            float depth = widthAlongX ? shelf.center.z : shelf.center.x;
            Vector3 position = widthAlongX ? new Vector3(across, shelf.max.y + 0.002f, depth) : new Vector3(depth, shelf.max.y + 0.002f, across);
            // Ekmeğin izinin ortası pivotunda değilse kaydır.
            position -= Quaternion.Euler(0f, yaw, 0f) * new Vector3(footprint.center.x, 0f, footprint.center.z);

            KitchenCounterInstaller.PlaceItem(fridge, bread, position, yaw);
            return true;
        }

        // ── KESME TAHTASI ───────────────────────────────────────────────────

        /// <summary>
        /// Tahtanın kendi kutusu yoktu: el ışını tahtanın içinden geçip tezgaha değiyordu (görmeyenin eli tahtaya
        /// gömülüyordu). Kutu tahtanın görünen hacmi; üstüne bırakılan tahtanın yüzeyine oturuyor (boardHeight).
        /// </summary>
        static void AddCuttingBoardCollider()
        {
            GameObject counter = PrefabUtility.LoadPrefabContents(CounterPath);
            try
            {
                Transform board = counter.TryGetComponent(out KitchenCounter kitchen) ? kitchen.cuttingBoard : null;
                if (board == null || board.GetComponent<Collider>() != null) return;

                Bounds bounds = MeshBounds(board, board);
                var box = board.gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
                PrefabUtility.SaveAsPrefabAsset(counter, CounterPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(counter);
            }
        }

        // ── ÇİLEK ───────────────────────────────────────────────────────────

        static void RemoveStrawberriesFromFridge()
        {
            GameObject fridge = PrefabUtility.LoadPrefabContents(FridgePath);
            try
            {
                KitchenItem[] berries = fridge.GetComponentsInChildren<KitchenItem>(true).Where(IsStrawberry).ToArray();
                foreach (KitchenItem item in berries)
                    Object.DestroyImmediate(item.gameObject);
                if (berries.Length > 0) PrefabUtility.SaveAsPrefabAsset(fridge, FridgePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fridge);
            }
        }

        static bool IsStrawberry(KitchenItem item) => item.name.StartsWith("Art_Strawberry");

        /// <summary>
        /// Ek kasadakiler (yanındakinin kopyası) yerine altı çilek, iki sıra; kasanın tabanında, eski yiyeceklerin
        /// yüksekliğinde. Kasaların içeriği her oyunda karıştığı için çilekler de her oyunda başka kasada.
        /// </summary>
        static void PutStrawberriesInCrate(Scene scene)
        {
            Transform crates = FindInScene(scene, "Crates");
            Transform box = crates != null ? crates.Find(ExtraCrateName) : null;
            var strawberry = AssetDatabase.LoadAssetAtPath<GameObject>(StrawberryPath);
            if (box == null || strawberry == null)
            {
                Debug.LogWarning("[Dishaster] Ek kasa ya da çilek eşyası yok; çilekler kasaya konmadı.");
                return;
            }

            KitchenItem[] inside = crates.GetComponentsInChildren<KitchenItem>(true).Where(food => Inside(box, food.transform.position)).ToArray();
            if (inside.Any(IsStrawberry)) return;

            Bounds bounds = MeshBounds(box, box);
            float floor = inside.Length > 0 ? inside.Average(food => box.InverseTransformPoint(food.transform.position).y) : bounds.min.y + 0.04f;
            foreach (KitchenItem food in inside)
                Object.DestroyImmediate(food.gameObject);

            for (int i = 0; i < 6; i++)
            {
                var local = new Vector3(bounds.center.x + (i % 3 - 1) * bounds.size.x * 0.25f, floor, bounds.center.z + (i < 3 ? -1f : 1f) * bounds.size.z * 0.22f);
                var berry = (GameObject)PrefabUtility.InstantiatePrefab(strawberry, crates);
                berry.name = "Art_Strawberry";
                berry.transform.SetPositionAndRotation(box.TransformPoint(local), box.rotation * Quaternion.Euler(0f, 35f * i, 0f));
            }
        }

        // ── TABAKLAR ────────────────────────────────────────────────────────

        /// <summary>
        /// Duvar rafındaki ve tezgahtaki tabaklar alınabilir tabak oluyor: tetik kutusu tabağın kendisi, avuçta
        /// önde taşınıyor; üstüne dilimler diziliyor.
        /// </summary>
        static void SetupPlates(Scene scene)
        {
            Transform[] plates = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(part => part.name.StartsWith(PlatePrefix) && part.GetComponentInChildren<MeshFilter>(true) != null)
                .ToArray();

            foreach (Transform plate in plates)
            {
                Bounds bounds = MeshBounds(plate, plate);
                if (!plate.TryGetComponent(out BoxCollider box)) box = plate.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = bounds.center;
                box.size = bounds.size;

                if (!plate.TryGetComponent(out KitchenItem item)) item = plate.gameObject.AddComponent<KitchenItem>();
                item.kind = KitchenItemKind.Generic;
                item.height = bounds.max.y;
                item.grabRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                item.holdForward = 0.12f;

                if (!plate.TryGetComponent(out KitchenPlate dish)) dish = plate.gameObject.AddComponent<KitchenPlate>();
                dish.surfaceHeight = Mathf.Clamp(bounds.size.y * 0.3f, 0.008f, 0.02f);
                dish.ringRadius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.48f;

                EditorUtility.SetDirty(plate.gameObject);
            }

            Debug.Log($"[Dishaster] {plates.Length} tabak alınabilir oldu.");
        }

        // ── EŞYALARIN TABANI ────────────────────────────────────────────────

        /// <summary>
        /// Eşyanın pivotu görünen tabanında olmalı (KitchenItem öyle varsayıyor: bırakılınca tabanı yüzeye oturuyor).
        /// Baharat kavanozlarının görüntüsü pivotun 10,8 cm altına iniyordu (raf alçalınca art kaydırılmış): bırakılınca
        /// tezgaha gömülüyor, kutusu havada kalıyordu. Görüntü yerinde kalacak şekilde pivot tabana alınıyor; kutu
        /// görüntüyle aynı yükseklikte değilse oturtuluyor.
        /// </summary>
        static void RestItemsOnBase(Scene scene)
        {
            int fixedCount = 0;
            foreach (KitchenItem item in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<KitchenItem>(true)))
            {
                Transform root = item.transform;
                if (root.childCount == 0 || root.GetComponentInChildren<MeshFilter>(true) == null) continue;

                Bounds bounds = MeshBounds(root, root);
                float below = -bounds.min.y;
                if (Mathf.Abs(below) > 0.005f)
                {
                    foreach (Transform child in root)
                        child.localPosition += Vector3.up * below;
                    root.position -= root.up * below;
                    bounds.center += Vector3.up * below;
                    fixedCount++;
                }

                if (root.TryGetComponent(out BoxCollider box) && Mathf.Abs(box.center.y - box.size.y / 2f - bounds.min.y) > 0.01f)
                {
                    box.center = new Vector3(box.center.x, bounds.center.y, box.center.z);
                    box.size = new Vector3(box.size.x, bounds.size.y, box.size.z);
                }

                EditorUtility.SetDirty(item.gameObject);
            }

            Debug.Log($"[Dishaster] {fixedCount} eşyanın pivotu tabanına alındı.");
        }

        // ── ADA ─────────────────────────────────────────────────────────────

        /// <summary>Art_Ekler'de adanın üstünde duran, eşya olmayan süsler kalkıyor (ada üstünde sadece aletler).</summary>
        static void ClearIsland(Scene scene)
        {
            KitchenCounter counter = FindInScene<KitchenCounter>(scene);
            GameObject extras = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Art_Ekler");
            if (counter == null || extras == null) return;

            Transform top = counter.transform;
            foreach (Transform child in extras.transform.Cast<Transform>().ToArray())
            {
                if (child.GetComponentInChildren<KitchenItem>(true) != null || child.GetComponentInChildren<MeshFilter>() == null) continue;

                Bounds bounds = MeshBounds(child, top);
                bool onIsland = Mathf.Abs(bounds.center.x) <= counter.topSize.x / 2f
                    && Mathf.Abs(bounds.center.z) <= counter.topSize.y / 2f
                    && bounds.min.y >= counter.topHeight - 0.05f
                    && bounds.min.y <= counter.topHeight + 0.3f;
                if (!onIsland) continue;

                Debug.Log("[Dishaster] Adadan kalktı: " + child.name);
                Object.DestroyImmediate(child.gameObject);
            }
        }

        // ── BAHARAT RAFI ────────────────────────────────────────────────────

        static void FixSpiceShelf(Scene scene)
        {
            Transform shelf = FindInScene(scene, "SpiceShelf");
            if (shelf == null) return;

            if (shelf.TryGetComponent(out BoxCollider whole)) whole.enabled = false;
            AddBoardColliders(shelf);
        }

        /// <summary>Rafın her (açık) katının tahtasına kendi kutusu: kavanozlar tahtanın üstünde, el onlara değiyor.</summary>
        internal static void AddBoardColliders(Transform shelf)
        {
            foreach (Transform board in shelf.GetComponentsInChildren<Transform>(false))
            {
                if (board.name != "Raf" || !board.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                if (board.GetComponent<Collider>() != null) continue;

                var box = board.gameObject.AddComponent<BoxCollider>();
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }
        }

        // ── KASALAR ─────────────────────────────────────────────────────────

        static void SetupCrates(Scene scene)
        {
            Transform crates = FindInScene(scene, "Crates");
            if (crates == null)
            {
                Debug.LogWarning("[Dishaster] Kilerde kasa yok (Crates); kasalar ayarlanmadı.");
                return;
            }

            List<Transform> boxes = crates.Cast<Transform>().Where(child => child.name.StartsWith("Crate_")).ToList();
            if (boxes.Count == 0) return;

            AddExtraCrate(crates, boxes, crates.GetComponentsInChildren<KitchenItem>(true));

            KitchenItem[] foods = crates.GetComponentsInChildren<KitchenItem>(true);
            foreach (Transform box in boxes)
            {
                if (!box.TryGetComponent(out KitchenCrate crate)) crate = box.gameObject.AddComponent<KitchenCrate>();
                crate.items = foods.Where(food => Inside(box, food.transform.position)).ToArray();
                EditorUtility.SetDirty(crate);
            }

            Bounds area = AddNoClimb(crates);
            PushSpawns(scene, crates, area);
        }

        /// <summary>Ön sıranın (yerdekiler) açık ucuna, aynı aralıkla bir kasa daha; içi yanındakinin aynısı.</summary>
        static void AddExtraCrate(Transform crates, List<Transform> boxes, KitchenItem[] foods)
        {
            if (boxes.Any(box => box.name == ExtraCrateName)) return;

            float lowest = boxes.Min(box => box.position.y);
            List<Transform> front = boxes.Where(box => box.position.y < lowest + 0.1f).OrderBy(box => box.position.x).ToList();
            if (front.Count < 2) return;

            // Kasalar doğu duvarına dayalı; açık uç batıda (en küçük x).
            Transform source = front[0];
            Vector3 step = source.position - front[1].position;

            GameObject extra = Copy(source.gameObject, crates);
            extra.name = ExtraCrateName;
            extra.transform.SetPositionAndRotation(source.position + step, source.rotation);
            if (extra.GetComponentInChildren<Collider>() == null) AddMeshColliders(extra);

            foreach (KitchenItem food in foods.Where(food => Inside(source, food.transform.position)))
            {
                GameObject copy = Copy(food.gameObject, crates);
                copy.name = food.name.Split(' ')[0];
                copy.transform.SetPositionAndRotation(food.transform.position + step, food.transform.rotation);
            }

            boxes.Add(extra.transform);
        }

        // Prefab kopyasıysa yine prefab kopyası (art değişince o da değişsin), değilse düz kopya.
        static GameObject Copy(GameObject source, Transform parent)
        {
            if (PrefabUtility.GetNearestPrefabInstanceRoot(source) == source)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source));
                if (asset != null) return (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            }

            return Object.Instantiate(source, parent);
        }

        // Nokta kasanın içinde mi: kasanın kendi uzayında mesh kutusunun yatayda içinde, tabanından yukarıda.
        static bool Inside(Transform box, Vector3 point)
        {
            Bounds bounds = MeshBounds(box, box);
            Vector3 local = box.InverseTransformPoint(point);
            return local.x >= bounds.min.x && local.x <= bounds.max.x
                && local.z >= bounds.min.z && local.z <= bounds.max.z
                && local.y >= bounds.min.y - 0.02f && local.y <= bounds.max.y;
        }

        /// <summary>Kasaları (taban kasaları dahil) saran görünmez duvar; Crates'in uzayında alanı döndürüyor.</summary>
        static Bounds AddNoClimb(Transform crates)
        {
            bool any = false;
            var area = new Bounds();
            foreach (Transform child in crates)
            {
                if (child.name == NoClimbName || child.GetComponentInChildren<KitchenItem>(true) != null) continue;
                if (child.GetComponentInChildren<MeshFilter>() == null) continue;

                Bounds part = MeshBounds(child, crates);
                if (any)
                {
                    area.Encapsulate(part);
                }
                else
                {
                    area = part;
                    any = true;
                }
            }

            if (!any) return area;

            Transform existing = crates.Find(NoClimbName);
            GameObject blocker = existing != null ? existing.gameObject : new GameObject(NoClimbName);
            blocker.transform.SetParent(crates, false);
            blocker.layer = IgnoreRaycastLayer;
            blocker.transform.localPosition = new Vector3(area.center.x, area.min.y + NoClimbHeight / 2f, area.center.z);
            blocker.transform.localRotation = Quaternion.identity;
            blocker.transform.localScale = Vector3.one;
            if (!blocker.TryGetComponent(out BoxCollider box)) box = blocker.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(area.size.x + 0.02f, NoClimbHeight, area.size.z + 0.02f);
            return area;
        }

        static void PushSpawns(Scene scene, Transform crates, Bounds area)
        {
            Transform points = FindInScene(scene, "SpawnPoints");
            if (points == null || area.size == Vector3.zero) return;

            foreach (Transform spawn in points)
            {
                Vector3 local = crates.InverseTransformPoint(spawn.position);
                bool near = local.x > area.min.x - SpawnClearance && local.x < area.max.x + SpawnClearance
                    && local.z > area.min.z - SpawnClearance && local.z < area.max.z + SpawnClearance;
                if (!near) continue;

                // Duvardan odaya doğru, alanın önüne.
                local.z = local.z >= area.center.z ? area.max.z + SpawnClearance : area.min.z - SpawnClearance;
                spawn.position = crates.TransformPoint(local);
                Debug.Log($"[Dishaster] {spawn.name} kasaların önüne alındı: {spawn.position}");
            }
        }

        // ── YARDIMCILAR ─────────────────────────────────────────────────────

        static Transform FindInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == objectName)
                        return child;

            return null;
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }

            return null;
        }
    }
}
