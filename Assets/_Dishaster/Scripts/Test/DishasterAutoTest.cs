using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Uçtan uca otomatik test: build "-dishasterAutotest" bayrağıyla açılınca (başka türlü hiç çalışmıyor) local host
    /// açıyor, Görmedim'i seçip oyuna giriyor ve çilekli sütü gerçek mutfak mesajlarıyla baştan sona yapıyor:
    /// buzdolabını aç, çileği al, tahtaya koy, bıçakla doğra, süt kutusundan ölçü bardağına dök, bardaktan kaseye dök,
    /// dilimleri kaseye at. Sonunda tarifin tamamlandığını ve hiç hata/istisna loglanmadığını denetleyip çıkıyor
    /// (çıkış kodu 0 = geçti). Sonuçlar "[AUTOTEST]" önekiyle.
    /// </summary>
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

        string _shots;
        bool _mute;
        bool _deaf;
        bool _observe;
        int _players = 1;
        const float TimeoutSeconds = 150f;

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

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == ShotsFlag) _shots = args[i + 1];
                if (args[i] == RoleFlag) _mute = args[i + 1] == "mute";
                if (args[i] == RoleFlag) _deaf = args[i + 1] == "deaf";
                if (args[i] == RoleFlag) _observe = args[i + 1] == "observe";
                if (args[i] == PlayersFlag) int.TryParse(args[i + 1], out _players);
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
            int slices = recipe.TargetOf(Ingredient.Strawberry);
            int milk = recipe.TargetOf(Ingredient.Milk);
            Log($"tarif: {slices} dilim çilek, {milk} ml süt");

            Check(player.GetComponentInChildren<CharacterBody>() != null, "karakter giydirilmedi");
            Check(FindObjectOfType<FirstPersonHands>() != null, "FPS elleri yok");

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

            // Buzdolabı kapağı.
            KitchenDoor door = FindObjectOfType<KitchenDoor>();
            if (door != null && door.panel != null)
            {
                yield return Approach(door.panel.bounds.center);
                counter.RequestGrabOrDrop(door.panel.bounds.center);
                yield return Wait();
            }

            // Çilek → tahta.
            KitchenItem strawberry = counter.Items.FirstOrDefault(item => item != null && item.IsCuttable && item.gameObject.activeSelf
                && item.slicePrefab != null && item.slicePrefab.GetComponent<KitchenIngredientItem>() != null);
            Check(strawberry != null, "çilek bulunamadı");
            if (strawberry == null) yield break;

            yield return Grab(counter, strawberry);
            Vector3 board = counter.cuttingBoard.position + Vector3.up * 0.03f;
            yield return Approach(board);
            counter.RequestGrabOrDrop(board);
            yield return Wait();
            Check(counter.Held == null, "çilek tahtaya bırakılamadı");

            // Boş el tahtaya dokunuyor: dışarıdan da el tahtada (gövde eğilerek yetişiyor).
            yield return new WaitForSeconds(0.6f);
            yield return Shot("blind_fp_touch");
            yield return Third("blind_3p_touch", player, true);

            // Bıçakla doğra.
            KitchenItem knife = counter.Items.First(item => item != null && item.kind == KitchenItemKind.Knife);
            yield return Grab(counter, knife);
            yield return Shot("blind_fp_knife");
            yield return Third("blind_3p_knife", player, false);
            yield return Approach(strawberry.transform.position);
            for (int i = 0; i < slices; i++)
            {
                if (!strawberry.gameObject.activeSelf) break;
                counter.RequestCut(strawberry.transform.position);
                if (i == 0)
                {
                    yield return new WaitForSeconds(0.1f);
                    yield return Shot("blind_fp_chop");
                }

                yield return new WaitForSeconds(0.35f);
            }

            Check(recipe.StepState(0) == RecipeStepState.Done, "adım 1 (doğra) tamamlanmadı");
            yield return Drop(counter, new Vector3(0.65f, 0f, 0.25f));

            // Süt kutusu → ölçü bardağı.
            KitchenContainer cup = Container(counter, c => c.canPour && c.canReceive);
            KitchenContainer bowl = Container(counter, c => c.isMixingBowl);
            KitchenContainer carton = Container(counter, c => c.canPour && !c.canReceive && c.AmountOf(Ingredient.Milk) > 0f);
            Check(cup != null && bowl != null && carton != null, "bardak/kase/süt kutusu bulunamadı");
            if (cup == null || bowl == null || carton == null) yield break;

            yield return Grab(counter, carton.Item);

            // Bir yere değmeden taşıyor: el ve kutu göğüs önünde (ekranın sağ altında), dışarıdan da öyle.
            // Açıkta (doğduğu yerde), önünde bir şey yokken.
            CharacterController carrier = player.GetComponent<CharacterController>();
            carrier.enabled = false;
            player.transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            carrier.enabled = true;
            player.FaceTowards(player.headPivot.position + _spawnRotation * Vector3.forward * 3f);
            yield return new WaitForSeconds(0.6f);
            Check(!LowVisionRole.Touching, "düz bakarken el bir yere değiyor sayıldı");
            yield return Shot("blind_fp_carry");
            yield return Third("blind_3p_carry", player, false);
            yield return Third("blind_3p_carry_side", player, true);

            yield return Pour(counter, cup, () => cup.AmountOf(Ingredient.Milk) >= milk * 0.5f);
            yield return Shot("blind_fp_pour");
            yield return Third("blind_3p_pour", player, false);
            yield return Pour(counter, cup, () => cup.AmountOf(Ingredient.Milk) >= milk - 8f);
            Log($"bardakta {cup.AmountOf(Ingredient.Milk):0} ml süt");
            Check(recipe.StepState(1) == RecipeStepState.Done, "adım 2 (bardağa dök) tamamlanmadı");
            yield return Drop(counter, new Vector3(-0.65f, 0f, 0.25f));

            // Bardak → kase.
            yield return Grab(counter, cup.Item);
            yield return Pour(counter, bowl, () => cup.IsEmpty);
            Log($"kasede {bowl.AmountOf(Ingredient.Milk):0} ml süt");
            Check(recipe.StepState(2) == RecipeStepState.Done, "adım 3 (kaseye dök) tamamlanmadı");
            yield return Drop(counter, new Vector3(-0.4f, 0f, 0.3f));

            // Dilimler → kase.
            for (int i = 0; i < slices; i++)
            {
                KitchenItem slice = counter.Items.FirstOrDefault(item => item != null && item.gameObject.activeSelf && item.Ingredient != null
                    && item.Ingredient.ingredient == Ingredient.Strawberry);
                if (slice == null) break;

                yield return Grab(counter, slice);
                yield return Approach(bowl.Item.Center);
                counter.RequestGrabOrDrop(bowl.Item.Center);
                yield return Wait();
            }

            yield return new WaitForSeconds(0.5f);
            Check(recipe.StepState(3) == RecipeStepState.Done, "adım 4 (dilimler) tamamlanmadı");
            Check(!recipe.Failed, "tarif bozuldu: " + recipe.FailReason);
            Check(recipe.Completed, "tarif tamamlanmadı");

            // Art yiyeceği: domatesi al, tahtaya koy, bıçakla sonuna kadar doğra; her kesimde bir dilim çıkmalı.
            KitchenItem tomato = counter.Items.FirstOrDefault(item => item != null && item.gameObject.activeSelf
                && item.slicePrefabs != null && item.slicePrefabs.Length > 0 && item.name.Contains("Tomato"));
            Check(tomato != null, "art domatesi (alınır/kesilir) bulunamadı");
            if (tomato != null)
            {
                int pieces = tomato.segments.Length;
                yield return Grab(counter, tomato);
                Vector3 spot = counter.cuttingBoard.position + Vector3.up * 0.03f;
                yield return Approach(spot);
                counter.RequestGrabOrDrop(spot);
                yield return Wait();
                yield return Grab(counter, knife);
                yield return Approach(tomato.transform.position);
                for (int i = 0; i < pieces + 1; i++)
                {
                    if (!tomato.gameObject.activeSelf) break;
                    counter.RequestCut(tomato.transform.position);
                    yield return new WaitForSeconds(0.35f);
                    if (i == 1) yield return Shot("blind_fp_tomato");
                }

                int slicesOut = counter.Items.Count(item => item != null && item.gameObject.activeSelf && item.Ingredient != null
                    && item.Ingredient.ingredient == Ingredient.Tomato);
                Log($"domates: {pieces} parça, {slicesOut} dilim çıktı");
                Check(!tomato.gameObject.activeSelf, "domates sonuna kadar kesilemedi");
                Check(slicesOut == pieces, $"domates dilim sayısı {slicesOut} (beklenen {pieces})");
                yield return Drop(counter, new Vector3(0.65f, 0f, 0.25f));
            }

            // Biraz daha oynat: efektler, karakterler, eller hata vermesin.
            yield return new WaitForSeconds(0.3f);
            yield return Shot("blind_fp_done");
            yield return new WaitForSeconds(2f);
            Finish(_failures.Count == 0 && _errors.Count == 0, null);
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
            yield return Third("mute_3p_hold", player, false);

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
            yield return Third("mute_3p_draw", player, false);

            notebook.SetPoseForTest(NotebookPose.Show);
            yield return new WaitForSeconds(1f);
            Measure("show", player, notebook);
            yield return Shot("mute_fp_show");
            yield return Third("mute_3p_show", player, false);

            notebook.SetPoseForTest(NotebookPose.Hold);
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

        /// <summary>Kameraya göre konumlar (x sağ, y yukarı, z ileri): FPS elleri nerede, defter nerede.</summary>
        void Measure(string label, DishasterPlayer player, NotebookRole notebook)
        {
            Transform view = player.playerCamera.transform;
            string Local(Vector3 world) => view.InverseTransformPoint(world).ToString("F2");

            FirstPersonHands hands = FindObjectOfType<FirstPersonHands>();
            Transform leftWrist = hands != null ? CharacterBody.FindBone(hands.transform, "LeftHand") : null;
            Transform rightWrist = hands != null ? CharacterBody.FindBone(hands.transform, "RightHand") : null;
            Transform leftFore = hands != null ? CharacterBody.FindBone(hands.transform, "LeftForeArm") : null;
            Transform rightFore = hands != null ? CharacterBody.FindBone(hands.transform, "RightForeArm") : null;
            Transform pencil = CharacterBody.FindBone(notebook.rightHand, "Pencil");

            Log($"[{label}] notebook {Local(notebook.notebook.position)} leftGrip {Local(notebook.LeftGripPoint)} penGrip {Local(notebook.PenGripPoint)}");
            if (leftWrist != null) Log($"[{label}] FP leftWrist {Local(leftWrist.position)} scale {leftFore.localScale.x:F1} | rightWrist {Local(rightWrist.position)} scale {rightFore.localScale.x:F1}");
            if (pencil != null) Log($"[{label}] pencil {Local(pencil.position)} active {pencil.gameObject.activeInHierarchy} rendered {pencil.GetComponent<Renderer>().enabled}");
            Renderer skin = hands != null ? hands.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (skin != null) Log($"[{label}] FP renderer enabled {skin.enabled} bounds {Local(skin.bounds.center)} size {skin.bounds.size.ToString("F2")} layer {skin.gameObject.layer}");
        }

        // ── Boşta (Duymadım): eller köşede, kıpırdamıyor ─────────────────────

        IEnumerator RunIdle(DishasterPlayer player, string label)
        {
            yield return new WaitForSeconds(1f);
            yield return Shot(label + "_fp_idle");
            yield return Third(label + "_3p_idle", player, false);

            // FPS elleri kameraya göre yerinde durmalı (dönmüyor, kaymıyor) ve sağ el sağda, sol el solda.
            FirstPersonHands hands = FindObjectOfType<FirstPersonHands>();
            Transform view = player.playerCamera.transform;
            Transform leftWrist = CharacterBody.FindBone(hands.transform, "LeftHand");
            Transform rightWrist = CharacterBody.FindBone(hands.transform, "RightHand");
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
            Log($"[{label}] FPS bilekleri: sol {left[left.Count - 1].ToString("F2")} sağ {right[right.Count - 1].ToString("F2")}, oynama sol {spreadL * 100f:0.0} cm sağ {spreadR * 100f:0.0} cm");
            Check(spreadL < 0.01f && spreadR < 0.01f, "boştaki FPS elleri kıpırdıyor");
            Check(left[0].x < -0.05f && right[0].x > 0.05f, "boştaki FPS elleri yanlış tarafta");

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
                    if (watchedBody != null && watchedBody.LastSnapDistance > 0f)
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
                view.depth = 100f;
                view.fieldOfView = 50f;
                view.nearClipPlane = 0.05f;
                Transform root = other.transform;
                Vector3 side = shot % 2 == 0 ? root.forward : (root.forward + root.right).normalized;
                cameraObject.transform.position = root.position + side * 2.4f + Vector3.up * 0.55f;
                cameraObject.transform.LookAt(root.position + Vector3.up * 0.25f);

                yield return null;
                yield return new WaitForEndOfFrame();
                if (_shots != null) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shots, $"observe_{shot:00}.png"));
                yield return null;
                Destroy(cameraObject);
            }

            snaps.Sort();
            float p95 = snaps.Count > 0 ? snaps[Mathf.Min(snaps.Count - 1, (int)(snaps.Count * 0.95f))] : 0f;
            float mean = snapCount > 0 ? snapSum / snapCount : 0f;
            Log($"[gözlem] elde tutarken avuç farkı: ortalama {mean * 100f:0.0} cm, %95 {p95 * 100f:0.0} cm, en çok {snapMax * 100f:0.0} cm ({snapCount} kare)");
            Check(mean < 0.05f && p95 < 0.12f, "uzaktan eşya/defter ele yetişmiyor (iki taraf eşleşmiyor)");
            Finish(_failures.Count == 0 && _errors.Count == 0, null);
        }

        // ── Ekran görüntüleri ────────────────────────────────────────────────

        IEnumerator Shot(string name)
        {
            if (_shots == null) yield break;

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shots, name + ".png"));
            yield return null;
            yield return null;
        }

        /// <summary>Oyuncuya önden (ya da yandan) bakan geçici kamera: kendi karakterini başkalarının gördüğü gibi.</summary>
        IEnumerator Third(string name, DishasterPlayer player, bool side)
        {
            if (_shots == null) yield break;

            // Kendi gövden sadece gölge, FPS elleri sadece sende: çekimde başkalarının gördüğü hale getir.
            CharacterBody body = player.GetComponentInChildren<CharacterBody>();
            Renderer[] bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            foreach (Renderer renderer in bodyRenderers) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            FirstPersonHands hands = FindObjectOfType<FirstPersonHands>();
            Renderer[] handRenderers = hands != null ? hands.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            foreach (Renderer renderer in handRenderers) renderer.enabled = false;

            var cameraObject = new GameObject("AutoTestCamera");
            var view = cameraObject.AddComponent<Camera>();
            view.depth = 100f;
            view.fieldOfView = 50f;
            view.nearClipPlane = 0.05f;
            Transform root = player.transform;
            Vector3 direction = side ? root.right : root.forward;
            cameraObject.transform.position = root.position + direction * 2.6f + Vector3.up * 0.55f;
            cameraObject.transform.LookAt(root.position + Vector3.up * 0.25f);

            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shots, name + ".png"));
            yield return null;
            yield return null;

            Destroy(cameraObject);
            foreach (Renderer renderer in bodyRenderers) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            foreach (Renderer renderer in handRenderers) renderer.enabled = true;
        }

        readonly List<string> _failures = new List<string>();
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

        /// <summary>Oyuncuyu hedefin önüne koyup ona baktırıyor: el (bakış ışını) gerçekten oraya gidiyor.</summary>
        IEnumerator Approach(Vector3 point)
        {
            if (_player == null) yield break;

            Vector3 flat = new Vector3(point.x, 0f, point.z);
            Vector3 away = flat.magnitude < 1.5f ? Vector3.back : -flat.normalized;
            CharacterController controller = _player.GetComponent<CharacterController>();
            controller.enabled = false;
            _player.transform.position = new Vector3(point.x, 1.05f, point.z) + away * 0.75f;
            controller.enabled = true;

            for (int i = 0; i < 3; i++)
            {
                _player.FaceTowards(point);
                yield return null;
            }
        }

        IEnumerator Grab(KitchenCounter counter, KitchenItem item)
        {
            yield return Approach(item.Center);
            counter.RequestGrabOrDrop(item.Center);
            yield return Wait();
            Check(counter.Held == item, "alınamadı: " + item.name);
        }

        IEnumerator Drop(KitchenCounter counter, Vector3 local)
        {
            Vector3 aim = counter.transform.TransformPoint(new Vector3(local.x, counter.topHeight + 0.02f, local.z));
            yield return Approach(aim);
            counter.RequestGrabOrDrop(aim);
            yield return Wait();
            Check(counter.Held == null, "bırakılamadı");
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
