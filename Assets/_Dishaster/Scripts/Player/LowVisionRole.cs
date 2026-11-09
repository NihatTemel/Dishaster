using Mirror;
using UnityEngine;

namespace Dishaster
{
    /// <summary>El, görmeyen oyuncunun kendi uzayında (kökü ve yönüne göre): uzakta da gövdeyle birlikte gidiyor.</summary>
    public struct LowVisionHandMessage : NetworkMessage
    {
        public Vector3 position;
        public float yaw;
        public bool touching;
    }

    /// <summary>
    /// Görmeyen oyuncuya özel kurulum (prefab). Normal yürüyor ama ekranı siyah. Eli baktığı yere uzanıyor:
    /// kol boyunda bir yüzeye değince oradaki hatlar beliriyor, el gidince kapanıyor. Değmiyorken el göğüs önünde
    /// (eşya taşıyorsa eşya orada); uzaktakiler eli ve eşyayı aynı yerde görüyor. Sol tık al/bırak ya da kapak
    /// aç/kapa, sağ tık kes; elde dökülebilen bir kap varken sağ tık basılı tutulunca döküyor (bkz. KitchenCounter). Diğer oyuncular eli görüyor (Mirror mesajı): tahtadaki gibi sahne objesine
    /// NetworkIdentity gerekmesin diye.
    /// </summary>
    public class LowVisionRole : MonoBehaviour
    {
        const float HandSendInterval = 0.05f;
        const float HandSmoothing = 25f;
        const float RemoteHandSmoothing = 15f;
        const float HeldTouchScale = 1.3f;
        const float SurfaceOffset = 0.01f;
        const float PalmAboveSurface = 0.02f;
        const float RestDrop = 0.1f;

        // Bir yere dokunmuyorken el (ve tutulan eşya) göğüs önünde: sağda, aşağıda, öne doğru. Bakışın eğimini
        // yarı yarıya izliyor: aşağı bakınca elindekini görüyor, düz bakınca ekranın sağ altında; dışarıdan da
        // eşya göğüs hizasında (yüzünde değil).
        static readonly Vector3 CarryOffset = new Vector3(0.17f, -0.4f, 0.4f);
        const float CarryPitchFollow = 0.5f;

        static LowVisionRole s_instance;

        /// <summary>Tutulan eşyalar bu ele bağlanıyor.</summary>
        public static Transform Hand => s_instance != null ? s_instance.hand : null;

        /// <summary>El şu an bir yüzeye/eşyaya değiyor mu (uzaktakilerde mesajla geliyor): değmiyorsa el boşta ya da taşıyor.</summary>
        public static bool Touching => s_instance != null && s_instance._touching;

        /// <summary>Kendi makinesinde: tıklayınca gönderilen nokta (nişangahın değdiği yer). Test de buradan tıklıyor.</summary>
        public static Vector3 AimPoint => s_instance != null ? s_instance._aimPoint : Vector3.zero;

        /// <summary>Bu makinedeki oyuncu görmeyen mi: kaplardaki miktar ona hiç çizilmiyor.</summary>
        public static bool LocalIsLowVision { get; private set; }

        [Header("Referanslar")]
        public Shader effectShader;
        public Shader outlineMaskShader;
        public Transform hand;
        [Tooltip("Sadece görmeyende açılıyor: tahta tarafına geçemesin.")]
        public Collider boardBarrier;

        [Header("El (metre)")]
        [Tooltip("Gözden (nişangahtan): bu kadar yakındaki yüzeye/eşyaya el değiyor. Buzdolabının alt rafına üst rafın altından bakabilmek için bu kadar.")]
        public float reach = 1.45f;
        public float restDistance = 0.55f;

        [Header("Dokunma")]
        public float touchRadius = 0.18f;
        public float touchMemorySeconds = 1f;

        [Header("Hatlar (renk yok, sadece gri)")]
        public float edgeWidth = 1.5f;
        public float edgeStrength = 1f;
        public float edgeDepthThreshold = 0.02f;
        public float edgeNormalThreshold = 0.5f;
        [Range(0f, 1f)] public float greyFill = 0.25f;

        [Header("Sürekli hatlar (el değmeden, uzaktan da)")]
        [Range(0f, 1f)] public float wallOutline = 0.45f;
        [Range(0f, 1f)] public float tableOutline = 0.8f;
        [Tooltip("Düz yüzeyde hat çıkmıyor; bu sadece kopukluk ve kıvrımlar için.")]
        public float outlineDepthThreshold = 0.01f;

        bool _resolved;
        bool _controlsHand;
        bool _hasRemoteTarget;
        bool _touching;
        bool _lastSentTouching;
        DishasterPlayer _player;
        Vector3 _aimPoint;
        // El oyuncunun uzayında yumuşuyor: yürürken/dönerken eşya elde geride kalmıyor, uzaktakilerde de gövdeyle gidiyor.
        Vector3 _handLocal;
        Vector3 _remoteTarget;
        float _remoteYaw;
        Vector3 _remoteLocal;
        float _remoteYawSmoothed;
        DishasterPlayer _remotePlayer;
        Vector3 _lastSent;
        float _lastSentYaw;
        float _nextSendTime;

