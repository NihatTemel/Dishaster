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
        LidOff,
        AddToPlate,
        // Bırakılamadı (yer yok ya da düz bir yüzey değil): sadece ses, bir şey değişmiyor, saklanmıyor.
        Refused,
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
        // Bırakmada ve kesmede yer; kapak düşerken kapağın fırlama hızı.
        public Vector3 position;
        // Bırakmada eşyanın duruşu (yüzeyin eğimine göre; kasanın tabanı eğik olabiliyor).
        public Quaternion rotation;
        // Dökmede hedef kap (-1: yere/tezgaha döküldü) ve hacim; kaseye ya da tabağa eklemede kase/tabak.
        public int targetId;
        public float amount;
    }

    public struct KitchenReadyMessage : NetworkMessage { }

    public struct KitchenRecipeMessage : NetworkMessage
    {
        public int[] targets;
    }

    /// <summary>Kasaların bu oyundaki içeriği: i. kasaya order[i]. kasanın yiyecekleri.</summary>
    public struct KitchenCratesMessage : NetworkMessage
    {
        public int[] order;
    }

    /// <summary>
    /// Tezgah (prefab) ve sahnedeki tüm eşyaların yönetimi: alma, tezgaha bırakma, kesme, dökme, kaseye ekleme,
    /// buzdolabı kapağı ve tarif. Kararı sunucu
    /// veriyor, olaylar Mirror mesajıyla herkese gidiyor. Geç yüklenen istemci kaçırmasın diye olaylar saklanıp
    /// o hazır olunca baştan oynatılıyor; eşya numaraları da bu sırayla herkeste aynı kalıyor.
    /// </summary>
    public class KitchenCounter : MonoBehaviour
    {
        const float ChopSeconds = 0.25f;
        const float ChopDepth = 0.05f;
        const float MinCutReach = 0.1f;

        const float MinGrabRadius = 0.07f;
        const float SurfaceGrabTolerance = 0.03f;
        const float EdgeMargin = 0.05f;
        const float DropCeiling = 0.35f;
        // Bırakılan eşya diğerlerinden en az bu kadar ayrı; yer doluysa bu adımlarla bu kadar uzağa kadar boş yer aranıyor.
        const float DropGap = 0.005f;
        const float DropSearchStep = 0.02f;
        const float DropSearchReach = 0.35f;
        // Adanın dışında: bakılan noktanın altında düz bir yüzey aranıyor (raf, kasa, buzdolabı, yer). Yüzeyin eğimi en çok
        // ~40°; eşyanın köşelerinin altı da aynı yüzey olmalı (raftan taşmasın), üstünde ve yanlarında duvar/raf olmamalı.
        const float SurfaceProbeAbove = 0.06f;
        const float SurfaceProbeDepth = 0.5f;
        const float MinSurfaceUp = 0.75f;
        const float SupportTolerance = 0.05f;
        const float ClearanceLift = 0.015f;
        // Tabağın üstüne bu kadar yukarıdan bakarken dilim tabağa konuyor; tabaktaki dilim tabaktan önce alınıyor.
        const float PlateAimAbove = 0.25f;
        const float PieceGrabBonus = 0.03f;

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

        // Kapaklı kavanoz elde tutulurken kapak bu aralıkta bir anda düşüyor (her alışta yeniden); yukarı fırlayıp
        // yana savruluyor.
        const float LidMinSeconds = 1.5f;
        const float LidMaxSeconds = 6f;
        const float LidPopSpeed = 1.1f;
        const float LidThrowSpeed = 0.7f;

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
        readonly List<KitchenCrate> _crates = new List<KitchenCrate>();
        readonly List<KitchenPlate> _plates = new List<KitchenPlate>();
        readonly Recipe _recipe = new Recipe();

        int[] _crateOrder;
        bool _cratesArranged;
        float _lidTime = float.PositiveInfinity;

        KitchenContainer _bowl;
        KitchenContainer _cup;
        float _nextPourSend;
        float _lastPourTime = float.NegativeInfinity;

        KitchenItem _held;
        bool _announced;
        float _nextCutTime;
        float _chopTimer;

        public KitchenItem Held => _held;

        /// <summary>Son bırakılamamanın sebebi (test ve hata ayıklama için).</summary>
        public string LastRefusal { get; private set; }

        public Vector3 DoorPosition(int id) => id >= 0 && id < _doors.Count ? _doors[id].transform.position : transform.position;

        /// <summary>Sahnedeki tüm eşyalar (dilimler dahil); sıra eşya numarası.</summary>
        public IReadOnlyList<KitchenItem> Items => _items;

        public Recipe Recipe => _recipe;

        public KitchenContainer Bowl => _bowl;
        public KitchenContainer Cup => _cup;

        /// <summary>Tabaklar (sıra eşya numarasıyla).</summary>
        public IReadOnlyList<KitchenPlate> Plates => _plates;

        /// <summary>Bu nokta adanın üstünde mi (tarif: tabak adada mı).</summary>
        public bool IsOnIsland(Vector3 position) => IsOverCounter(position + Vector3.up * 0.01f);

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
                if (item.TryGetComponent(out KitchenPlate plate)) _plates.Add(plate);
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

            // Kiler kasaları da konuma göre. Eşya numaraları yukarıda verildi; karıştırma onları değiştirmiyor.
            _crates.AddRange(FindObjectsOfType<KitchenCrate>(true));
            _crates.Sort((a, b) => ComparePosition(a.transform, b.transform));

            // Miktarları ve kasaların içeriğini sunucu seçiyor; istemciler hazır olunca alıyor.
            if (NetworkServer.active)
            {
                _recipe.SetTargets(Recipe.Roll());
                _crateOrder = RollCrates(_crates.Count);
                ArrangeCrates(_crateOrder);
            }
        }

        static int[] RollCrates(int count)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++)
                order[i] = i;

            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }

        /// <summary>
        /// i. kasaya order[i]. kasanın yiyecekleri geçiyor; kasaya göre yerleri ve dönüşleri aynı kalıyor (öne eğik
        /// arka sıradan yerdeki ön sıraya geçen yiyecek de kasanın tabanında duruyor). Oyun başında bir kez.
        /// </summary>
        void ArrangeCrates(int[] order)
        {
            if (_cratesArranged || order == null || order.Length != _crates.Count) return;
            _cratesArranged = true;

            var contents = new List<(Transform item, Vector3 position, Quaternion rotation)>[_crates.Count];
            for (int i = 0; i < _crates.Count; i++)
            {
                Transform crate = _crates[i].transform;
                contents[i] = new List<(Transform, Vector3, Quaternion)>();
                if (_crates[i].items == null) continue;

                foreach (KitchenItem item in _crates[i].items)
                {
                    if (item == null) continue;
                    Transform food = item.transform;
                    contents[i].Add((food, crate.InverseTransformPoint(food.position), Quaternion.Inverse(crate.rotation) * food.rotation));
                }
            }

            for (int i = 0; i < _crates.Count; i++)
            {
                Transform crate = _crates[i].transform;
                foreach ((Transform food, Vector3 position, Quaternion rotation) in contents[order[i]])
                    food.SetPositionAndRotation(crate.TransformPoint(position), crate.rotation * rotation);
            }
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
            if (NetworkServer.active) ServerUpdateLid();
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
            _held.transform.localPosition = _held.HoldPosition + Vector3.down * (Mathf.Sin(progress * Mathf.PI) * ChopDepth);
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

        Vector3 SnapToSurface(Vector3 position, float margin = EdgeMargin)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            local.x = Mathf.Clamp(local.x, -topSize.x / 2f + margin, topSize.x / 2f - margin);
            local.z = Mathf.Clamp(local.z, -topSize.y / 2f + margin, topSize.y / 2f - margin);
            local.y = topHeight;

            Vector3 snapped = transform.TransformPoint(local);
            if (IsOnBoard(snapped)) snapped.y += boardHeight;
            return snapped;
        }

        // ── BIRAKMA YERİ ────────────────────────────────────────────────────

        /// <summary>Bir eşyanın tezgah üstündeki izi: yatayda döndürülmüş dikdörtgen (dokunma kutusundan).</summary>
        struct Footprint
        {
            public Vector2 center;
            public Vector2 axisX;
            public Vector2 axisZ;
            public Vector2 half;
        }

        static Footprint FootprintOf(KitchenItem item, Vector3 position, Quaternion rotation)
        {
            Bounds box = item.LocalBox;
            Vector3 scale = item.transform.lossyScale;
            Vector3 center = position + rotation * Vector3.Scale(box.center, scale);
            return new Footprint
            {
                center = new Vector2(center.x, center.z),
                axisX = Flat(rotation * Vector3.right),
                axisZ = Flat(rotation * Vector3.forward),
                half = new Vector2(box.extents.x * Mathf.Abs(scale.x), box.extents.z * Mathf.Abs(scale.z)),
            };
        }

        static Vector2 Flat(Vector3 direction)
        {
            var flat = new Vector2(direction.x, direction.z);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.right;
        }

        // Ayırma ekseni: iki dikdörtgenin dört kenar yönünden birinde araları açıksa değmiyorlar.
        static bool Overlaps(Footprint a, Footprint b)
        {
            Vector2 offset = b.center - a.center;
            foreach (Vector2 axis in new[] { a.axisX, a.axisZ, b.axisX, b.axisZ })
            {
                float reachA = a.half.x * Mathf.Abs(Vector2.Dot(a.axisX, axis)) + a.half.y * Mathf.Abs(Vector2.Dot(a.axisZ, axis));
                float reachB = b.half.x * Mathf.Abs(Vector2.Dot(b.axisX, axis)) + b.half.y * Mathf.Abs(Vector2.Dot(b.axisZ, axis));
                if (Mathf.Abs(Vector2.Dot(offset, axis)) >= reachA + reachB + DropGap) return false;
            }

            return true;
        }

        /// <summary>
        /// Bu yerde başka bir eşyaya giriyorsa o eşya; boşsa null. Yatayda izleri, dikeyde kutuları kesişmeli (üst
        /// raftaki eşya alttakini engellemiyor). Tutulan eşyanın üstündekiler (tabaktaki dilimler) sayılmıyor.
        /// </summary>
        KitchenItem FindBlocker(KitchenItem held, Vector3 position, Quaternion rotation)
        {
            Footprint footprint = FootprintOf(held, position, rotation);
            Bounds box = held.LocalBox;
            float scale = held.transform.lossyScale.y;
            float bottom = position.y + box.min.y * scale;
            float top = position.y + box.max.y * scale;

            foreach (KitchenItem item in _items)
            {
                if (item == held || !item.gameObject.activeInHierarchy || item.transform.IsChildOf(held.transform)) continue;

                Bounds other = item.WorldBox;
                if (other.min.y >= top - DropGap || other.max.y <= bottom + DropGap) continue;
                if (Overlaps(footprint, FootprintOf(item, item.transform.position, item.transform.rotation))) return item;
            }

            return null;
        }

        /// <summary>
        /// Eşya başka bir eşyanın içine girmesin (kaseye ve tabağa girebilenler bundan önce oraya ekleniyor): bakılan
        /// yer doluysa en yakın boş yere kayıyor; yakında boş yer yoksa bırakılmıyor. Adada tezgahın (ve tahtanın)
        /// üstüne; başka yerde bakılan noktanın altındaki düz yüzeye (raf, kasa, buzdolabı, yer).
        /// </summary>
        bool TryFindDropSpot(KitchenItem held, Vector3 aimPoint, Vector3 from, out Vector3 spot, out Quaternion rotation)
        {
            rotation = transform.rotation;
            if (IsOverCounter(aimPoint)) return TryFindCounterSpot(held, aimPoint, out spot);

            if (!FindSurface(aimPoint, out Vector3 surface, out Vector3 normal))
            {
                spot = aimPoint;
                LastRefusal = "altında düz yüzey yok";
                return false;
            }

            rotation = Quaternion.FromToRotation(Vector3.up, normal) * transform.rotation;
            spot = Rest(held, surface, rotation);
            if (IsFreeSpot(held, spot, rotation)) return true;

            // Önce bırakanın tarafına (duvardan, raf kenarından odaya doğru), sonra iki yana.
            Vector3 wanted = spot;
            KitchenItem blocker = FindBlocker(held, wanted, rotation);
            Vector3 away = blocker != null ? wanted - blocker.transform.position : from - wanted;
            away.y = 0f;
            float startAngle = away.sqrMagnitude > 1e-6f ? Mathf.Atan2(away.z, away.x) : -Mathf.PI / 2f;

            foreach (Vector3 candidate in Ring(wanted, startAngle))
            {
                if (!FindSurface(candidate, out Vector3 found, out Vector3 foundNormal) || Mathf.Abs(found.y - wanted.y) > SupportTolerance) continue;

                Quaternion foundRotation = Quaternion.FromToRotation(Vector3.up, foundNormal) * transform.rotation;
                Vector3 pivot = Rest(held, found, foundRotation);
                if (!IsFreeSpot(held, pivot, foundRotation)) continue;

                spot = pivot;
                rotation = foundRotation;
                return true;
            }

            return false;
        }

        /// <summary>Test için: bu yerde neden durmuyor (boşsa duruyor).</summary>
        string WhyNotFree(KitchenItem held, Vector3 spot, Quaternion rotation)
        {
            KitchenItem blocker = FindBlocker(held, spot, rotation);
            if (blocker != null) return "dolu: " + blocker.name;

            Bounds box = held.LocalBox;
            Vector3 scale = held.transform.lossyScale;
            Vector3 center = spot + rotation * Vector3.Scale(box.center, scale);
            Vector3 half = Vector3.Scale(box.extents, scale) * 0.9f;
            Collider[] walls = Physics.OverlapBox(center + Vector3.up * ClearanceLift, half, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (walls.Length > 0) return $"değiyor: {walls[0].name} (yer {spot.ToString("F3")}, kutu {box.center.ToString("F3")}/{box.size.ToString("F3")}, ölçek {scale.ToString("F2")}, duruş {rotation.eulerAngles.ToString("F0")})";

            return "kenardan taşıyor";
        }

        /// <summary>
        /// Adanın üstü: tezgaha (tahtadaysa tahtaya) oturuyor; dolu yerden doluluğun dışına doğru kayıyor. Büyük eşya
        /// (tabak) kenardan en çok çeyreği kadar taşıyor.
        /// </summary>
        bool TryFindCounterSpot(KitchenItem held, Vector3 aimPoint, out Vector3 spot)
        {
            Vector2 half = FootprintOf(held, aimPoint, transform.rotation).half;
            float margin = Mathf.Max(EdgeMargin, Mathf.Min(half.x, half.y) * 0.75f);
            spot = Rest(held, SnapToSurface(aimPoint, margin), transform.rotation);
            KitchenItem blocker = FindBlocker(held, spot, transform.rotation);
            if (blocker == null) return true;

            Vector3 wanted = spot;
            Vector3 away = wanted - blocker.transform.position;
            away.y = 0f;
            float startAngle = away.sqrMagnitude > 1e-6f ? Mathf.Atan2(away.z, away.x) : -Mathf.PI / 2f;

            foreach (Vector3 candidate in Ring(wanted, startAngle))
            {
                Vector3 snapped = Rest(held, SnapToSurface(candidate, margin), transform.rotation);
                if (FlatDistance(snapped, candidate) > 0.001f || FindBlocker(held, snapped, transform.rotation) != null) continue;

                spot = snapped;
                return true;
            }

            return false;
        }

        /// <summary>Eşyanın tabanı yüzeye otursun diye pivotun yeri (pivotu tabanında değilse kaydırıyor).</summary>
        static Vector3 Rest(KitchenItem item, Vector3 surface, Quaternion rotation)
        {
            return surface + rotation * Vector3.up * (-item.LocalBox.min.y * item.transform.lossyScale.y);
        }

        /// <summary>Halka halka uzaklaşan aday yerler: her halkada önce verilen yöne, sonra iki yana sırayla.</summary>
        static IEnumerable<Vector3> Ring(Vector3 center, float startAngle)
        {
            for (float distance = DropSearchStep; distance <= DropSearchReach; distance += DropSearchStep)
            {
                int count = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * distance / DropSearchStep));
                for (int i = 0; i < count; i++)
                {
                    int side = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                    float angle = startAngle + side * 2f * Mathf.PI / count;
                    yield return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                }
            }
        }

        /// <summary>
        /// Noktanın hemen altındaki katı yüzey (eşyalar tetik, sayılmıyor; oyuncular da). Düz değilse (duvar, kasanın
        /// kenarı) yok sayılıyor.
        /// </summary>
        static bool FindSurface(Vector3 point, out Vector3 surface, out Vector3 normal)
        {
            surface = point;
            normal = Vector3.up;

            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * SurfaceProbeAbove, Vector3.down, SurfaceProbeAbove + SurfaceProbeDepth,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.distance >= nearest || hit.collider is CharacterController || hit.collider.GetComponentInParent<DishasterPlayer>() != null) continue;

                nearest = hit.distance;
                surface = hit.point;
                normal = hit.normal;
                found = true;
            }

            return found && normal.y >= MinSurfaceUp;
        }

        /// <summary>
        /// Eşya bu yerde duruyor mu: başka eşyaya girmiyor, kutusu duvara/rafa değmiyor, köşelerinin altı da aynı yüzey
        /// (raftan, kasanın kenarından taşmıyor).
        /// </summary>
        bool IsFreeSpot(KitchenItem held, Vector3 spot, Quaternion rotation)
        {
            if (FindBlocker(held, spot, rotation) != null) return false;

            Bounds box = held.LocalBox;
            Vector3 scale = held.transform.lossyScale;
            Vector3 center = spot + rotation * Vector3.Scale(box.center, scale);
            Vector3 half = Vector3.Scale(box.extents, scale) * 0.9f;
            if (Physics.CheckBox(center + Vector3.up * ClearanceLift, half, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, 0f, (i & 2) == 0 ? -half.z : half.z) * 0.8f;
                Vector3 under = spot + rotation * (Vector3.Scale(new Vector3(box.center.x, 0f, box.center.z), scale) + corner);
                if (!FindSurface(under, out Vector3 found, out _) || Mathf.Abs(found.y - spot.y) > SupportTolerance) return false;
            }

            return true;
        }

        /// <summary>
        /// Üstünde başka bir eşya duruyorsa (tabak yığını): alttaki çekilip alınamıyor. Üstündekiler kendi
        /// çocuklarıysa (tabaktaki dilimler) sayılmıyor.
        /// </summary>
        bool IsCovered(KitchenItem item)
        {
            Bounds box = item.WorldBox;
            float reach = Mathf.Min(box.extents.x, box.extents.z);
            foreach (KitchenItem other in _items)
            {
                if (other == item || other == _held || !other.gameObject.activeInHierarchy || other.transform.IsChildOf(item.transform)) continue;

                Bounds above = other.WorldBox;
                bool restsOn = above.min.y > box.min.y + 0.005f && above.min.y < box.max.y + 0.01f;
                if (restsOn && FlatDistance(above.center, box.center) < reach) return true;
            }

            return false;
        }

        /// <summary>Bakılan noktanın altındaki tabak (üstü açık, elde olmayan): dilim oraya konuyor.</summary>
        KitchenPlate FindPlateAt(Vector3 aimPoint)
        {
            KitchenPlate nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (KitchenPlate plate in _plates)
            {
                KitchenItem item = plate.Item;
                if (item == _held || !item.gameObject.activeInHierarchy) continue;

                float bottom = item.transform.position.y;
                float distance = FlatDistance(item.transform.position, aimPoint);
                if (distance > item.grabRadius || aimPoint.y < bottom - 0.02f || aimPoint.y > bottom + item.height + PlateAimAbove) continue;
                if (distance >= nearestDistance || IsCovered(item)) continue;

                nearest = plate;
                nearestDistance = distance;
            }

            return nearest;
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

        // ── İPUCU (test) ────────────────────────────────────────────────────

        /// <summary>Sol ve sağ tıkın o an ne yapacağı (sunucunun kararıyla aynı kurallar); test ipucu için.</summary>
        public string DescribeActions(Vector3 aimPoint)
        {
            string left = null;
            foreach (KitchenDoor door in _doors)
                if (door.IsTouched(aimPoint)) left = "Kapak";

            if (left == null && _held != null)
            {
                if (_held.Ingredient != null && _bowl != null && IsAimingInto(_bowl, aimPoint, BowlAimAbove)) left = "Kaseye koy";
                else if (_held.Ingredient != null && FindPlateAt(aimPoint) != null) left = "Tabağa koy";
                else left = TryFindDropSpot(_held, aimPoint, aimPoint, out _, out _) ? "Bırak" : "Bırakılamaz";
            }
            else if (left == null)
            {
                KitchenItem nearest = FindGrabbable(aimPoint);
                if (nearest != null) left = "Al: " + NameOf(nearest);
            }

            string right = null;
            if (_held != null && _held.kind == KitchenItemKind.Knife) right = "Kes";
            else if (_held != null && _held.Container != null && _held.Container.canPour)
            {
                KitchenContainer target = FindPourTarget(aimPoint, _held.Container);
                right = target != null ? "Dök → " + NameOf(target.Item) + " (basılı tut)" : "Dök (yere)";
            }

            string text = left != null ? "[Sol tık] " + left : "";
            if (right != null) text += (text.Length > 0 ? "     " : "") + "[Sağ tık] " + right;
            return text;
        }

        static string NameOf(KitchenItem item)
        {
            if (item.Container != null && !string.IsNullOrEmpty(item.Container.displayName)) return item.Container.displayName;
            if (item.Ingredient != null) return Ingredients.NameOf(item.Ingredient.ingredient);
            return item.gameObject.name.Replace("(Clone)", "").Trim();
        }

        // ── SUNUCU ──────────────────────────────────────────────────────────

        /// <summary>Elde kapaklı kavanoz varsa süresi gelince kapağı düşürüyor; nereye düştüğü önemli değil.</summary>
        void ServerUpdateLid()
        {
            if (_held == null || !_held.HasLid || Time.time < _lidTime) return;

            _lidTime = float.PositiveInfinity;
            Vector2 side = Random.insideUnitCircle.normalized * LidThrowSpeed;
            ServerSend(new KitchenEventMessage
            {
                type = KitchenEventType.LidOff,
                itemId = _held.Id,
                position = new Vector3(side.x, LidPopSpeed, side.y),
            });
        }

        void ServerGrabOrDrop(Vector3 aimPoint, Vector3 from)
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
                // Yumurta, dilim gibi tane tane eklenenler kasenin üstüne bırakılınca içine giriyor, tabağın üstüne
                // bırakılınca tabağa diziliyor.
                if (_held.Ingredient != null && _bowl != null && IsAimingInto(_bowl, aimPoint, BowlAimAbove))
                {
                    ServerSend(new KitchenEventMessage { type = KitchenEventType.AddToBowl, itemId = _held.Id, targetId = _bowl.Item.Id });
                    return;
                }

                KitchenPlate plate = _held.Ingredient != null ? FindPlateAt(aimPoint) : null;
                if (plate != null)
                {
                    ServerSend(new KitchenEventMessage { type = KitchenEventType.AddToPlate, itemId = _held.Id, targetId = plate.Item.Id });
                    return;
                }

                LastRefusal = null;
                if (TryFindDropSpot(_held, aimPoint, from, out Vector3 spot, out Quaternion rotation))
                {
                    ServerSend(new KitchenEventMessage { type = KitchenEventType.Drop, itemId = _held.Id, position = spot, rotation = rotation });
                }
                else
                {
                    if (LastRefusal == null && !IsOverCounter(aimPoint) && FindSurface(aimPoint, out Vector3 surface, out Vector3 up))
                    {
                        Quaternion tilt = Quaternion.FromToRotation(Vector3.up, up) * transform.rotation;
                        LastRefusal = "yakında yer yok (" + WhyNotFree(_held, Rest(_held, surface, tilt), tilt) + ")";
                    }
                    else if (LastRefusal == null) LastRefusal = "adada yakında boş yer yok";
                    ServerNotify(new KitchenEventMessage { type = KitchenEventType.Refused, itemId = _held.Id, position = aimPoint });
                }
                return;
            }

            KitchenItem nearest = FindGrabbable(aimPoint);
            if (nearest != null)
                ServerSend(new KitchenEventMessage { type = KitchenEventType.Grab, itemId = nearest.Id });
        }

        /// <summary>
        /// Tıklanan noktadaki eşya: yüzeyi noktaya en yakın olan, yani nişangahın değdiği eşya (uzun bardağın üst
        /// kenarına tıklayınca yanındaki kısa kutu alınmasın). Aday: yüzeyi 3 cm içinde ya da merkezi tutma
        /// yarıçapında olanlar; küçük eşyalar (dilim, yumurta) için en az 7 cm, el tam üstüne denk gelmese de alınsın.
        /// Üstünde başka eşya duran (yığındaki alt tabak) alınmıyor.
        /// </summary>
        KitchenItem FindGrabbable(Vector3 aimPoint)
        {
            KitchenItem nearest = null;
            float nearestDistance = float.PositiveInfinity;

            foreach (KitchenItem item in _items)
            {
                if (!item.gameObject.activeInHierarchy || item == _held) continue;

                float surface = item.SurfaceDistance(aimPoint);
                bool near = surface <= SurfaceGrabTolerance || Vector3.Distance(item.Center, aimPoint) <= Mathf.Max(item.grabRadius, MinGrabRadius);
                // Tabaktaki dilim tabağın kutusunun içinde: dilime yakın tıklanınca tabak değil dilim alınıyor.
                float score = OnPlate(item) ? surface - PieceGrabBonus : surface;
                if (!near || score >= nearestDistance || IsCovered(item)) continue;

                nearest = item;
                nearestDistance = score;
            }

            return nearest;
        }

        static bool OnPlate(KitchenItem item)
        {
            Transform parent = item.transform.parent;
            return parent != null && parent.TryGetComponent(out KitchenPlate plate) && plate.Holds(item);
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
            _log.Add(message);
            ServerNotify(message);
        }

        /// <summary>Saklanmadan herkese (sonradan gelen kaçırsa da bir şey değişmiyor: sadece ses).</summary>
        void ServerNotify(KitchenEventMessage message)
        {
            Apply(message);

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
                Applied?.Invoke(message, null);
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

                case KitchenEventType.AddToPlate:
                    _held = null;
                    _items[message.targetId].Plate.Add(item);
                    break;

                case KitchenEventType.Grab:
                    // Tabaktan alınan dilim artık tabakta sayılmıyor; tabak alınınca üstündekiler onunla gidiyor.
                    KitchenPlate onPlate = item.transform.parent != null ? item.transform.parent.GetComponent<KitchenPlate>() : null;
                    if (onPlate != null) onPlate.Remove(item);
                    if (item.Plate != null) item.Plate.SetPiecesTouchable(false);
                    Transform hand = LowVisionRole.Hand;
                    hand.gameObject.SetActive(true);
                    item.SetTouchable(false);
                    item.transform.SetParent(hand, false);
                    item.transform.localPosition = item.HoldPosition;
                    item.transform.localRotation = item.HoldRotation;
                    _held = item;
                    if (NetworkServer.active)
                        _lidTime = item.HasLid ? Time.time + Random.Range(LidMinSeconds, LidMaxSeconds) : float.PositiveInfinity;
                    break;

                case KitchenEventType.LidOff:
                    item.DropLid(message.position);
                    break;

                case KitchenEventType.Drop:
                    item.transform.SetParent(transform, false);
                    item.transform.SetPositionAndRotation(message.position, message.rotation);
                    item.SetTouchable(true);
                    if (item.Plate != null) item.Plate.SetPiecesTouchable(true);
                    _held = null;
                    break;

                case KitchenEventType.Cut:
                    KitchenItem slice = Instantiate(item.NextSlice(out float sliceScale), transform);
                    slice.transform.position = message.position;
                    slice.transform.localScale = new Vector3(sliceScale, 1f, sliceScale);
                    Register(slice);
                    item.CutOnce();
                    if (slice.TryGetComponent(out KitchenIngredientItem sliceIngredient)) _recipe.NoteSlice(sliceIngredient.ingredient);
                    if (_chopTimer <= 0f) _chopTimer = ChopSeconds;
                    break;
            }

            _recipe.UpdateSteps(this);
            Applied?.Invoke(message, item);
        }

        /// <summary>Her mutfak olayı uygulandıktan sonra (herkeste): efekt ve sesler buna bağlanıyor.</summary>
        public static event System.Action<KitchenEventMessage, KitchenItem> Applied;

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
            NetworkClient.RegisterHandler<KitchenCratesMessage>(OnClientCrates);
        }

        static void OnServerRequest(NetworkConnectionToClient sender, KitchenRequestMessage message)
        {
            if (s_instance == null || !LowVisionRole.IsLowVision(sender)) return;

            switch (message.request)
            {
                case KitchenRequest.Cut: s_instance.ServerCut(message.aimPoint); break;
                case KitchenRequest.Pour: s_instance.ServerPour(message.aimPoint); break;
                default:
                    Vector3 from = sender.identity != null ? sender.identity.transform.position : message.aimPoint;
                    s_instance.ServerGrabOrDrop(message.aimPoint, from);
                    break;
            }
        }

        static void OnServerReady(NetworkConnectionToClient sender, KitchenReadyMessage message)
        {
            if (s_instance == null) return;

            s_instance._readyClients.Add(sender);

            // Kasalar ve tarif önce: olaylar yiyeceklerin bu oyundaki yerinden, kaseye konanlar tarife göre.
            sender.Send(new KitchenCratesMessage { order = s_instance._crateOrder ?? new int[0] });
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

        static void OnClientCrates(KitchenCratesMessage message)
        {
            if (s_instance != null) s_instance.ArrangeCrates(message.order);
        }
    }
}
