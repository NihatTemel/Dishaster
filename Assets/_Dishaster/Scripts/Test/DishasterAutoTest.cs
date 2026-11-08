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
            manager.minPlayers = 1;
            manager.HostLocal();
            Log("host açıldı");

            DishasterRoomPlayer room = null;
            while (room == null)
            {
                if (NetworkClient.localPlayer != null) NetworkClient.localPlayer.TryGetComponent(out room);
                yield return null;
            }

            room.CmdSelectRole(MonkeyRoles.LowVision);
            while (room.monkeyRole != MonkeyRoles.LowVision) yield return null;
            room.CmdChangeReadyState(true);
            while (!manager.allPlayersReady) yield return null;
            manager.StartGame();
            Log("oyun başlatıldı (Görmedim)");

            // ── Oyun ─────────────────────────────────────────────────────────
            DishasterPlayer player = null;
            while (player == null || KitchenCounter.Instance == null || !KitchenCounter.Instance.Recipe.HasTargets)
            {
                if (NetworkClient.localPlayer != null) NetworkClient.localPlayer.TryGetComponent(out player);
                yield return null;
            }

            yield return new WaitForSeconds(1f);
            KitchenCounter counter = KitchenCounter.Instance;
            Recipe recipe = counter.Recipe;
            int slices = recipe.TargetOf(Ingredient.Strawberry);
            int milk = recipe.TargetOf(Ingredient.Milk);
            Log($"tarif: {slices} dilim çilek, {milk} ml süt");

            Check(player.GetComponentInChildren<CharacterBody>() != null, "karakter giydirilmedi");
            Check(FindObjectOfType<FirstPersonHands>() != null, "FPS elleri yok");

            // Buzdolabı kapağı.
            KitchenDoor door = FindObjectOfType<KitchenDoor>();
            if (door != null && door.panel != null)
            {
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
            counter.RequestGrabOrDrop(board);
            yield return Wait();
            Check(counter.Held == null, "çilek tahtaya bırakılamadı");

            // Bıçakla doğra.
            KitchenItem knife = counter.Items.First(item => item != null && item.kind == KitchenItemKind.Knife);
            yield return Grab(counter, knife);
            for (int i = 0; i < slices; i++)
            {
                if (!strawberry.gameObject.activeSelf) break;
                counter.RequestCut(strawberry.transform.position);
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
                counter.RequestGrabOrDrop(bowl.Item.Center);
                yield return Wait();
            }

            yield return new WaitForSeconds(0.5f);
            Check(recipe.StepState(3) == RecipeStepState.Done, "adım 4 (dilimler) tamamlanmadı");
            Check(!recipe.Failed, "tarif bozuldu: " + recipe.FailReason);
            Check(recipe.Completed, "tarif tamamlanmadı");

            // Biraz daha oynat: efektler, karakterler, eller hata vermesin.
            yield return new WaitForSeconds(2f);
            Finish(_failures.Count == 0 && _errors.Count == 0, null);
        }

        readonly List<string> _failures = new List<string>();

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

        IEnumerator Grab(KitchenCounter counter, KitchenItem item)
        {
            counter.RequestGrabOrDrop(item.Center);
            yield return Wait();
            Check(counter.Held == item, "alınamadı: " + item.name);
        }

        IEnumerator Drop(KitchenCounter counter, Vector3 local)
        {
            Vector3 aim = counter.transform.TransformPoint(new Vector3(local.x, counter.topHeight + 0.02f, local.z));
            counter.RequestGrabOrDrop(aim);
            yield return Wait();
            Check(counter.Held == null, "bırakılamadı");
        }

        IEnumerator Pour(KitchenCounter counter, KitchenContainer into, System.Func<bool> done)
        {
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
