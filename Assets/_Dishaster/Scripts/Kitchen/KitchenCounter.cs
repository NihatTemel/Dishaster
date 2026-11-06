using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Dishaster
{
    public enum KitchenRequest : byte
    {
        GrabOrDrop,
        Cut,
        Pour,
    }

    public enum KitchenEventType : byte
    {
        Grab,
        Drop,
        Cut,
        Pour,
        AddToBowl,
        Door,
    }

    public struct KitchenRequestMessage : NetworkMessage
    {
        public KitchenRequest request;
        public Vector3 aimPoint;
    }

    public struct KitchenEventMessage : NetworkMessage
    {
        public KitchenEventType type;
        public int itemId;
        public Vector3 position;
        // Dökmede hedef kap (-1: yere/tezgaha döküldü) ve hacim; kaseye eklemede kase.
        public int targetId;
        public float amount;
    }

    public struct KitchenReadyMessage : NetworkMessage { }

    public struct KitchenRecipeMessage : NetworkMessage
    {
        public int[] targets;
    }

    /// <summary>
    /// Tezgah (prefab) ve sahnedeki tüm eşyaların yönetimi: alma, tezgaha bırakma, kesme, dökme, kaseye ekleme,
    /// buzdolabı kapağı ve tarif. Kararı sunucu
    /// veriyor, olaylar Mirror mesajıyla herkese gidiyor. Geç yüklenen istemci kaçırmasın diye olaylar saklanıp
    /// o hazır olunca baştan oynatılıyor; eşya numaraları da bu sırayla herkeste aynı kalıyor.
    /// </summary>
    public class KitchenCounter : MonoBehaviour
    {
        const float PalmTop = 0.016f;

        const float ChopSeconds = 0.25f;
        const float ChopDepth = 0.04f;
        const float MinCutReach = 0.1f;

        const float EdgeMargin = 0.05f;
        const float DropCeiling = 0.35f;

        // Dilimler tahtanın iki kenarında sıraya diziliyor; ortası kesilen ürünün.
        const float SliceSpacing = 0.055f;
        const float SliceRowEdgeMargin = 0.03f;
        const float SliceFreeDistance = 0.04f;

        // İstemci sağ tık basılıyken bu aralıkla istiyor; sunucu geçen süreye göre döküyor.
        const float PourSendInterval = 0.1f;
        const float MaxPourStep = 0.15f;
        const float PourGap = 0.3f;
        const float PourAimAbove = 0.35f;
        const float BowlAimAbove = 0.3f;

        static KitchenCounter s_instance;

        public static KitchenCounter Instance => s_instance;

        [Header("Yüzey (metre, tezgaha göre)")]
        public float topHeight = 0.8f;
        public Vector2 topSize = new Vector2(1.8f, 0.9f);

        [Header("Kesme tahtası: ürünler sadece bunun üstünde kesiliyor")]
        public Transform cuttingBoard;
        public Vector2 boardSize = new Vector2(0.5f, 0.3f);
        public float boardHeight = 0.02f;

        readonly List<KitchenItem> _items = new List<KitchenItem>();
        readonly List<KitchenEventMessage> _log = new List<KitchenEventMessage>();
        readonly HashSet<NetworkConnectionToClient> _readyClients = new HashSet<NetworkConnectionToClient>();
        readonly List<KitchenDoor> _doors = new List<KitchenDoor>();
        readonly Recipe _recipe = new Recipe();

        KitchenContainer _bowl;
        KitchenContainer _cup;
        float _nextPourSend;
        float _lastPourTime = float.NegativeInfinity;

        KitchenItem _held;
        bool _announced;
        float _nextCutTime;
        float _chopTimer;

        public KitchenItem Held => _held;

        /// <summary>Sahnedeki tüm eşyalar (dilimler dahil); sıra eşya numarası.</summary>
        public IReadOnlyList<KitchenItem> Items => _items;

        public Recipe Recipe => _recipe;

        // Son dökme ya da kaseye ekleme (dökme göstergesi için). Kaynak yoksa tane tane eklendi; hedef yoksa yere döküldü.
        public KitchenContainer PourSource { get; private set; }
        public KitchenContainer PourTarget { get; private set; }
        public Ingredient PourIngredient { get; private set; }
        public float PourTime { get; private set; } = float.NegativeInfinity;
        /// <summary>Bu dökmede (aynı kaptan aynı yere, aralıksız) yere giden miktar.</summary>
        public float PourSpilled { get; private set; }

        void NotePour(KitchenContainer source, KitchenContainer target, Ingredient ingredient, float spilled)
        {
            bool sameStream = source == PourSource && target == PourTarget && Time.time - PourTime < PourGap * 3f;
            PourSpilled = (sameStream ? PourSpilled : 0f) + spilled;
            PourSource = source;
            PourTarget = target;
            PourIngredient = ingredient;
            PourTime = Time.time;
        }

        void Awake()
        {
            s_instance = this;

            // Kiler ayrı prefab. Sıra konuma göre: sahne herkeste aynı, numaralar da aynı çıkıyor.
            var items = new List<KitchenItem>(FindObjectsOfType<KitchenItem>(true));
            items.Sort(ComparePosition);
            foreach (KitchenItem item in items)
            {
                Register(item);
                if (!item.TryGetComponent(out KitchenContainer container)) continue;
                if (container.isMixingBowl) _bowl = container;
                // Ölçü bardağı: hem alan hem döken tek kap.
                else if (container.canPour && container.canReceive) _cup = container;
            }

            // Buzdolabı ayrı prefab; kapak numaraları da konuma göre.
            _doors.AddRange(FindObjectsOfType<KitchenDoor>(true));
            _doors.Sort((a, b) => ComparePosition(a.transform, b.transform));
            for (int i = 0; i < _doors.Count; i++)
                _doors[i].Id = i;

            // Miktarları sunucu seçiyor; istemciler hazır olunca alıyor.
            if (NetworkServer.active) _recipe.SetTargets(Recipe.Roll());
        }

        static int ComparePosition(KitchenItem a, KitchenItem b) => ComparePosition(a.transform, b.transform);

        static int ComparePosition(Transform a, Transform b)
        {
            Vector3 p = a.position;
            Vector3 q = b.position;

            int result = p.x.CompareTo(q.x);
            if (result == 0) result = p.y.CompareTo(q.y);
            if (result == 0) result = p.z.CompareTo(q.z);
            return result;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        void Register(KitchenItem item)
        {
            item.Id = _items.Count;
            _items.Add(item);
        }

        void Update()
        {
            if (_announced) return;

            // Oyun oyuncusu doğduysa sunucu sahnesi de yüklenmiş demek. Host'un kendi tarafı olayları doğrudan uyguluyor.
            if (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.TryGetComponent(out DishasterPlayer _)) return;

            _announced = true;
            if (!NetworkServer.active) NetworkClient.Send(new KitchenReadyMessage());
        }

        void LateUpdate()
        {
            if (_chopTimer <= 0f) return;

            _chopTimer -= Time.deltaTime;
            if (_held == null || _held.kind != KitchenItemKind.Knife) return;

            float progress = Mathf.Clamp01(1f - _chopTimer / ChopSeconds);
            _held.transform.localPosition = HoldPosition(_held) + Vector3.down * (Mathf.Sin(progress * Mathf.PI) * ChopDepth);
        }

        // ── EL ──────────────────────────────────────────────────────────────

        public void RequestGrabOrDrop(Vector3 aimPoint)
        {
            NetworkClient.Send(new KitchenRequestMessage { request = KitchenRequest.GrabOrDrop, aimPoint = aimPoint });
        }

        /// <summary>Bıçak havada iniyor; denk geldiyse sunucu dilimi kesiyor.</summary>
        public void RequestCut(Vector3 aimPoint)
        {
            if (_held == null || _held.kind != KitchenItemKind.Knife || Time.time < _nextCutTime) return;

            _nextCutTime = Time.time + ChopSeconds;
            _chopTimer = ChopSeconds;
            NetworkClient.Send(new KitchenRequestMessage { request = KitchenRequest.Cut, aimPoint = aimPoint });
        }

        /// <summary>Sağ tık basılıyken her kare çağrılıyor; elde dökülebilen kap varsa aralıklarla sunucuya soruyor.</summary>
        public void RequestPour(Vector3 aimPoint)
        {
            if (_held == null || _held.Container == null || !_held.Container.canPour || Time.time < _nextPourSend) return;

            _nextPourSend = Time.time + PourSendInterval;
            NetworkClient.Send(new KitchenRequestMessage { request = KitchenRequest.Pour, aimPoint = aimPoint });
        }

        static Vector3 HoldPosition(KitchenItem item) => new Vector3(0f, PalmTop, item.holdForward);

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ── YÜZEY ───────────────────────────────────────────────────────────

        bool IsOnBoard(Vector3 position)
        {
            Vector3 offset = position - cuttingBoard.position;
            return Mathf.Abs(offset.x) <= boardSize.x / 2f && Mathf.Abs(offset.z) <= boardSize.y / 2f;
        }

        bool IsOverCounter(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= topSize.x / 2f
                && Mathf.Abs(local.z) <= topSize.y / 2f
                && local.y >= topHeight - EdgeMargin
                && local.y <= topHeight + DropCeiling;
        }

        Vector3 SnapToSurface(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            local.x = Mathf.Clamp(local.x, -topSize.x / 2f + EdgeMargin, topSize.x / 2f - EdgeMargin);
            local.z = Mathf.Clamp(local.z, -topSize.y / 2f + EdgeMargin, topSize.y / 2f - EdgeMargin);
            local.y = topHeight;

            Vector3 snapped = transform.TransformPoint(local);
            if (IsOnBoard(snapped)) snapped.y += boardHeight;
            return snapped;
        }

        /// <summary>Tahtada ilk boş dilim yeri; hepsi doluysa son yere üst üste geliyor.</summary>
        Vector3 FindSliceSpot()
        {
            int columns = Mathf.FloorToInt((boardSize.x - SliceRowEdgeMargin * 2f) / SliceSpacing) + 1;
            float rowZ = boardSize.y / 2f - SliceRowEdgeMargin;
            float startX = -(columns - 1) * SliceSpacing / 2f;
            Vector3 spot = cuttingBoard.position;

            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    spot = cuttingBoard.position + new Vector3(startX + column * SliceSpacing, boardHeight, row == 0 ? -rowZ : rowZ);
                    if (IsSpotFree(spot)) return spot;
                }
            }

            return spot;
        }

        bool IsSpotFree(Vector3 spot)
        {
            foreach (KitchenItem item in _items)
                if (item != _held && !item.IsCuttable && item.gameObject.activeSelf && FlatDistance(item.transform.position, spot) < SliceFreeDistance)
                    return false;

            return true;
        }

        // ── SUNUCU ──────────────────────────────────────────────────────────

        void ServerGrabOrDrop(Vector3 aimPoint)
        {
            // Kapağa dokunuluyorsa açılıp kapanıyor; elde eşya olsa da.
            foreach (KitchenDoor door in _doors)
            {
                if (!door.IsTouched(aimPoint)) continue;

                ServerSend(new KitchenEventMessage { type = KitchenEventType.Door, itemId = door.Id });
                return;
            }

            if (_held != null)
            {
                // Yumurta gibi tane tane eklenenler kasenin üstüne bırakılınca içine giriyor.
                if (_held.Ingredient != null && _bowl != null && IsAimingInto(_bowl, aimPoint, BowlAimAbove))
                {
                    ServerSend(new KitchenEventMessage { type = KitchenEventType.AddToBowl, itemId = _held.Id, targetId = _bowl.Item.Id });
                    return;
                }

                // Şimdilik sadece tezgaha bırakılıyor.
                if (IsOverCounter(aimPoint))
                    ServerSend(new KitchenEventMessage { type = KitchenEventType.Drop, itemId = _held.Id, position = SnapToSurface(aimPoint) });
                return;
            }

            KitchenItem nearest = null;
            float nearestDistance = float.PositiveInfinity;

            foreach (KitchenItem item in _items)
            {
                if (!item.gameObject.activeSelf) continue;

                float distance = Vector3.Distance(item.Center, aimPoint);
                if (distance > item.grabRadius || distance >= nearestDistance) continue;

                nearest = item;
                nearestDistance = distance;
            }

            if (nearest != null)
                ServerSend(new KitchenEventMessage { type = KitchenEventType.Grab, itemId = nearest.Id });
        }

        void ServerCut(Vector3 aimPoint)
        {
            if (_held == null || _held.kind != KitchenItemKind.Knife) return;

            KitchenItem target = null;
            float targetDistance = float.PositiveInfinity;

            foreach (KitchenItem item in _items)
            {
                if (!item.IsCuttable || !item.gameObject.activeSelf || !IsOnBoard(item.transform.position)) continue;

                float distance = FlatDistance(item.transform.position, aimPoint);
                if (distance > Mathf.Max(MinCutReach, item.grabRadius) || distance >= targetDistance) continue;

                target = item;
                targetDistance = distance;
            }

            if (target != null)
                ServerSend(new KitchenEventMessage { type = KitchenEventType.Cut, itemId = target.Id, position = FindSliceSpot() });
        }

        /// <summary>Bakılan kaba (yoksa yere) geçen süre kadar döküyor. Taşan ya da yere giden kayboluyor.</summary>
        void ServerPour(Vector3 aimPoint)
        {
            if (_held == null || _held.Container == null || !_held.Container.canPour || _held.Container.IsEmpty) return;

            float now = Time.time;
            float elapsed = now - _lastPourTime > PourGap ? PourSendInterval : Mathf.Min(now - _lastPourTime, MaxPourStep);
            _lastPourTime = now;

            KitchenContainer source = _held.Container;
            float volume = Mathf.Min(source.pourRate * elapsed, source.Volume);
            if (volume <= 0f) return;

            KitchenContainer target = FindPourTarget(aimPoint, source);
            ServerSend(new KitchenEventMessage
            {
                type = KitchenEventType.Pour,
                itemId = _held.Id,
                targetId = target != null ? target.Item.Id : -1,
                amount = volume,
            });
        }

        KitchenContainer FindPourTarget(Vector3 aimPoint, KitchenContainer source)
        {
            KitchenContainer nearest = null;
            float nearestDistance = float.PositiveInfinity;

            foreach (KitchenItem item in _items)
            {
                KitchenContainer container = item.Container;
                if (container == null || container == source || !container.canReceive || item == _held || !item.gameObject.activeInHierarchy) continue;
                if (!IsAimingInto(container, aimPoint, PourAimAbove)) continue;

                float distance = FlatDistance(item.Center, aimPoint);
                if (distance >= nearestDistance) continue;

                nearest = container;
                nearestDistance = distance;
            }

            return nearest;
        }

        /// <summary>El kabın ağzının üstünde mi: yatayda kabın içinde, dikeyde ağzından biraz yukarıya kadar.</summary>
        static bool IsAimingInto(KitchenContainer container, Vector3 aimPoint, float above)
        {
            KitchenItem item = container.Item;
            float bottom = item.transform.position.y;
            return FlatDistance(item.Center, aimPoint) <= item.grabRadius
                && aimPoint.y >= bottom - 0.02f
                && aimPoint.y <= bottom + item.height + above;
        }

        void ServerSend(KitchenEventMessage message)
        {
            Apply(message);
            _log.Add(message);

            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (_readyClients.Contains(connection))
                    connection.Send(message);
        }

        // ── OLAYLAR (herkeste aynı) ─────────────────────────────────────────

        void Apply(KitchenEventMessage message)
        {
            if (message.type == KitchenEventType.Door)
            {
                _doors[message.itemId].Toggle();
                return;
            }

            KitchenItem item = _items[message.itemId];

            switch (message.type)
            {
                case KitchenEventType.Pour:
                    KitchenContainer target = message.targetId >= 0 ? _items[message.targetId].Container : null;
                    float[] poured = item.Container.Take(message.amount);
                    item.Container.MarkPouring();
                    if (target != null) target.AddMix(poured);
                    NotePour(item.Container, target, Ingredients.MainOf(poured), target == null ? message.amount : 0f);
                    if (target != null && target == _bowl)
                    {
                        _recipe.CheckPour(item.Container, poured);
                        _recipe.Check(_bowl);
                    }
                    break;

                case KitchenEventType.AddToBowl:
                    KitchenContainer bowl = _items[message.targetId].Container;
                    item.transform.SetParent(transform, false);
                    item.gameObject.SetActive(false);
                    _held = null;
                    bowl.Add(item.Ingredient.ingredient, item.Ingredient.amount);
                    NotePour(null, bowl, item.Ingredient.ingredient, 0f);
                    if (bowl == _bowl) _recipe.Check(_bowl);
                    break;

                case KitchenEventType.Grab:
                    Transform hand = LowVisionRole.Hand;
                    hand.gameObject.SetActive(true);
                    item.SetTouchable(false);
                    item.transform.SetParent(hand, false);
                    item.transform.localPosition = HoldPosition(item);
                    item.transform.localRotation = Quaternion.identity;
                    _held = item;
                    break;

                case KitchenEventType.Drop:
                    item.transform.SetParent(transform, false);
                    item.transform.position = message.position;
                    item.transform.localRotation = Quaternion.identity;
                    item.SetTouchable(true);
                    _held = null;
                    break;

                case KitchenEventType.Cut:
                    KitchenItem slice = Instantiate(item.slicePrefab, transform);
                    slice.transform.position = message.position;
                    slice.transform.localScale = new Vector3(item.NextSliceScale, 1f, item.NextSliceScale);
                    Register(slice);
                    item.CutOnce();
                    if (slice.TryGetComponent(out KitchenIngredientItem sliceIngredient)) _recipe.NoteSlice(sliceIngredient.ingredient);
                    if (_chopTimer <= 0f) _chopTimer = ChopSeconds;
                    break;
            }

            _recipe.UpdateSteps(_cup, _bowl);
        }

        // ── AĞ ──────────────────────────────────────────────────────────────

        public static void RegisterServerHandlers()
        {
            NetworkServer.RegisterHandler<KitchenRequestMessage>(OnServerRequest);
            NetworkServer.RegisterHandler<KitchenReadyMessage>(OnServerReady);
        }

        public static void RegisterClientHandlers()
        {
            NetworkClient.RegisterHandler<KitchenEventMessage>(OnClientEvent);
            NetworkClient.RegisterHandler<KitchenRecipeMessage>(OnClientRecipe);
        }

        static void OnServerRequest(NetworkConnectionToClient sender, KitchenRequestMessage message)
        {
            if (s_instance == null || !LowVisionRole.IsLowVision(sender)) return;

            switch (message.request)
            {
                case KitchenRequest.Cut: s_instance.ServerCut(message.aimPoint); break;
                case KitchenRequest.Pour: s_instance.ServerPour(message.aimPoint); break;
                default: s_instance.ServerGrabOrDrop(message.aimPoint); break;
            }
        }

        static void OnServerReady(NetworkConnectionToClient sender, KitchenReadyMessage message)
        {
            if (s_instance == null) return;

            s_instance._readyClients.Add(sender);

            // Tarif önce: kaseye konanlar ona göre kontrol ediliyor.
            sender.Send(new KitchenRecipeMessage { targets = s_instance._recipe.Targets });
            foreach (KitchenEventMessage logged in s_instance._log)
                sender.Send(logged);
        }

        static void OnClientEvent(KitchenEventMessage message)
        {
            if (s_instance != null) s_instance.Apply(message);
        }

        static void OnClientRecipe(KitchenRecipeMessage message)
        {
            if (s_instance != null) s_instance._recipe.SetTargets(message.targets);
        }
    }
}
