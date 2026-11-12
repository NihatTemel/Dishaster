using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Uçtan uca otomatik test: build "-dishasterAutotest" bayrağıyla açılınca (başka türlü hiç çalışmıyor) local host
    /// açıyor, Görmedim'i seçip oyuna giriyor ve ilk tarifi (domatesli tabak) gerçek mutfak mesajlarıyla baştan sona
    /// yapıyor: yığından tabak al, adaya koy, kasadan domates al, tahtaya koy, bıçakla 3 dilim doğra, dilimleri tabağa
    /// koy. Ayrıca çömelme, yerdeki kasaya uzanma ve adanın dışına (kiler tezgahı, buzdolabı rafı, kasa) bırakma.
    /// Sonunda tarifin tamamlandığını ve hiç hata/istisna loglanmadığını denetleyip çıkıyor (çıkış kodu 0 = geçti).
    /// Sonuçlar "[AUTOTEST]" önekiyle.
    /// </summary>
    // Çekimler kare sonunda (karakterin kolları LateUpdate'te yerleştikten sonra) alınıyor.
    [DefaultExecutionOrder(1000)]
    public class DishasterAutoTest : MonoBehaviour
    {
        const string Flag = "-dishasterAutotest";
        // "-dishasterRole mute": Bilmiyorum olarak defter duruşları ve yürüme; varsayılan Görmedim ve tarif.
        const string RoleFlag = "-dishasterRole";
        // "-dishasterShots <klasör>": önemli anlarda ekran görüntüsü (birinci ve üçüncü şahıs).
        const string ShotsFlag = "-dishasterShots";

        // "-dishasterPlayers 2": host ikinci oyuncuyu bekliyor; "-dishasterRole observe": local'e katılan gözlemci
        // (Duymadım), diğer oyuncuyu önden periyodik fotoğraflıyor (uzaktan nasıl göründüğü).
        const string PlayersFlag = "-dishasterPlayers";
        // "-dishasterPort 7790": testler editörde açık oyunla (7777) çakışmasın.
        const string PortFlag = "-dishasterPort";

        string _shots;
        bool _mute;
        bool _deaf;
        bool _observe;
        int _players = 1;
        ushort _port;
        const float TimeoutSeconds = 420f;

        readonly List<string> _errors = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!System.Environment.GetCommandLineArgs().Contains(Flag)) return;

            var go = new GameObject("DishasterAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<DishasterAutoTest>();
        }

        void Awake()
        {
            Application.logMessageReceived += OnLog;

            // Pencere yokken kare sınırsız hızlanıyor: karakterin bir karedeki hareketi CharacterController'ın en küçük
            // adımının altına düşüyordu (yürüme testi yerinde sayıyordu). Oyundaki gibi 60 kare.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == ShotsFlag) _shots = args[i + 1];
                if (args[i] == RoleFlag) _mute = args[i + 1] == "mute";
                if (args[i] == RoleFlag) _deaf = args[i + 1] == "deaf";
                if (args[i] == RoleFlag) _observe = args[i + 1] == "observe";
                if (args[i] == PlayersFlag) int.TryParse(args[i + 1], out _players);
                if (args[i] == PortFlag) ushort.TryParse(args[i + 1], out _port);
            }

            if (_shots != null) System.IO.Directory.CreateDirectory(_shots);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (message.StartsWith("[AUTOTEST]")) return;
            _errors.Add(type + ": " + message + (type == LogType.Exception ? "\n" + stackTrace : ""));
        }

        static void Log(string message) => Debug.Log("[AUTOTEST] " + message);

        IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            IEnumerator run = Run();
            while (true)
            {
                bool more;
                try
                {
                    more = run.MoveNext();
                }
                catch (System.Exception exception)
                {
                    Finish(false, "istisna: " + exception);
                    yield break;
                }

                if (!more) yield break;
                if (Time.realtimeSinceStartup > deadline)
                {
                    Finish(false, "zaman aşımı");
                    yield break;
                }

                yield return run.Current;
            }
        }

        IEnumerator Run()
        {
            // ── Lobi ─────────────────────────────────────────────────────────
            while (DishasterNetworkManager.Instance == null) yield return null;
            DishasterNetworkManager manager = DishasterNetworkManager.Instance;
            if (_port != 0 && manager.localTransport is PortTransport portTransport) portTransport.Port = _port;
            if (_observe)
            {
                IEnumerator observe = RunObserver(manager);
                while (observe.MoveNext()) yield return observe.Current;
                yield break;
            }

            manager.minPlayers = Mathf.Max(1, _players);
            manager.HostLocal();
            Log("host açıldı");

            DishasterRoomPlayer room = null;
            while (room == null)
            {
                if (NetworkClient.localPlayer != null) NetworkClient.localPlayer.TryGetComponent(out room);
                yield return null;
            }

            int role = _mute ? MonkeyRoles.Mute : _deaf ? MonkeyRoles.Deaf : MonkeyRoles.LowVision;
            room.CmdSelectRole(role);
            while (room.monkeyRole != role) yield return null;
            room.CmdChangeReadyState(true);
            while (!manager.allPlayersReady || manager.roomSlots.Count < _players) yield return null;
            manager.StartGame();
            Log("oyun başlatıldı (" + MonkeyRoles.NameOf(role) + ")");

            // ── Oyun ─────────────────────────────────────────────────────────
            DishasterPlayer player = null;
            while (player == null || KitchenCounter.Instance == null || !KitchenCounter.Instance.Recipe.HasTargets)
            {
                if (NetworkClient.localPlayer != null) NetworkClient.localPlayer.TryGetComponent(out player);
                yield return null;
            }

            _player = player;
            _spawnPosition = player.transform.position;
            _spawnRotation = player.transform.rotation;

            yield return new WaitForSeconds(1f);
            KitchenCounter counter = KitchenCounter.Instance;
            Recipe recipe = counter.Recipe;
            Log($"tarif: {recipe.Title}, {recipe.TargetOf(Ingredient.Tomato)} dilim domates");

            Check(player.GetComponentInChildren<CharacterBody>() != null, "karakter giydirilmedi");
            Check(CharacterBody.FindBone(player.transform, "LeftHand") != null, "karakterin elleri yok");

            // Gözlemci boştaki halini ölçebilsin.
            if (_players > 1) yield return new WaitForSeconds(5f);

            if (_deaf)
            {
                IEnumerator deafRun = RunIdle(player, "deaf");
                while (deafRun.MoveNext()) yield return deafRun.Current;
                yield break;
            }

            if (_mute)
            {
                IEnumerator notebookRun = RunNotebook(player);
                while (notebookRun.MoveNext()) yield return notebookRun.Current;
                yield break;
            }

            yield return Shot("blind_fp_idle");
            yield return Third("blind_3p_idle", player, false);

            IEnumerator pantry = ShowPantry(counter, player);
            while (pantry.MoveNext()) yield return pantry.Current;

            IEnumerator overlap = CheckNoOverlap(counter);
            while (overlap.MoveNext()) yield return overlap.Current;

            IEnumerator grips = ShowGrips(counter, player);
            while (grips.MoveNext()) yield return grips.Current;

            IEnumerator crouch = CheckCrouch(player);
            while (crouch.MoveNext()) yield return crouch.Current;

            IEnumerator reach = CheckCrateReach(counter, player);
            while (reach.MoveNext()) yield return reach.Current;

            // Buzdolabının kapakları (raflara bırakma testi için de açık kalıyor).
            foreach (KitchenDoor door in FindObjectsOfType<KitchenDoor>())
            {
                if (door.panel == null) continue;
                yield return Approach(door.panel.bounds.center);
                counter.RequestGrabOrDrop(door.panel.bounds.center);
                yield return Wait();
            }

            yield return new WaitForSeconds(0.5f);
            yield return Look("fridge_open", new Vector3(-3.3f, 1.5f, -0.53f), new Vector3(-4.6f, 1.15f, -0.53f));

            IEnumerator tomatoPlate = RunTomatoPlate(counter, player);
            while (tomatoPlate.MoveNext()) yield return tomatoPlate.Current;

            IEnumerator drops = CheckDrops(counter);
            while (drops.MoveNext()) yield return drops.Current;

            // ESC menüsü (herkesin gördüğü).
            if (GameHud.Instance != null)
            {
                GameHud.Instance.pausePanel.SetActive(true);
                yield return LookUi("ui_pause", new Vector3(-0.5f, 1.7f, -2.2f), counter.transform.position + Vector3.up * 0.9f);
                GameHud.Instance.pausePanel.SetActive(false);
            }

            // Biraz daha oynat: efektler, karakterler, eller hata vermesin.
            yield return new WaitForSeconds(0.3f);
            yield return Shot("blind_fp_done");
            yield return new WaitForSeconds(2f);
            Finish(_failures.Count == 0 && _errors.Count == 0, null);
        }

        // ── Domatesli tabak (ilk tarif) ──────────────────────────────────────

        /// <summary>
        /// Yığından tabak al, adaya koy; kasadan domates al, tahtaya koy, bıçakla 3 dilim; dilimleri tabağa koy →
        /// tamamlanmalı. Sonra 4. dilim: adım kırmızı (fazla); geri alınınca yine tamam. Yığındaki alt tabak alınamıyor.
        /// </summary>
        IEnumerator RunTomatoPlate(KitchenCounter counter, DishasterPlayer player)
        {
            Recipe recipe = counter.Recipe;
            Check(recipe.Kind == RecipeKind.TomatoPlate, "ilk tarif domatesli tabak değil");
            Log("[tarif] " + recipe.Describe().Replace("\n", " | "));

            // Tabak: en yüksek yığının en üstündeki; altındaki alınamıyor.
            KitchenPlate[] plates = counter.Plates.Where(dish => dish != null && dish.gameObject.activeInHierarchy).ToArray();
            Log($"[tarif] {plates.Length} tabak: " + string.Join(", ", plates.Select(dish => dish.transform.position.ToString("F2"))));
            Check(plates.Length >= 2, "alınabilir tabak yok");
            if (plates.Length == 0) yield break;

            KitchenPlate plate = plates.OrderByDescending(dish => dish.transform.position.y).First();
            KitchenPlate under = plates.Where(dish => dish != plate && Vector2.Distance(Flat(dish.transform.position), Flat(plate.transform.position)) < 0.05f)
                .OrderByDescending(dish => dish.transform.position.y).FirstOrDefault();
            if (under != null)
            {
                yield return Approach(under.Item.Center);
                counter.RequestGrabOrDrop(under.Item.Center);
                yield return Wait();
                Check(counter.Held != under, "yığının altındaki tabak çekilip alındı");
                Log($"[tarif] alttaki tabağa tıklandı: elde {(counter.Held != null ? counter.Held.name : "yok")}");
            }

            if (counter.Held != plate.Item)
            {
                if (counter.Held != null) yield return Drop(counter, new Vector3(0.7f, 0f, 0.6f));
                yield return Grab(counter, plate.Item);
            }

            if (counter.Held == plate.Item)
            {
                yield return ShowGrip("plate", player);
                yield return Drop(counter, new Vector3(-0.5f, 0f, 0.45f));
            }

            Check(counter.IsOnIsland(plate.transform.position), "tabak adaya konmadı");
            Check(recipe.StepState(1) == RecipeStepState.Done, "adım 2 (tabak adada) tamamlanmadı");

            // Domates: kasadan (karıştırılmış kasalar; hangisinde olduğu belli değil).
            KitchenItem tomato = counter.Items.FirstOrDefault(item => item != null && item.gameObject.activeInHierarchy && item.IsCuttable
                && item.name.StartsWith("Art_Tomato") && item.transform.parent != null && item.transform.parent.name == "Crates"
                && !counter.IsOnIsland(item.transform.position));
            Check(tomato != null, "kasada domates yok");
            if (tomato == null) yield break;

            yield return Grab(counter, tomato);
            Vector3 board = counter.cuttingBoard.position + Vector3.up * (counter.boardHeight + 0.01f);
            yield return Approach(board);
            counter.RequestGrabOrDrop(board);
            yield return Wait();
            Check(counter.Held == null, "domates tahtaya bırakılamadı");

            KitchenItem knife = counter.Items.First(item => item != null && item.kind == KitchenItemKind.Knife);
            yield return Grab(counter, knife);
            yield return Approach(tomato.transform.position);
            int target = recipe.TargetOf(Ingredient.Tomato);
            for (int i = 0; i < target; i++)
            {
                counter.RequestCut(tomato.transform.position);
                yield return new WaitForSeconds(0.35f);
            }

            Check(recipe.StepState(0) == RecipeStepState.Done, "adım 1 (doğra) tamamlanmadı");
            yield return Drop(counter, new Vector3(0.75f, 0f, -0.62f));

            // Dilimler tabağa.
            for (int i = 0; i < target; i++)
            {
                KitchenItem slice = FreeSlice(counter, plate);
                Check(slice != null, "tabağa konacak dilim yok");
                if (slice == null) break;

                yield return Grab(counter, slice);
                yield return Approach(plate.Item.Center + Vector3.up * 0.02f, plate.Item);
                counter.RequestGrabOrDrop(plate.Item.Center + Vector3.up * 0.03f);
                yield return Wait();
                Check(plate.CountOf(Ingredient.Tomato) == i + 1, $"{i + 1}. dilim tabağa konmadı (tabakta {plate.CountOf(Ingredient.Tomato)})");
            }

            yield return new WaitForSeconds(0.4f);
            Log($"[tarif] tabakta {plate.CountOf(Ingredient.Tomato)} dilim domates; tamamlandı {recipe.Completed}, bozuldu {recipe.Failed}");
            Check(recipe.StepState(2) == RecipeStepState.Done, "adım 3 (3 dilim tabakta) tamamlanmadı");
            Check(recipe.Completed && !recipe.Failed, "domatesli tabak tamamlanmadı");

            Vector3 plateTop = plate.Item.Center;
            yield return Look("plate_tomatoes", plateTop + new Vector3(0.05f, 0.55f, -0.35f), plateTop);
            yield return ShowCard("ui_card_done", plateTop);
            yield return LookUi("ui_result", plateTop + new Vector3(0.3f, 0.9f, -1.3f), plateTop);

            // Fazlası: bir dilim daha kes, tabağa koy → adım kırmızı; geri al → yine tamam.
            if (tomato.gameObject.activeSelf)
            {
                yield return Grab(counter, knife);
                yield return Approach(tomato.transform.position);
                counter.RequestCut(tomato.transform.position);
                yield return new WaitForSeconds(0.4f);
                yield return Drop(counter, new Vector3(0.75f, 0f, -0.62f));

                KitchenItem extra = FreeSlice(counter, plate);
                if (extra != null)
                {
                    yield return Grab(counter, extra);
                    yield return Approach(plate.Item.Center + Vector3.up * 0.02f, plate.Item);
                    counter.RequestGrabOrDrop(plate.Item.Center + Vector3.up * 0.03f);
                    yield return Wait();
                    Check(recipe.StepState(2) == RecipeStepState.Over, "tabakta 4 dilim varken adım kırmızı (fazla) değil");
                    yield return ShowCard("ui_card_over", plateTop);

                    yield return Grab(counter, extra);
                    Check(counter.Held == extra, "tabaktaki dilim geri alınamadı");
                    Check(plate.CountOf(Ingredient.Tomato) == target && recipe.StepState(2) == RecipeStepState.Done, "dilim geri alınınca adım düzelmedi");
                    yield return Drop(counter, new Vector3(0.75f, 0f, -0.3f));
                }
            }
        }

        static Vector2 Flat(Vector3 point) => new Vector2(point.x, point.z);

        /// <summary>Tabakta olmayan, ortadaki domates dilimi.</summary>
        static KitchenItem FreeSlice(KitchenCounter counter, KitchenPlate plate)
        {
            return counter.Items.FirstOrDefault(item => item != null && item.gameObject.activeInHierarchy && item.Ingredient != null
                && item.Ingredient.ingredient == Ingredient.Tomato && !plate.Holds(item) && item != counter.Held);
        }

        /// <summary>Tarif kartı (normalde sadece Bilmiyorum görüyor): çekim için bir an açılıyor.</summary>
        IEnumerator ShowCard(string name, Vector3 focus)
        {
            RecipeHud hud = FindObjectOfType<RecipeHud>();
            if (hud == null || _shots == null) yield break;

            bool was = hud.recipePanel.activeSelf;
            hud.recipePanel.SetActive(true);
            yield return null;
            yield return LookUi(name, focus + new Vector3(0.3f, 0.9f, -1.3f), focus);
            hud.recipePanel.SetActive(was);
        }

        // ── Çömelme ve kasaya uzanma ────────────────────────────────────────

        /// <summary>
        /// Ctrl: kamera ve karakter aynı kadar iniyor, ayaklar yerde kalıyor; çömelerek yürüyor; kalkınca eski haline
        /// dönüyor.
        /// </summary>
        IEnumerator CheckCrouch(DishasterPlayer player)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            controller.enabled = true;
            player.FaceTowards(player.headPivot.position + _spawnRotation * Vector3.forward * 3f);
            yield return new WaitForSeconds(0.6f);

            CharacterBody body = player.GetComponentInChildren<CharacterBody>();
            float standEye = player.playerCamera.transform.position.y;
            float standFeet = Feet(player);
            float standHeight = controller.height;

            player.SetCrouchForTest(true);
            yield return new WaitForSeconds(0.7f);
            float eye = player.playerCamera.transform.position.y;
            float feet = Feet(player);
            Log($"[crouch] göz {standEye:0.00} → {eye:0.00} m (iniş {(standEye - eye) * 100f:0} cm, beklenen {player.CrouchDrop * 100f:0}), " +
                $"kalça {body.Squat * 100f:0} cm, ayak {standFeet:0.00} → {feet:0.00}, kapsül {standHeight:0.00} → {controller.height:0.00}");
            Check(player.CrouchAmount > 0.99f, "çömelme tamamlanmadı");
            Check(Mathf.Abs(standEye - eye - player.CrouchDrop) < 0.04f, "çömelince kamera beklenen kadar inmedi");
            Check(Mathf.Abs(body.Squat - player.CrouchDrop) < 0.02f, "çömelince karakter kamerayla birlikte inmedi");
            Check(Mathf.Abs(feet - standFeet) < 0.03f, "çömelince ayaklar yerden kalktı ya da yere gömüldü");
            Check(controller.height < standHeight - 0.3f, "çömelince kapsül kısalmadı");
            yield return Whole("blind_full_crouch", player, false);
            yield return Whole("blind_full_crouch_side", player, true);

            // Çömelerek yürüme, odanın ortasına doğru: kapsülü test sürüyor (animasyon konum farkından).
            // Önü en açık yön (kapsülün süpürdüğü yol).
            Vector3 walk = player.transform.forward;
            float open = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, 45f * i, 0f) * Vector3.forward;
                Vector3 bottom = player.transform.position + Vector3.down * 0.4f;
                float free = Physics.CapsuleCast(bottom, bottom + Vector3.up * 0.6f, controller.radius * 0.9f, direction, out RaycastHit hit, 4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.distance : 4f;
                if (free <= open) continue;
                open = free;
                walk = direction;
            }
            player.transform.rotation = Quaternion.LookRotation(walk);
            Vector3 walkStart = player.transform.position;
            float until = Time.time + 1.4f;
            bool shot = false;
            float lowest = float.PositiveInfinity;
            float highest = float.NegativeInfinity;
            while (Time.time < until)
            {
                controller.Move(walk * player.crouchSpeed * Time.deltaTime + Vector3.down * 0.2f * Time.deltaTime);
                float y = Feet(player);
                lowest = Mathf.Min(lowest, y);
                highest = Mathf.Max(highest, y);
                if (!shot && Time.time > until - 0.6f)
                {
                    shot = true;
                    yield return Whole("blind_full_crouch_walk_side", player, true);
                }

                yield return null;
            }

            Log($"[crouch] çömelerek {Vector3.Distance(walkStart, player.transform.position):0.00} m yürüdü; en alçak ayak {lowest:0.00}, en yüksek ayak {highest:0.00}");
            Check(lowest > standFeet - 0.05f, "çömelerek yürürken ayak yere gömülüyor");

            player.SetCrouchForTest(false);
            yield return new WaitForSeconds(0.8f);
            float back = player.playerCamera.transform.position.y;
            Log($"[crouch] kalkınca göz {back:0.00} m, kalça {body.Squat * 100f:0} cm, kapsül {controller.height:0.00}");
            Check(Mathf.Abs(back - standEye) < 0.03f, "kalkınca kamera eski yüksekliğine dönmedi");
            Check(body.Squat < 0.01f, "kalkınca karakter çömelik kaldı");
            Check(Mathf.Abs(controller.height - standHeight) < 0.01f, "kalkınca kapsül eski boyuna dönmedi");
        }

        /// <summary>Oyuncunun tamamı (ayaklar dahil), önden ya da yandan: çömelme ve dizler görünsün.</summary>
        IEnumerator Whole(string name, DishasterPlayer player, bool side)
        {
            if (_shots == null) yield break;

            CharacterBody body = player.GetComponentInChildren<CharacterBody>();
            Renderer[] bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var modes = new UnityEngine.Rendering.ShadowCastingMode[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                modes[i] = bodyRenderers[i].shadowCastingMode;
                bodyRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            var cameraObject = new GameObject("WholeCamera");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false;
            view.fieldOfView = 45f;
            view.nearClipPlane = 0.05f;
            Transform root = player.transform;
            Vector3 direction = side ? root.right : root.forward;
            Vector3 feet = root.position + Vector3.down * 1f;
            cameraObject.transform.position = feet + direction * 3.2f + Vector3.up * 0.9f;
            cameraObject.transform.LookAt(feet + Vector3.up * 0.75f);

            yield return null;
            yield return Capture(view, name);
            yield return null;
            Destroy(cameraObject);
            for (int i = 0; i < bodyRenderers.Length; i++) bodyRenderers[i].shadowCastingMode = modes[i];
        }

        static float Feet(DishasterPlayer player)
        {
            Transform left = CharacterBody.FindBone(player.transform, "LeftFoot");
            Transform right = CharacterBody.FindBone(player.transform, "RightFoot");
            return left != null && right != null ? Mathf.Min(left.position.y, right.position.y) : 0f;
        }

        /// <summary>
        /// Yerdeki (ön sıra) ve arka sıradaki kasaya ayakta, kasaların önünden bakınca el değiyor ve yetişiyor
        /// (karakter eğilip çömeliyor); kasadaki yiyecek alınıyor.
        /// </summary>
        IEnumerator CheckCrateReach(KitchenCounter counter, DishasterPlayer player)
        {
            KitchenCrate[] crates = FindObjectsOfType<KitchenCrate>().OrderBy(crate => crate.transform.position.y).ToArray();
            if (crates.Length == 0) yield break;

            CharacterBody body = player.GetComponentInChildren<CharacterBody>();
            foreach ((KitchenCrate crate, string label) in new[] { (crates[crates.Length / 4], "ön"), (crates[crates.Length - 2], "arka") })
            {
                // Kasadaki, önden görülen bir yiyecek (önündeki kasanın duvarı ya da uzun havuç ışını kesebiliyor): ışının
                // değdiği alınmalı.
                KitchenItem[] candidates = counter.Items
                    .Where(item => item != null && item.gameObject.activeInHierarchy && item.transform.parent != null && item.transform.parent.name == "Crates")
                    .OrderBy(item => Vector3.Distance(item.transform.position, crate.transform.position))
                    .Take(5).ToArray();
                KitchenItem food = null;
                foreach (KitchenItem candidate in candidates)
                {
                    yield return Approach(candidate.Center, candidate);
                    yield return new WaitForSeconds(0.9f);
                    Transform eye = player.playerCamera.transform;
                    if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit seen, LowVisionRole.ReachAlong(eye.forward), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) continue;

                    food = seen.collider.GetComponentInParent<KitchenItem>();
                    if (food != null) break;
                }

                Check(food != null, $"{label} sıradaki kasada önden görülen yiyecek yok");
                if (food == null) continue;
                float distance = Vector3.Distance(player.headPivot.position, food.Center);
                Log($"[reach] {label} sıradaki kasa ({food.name}): değiyor {LowVisionRole.Touching}, gözden {distance:0.00} m, " +
                    $"avuç farkı {body.ReachGap * 100f:0} cm | {body.Describe()}");
                Check(LowVisionRole.Touching, $"{label} sıradaki kasadaki yiyeceğe el değmiyor");
                // Avucun ortası hedefte ya da parmak boyu kadar yakınında (en uzak köşede, kısa kollu Aşçı parmak uçlarıyla değiyor).
                Check(body.ReachGap < 0.10f, $"{label} sıradaki kasaya el yetişmiyor ({body.ReachGap * 100f:0} cm)");
                string tag = label == "ön" ? "front" : "back";
                yield return Shot($"blind_fp_reach_{tag}");
                LowVisionEffect effect = player.playerCamera.GetComponent<LowVisionEffect>();
                if (effect != null) effect.enabled = false;
                yield return Shot($"blind_fp_reach_{tag}_plain");
                if (effect != null) effect.enabled = true;
                yield return Third($"blind_3p_reach_{tag}", player, true);
                yield return Near($"blind_near_reach_{tag}", player, LowVisionRole.Hand.position, 0.7f);

                counter.RequestGrabOrDrop(LowVisionRole.AimPoint);
                yield return Wait();
                Check(counter.Held == food, $"{label} sıradaki kasadaki yiyecek alınamadı");
                if (counter.Held != food) continue;

                // Yerine geri: kasanın içine bırakılıyor.
                Vector3 back = food.Center;
                yield return new WaitForSeconds(0.3f);
                counter.RequestGrabOrDrop(LowVisionRole.AimPoint);
                yield return Wait();
                Check(counter.Held == null, $"{label} sıradaki kasaya geri bırakılamadı");
                Log($"[reach] {food.name} kasaya geri bırakıldı: {food.transform.position.ToString("F2")} (alındığı yer {back.ToString("F2")})");
            }
        }

        // ── Bırakma: adanın dışında ─────────────────────────────────────────

        int _refused;

        void OnKitchenEvent(KitchenEventMessage message, KitchenItem item)
        {
            if (message.type == KitchenEventType.Refused) _refused++;
        }

        /// <summary>
        /// Kilerdeki tezgaha, buzdolabının alt rafına ve kasaya bırakılıyor (düz yüzey, üstü açık); adanın yan
        /// yüzüne (dik, altı boş) bırakılamıyor: elde kalıyor, "olmaz" sesi.
        /// </summary>
        IEnumerator CheckDrops(KitchenCounter counter)
        {
            KitchenCounter.Applied += OnKitchenEvent;
            // Ön sıradan (yerdeki kasalar) bir yiyecek: arkadaki uzun havuçlar ışını kesmesin.
            KitchenItem onion = counter.Items
                .Where(item => item != null && item.gameObject.activeInHierarchy && item.IsCuttable && item.transform.parent != null
                    && item.transform.parent.name == "Crates" && item.transform.position.y < 0.2f && !item.name.StartsWith("Art_Carrot"))
                .OrderBy(item => Mathf.Abs(item.transform.position.x - 3f)).FirstOrDefault();
            Check(onion != null, "ön sıradaki kasada yiyecek yok");
            if (onion == null) yield break;

            Vector3 home = onion.transform.position;
            yield return Grab(counter, onion);

            var spots = new (string label, Vector3 point)[]
            {
                ("kilerdeki tezgah", new Vector3(4.55f, 0.92f, 0.3f)),
                ("buzdolabının alt rafı", new Vector3(-4.42f, 0.5f, -0.45f)),
                ("kasa", home + Vector3.up * 0.02f),
            };

            foreach ((string label, Vector3 point) in spots)
            {
                if (counter.Held != onion)
                {
                    yield return Grab(counter, onion);
                    if (counter.Held != onion) break;
                }

                yield return Approach(point);
                counter.RequestGrabOrDrop(LowVisionRole.Touching ? LowVisionRole.AimPoint : point);
                yield return Wait();
                Log($"[drop] {label}: {(counter.Held == null ? "bırakıldı " + onion.transform.position.ToString("F2") : "bırakılamadı: " + counter.LastRefusal)} (hedef {point.ToString("F2")})");
                Check(counter.Held == null, $"{label} üstüne bırakılamadı");
                Check(counter.Held != null || Vector3.Distance(onion.transform.position, point) < 0.4f, $"{label}: çok uzağa bırakıldı");
                if (label == "buzdolabının alt rafı") yield return Look("fridge_drop", new Vector3(-3.6f, 1.2f, -0.45f), onion.transform.position);
            }

            // Adanın yan yüzü: altında 25 cm'den yakın düz yer yok.
            if (counter.Held != onion) yield return Grab(counter, onion);
            if (counter.Held == onion)
            {
                int before = _refused;
                Vector3 side = counter.transform.TransformPoint(new Vector3(counter.topSize.x / 2f + 0.02f, 0.75f, 0f));
                counter.RequestGrabOrDrop(side);
                yield return Wait();
                Log($"[drop] adanın yan yüzü: {(counter.Held == onion ? "elde kaldı" : "bırakıldı " + onion.transform.position.ToString("F2"))}, olmaz sesi {_refused - before}");
                Check(counter.Held == onion, "dik yüzeye bırakılabildi");
                Check(_refused > before, "bırakılamayınca \"olmaz\" olayı gelmedi");
                yield return Drop(counter, new Vector3(0.75f, 0f, 0.62f));
            }

            KitchenCounter.Applied -= OnKitchenEvent;
        }

        // ── Bilmiyorum: defter duruşları ve yürüme ───────────────────────────

        IEnumerator RunNotebook(DishasterPlayer player)
        {
            NotebookRole notebook = NotebookRole.Instance;
            Check(notebook != null && notebook.Owner == player, "defter Bilmiyorum'a bağlanmadı");
            if (notebook == null) yield break;

            yield return new WaitForSeconds(1f);
            Measure("hold", player, notebook);
            yield return Shot("mute_fp_hold");
            bool prompt = notebook.prompt.activeSelf;
            notebook.prompt.SetActive(true);
            yield return ShotUi("mute_ui_hold");
            notebook.prompt.SetActive(prompt);
            yield return Third("mute_3p_hold", player, false);
            yield return Near("mute_near_hold", player, notebook.LeftGripPoint, -0.6f);

            notebook.SetPoseForTest(NotebookPose.Draw);
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f;
                float b = (i + 1) / 12f;
                notebook.page.LocalStroke(new Vector2(0.2f + 0.6f * a, 0.5f + 0.2f * Mathf.Sin(a * 6f)), new Vector2(0.2f + 0.6f * b, 0.5f + 0.2f * Mathf.Sin(b * 6f)), 0, 2);
            }

            yield return new WaitForSeconds(0.3f);
            Measure("draw", player, notebook);
            yield return Shot("mute_fp_draw");
            bool toolbar = notebook.toolbar.activeSelf;
            notebook.toolbar.SetActive(true);
            yield return ShotUi("mute_ui_draw");
            notebook.toolbar.SetActive(toolbar);
            yield return Third("mute_3p_draw", player, false);
            yield return Near("mute_near_draw", player, notebook.PenGripPoint, 0.6f);

            // Çiz → göster ve göster → tut dönüşleri boyunca: bilek kırılmamalı, parmak defterin içinden geçmemeli.
            CharacterBody turnBody = player.GetComponentInChildren<CharacterBody>();
            notebook.SetPoseForTest(NotebookPose.Show);
            yield return WatchTurn("draw→show", player, turnBody, notebook, 0.3f);
            yield return Shot("mute_fp_turn");
            yield return Near("mute_near_turn", player, notebook.LeftGripPoint, -0.6f);
            yield return WatchTurn("draw→show", player, turnBody, notebook, 0.7f);
            Measure("show", player, notebook);
            yield return Shot("mute_fp_show");
            yield return Third("mute_3p_show", player, false);
            yield return Near("mute_near_show", player, notebook.LeftGripPoint, -0.6f);

            notebook.SetPoseForTest(NotebookPose.Hold);
            yield return WatchTurn("show→hold", player, turnBody, notebook, 1f);
            notebook.SetPoseForTest(NotebookPose.Show);
            yield return WatchTurn("hold→show", player, turnBody, notebook, 0.2f);
            yield return Near("mute_near_turn2", player, notebook.LeftGripPoint, -0.6f);
            yield return WatchTurn("hold→show", player, turnBody, notebook, 0.8f);
            notebook.SetPoseForTest(NotebookPose.Hold);
            yield return WatchTurn("show→hold", player, turnBody, notebook, 1f);
            CharacterController controller = player.GetComponent<CharacterController>();
            float until = Time.time + 1.4f;
            bool shot = false;
            while (Time.time < until)
            {
                controller.Move(player.transform.forward * 4f * Time.deltaTime + Vector3.down * 0.1f);
                if (!shot && Time.time > until - 0.5f)
                {
                    shot = true;
                    yield return Third("mute_3p_walk", player, true);
                }

                yield return null;
            }

            yield return new WaitForSeconds(1f);
            yield return Third("mute_3p_idle", player, false);
            Finish(_failures.Count == 0 && _errors.Count == 0, null);
        }

        /// <summary>Bir süre her kare: sol bileğin en çok bükülmesi ve defterin içinden geçen parmak kemiği sayısı.</summary>
        IEnumerator WatchTurn(string label, DishasterPlayer player, CharacterBody body, NotebookRole notebook, float seconds)
        {
            float maxBend = 0f;
            int maxThrough = 0;
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return null;
                maxBend = Mathf.Max(maxBend, body.WristBend(true));
                maxThrough = Mathf.Max(maxThrough, FingersThroughBook(player, notebook));
            }

            Log($"[show] dönüş {label} ({seconds:0.0} sn): sol bilek en çok {maxBend:0}°, defterden geçen parmak kemiği {maxThrough}");
            Check(maxBend <= 40f, $"dönüşte ({label}) bilek kırık gibi bükülüyor ({maxBend:0}°)");
            Check(maxThrough == 0, $"dönüşte ({label}) parmak defterin içinden geçiyor ({maxThrough} kemik)");
        }

        /// <summary>
        /// Sol elin defterin içinden geçen kemikleri: defterin alanında (kenarları içinde) ve kapağın içinde ya da sayfanın
        /// önünde. Kenardan dolanan başparmak alanın dışında kalıyor.
        /// </summary>
        static int FingersThroughBook(DishasterPlayer player, NotebookRole notebook)
        {
            Transform hand = CharacterBody.FindBone(player.transform, "LeftHand");
            if (hand == null) return 0;

            Transform book = notebook.notebook;
            Vector2 half = notebook.pageSize * 0.5f;
            int count = 0;
            foreach (Transform bone in hand.GetComponentsInChildren<Transform>())
            {
                if (bone == hand || !bone.name.StartsWith("LeftHand")) continue;

                Vector3 local = book.InverseTransformPoint(bone.position);
                if (Mathf.Abs(local.x) < half.x && Mathf.Abs(local.y) < half.y && local.z < 0.01f && local.z > -0.04f) count++;
            }

            return count;
        }

        /// <summary>Kameraya göre konumlar (x sağ, y yukarı, z ileri): karakterin bilekleri, defter, kalem; avuç farkı.</summary>
        void Measure(string label, DishasterPlayer player, NotebookRole notebook)
        {
            Transform view = player.playerCamera.transform;
            string Local(Vector3 world) => view.InverseTransformPoint(world).ToString("F2");

            Transform leftWrist = CharacterBody.FindBone(player.transform, "LeftHand");
            Transform rightWrist = CharacterBody.FindBone(player.transform, "RightHand");
            Transform pencil = CharacterBody.FindBone(notebook.rightHand, "Pencil");
            CharacterBody body = player.GetComponentInChildren<CharacterBody>();

            Log($"[{label}] notebook {Local(notebook.notebook.position)} leftGrip {Local(notebook.LeftGripPoint)} penGrip {Local(notebook.PenGripPoint)} kamera {view.localPosition.ToString("F2")} pivot {player.headPivot.localPosition.ToString("F2")}");
            if (leftWrist != null) Log($"[{label}] bilekler sol {Local(leftWrist.position)} sağ {Local(rightWrist.position)} | avuç farkı {(body != null ? body.LastSnapDistance * 100f : 0f):0.0} cm");
            if (pencil != null) Log($"[{label}] pencil {Local(pencil.position)} active {pencil.gameObject.activeInHierarchy} rendered {pencil.GetComponent<Renderer>().enabled}");
            if (body != null) Log($"[{label}] {body.Describe()}");

            // Bilek kırık görünmesin; defter kendi ekranını kapatmasın (çizerken hariç).
            float coverage = Coverage(player.playerCamera, notebook);
            int through = FingersThroughBook(player, notebook);
            Log($"[{label}] defter ekranın %{coverage * 100f:0}'ini kaplıyor; sol bilek {body.WristBend(true):0}°; defterden geçen parmak kemiği {through}");
            Check(through == 0, $"{label}: parmak defterin içinden geçiyor ({through} kemik)");
            Check(body.WristBend(true) <= 40f, $"{label}: defteri tutan bilek kırık gibi bükülüyor ({body.WristBend(true):0}°)");
            if (label != "draw") Check(coverage <= 0.25f, $"{label}: defter ekranın çoğunu kaplıyor (%{coverage * 100f:0})");
        }

        /// <summary>Defterin (kapakla) ekranda kapladığı alan (0-1): ekranda 32x18 noktadan ışın, defterin düzlemine.</summary>
        static float Coverage(Camera camera, NotebookRole notebook)
        {
            Transform book = notebook.notebook;
            var plane = new Plane(book.forward, book.position);
            Vector2 half = notebook.pageSize * 0.5f + new Vector2(0.01f, 0.01f);
            int inside = 0;
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 18; y++)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3((x + 0.5f) / 32f, (y + 0.5f) / 18f, 0f));
                    if (!plane.Raycast(ray, out float distance)) continue;

                    Vector3 local = book.InverseTransformPoint(ray.GetPoint(distance));
                    if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y) inside++;
                }
            }

            return inside / (32f * 18f);
        }

        // ── Boşta (Duymadım): eller köşede, kıpırdamıyor ─────────────────────

        IEnumerator RunIdle(DishasterPlayer player, string label)
        {
            yield return new WaitForSeconds(1f);
            yield return Shot(label + "_fp_idle");
            yield return Third(label + "_3p_idle", player, false);
            yield return Near(label + "_near_idle", player, player.headPivot.position + player.transform.forward * 0.3f - Vector3.up * 0.3f, -0.6f);

            // Boştaki eller gövdeye göre yerinde durmalı (dönmüyor, kaymıyor), sağ el sağda, sol el solda.
            Transform view = player.transform;
            Transform leftWrist = CharacterBody.FindBone(player.transform, "LeftHand");
            Transform rightWrist = CharacterBody.FindBone(player.transform, "RightHand");
            var left = new List<Vector3>();
            var right = new List<Vector3>();
            float until = Time.time + 2f;
            while (Time.time < until)
            {
                left.Add(view.InverseTransformPoint(leftWrist.position));
                right.Add(view.InverseTransformPoint(rightWrist.position));
                yield return null;
            }

            float spreadL = Spread(left);
            float spreadR = Spread(right);
            Log($"[{label}] bilekler (gövdeye göre): sol {left[left.Count - 1].ToString("F2")} sağ {right[right.Count - 1].ToString("F2")}, oynama sol {spreadL * 100f:0.0} cm sağ {spreadR * 100f:0.0} cm");
            Check(spreadL < 0.02f && spreadR < 0.02f, "boştaki eller kıpırdıyor");
            Check(left[0].x < -0.05f && right[0].x > 0.05f, "boştaki eller yanlış tarafta");

            // Aşağı bakınca da eller yerinde.
            player.FaceTowards(view.position + player.transform.forward * 1f + Vector3.down * 1.2f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot(label + "_fp_down");
            Finish(_failures.Count == 0 && _errors.Count == 0, null);
        }

        /// <summary>Noktaların ortalamadan en uzak sapması.</summary>
        static float Spread(List<Vector3> points)
        {
            if (points.Count == 0) return 0f;

            Vector3 mean = Vector3.zero;
            foreach (Vector3 point in points) mean += point;
            mean /= points.Count;

            float spread = 0f;
            foreach (Vector3 point in points) spread = Mathf.Max(spread, Vector3.Distance(point, mean));
            return spread;
        }

        // ── Gözlemci ─────────────────────────────────────────────────────────

        IEnumerator RunObserver(DishasterNetworkManager manager)
        {
            yield return new WaitForSeconds(2f);
            manager.JoinLocal();

            DishasterRoomPlayer room = null;
            while (room == null)
            {
                if (NetworkClient.localPlayer != null) NetworkClient.localPlayer.TryGetComponent(out room);
                yield return null;
            }

            // Boştaki ilk rol (host hangisini seçtiyse o hariç).
            int[] roles = { MonkeyRoles.Deaf, MonkeyRoles.Mute, MonkeyRoles.LowVision };
            foreach (int role in roles)
            {
                room.CmdSelectRole(role);
                float wait = Time.time + 1f;
                while (room.monkeyRole != role && Time.time < wait) yield return null;
                if (room.monkeyRole == role) break;
            }

            room.CmdChangeReadyState(true);
            Log("gözlemci hazır (" + MonkeyRoles.NameOf(room.monkeyRole) + ")");

            DishasterPlayer local = null;
            while (local == null)
            {
                if (NetworkClient.localPlayer != null) NetworkClient.localPlayer.TryGetComponent(out local);
                yield return null;
            }

            // Hareket etmesin, kimseyi engellemesin.
            local.GetComponent<CharacterController>().enabled = false;
            local.transform.position = new Vector3(0f, -10f, 0f);

            // Diğer oyuncu giydirilince: boştayken kolları yerinde mi (eskiden defter/eşya avuca oturtulup hedef
            // oradan okununca el kendini kovalıyor, kol pervane gibi dönüyordu).
            DishasterPlayer watched = null;
            CharacterBody watchedBody = null;
            while (watchedBody == null)
            {
                foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
                    if (identity != null && identity.TryGetComponent(out DishasterPlayer player) && !player.isLocalPlayer)
                        watched = player;

                if (watched != null) watchedBody = watched.GetComponentInChildren<CharacterBody>();
                yield return null;
            }

            yield return new WaitForSeconds(1f);
            Animator animator = watchedBody.GetComponent<Animator>();
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var lefts = new List<Vector3>();
            var rights = new List<Vector3>();
            float measureUntil = Time.time + 3f;
            while (Time.time < measureUntil)
            {
                lefts.Add(watchedBody.transform.InverseTransformPoint(leftHand.position));
                rights.Add(watchedBody.transform.InverseTransformPoint(rightHand.position));
                yield return null;
            }

            float armL = Spread(lefts);
            float armR = Spread(rights);
            Log($"[gözlem] {MonkeyRoles.NameOf(watched.role)} boşta kol oynaması: sol {armL * 100f:0.0} cm, sağ {armR * 100f:0.0} cm; sol bilek {lefts[0].ToString("F2")}, sağ bilek {rights[0].ToString("F2")}");
            Check(armL < 0.03f && armR < 0.03f, MonkeyRoles.NameOf(watched.role) + " boştayken kolu oynuyor");

            // Görmedim Ctrl'le çömelince burada da çömeliyor mu (sunucudan gelen): fotoğraf aralarında da izleniyor.
            float crouchSquat = -1f;
            void WatchCrouch()
            {
                if (watched.IsCrouching && watched.CrouchAmount > 0.99f) crouchSquat = Mathf.Max(crouchSquat, watchedBody.Squat);
            }

            float snapMax = 0f;
            float snapSum = 0f;
            int snapCount = 0;
            var snaps = new List<float>();

            for (int shot = 0; shot < 18; shot++)
            {
                // Arada: eşya/defter elde tutulurken IK'nın avuca ne kadar uzak kaldığı (iki taraf eşleşiyor mu).
                float next = Time.time + 2.5f;
                while (Time.time < next)
                {
                    WatchCrouch();

                    // Defter duruşu değişince yakın çekim: dönüşün ortası ve yerine oturmuş hali.
                    NotebookRole watchedBook = NotebookRole.Instance;
                    if (watchedBook != null && watchedBook.Owner == watched && watched != null)
                    {
                        if (watchedBook.Pose != _seenPose)
                        {
                            _seenPose = watchedBook.Pose;
                            _poseSince = Time.time;
                            _poseShots = 0;
                        }

                        float since = Time.time - _poseSince;
                        if (_poseShots == 0 && since >= 0.12f && _seenPose != NotebookPose.Hold)
                        {
                            _poseShots = 1;
                            yield return CloseUp("observe_" + _seenPose.ToString().ToLowerInvariant() + "_turn", watched, watchedBook);
                        }
                        else if (_poseShots <= 1 && since >= 1f)
                        {
                            _poseShots = 2;
                            yield return CloseUp("observe_" + _seenPose.ToString().ToLowerInvariant(), watched, watchedBook);
                        }
                    }

                    // Sadece yetişilebilecek hedeflerde (raf arkası gibi kolun hiç yetişmediği yerde iki tarafta da eşya avuca
                    // oturuyor; ölçülen, uzaktaki IK'nın aynı yere varıp varmadığı).
                    if (watchedBody != null && watchedBody.LastSnapDistance > 0f && watchedBody.RightReachable)
                    {
                        snapMax = Mathf.Max(snapMax, watchedBody.LastSnapDistance);
                        snapSum += watchedBody.LastSnapDistance;
                        snapCount++;
                        snaps.Add(watchedBody.LastSnapDistance);

                        // Avuç hedefinden uzak kaldıysa neden: eğilme, ağırlık, hedef ve avuç (karakterin uzayında).
                        if (watchedBody.LastSnapDistance > 0.08f && Time.time >= _nextSnapLog)
                        {
                            _nextSnapLog = Time.time + 0.4f;
                            NotebookRole book = NotebookRole.Instance;
                            string pose = book != null && book.Owner == watched ? (book.IsDrawing ? "çizim" : "defter") : "eşya";
                            Log($"[gözlem] avuç farkı {watchedBody.LastSnapDistance * 100f:0} cm ({pose}, hız {watched.GetComponent<CharacterController>().velocity.magnitude:0.0}): {watchedBody.Describe()}");
                        }
                    }

                    yield return null;
                }

                DishasterPlayer other = watched;
                if (other == null) break;

                var cameraObject = new GameObject("ObserverCamera");
                var view = cameraObject.AddComponent<Camera>();
                view.enabled = false;
                view.fieldOfView = 50f;
                view.nearClipPlane = 0.05f;
                Transform root = other.transform;
                Vector3 side = shot % 2 == 0 ? root.forward : (root.forward + root.right).normalized;
                cameraObject.transform.position = root.position + side * 2.4f + Vector3.up * 0.55f;
                cameraObject.transform.LookAt(root.position + Vector3.up * 0.25f);

                yield return null;
                yield return Capture(view, $"observe_{shot:00}");
                yield return null;
                Destroy(cameraObject);
            }

            snaps.Sort();
            float p95 = snaps.Count > 0 ? snaps[Mathf.Min(snaps.Count - 1, (int)(snaps.Count * 0.95f))] : 0f;
            float mean = snapCount > 0 ? snapSum / snapCount : 0f;
            Log($"[gözlem] elde tutarken avuç farkı: ortalama {mean * 100f:0.0} cm, %95 {p95 * 100f:0.0} cm, en çok {snapMax * 100f:0.0} cm ({snapCount} kare)");
            Check(mean < 0.05f && p95 < 0.12f, "uzaktan eşya/defter ele yetişmiyor (iki taraf eşleşmiyor)");

            // Görmedim Ctrl'le çömelince burada da çömeliyor mu (sunucudan gelen), kalkınca kalkıyor mu.
            if (watched.role == MonkeyRoles.LowVision)
            {
                float until = Time.time + 180f;
                while (Time.time < until && crouchSquat < 0f)
                {
                    WatchCrouch();
                    yield return null;
                }

                if (watched.IsCrouching)
                {
                    yield return new WaitForSeconds(0.3f);
                    yield return Third("observe_crouch", watched, true);
                }

                Log($"[gözlem] uzaktan çömelme: {(crouchSquat >= 0f ? "görüldü" : "görülmedi")}, kalça {crouchSquat * 100f:0} cm (beklenen {watched.CrouchDrop * 100f:0})");
                Check(crouchSquat >= 0f && Mathf.Abs(crouchSquat - watched.CrouchDrop) < 0.03f, "çömelme uzaktaki oyuncuya gitmiyor");

                until = Time.time + 20f;
                while (Time.time < until && watched.IsCrouching) yield return null;
                yield return new WaitForSeconds(0.6f);
                Log($"[gözlem] uzaktan kalkış: kalça {watchedBody.Squat * 100f:0} cm");
                Check(!watched.IsCrouching, "uzaktaki oyuncu çömelik kaldı");
            }

            Finish(_failures.Count == 0 && _errors.Count == 0, null);
        }

        // ── Ekran görüntüleri ────────────────────────────────────────────────

        IEnumerator Shot(string name)
        {
            if (_shots == null || _player == null) yield break;

            yield return Capture(_player.playerCamera, name);
        }

        // Pencere olmadan da (-batchmode, ekrana hiçbir şey gelmeden) çekilebilsin: kamera bir RenderTexture'a
        // çiziliyor, PNG'ye yazılıyor. İstek LateUpdate'te (kare sonunda) karşılanıyor.
        readonly List<(Camera view, string name, bool ui)> _pendingShots = new List<(Camera view, string name, bool ui)>();

        IEnumerator Capture(Camera view, string name, bool ui = false)
        {
            if (_shots == null || view == null) yield break;

            _pendingShots.Add((view, name, ui));
            while (_pendingShots.Count > 0) yield return null;
        }

        void LateUpdate()
        {
            if (_pendingShots.Count == 0) return;

            foreach ((Camera view, string name, bool ui) in _pendingShots)
                if (view != null) Render(view, name, ui);
            _pendingShots.Clear();
        }

        /// <summary>Oyuncunun kamerası, arayüzle birlikte (ekrandaki gibi).</summary>
        IEnumerator ShotUi(string name)
        {
            if (_shots == null || _player == null) yield break;

            yield return Capture(_player.playerCamera, name, true);
        }

        /// <summary>Bir yerden bakan geçici kamera, arayüzle birlikte (görmeyenin karanlık ekranı yerine mutfak görünsün).</summary>
        IEnumerator LookUi(string name, Vector3 from, Vector3 at)
        {
            if (_shots == null) yield break;

            var cameraObject = new GameObject("LookUiCamera");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false;
            view.fieldOfView = 55f;
            view.nearClipPlane = 0.03f;
            cameraObject.transform.position = from;
            cameraObject.transform.LookAt(at);

            yield return null;
            yield return Capture(view, name, true);
            yield return null;
            Destroy(cameraObject);
        }

        void Render(Camera view, string name, bool ui)
        {
            // Pencere yok: üstte çizilen arayüz (overlay) kameraya bağlanıp onunla çiziliyor, sonra geri alınıyor.
            var switched = new List<(Canvas canvas, RenderMode mode, Camera camera, float plane)>();
            if (ui)
            {
                foreach (Canvas canvas in FindObjectsOfType<Canvas>())
                {
                    if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

                    switched.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance));
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = view;
                    canvas.planeDistance = view.nearClipPlane + 0.02f + 0.001f * (100 - Mathf.Clamp(canvas.sortingOrder, 0, 100));
                }

                // Teşhis: hangi arayüz parçası çok büyük ağ üretiyor.
                foreach ((Canvas canvas, RenderMode _, Camera _, float _) in switched)
                {
                    foreach (UnityEngine.UI.Graphic graphic in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>())
                    {
                        try
                        {
                            graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender);
                        }
                        catch (System.Exception exception)
                        {
                            Log($"[ui] {canvas.name}/{graphic.name} ({graphic.GetType().Name}, {graphic.rectTransform.rect.size}) ağ kurulamadı: {exception.Message}");
                        }
                    }
                }

                Canvas.ForceUpdateCanvases();
            }

            const int width = 1280;
            const int height = 720;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture previousTarget = view.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            view.targetTexture = target;
            view.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            view.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(_shots, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);

            foreach ((Canvas canvas, RenderMode mode, Camera camera, float plane) in switched)
            {
                canvas.renderMode = mode;
                canvas.worldCamera = camera;
                canvas.planeDistance = plane;
            }
        }

        /// <summary>
        /// Tutuşların çekimi: bıçak (göğüs önünde ve tahtaya uzanmışken), avuçta biber ve domates, yandan kavanoz.
        /// Her biri sonra tezgaha bırakılıyor (tarif akışı onları oradan da bulabiliyor).
        /// </summary>
        IEnumerator ShowGrips(KitchenCounter counter, DishasterPlayer player)
        {
            if (_shots == null) yield break;

            KitchenItem knife = counter.Items.FirstOrDefault(item => item != null && item.kind == KitchenItemKind.Knife);
            if (knife != null)
            {
                yield return Grab(counter, knife);
                if (counter.Held == knife)
                {
                    yield return ShowGrip("knife", player);
                    Vector3 board = counter.cuttingBoard.position + Vector3.up * (counter.boardHeight + 0.01f);
                    yield return Approach(board);
                    yield return new WaitForSeconds(0.6f);
                    Log($"[grip] bıçak tahtada: {player.GetComponentInChildren<CharacterBody>().Describe()}");
                    yield return Near("blind_near_knife_board", player, LowVisionRole.Hand.position, 0.6f);
                    yield return Near("blind_near_knife_board_left", player, LowVisionRole.Hand.position, -0.6f);
                    yield return Third("blind_3p_knife_board_side", player, true);
                    counter.RequestCut(board);
                    yield return new WaitForSeconds(0.12f);
                    yield return Near("blind_near_knife_chop", player, LowVisionRole.Hand.position, 0.6f);
                    yield return new WaitForSeconds(0.3f);
                    yield return Drop(counter, new Vector3(0.3f, 0f, 0.3f));
                }
            }

            // Kiler ve buzdolabındakiler ulaşılır olsun diye önce tezgahın önüne konuyor (sadece test).
            string[] names = { "Pepper", "Tomato", "SpiceJar", "MilkCarton", "MeasuringCup" };
            for (int i = 0; i < names.Length; i++)
            {
                string wanted = names[i];
                KitchenItem extra = counter.Items.FirstOrDefault(item => item != null && item.gameObject.activeSelf && item.name.Contains(wanted));
                if (extra == null) continue;

                extra.transform.position = counter.transform.TransformPoint(new Vector3(-0.75f + 0.3f * i, counter.topHeight, 0.62f));
                yield return null;
                yield return Grab(counter, extra);
                if (counter.Held != extra) continue;
                yield return ShowGrip(wanted.ToLowerInvariant(), player);
                // Ada tarif için boş kalsın: kilerdeki tezgaha, yan yana (adanın dışına bırakma da böylece deneniyor).
                yield return DropAt(counter, new Vector3(4.4f, 0.92f, 0.9f + 0.4f * i));
            }
        }

        /// <summary>
        /// Kiler ve ada: adada sadece aletler, ekmek buzdolabında, kasaların içeriği karışık (hangi kasada ne var),
        /// kasaların üstüne zıplanamıyor (önden ve açık yanından), raftaki kavanoz alınıyor ve elde bir ara kapağı düşüyor.
        /// </summary>
        IEnumerator ShowPantry(KitchenCounter counter, DishasterPlayer player)
        {
            string[] onIsland = counter.Items
                .Where(item => item != null && item.gameObject.activeSelf && OnIsland(counter, item.transform.position))
                .Select(item => item.name).ToArray();
            Log("[pantry] adada: " + string.Join(", ", onIsland));
            Check(onIsland.Length == 3 && !onIsland.Any(name => name.Contains("Bread")), "adada aletlerden başka eşya var");

            KitchenItem bread = counter.Items.FirstOrDefault(item => item != null && item.name.StartsWith("Art_Bread"));
            GameObject fridge = GameObject.Find("Fridge");
            Check(bread != null && fridge != null && bread.transform.IsChildOf(fridge.transform), "ekmek buzdolabında değil");
            if (bread != null) Log($"[pantry] ekmek: {bread.transform.position.ToString("F2")}");

            KitchenCrate[] crates = FindObjectsOfType<KitchenCrate>()
                .OrderBy(crate => crate.transform.position.y).ThenBy(crate => crate.transform.position.x).ToArray();
            IEnumerable<string> contents = crates.Select(crate => counter.Items.Where(item => item != null)
                .OrderBy(item => Vector3.Distance(item.transform.position, crate.transform.position)).First().name.Split(' ')[0].Replace("Art_", ""));
            Log($"[pantry] {crates.Length} kasa (önce ön sıra batıdan doğuya, sonra arka sıra): " + string.Join(", ", contents));
            Check(crates.Length == 11, "kasa sayısı 11 değil");

            KitchenItem[] berries = counter.Items.Where(item => item != null && item.name.StartsWith("Art_Strawberry")).ToArray();
            Log($"[pantry] {berries.Length} çilek: " + string.Join(", ", berries.Select(item => item.transform.position.ToString("F2"))));
            Check(berries.Length == 6 && berries.All(item => item.transform.parent != null && item.transform.parent.name == "Crates"), "çilekler kasada değil");
            Check(berries.All(item => crates.Any(crate => Vector3.Distance(Flat(item.transform.position), Flat(crate.transform.position)) < 0.35f)), "çilekler bir kasanın içinde değil");
            Check(counter.Plates.Count >= 2, "alınabilir tabak yok");

            yield return Look("pantry_crates", new Vector3(3.0f, 1.7f, -0.8f), new Vector3(3.0f, 0.25f, -2.5f));
            yield return Look("pantry_crates_west", new Vector3(0.3f, 1.5f, -1.3f), new Vector3(2.0f, 0.25f, -2.5f));
            yield return Look("island_top", new Vector3(-0.5f, 2.1f, -1.0f), counter.transform.position + Vector3.up * 0.9f);
            yield return Look("spice_shelf", new Vector3(3.5f, 1.75f, 1.5f), new Vector3(4.72f, 1.6f, 1.5f));

            yield return TryClimb(player, new Vector3(3.3f, 1.05f, -1.4f), Vector3.back, "önden");
            yield return TryClimb(player, new Vector3(0.6f, 1.05f, -2.3f), Vector3.right, "yandan");

            // Raftaki kavanoz (tezgaha taşımadan): alınıyor, elde bir ara kapağı düşüyor.
            KitchenItem jar = counter.Items.FirstOrDefault(item => item != null && item.name.StartsWith("SpiceJar") && item.HasLid);
            Check(jar != null, "kapaklı kavanoz yok");
            if (jar == null) yield break;

            yield return Grab(counter, jar);
            if (counter.Held != jar) yield break;

            // Kavanoz elde, doğduğu yere dönüp düz bakıyor: kapak açık yere düşsün.
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            controller.enabled = true;
            player.FaceTowards(player.headPivot.position + _spawnRotation * Vector3.forward * 3f);
            yield return new WaitForSeconds(0.4f);
            yield return Near("blind_near_jar_lid", player, LowVisionRole.Hand.position, 0.6f);

            float until = Time.time + 8f;
            while (jar.HasLid && Time.time < until) yield return null;
            Check(!jar.HasLid, "kavanozun kapağı elde düşmedi");
            yield return new WaitForSeconds(1.5f);

            KitchenLid lid = FindObjectOfType<KitchenLid>();
            if (lid != null)
            {
                Vector3 at = lid.transform.position;
                Log($"[pantry] {jar.name} kapağı düştü, şimdi {at.ToString("F2")}");
                yield return Look("lid_fallen", at + Vector3.up * 0.9f + player.transform.right * 0.45f - player.transform.forward * 0.3f, at);
            }

            yield return Near("blind_near_jar_open", player, LowVisionRole.Hand.position, 0.6f);
            yield return Third("blind_3p_jar_open", player, true);
            yield return Drop(counter, new Vector3(0.6f, 0f, 0.45f));
        }

        /// <summary>Ölçü bardağı kasenin ortasına bakılarak bırakılınca kasenin içine girmiyor, yanına konuyor.</summary>
        IEnumerator CheckNoOverlap(KitchenCounter counter)
        {
            KitchenItem cup = Container(counter, container => container.canPour && container.canReceive)?.Item;
            KitchenItem bowl = Container(counter, container => container.isMixingBowl)?.Item;
            Check(cup != null && bowl != null, "ölçü bardağı ya da kase yok");
            if (cup == null || bowl == null) yield break;

            yield return Grab(counter, cup);
            if (counter.Held != cup) yield break;

            yield return Approach(bowl.Center, bowl);
            counter.RequestGrabOrDrop(LowVisionRole.AimPoint);
            yield return Wait();

            Physics.SyncTransforms();
            Bounds cupBox = cup.GetComponent<BoxCollider>().bounds;
            Bounds bowlBox = bowl.GetComponent<BoxCollider>().bounds;
            cupBox.Expand(-0.01f);
            Log($"[drop] bardak kasenin ortasına bırakıldı: bardak {cup.transform.position.ToString("F2")}, kase {bowl.transform.position.ToString("F2")}, " +
                $"elde {(counter.Held != null ? counter.Held.name : "yok")}");
            Check(counter.Held == null, "bardak kasenin yanına bırakılamadı");
            Check(!cupBox.Intersects(bowlBox), "bardak kasenin içine girdi");
            yield return Look("island_cup_by_bowl", new Vector3(-0.5f, 2.1f, -1.0f), counter.transform.position + Vector3.up * 0.9f);
        }

        static bool OnIsland(KitchenCounter counter, Vector3 position)
        {
            Vector3 local = counter.transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= counter.topSize.x / 2f && Mathf.Abs(local.z) <= counter.topSize.y / 2f
                && local.y >= counter.topHeight - 0.05f && local.y <= counter.topHeight + 0.4f;
        }

        /// <summary>
        /// Bir yöne koşarken üst üste zıplıyor (oyuncunun zıplaması, 1 m); kasaların üstüne çıkamamalı. Oyuncunun kendi
        /// hareketi bu sırada kapalı (kapsülü test sürüyor).
        /// </summary>
        IEnumerator TryClimb(DishasterPlayer player, Vector3 start, Vector3 direction, string label)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            player.enabled = false;
            controller.enabled = false;
            player.transform.position = start;
            controller.enabled = true;

            float top = start.y;
            float vertical = 0f;
            float until = Time.time + 2.4f;
            while (Time.time < until)
            {
                if (controller.isGrounded) vertical = Mathf.Sqrt(2f * player.gravity * player.jumpHeight);
                vertical -= player.gravity * Time.deltaTime;
                controller.Move((direction * 4f + Vector3.up * vertical) * Time.deltaTime);
                top = Mathf.Max(top, player.transform.position.y);
                yield return null;
            }

            float settle = Time.time + 1.5f;
            while (!controller.isGrounded && Time.time < settle)
            {
                vertical = Mathf.Min(vertical, 0f) - player.gravity * Time.deltaTime;
                controller.Move(Vector3.up * vertical * Time.deltaTime);
                yield return null;
            }

            Vector3 end = player.transform.position;
            player.enabled = true;
            Log($"[climb] {label}: son {end.ToString("F2")}, en yüksek {top:0.00}");
            Check(end.y < start.y + 0.15f, "kasaların üstüne çıkılabildi (" + label + ")");
        }

        /// <summary>Verilen yerden bir noktaya bakan geçici kamera (sahneyi görenin gözüyle).</summary>
        IEnumerator Look(string name, Vector3 from, Vector3 at)
        {
            if (_shots == null) yield break;

            var cameraObject = new GameObject("LookCamera");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false;
            view.fieldOfView = 55f;
            view.nearClipPlane = 0.03f;
            cameraObject.transform.position = from;
            cameraObject.transform.LookAt(at);

            yield return null;
            yield return Capture(view, name);
            yield return null;
            Destroy(cameraObject);
        }

        /// <summary>
        /// Tutuşu göster: doğduğu yerde düz bakarken (bir yere değmeden, eşya göğüs önünde) önden, yandan ve ele
        /// yakından iki yandan.
        /// </summary>
        IEnumerator ShowGrip(string label, DishasterPlayer player)
        {
            if (_shots == null) yield break;

            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            controller.enabled = true;
            player.FaceTowards(player.headPivot.position + _spawnRotation * Vector3.forward * 3f);
            yield return new WaitForSeconds(0.7f);

            Log($"[grip] {label}: {player.GetComponentInChildren<CharacterBody>().Describe()}");
            yield return Shot($"blind_fp_{label}");
            yield return Third($"blind_3p_{label}", player, false);
            yield return Third($"blind_3p_{label}_side", player, true);
            yield return Near($"blind_near_{label}", player, LowVisionRole.Hand.position, 0.6f);
            yield return Near($"blind_near_{label}_left", player, LowVisionRole.Hand.position, -0.6f);
        }

        /// <summary>Oyuncuya önden (ya da yandan) bakan geçici kamera: kendi karakterini başkalarının gördüğü gibi.</summary>
        IEnumerator Third(string name, DishasterPlayer player, bool side)
        {
            if (_shots == null) yield break;

            // Kendi kafan sadece gölge: çekimde başkalarının gördüğü hale getir.
            CharacterBody body = player.GetComponentInChildren<CharacterBody>();
            Renderer[] bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var modes = new UnityEngine.Rendering.ShadowCastingMode[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                modes[i] = bodyRenderers[i].shadowCastingMode;
                bodyRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            var cameraObject = new GameObject("AutoTestCamera");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false;
            view.fieldOfView = 50f;
            view.nearClipPlane = 0.05f;
            Transform root = player.transform;
            Vector3 direction = side ? root.right : root.forward;
            cameraObject.transform.position = root.position + direction * 2.6f + Vector3.up * 0.55f;
            cameraObject.transform.LookAt(root.position + Vector3.up * 0.25f);

            yield return null;
            yield return Capture(view, name);
            yield return null;
            yield return null;

            Destroy(cameraObject);
            for (int i = 0; i < bodyRenderers.Length; i++) bodyRenderers[i].shadowCastingMode = modes[i];
        }

        /// <summary>
        /// Kendi karakterine yakından, yandan-önden (Unity'nin sahne görünümü gibi): elin ve kolun dışarıdan nasıl
        /// durduğu. side eksi: soldan, artı: sağdan. Kafa da görünür.
        /// </summary>
        IEnumerator Near(string name, DishasterPlayer player, Vector3 focus, float side)
        {
            if (_shots == null) yield break;

            CharacterBody body = player.GetComponentInChildren<CharacterBody>();
            Renderer[] bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var modes = new UnityEngine.Rendering.ShadowCastingMode[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                modes[i] = bodyRenderers[i].shadowCastingMode;
                bodyRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            var cameraObject = new GameObject("NearCamera");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false;
            view.fieldOfView = 50f;
            view.nearClipPlane = 0.03f;
            Transform root = player.transform;
            cameraObject.transform.position = focus + root.right * side + root.forward * 0.55f + Vector3.up * 0.12f;
            cameraObject.transform.LookAt(focus);

            yield return null;
            yield return Capture(view, name);
            yield return null;
            yield return null;

            Destroy(cameraObject);
            for (int i = 0; i < bodyRenderers.Length; i++) bodyRenderers[i].shadowCastingMode = modes[i];
        }

        readonly List<string> _failures = new List<string>();
        NotebookPose _seenPose = (NotebookPose)255;
        float _poseSince;
        int _poseShots;

        /// <summary>Defteri tutan Bilmiyorum'a önden-soldan yakın kamera (karşıdaki oyuncunun gördüğü gibi).</summary>
        IEnumerator CloseUp(string name, DishasterPlayer other, NotebookRole book)
        {
            if (_shots == null) yield break;

            var cameraObject = new GameObject("CloseUpCamera");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false;
            view.fieldOfView = 45f;
            view.nearClipPlane = 0.05f;
            Transform root = other.transform;
            Vector3 target = book.LeftGripPoint;
            cameraObject.transform.position = target + root.forward * 1.1f - root.right * 0.25f + Vector3.up * 0.15f;
            cameraObject.transform.LookAt(target + root.right * 0.12f);

            yield return null;
            yield return Capture(view, name);
            yield return null;
            Destroy(cameraObject);
        }
        float _nextSnapLog;

        void Check(bool condition, string message)
        {
            if (condition) return;
            _failures.Add(message);
            Log("CHECK FAIL " + message);
        }

        static IEnumerator Wait()
        {
            yield return null;
            yield return null;
            yield return new WaitForSeconds(0.1f);
        }

        DishasterPlayer _player;
        Vector3 _spawnPosition;
        Quaternion _spawnRotation;

        /// <summary>
        /// Oyuncuyu hedefin önüne koyup nişangahı (kamerayı) ona çeviriyor: oyuncunun fareyle yaptığı gibi. Kamera
        /// gözlerde, kafa pivotundan biraz farklı yerde; birkaç kare yerine oturuyor.
        /// </summary>
        IEnumerator Approach(Vector3 point, KitchenItem item = null)
        {
            if (_player == null) yield break;

            // Oyuncunun yürüyüp duracağı gibi: önce merkeze bakan taraftan, yakından; kapsül bir yere girmiyorsa ve
            // nişangah hedefi görüyorsa (raf, dolap duvarı arada değilse) orada kalıyor.
            Vector3 flat = new Vector3(point.x, 0f, point.z);
            Vector3 first = flat.magnitude < 1.5f ? Vector3.back : -flat.normalized;
            CharacterController controller = _player.GetComponent<CharacterController>();
            float[] distances = { 0.55f, 0.75f, 0.95f };
            int[] turns = { 0, 1, -1, 2, -2, 3, -3, 4 };
            Vector3? fallback = null;
            foreach (float distance in distances)
            {
                foreach (int turn in turns)
                {
                    Vector3 position = new Vector3(point.x, 1.05f, point.z) + Quaternion.Euler(0f, 45f * turn, 0f) * first * distance;
                    controller.enabled = false;
                    bool blocked = Physics.CheckCapsule(position + Vector3.down * 0.6f, position + Vector3.up * 0.7f, controller.radius * 0.9f,
                        Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    if (blocked)
                    {
                        controller.enabled = true;
                        continue;
                    }

                    // Altında zemin olmalı (kasaların arkası gibi odanın dışındaki boşluk değil).
                    if (!Physics.Raycast(position, Vector3.down, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        controller.enabled = true;
                        continue;
                    }

                    fallback ??= position;
                    yield return StandAndAim(controller, position, point);

                    Transform view = _player.playerCamera.transform;
                    if (!Physics.Raycast(view.position, view.forward, out RaycastHit hit, LowVisionRole.ReachAlong(view.forward), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) continue;
                    KitchenItem seen = hit.collider.GetComponentInParent<KitchenItem>();
                    // Tabaktaki dilim tabağın kutusunun içinde: ışın tabağa değiyor, tıklama dilimi alıyor.
                    bool sees = item != null ? seen == item || (seen != null && seen.Plate != null && seen.Plate.Holds(item) && Vector3.Distance(hit.point, item.Center) < 0.06f)
                        : Vector3.Distance(hit.point, point) < 0.08f;
                    if (sees) yield break;
                }
            }

            Log($"[grab] {(item != null ? item.name : point.ToString("F2"))}: nişangahla görülen bir yer bulunamadı");
            if (fallback.HasValue) yield return StandAndAim(controller, fallback.Value, point);
        }

        IEnumerator StandAndAim(CharacterController controller, Vector3 position, Vector3 point)
        {
            controller.enabled = false;
            _player.transform.position = position;
            controller.enabled = true;

            // Kafa pivotundan: kamera eğilip uzanınca sadece bakış yönünde kayıyor, ışın aynı doğruda kalıyor.
            float until = Time.time + 0.5f;
            while (Time.time < until)
            {
                _player.FaceTowards(point);
                yield return null;
            }
        }

        /// <summary>Nişangahtaki eşyaya sol tık: gönderilen nokta görmeyenin bakış ışınından (oyundaki gibi).</summary>
        IEnumerator Grab(KitchenCounter counter, KitchenItem item)
        {
            yield return Approach(item.Center, item);
            Check(LowVisionRole.Touching, "nişangahtaki eşyaya el değmiyor: " + item.name);
            Vector3 aim = LowVisionRole.AimPoint;
            counter.RequestGrabOrDrop(aim);
            yield return Wait();
            Check(counter.Held == item, "nişangahtaki eşya alınamadı: " + item.name);
            if (counter.Held != item)
            {
                Transform view = _player.playerCamera.transform;
                Physics.Raycast(view.position, view.forward, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                Log($"[grab] {item.name}: elde {(counter.Held != null ? counter.Held.name : "yok")}, nişangah {aim.ToString("F2")} eşya {item.Center.ToString("F2")} fark {Vector3.Distance(aim, item.Center) * 100f:0} cm, ışın {(hit.collider != null ? hit.collider.name : "-")} {hit.distance:0.00} m, kamera {view.position.ToString("F2")}");
            }
        }

        /// <summary>Dünyadaki bir noktaya bakıp bırak (adanın dışı: raf, tezgah, kasa).</summary>
        IEnumerator DropAt(KitchenCounter counter, Vector3 point)
        {
            yield return Approach(point);
            counter.RequestGrabOrDrop(LowVisionRole.Touching ? LowVisionRole.AimPoint : point);
            yield return Wait();
            Check(counter.Held == null, $"bırakılamadı: {point.ToString("F2")} ({counter.LastRefusal})");
        }

        IEnumerator Drop(KitchenCounter counter, Vector3 local)
        {
            Vector3 aim = counter.transform.TransformPoint(new Vector3(local.x, counter.topHeight + 0.02f, local.z));
            yield return Approach(aim);
            counter.RequestGrabOrDrop(aim);
            yield return Wait();
            Check(counter.Held == null, "bırakılamadı: " + counter.LastRefusal);
        }

        IEnumerator Pour(KitchenCounter counter, KitchenContainer into, System.Func<bool> done)
        {
            yield return Approach(into.Item.Center + Vector3.up * 0.1f);
            float until = Time.time + 12f;
            while (!done() && Time.time < until)
            {
                counter.RequestPour(into.Item.Center);
                yield return null;
            }

            // Son istekler gelsin.
            yield return new WaitForSeconds(0.3f);
        }

        static KitchenContainer Container(KitchenCounter counter, System.Func<KitchenContainer, bool> match)
        {
            foreach (KitchenItem item in counter.Items)
                if (item != null && item.Container != null && item.gameObject.activeInHierarchy && match(item.Container))
                    return item.Container;
            return null;
        }

        void Finish(bool passed, string reason)
        {
            if (reason != null) _failures.Add(reason);
            foreach (string failure in _failures) Log("FAIL " + failure);
            foreach (string error in _errors.Distinct().Take(30)) Log("LOGGED " + error);

            Log(passed && reason == null ? "RESULT PASS" : $"RESULT FAIL ({_failures.Count} kontrol, {_errors.Count} hata logu)");
            Application.Quit(passed && reason == null ? 0 : 1);
        }
    }
}