        /// <summary>Tipin hat parlaklığı. Yeni LowVisionOutlineType eklenince buraya da eklenmeli.</summary>
        public float OutlineLevel(LowVisionOutlineType type)
        {
            switch (type)
            {
                case LowVisionOutlineType.Wall: return wallOutline;
                case LowVisionOutlineType.Table: return tableOutline;
                default: return 0f;
            }
        }

        KitchenItem HeldItem => KitchenCounter.Instance != null ? KitchenCounter.Instance.Held : null;

        /// <summary>
        /// Kol boyundaki dokunma noktası; görüş buradan açılıyor. Bir yere değmeden bir şey taşırken elindeki eşya.
        /// </summary>
        public Vector3 TouchCenter => !_touching && HeldItem != null ? HeldItem.Center : _aimPoint;

        // Elde tutulan eşya görünür kalıyor: bıçak gibi uzun olanlar için alan eşyaya göre açılıyor.
        public float TouchRadius => HeldItem != null ? Mathf.Max(touchRadius, HeldItem.grabRadius * HeldTouchScale) : touchRadius;

        void Awake()
        {
            s_instance = this;

            // Küp el artık sadece hedef (tutulan eşyalar buna bağlı): görünen el karakterin kendisi (IK).
            foreach (Transform part in hand)
                if (part.TryGetComponent(out Renderer renderer)) renderer.enabled = false;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            LocalIsLowVision = false;
        }

        // LateUpdate: oyuncunun o karedeki bakışı işlendikten sonra el yerine gitsin.
        void LateUpdate()
        {
            if (!_resolved)
            {
                ResolveRole();
                return;
            }

            if (_controlsHand) UpdateOwnHand();
            else UpdateRemoteHand();
        }

        void ResolveRole()
        {
            // Lobideki oyuncu objesi sahne geçişinde değişiyor; oyun oyuncusu doğana kadar bekliyoruz.
            if (NetworkClient.localPlayer == null) return;
            if (!NetworkClient.localPlayer.TryGetComponent(out DishasterPlayer player)) return;

            _resolved = true;

            if (player.role != MonkeyRoles.LowVision) return;

            _player = player;
            _controlsHand = true;
            LocalIsLowVision = true;
            _aimPoint = hand.position;
            _handLocal = player.transform.InverseTransformPoint(hand.position);
            hand.gameObject.SetActive(true);
            boardBarrier.enabled = true;

            // Test: normal görüş, nişangah da kalıyor (bkz. DishasterTest).
            if (DishasterTest.LowVisionSeesEverything) return;

            player.playerCamera.gameObject.AddComponent<LowVisionEffect>().Setup(this);
            // Kaplardaki miktar ve akıntı bu kamerada hiç çizilmiyor (hat olarak da çıkmıyor).
            BlindHiddenLayer.HideFrom(player.playerCamera);

            if (GameHud.Instance != null) GameHud.Instance.HideCrosshair();
        }

