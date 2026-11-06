using Mirror;
using UnityEngine;

namespace Dishaster
{
    public struct LowVisionHandMessage : NetworkMessage
    {
        public Vector3 position;
        public float yaw;
    }

    /// <summary>
    /// Görmeyen oyuncuya özel kurulum (prefab). Normal yürüyor ama ekranı siyah. Eli baktığı yere uzanıyor:
    /// kol boyunda bir yüzeye değince oradaki hatlar beliriyor, el gidince kapanıyor. Sol tık al/bırak ya da kapak
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

        static LowVisionRole s_instance;

        /// <summary>Tutulan eşyalar bu ele bağlanıyor.</summary>
        public static Transform Hand => s_instance != null ? s_instance.hand : null;

        /// <summary>Bu makinedeki oyuncu görmeyen mi: kaplardaki miktar ona hiç çizilmiyor.</summary>
        public static bool LocalIsLowVision { get; private set; }

        [Header("Referanslar")]
        public Shader effectShader;
        public Shader outlineMaskShader;
        public Transform hand;
        [Tooltip("Sadece görmeyende açılıyor: tahta tarafına geçemesin.")]
        public Collider boardBarrier;

        [Header("El (metre)")]
        public float reach = 1.7f;
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
        DishasterPlayer _player;
        Vector3 _aimPoint;
        Vector3 _remoteTarget;
        float _remoteYaw;
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

        /// <summary>Kol boyundaki dokunma noktası; görüş buradan açılıyor.</summary>
        public Vector3 TouchCenter => _aimPoint;

        // Elde tutulan eşya görünür kalıyor: bıçak gibi uzun olanlar için alan eşyaya göre açılıyor.
        public float TouchRadius => HeldItem != null ? Mathf.Max(touchRadius, HeldItem.grabRadius * HeldTouchScale) : touchRadius;

        void Awake()
        {
            s_instance = this;
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
            hand.gameObject.SetActive(true);
            boardBarrier.enabled = true;
            player.playerCamera.gameObject.AddComponent<LowVisionEffect>().Setup(this);
            // Kaplardaki miktar ve akıntı bu kamerada hiç çizilmiyor (hat olarak da çıkmıyor).
            BlindHiddenLayer.HideFrom(player.playerCamera);

            if (GameHud.Instance != null) GameHud.Instance.HideCrosshair();
        }

        void UpdateOwnHand()
        {
            Transform view = _player.playerCamera.transform;
            KitchenItem held = HeldItem;

            // Eşyalar trigger; oyuncunun kendi kapsülünü ışın içinden başladığı için görmüyor.
            bool touching = Physics.Raycast(view.position, view.forward, out RaycastHit hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            _aimPoint = touching
                ? hit.point + hit.normal * SurfaceOffset
                : view.position + view.forward * restDistance + Vector3.down * RestDrop;

            float yaw = view.eulerAngles.y;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            Vector3 target = _aimPoint + Vector3.up * PalmAboveSurface;
            if (held != null) target -= rotation * Vector3.forward * held.reachBack;

            hand.SetPositionAndRotation(Vector3.Lerp(hand.position, target, 1f - Mathf.Exp(-HandSmoothing * Time.deltaTime)), rotation);

            // İmleç serbestse ESC menüsü açık demek.
            if (Cursor.lockState == CursorLockMode.Locked && KitchenCounter.Instance != null)
            {
                if (Input.GetMouseButtonDown(0)) KitchenCounter.Instance.RequestGrabOrDrop(_aimPoint);
                if (Input.GetMouseButtonDown(1)) KitchenCounter.Instance.RequestCut(_aimPoint);
                if (Input.GetMouseButton(1)) KitchenCounter.Instance.RequestPour(_aimPoint);
            }

            Vector3 position = hand.position;
            if ((position != _lastSent || !Mathf.Approximately(yaw, _lastSentYaw)) && Time.time >= _nextSendTime)
            {
                _lastSent = position;
                _lastSentYaw = yaw;
                _nextSendTime = Time.time + HandSendInterval;
                NetworkClient.Send(new LowVisionHandMessage { position = position, yaw = yaw }, Channels.Unreliable);
            }
        }

        void UpdateRemoteHand()
        {
            if (!_hasRemoteTarget) return;

            float blend = 1f - Mathf.Exp(-RemoteHandSmoothing * Time.deltaTime);
            hand.SetPositionAndRotation(
                Vector3.Lerp(hand.position, _remoteTarget, blend),
                Quaternion.Slerp(hand.rotation, Quaternion.Euler(0f, _remoteYaw, 0f), blend));
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

            // İlk mesajda el beliriyor; o ana kadar kimse görmüyor.
            if (!s_instance._hasRemoteTarget)
            {
                s_instance._hasRemoteTarget = true;
                s_instance.hand.position = message.position;
                s_instance.hand.gameObject.SetActive(true);
            }

            s_instance._remoteTarget = message.position;
            s_instance._remoteYaw = message.yaw;
        }
    }
}
