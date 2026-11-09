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
        // "-dishasterPort 7790": testler editörde açık oyunla (7777) çakışmasın.
        const string PortFlag = "-dishasterPort";

        string _shots;
        bool _mute;
        bool _deaf;
        bool _observe;
        int _players = 1;
        ushort _port;
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
            int slices = recipe.TargetOf(Ingredient.Strawberry);
            int milk = recipe.TargetOf(Ingredient.Milk);
            Log($"tarif: {slices} dilim çilek, {milk} ml süt");

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

            // Boş el tahtaya dokunuyor: dışarıdan da el tahtada (gövde eğilerek yetişiyor). Eğilirken nişangah aynı
            // yerde kalmalı (tıklanan şey nişangahtaki), kamera aşağı dalmamalı.
            Vector3 aimBefore = LowVisionRole.AimPoint;
            yield return new WaitForSeconds(0.6f);
            CharacterBody blindBody = player.GetComponentInChildren<CharacterBody>();
            Vector3 eyeShift = player.playerCamera.transform.position - player.headPivot.position;
            float aimDrift = Vector3.Distance(aimBefore, LowVisionRole.AimPoint);
            Log($"[touch] {blindBody.Describe()} | kamera kayması {eyeShift.magnitude * 100f:0} cm, nişangah kayması {aimDrift * 100f:0.0} cm");
            Check(blindBody.Lean <= 20.5f, "uzanırken çok eğiliyor");
            Check(eyeShift.magnitude < 0.15f, "uzanırken kamera gözlerden kayıyor (aşağı dalıyor)");
            Check(aimDrift < 0.02f, "uzanırken nişangahın gösterdiği yer kayıyor");
            yield return Shot("blind_fp_touch");
            yield return Third("blind_3p_touch", player, true);
            yield return Near("blind_near_touch", player, LowVisionRole.AimPoint, 0.6f);

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
            // Bardağın yanına değil boş yere (ölçü bardağı -0.62, 0.25'te duruyor).
            yield return Drop(counter, new Vector3(-0.1f, 0f, 0.36f));

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
            yield return Third("mute_3p_draw", player, false);
            yield return Near("mute_near_draw", player, notebook.PenGripPoint, 0.6f);

            notebook.SetPoseForTest(NotebookPose.Show);
            yield return new WaitForSeconds(0.12f);
            yield return Shot("mute_fp_turn");
            yield return Near("mute_near_turn", player, notebook.LeftGripPoint, -0.6f);
            yield return new WaitForSeconds(0.9f);
            Measure("show", player, notebook);
            yield return Shot("mute_fp_show");
            yield return Third("mute_3p_show", player, false);
            yield return Near("mute_near_show", player, notebook.LeftGripPoint, -0.6f);

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
            Log($"[{label}] defter ekranın %{coverage * 100f:0}'ini kaplıyor; sol bilek {body.WristBend(true):0}°");
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
            view.depth = 100f;
            view.fieldOfView = 50f;
            view.nearClipPlane = 0.03f;
            Transform root = player.transform;
            cameraObject.transform.position = focus + root.right * side + root.forward * 0.55f + Vector3.up * 0.12f;
            cameraObject.transform.LookAt(focus);

            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shots, name + ".png"));
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
            view.depth = 100f;
            view.fieldOfView = 45f;
            view.nearClipPlane = 0.05f;
            Transform root = other.transform;
            Vector3 target = book.LeftGripPoint;
            cameraObject.transform.position = target + root.forward * 1.1f - root.right * 0.25f + Vector3.up * 0.15f;
            cameraObject.transform.LookAt(target + root.right * 0.12f);

            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shots, name + ".png"));
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
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (blocked)
                    {
                        controller.enabled = true;
                        continue;
                    }

                    fallback ??= position;
                    yield return StandAndAim(controller, position, point);

                    Transform view = _player.playerCamera.transform;
                    if (!Physics.Raycast(view.position, view.forward, out RaycastHit hit, 1.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) continue;
                    bool sees = item != null ? hit.collider.GetComponentInParent<KitchenItem>() == item : Vector3.Distance(hit.point, point) < 0.08f;
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

            float until = Time.time + 0.35f;
            while (Time.time < until)
            {
                Transform aim = _player.playerCamera.transform;
                _player.FaceTowards(_player.headPivot.position + (point - aim.position));
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