        void UpdateOwnHand()
        {
            // Işın kameradan (nişangah): tıklanan şey nişangahtaki. Kamera eğilince sadece bakış yönünde kaydığı için
            // ışın uzanmayla değişmiyor (el ile kamera birbirini kovalamıyor). İnceleme gibi kamera başka yerdeyken
            // gözlerden. Taşıma yeri kafaya göre.
            Transform head = _player.headPivot;
            Transform view = _player.playerCamera.transform;
            Vector3 origin = _player.ControlBlocked ? head.TransformPoint(_player.CameraOffset) : view.position;
            Vector3 direction = _player.ControlBlocked ? head.forward : view.forward;
            KitchenItem held = HeldItem;

            // Eşyalar trigger; oyuncunun kendi kapsülünü ışın içinden başladığı için görmüyor.
            bool touching = Physics.Raycast(origin, direction, out RaycastHit hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            _touching = touching;
            _aimPoint = touching
                ? hit.point + hit.normal * SurfaceOffset
                : origin + direction * restDistance + Vector3.down * RestDrop;

            float yaw = head.eulerAngles.y;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            // Değiyorsa el oraya uzanıyor (eşya tutuyorsa eşya oraya gelecek şekilde); değmiyorsa göğüs önünde.
            Vector3 target;
            if (touching)
            {
                target = _aimPoint + Vector3.up * PalmAboveSurface;
                if (held != null) target -= rotation * Vector3.forward * held.reachBack;
            }
            else
            {
                float pitch = Mathf.DeltaAngle(0f, head.eulerAngles.x);
                target = head.position + Quaternion.Euler(pitch * CarryPitchFollow, yaw, 0f) * CarryOffset;
            }

            Transform body = _player.transform;
            _handLocal = Vector3.Lerp(_handLocal, body.InverseTransformPoint(target), 1f - Mathf.Exp(-HandSmoothing * Time.deltaTime));
            hand.SetPositionAndRotation(body.TransformPoint(_handLocal), rotation);

            // İmleç serbestse ESC menüsü açık demek.
            UpdateHint();

            if (Cursor.lockState == CursorLockMode.Locked && !_player.ControlBlocked && KitchenCounter.Instance != null)
            {
                if (Input.GetMouseButtonDown(0)) KitchenCounter.Instance.RequestGrabOrDrop(_aimPoint);
                if (Input.GetMouseButtonDown(1)) KitchenCounter.Instance.RequestCut(_aimPoint);
                if (Input.GetMouseButton(1)) KitchenCounter.Instance.RequestPour(_aimPoint);
            }

            Vector3 position = _handLocal;
            float relativeYaw = Mathf.DeltaAngle(body.eulerAngles.y, yaw);
            bool changed = position != _lastSent || !Mathf.Approximately(relativeYaw, _lastSentYaw) || touching != _lastSentTouching;
            if (changed && Time.time >= _nextSendTime)
            {
                _lastSent = position;
                _lastSentYaw = relativeYaw;
                _lastSentTouching = touching;
                _nextSendTime = Time.time + HandSendInterval;
                NetworkClient.Send(new LowVisionHandMessage { position = position, yaw = relativeYaw, touching = touching }, Channels.Unreliable);
            }
        }

        TMPro.TextMeshProUGUI _hint;

        /// <summary>
        /// Sadece test anahtarı açıkken (Görmedim normal görürken): nişangahın altında sol/sağ tıkın ne yapacağı.
        /// Gerçek oyunda görmeyen hiçbir arayüz görmüyor.
        /// </summary>
        void UpdateHint()
        {
            if (!DishasterTest.LowVisionSeesEverything || KitchenCounter.Instance == null) return;

            if (_hint == null)
            {
                var canvasObject = new GameObject("LowVisionTestHint");
                canvasObject.transform.SetParent(transform, false);
                var canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5;
                var scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                _hint = new GameObject("Hint").AddComponent<TMPro.TextMeshProUGUI>();
                _hint.transform.SetParent(canvasObject.transform, false);
                _hint.fontSize = 28f;
                _hint.alignment = TMPro.TextAlignmentOptions.Center;
                _hint.raycastTarget = false;
                RectTransform rect = _hint.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, -110f);
                rect.sizeDelta = new Vector2(1200f, 50f);
            }

            bool show = Cursor.lockState == CursorLockMode.Locked && !_player.ControlBlocked;
            string text = show ? KitchenCounter.Instance.DescribeActions(_aimPoint) : "";
            if (_hint.text != text) _hint.text = text;
        }

        void UpdateRemoteHand()
        {
            if (!_hasRemoteTarget) return;

            if (_remotePlayer == null)
            {
                foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
                    if (identity != null && identity.TryGetComponent(out DishasterPlayer player) && player.role == MonkeyRoles.LowVision)
                        _remotePlayer = player;

                if (_remotePlayer == null) return;
                _remoteLocal = _remoteTarget;
                _remoteYawSmoothed = _remoteYaw;
            }

            float blend = 1f - Mathf.Exp(-RemoteHandSmoothing * Time.deltaTime);
            _remoteLocal = Vector3.Lerp(_remoteLocal, _remoteTarget, blend);
            _remoteYawSmoothed = Mathf.LerpAngle(_remoteYawSmoothed, _remoteYaw, blend);

            Transform body = _remotePlayer.transform;
            hand.SetPositionAndRotation(body.TransformPoint(_remoteLocal), body.rotation * Quaternion.Euler(0f, _remoteYawSmoothed, 0f));
        }

        // ── AĞ ──────────────────────────────────────────────────────────────

        public static void RegisterServerHandlers()
        {
            NetworkServer.RegisterHandler<LowVisionHandMessage>(OnServerHand);
        }

        public static void RegisterClientHandlers()
        {
            NetworkClient.RegisterHandler<LowVisionHandMessage>(OnClientHand);
        }

        /// <summary>Sunucu tarafı rol kontrolü: el ve mutfak mesajlarını sadece görmeyen gönderebilir.</summary>
        public static bool IsLowVision(NetworkConnectionToClient connection)
        {
            return connection.identity != null
                && connection.identity.TryGetComponent(out DishasterPlayer player)
                && player.role == MonkeyRoles.LowVision;
        }

        static void OnServerHand(NetworkConnectionToClient sender, LowVisionHandMessage message)
        {
            if (!IsLowVision(sender)) return;

            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != sender)
                    connection.Send(message, Channels.Unreliable);
        }

        static void OnClientHand(LowVisionHandMessage message)
        {
            if (s_instance == null) return;

            // İlk mesajda el beliriyor; o ana kadar kimse görmüyor. Yer, oyuncu bulununca (UpdateRemoteHand).
            if (!s_instance._hasRemoteTarget)
            {
                s_instance._hasRemoteTarget = true;
                s_instance._remotePlayer = null;
                s_instance.hand.gameObject.SetActive(true);
            }

            s_instance._remoteTarget = message.position;
            s_instance._remoteYaw = message.yaw;
            s_instance._touching = message.touching;
        }
    }
}
