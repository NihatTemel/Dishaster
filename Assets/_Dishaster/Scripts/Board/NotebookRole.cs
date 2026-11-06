using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dishaster
{
    public enum NotebookPose : byte
    {
        Hold,
        Draw,
        Show,
    }

    public struct NotebookPoseMessage : NetworkMessage
    {
        public NotebookPose pose;
    }

    public struct NotebookPenMessage : NetworkMessage
    {
        public Vector2 uv;
    }

    /// <summary>
    /// Bilmiyorum'un defteri (prefab). Bilmiyorum'un kafasını izliyor; kendi ekranında bilekten sonrası eller ve
    /// defter görünüyor. Normalde defter aşağıda tutuluyor; E ile yüze kaldırılıp imleçle çiziliyor (sağ el kalem
    /// tutuyor), sağ tık basılıyken çevrilip karşıya gösteriliyor. Eller deftere bağlı, duruşa göre geçiyor.
    /// Duruş ve kalem Mirror mesajıyla gidiyor; çizgiler DrawingBoard'un mesajlarıyla. Diğerleri de defteri ve
    /// elleri görüyor: sahne objesi olduğu için NetworkIdentity gerekmiyor.
    /// </summary>
    public class NotebookRole : MonoBehaviour
    {
        // Çok yakın noktalar için mesaj gönderilmesin (UV cinsinden).
        const float MinStep = 0.002f;
        const float PoseSpeed = 12f;
        const float HandSpeed = 18f;
        const float PenSendInterval = 0.05f;
        // Çizime defter yerine oturunca başlanıyor.
        const float DrawReadyBlend = 0.9f;

        static NotebookRole s_instance;

        [Header("Referanslar")]
        public Transform rig;
        public Transform notebook;
        public DrawingBoard page;
        public Transform leftHand;
        public Transform rightHand;
        [Tooltip("Sağ elin kenarı kavrayan hali.")]
        public GameObject rightGrip;
        [Tooltip("Sağ elin kalem tutan hali.")]
        public GameObject rightPen;

        [Header("Defter (metre)")]
        public Vector2 pageSize = new Vector2(0.5f, 0.25f);

        [Header("Duruşlar (kafaya göre)")]
        public Vector3 holdPosition = new Vector3(0f, -0.25f, 0.42f);
        public Vector3 holdRotation = new Vector3(40f, 0f, 0f);
        public Vector3 drawPosition = new Vector3(0f, 0.015f, 0.3f);
        public Vector3 drawRotation = Vector3.zero;
        public Vector3 showPosition = new Vector3(0f, -0.14f, 0.55f);
        public Vector3 showRotation = new Vector3(0f, 180f, 0f);
        [Tooltip("Diğer oyuncularda defter bu kadar öne alınıyor: gövdenin (kapsül) içinde kalmasın.")]
        public float remoteForward = 0.3f;

        [Header("Eller (deftere göre)")]
        public Vector3 rightGripPosition = new Vector3(0.25f, -0.03f, 0f);
        public Vector3 penLift = new Vector3(0f, 0f, -0.004f);

        [Header("UI")]
        public GameObject prompt;
        public GameObject toolbar;
        public Button[] colorButtons;
        public GameObject[] colorSelections;
        public Button[] brushButtons;
        public GameObject[] brushSelections;
        public Button clearButton;
        public Button closeButton;

        DishasterPlayer _owner;
        bool _isLocal;
        NotebookPose _pose = NotebookPose.Hold;
        Vector2 _penUv = new Vector2(0.5f, 0.5f);
        Vector3 _localPosition;
        Quaternion _localRotation;
        float _drawBlend;
        bool _placed;

        bool _stroking;
        bool _hasLast;
        Vector2 _last;
        int _color;
        int _brush = 1;
        Vector2 _sentPen;
        float _nextPenSend;

        void Awake()
        {
            s_instance = this;
            rig.gameObject.SetActive(false);
            toolbar.SetActive(false);
            prompt.SetActive(false);
            ApplyHandVisuals();
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        void Start()
        {
            for (int i = 0; i < colorButtons.Length; i++)
            {
                int index = i;
                colorButtons[i].onClick.AddListener(() => SelectColor(index));
            }

            for (int i = 0; i < brushButtons.Length; i++)
            {
                int index = i;
                brushButtons[i].onClick.AddListener(() => SelectBrush(index));
            }

            clearButton.onClick.AddListener(() => page.LocalClear());
            closeButton.onClick.AddListener(() => SetLocalPose(NotebookPose.Hold));

            SelectColor(_color);
            SelectBrush(_brush);
        }

        void SelectColor(int index)
        {
            _color = index;
            for (int i = 0; i < colorSelections.Length; i++)
                colorSelections[i].SetActive(i == index);
        }

        void SelectBrush(int index)
        {
            _brush = index;
            for (int i = 0; i < brushSelections.Length; i++)
                brushSelections[i].SetActive(i == index);
        }

        // ── SAHİP ───────────────────────────────────────────────────────────

        /// <summary>Bilmiyorum'u bulur. Oyuncular sahneden sonra doğuyor; düşerse defter gizleniyor.</summary>
        bool ResolveOwner()
        {
            if (_owner != null) return true;

            if (rig.gameObject.activeSelf) rig.gameObject.SetActive(false);
            if (_isLocal) LeaveLocal();

            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent(out DishasterPlayer player) || !MonkeyRoles.CanDraw(player.role)) continue;

                _owner = player;
                _isLocal = player.isLocalPlayer;
                _pose = NotebookPose.Hold;
                _placed = false;
                rig.gameObject.SetActive(true);
                return true;
            }

            return false;
        }

        void LeaveLocal()
        {
            _isLocal = false;
            prompt.SetActive(false);
            if (toolbar.activeSelf)
            {
                toolbar.SetActive(false);
                if (GameHud.Instance != null) GameHud.Instance.SetModal(false);
            }
        }

        // ── GİRİŞ (sadece Bilmiyorum'un makinesinde) ─────────────────────────

        void Update()
        {
            if (!ResolveOwner() || !_isLocal) return;

            if (_pose == NotebookPose.Draw)
            {
                UpdateDrawing();
                return;
            }

            // İmleç serbestse ESC menüsü açık demek.
            bool hasControl = Cursor.lockState == CursorLockMode.Locked && !_owner.ControlBlocked;
            prompt.SetActive(hasControl);
            if (!hasControl)
            {
                if (_pose == NotebookPose.Show) SetLocalPose(NotebookPose.Hold);
                return;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                SetLocalPose(NotebookPose.Draw);
                return;
            }

            SetLocalPose(Input.GetMouseButton(1) ? NotebookPose.Show : NotebookPose.Hold);
        }

        void SetLocalPose(NotebookPose pose)
        {
            if (pose == _pose) return;

            NotebookPose previous = _pose;
            SetPose(pose);
            NetworkClient.Send(new NotebookPoseMessage { pose = pose });

            if (pose == NotebookPose.Draw)
            {
                _stroking = false;
                _hasLast = false;
                prompt.SetActive(false);
                toolbar.SetActive(true);
                GameHud.Instance.SetModal(true);
            }
            else if (previous == NotebookPose.Draw)
            {
                _stroking = false;
                toolbar.SetActive(false);
                GameHud.Instance.SetModal(false);
            }
        }

        void SetPose(NotebookPose pose)
        {
            _pose = pose;
            if (pose != NotebookPose.Draw) _drawBlend = 0f;
            ApplyHandVisuals();
        }

        void ApplyHandVisuals()
        {
            bool pen = _pose == NotebookPose.Draw;
            rightGrip.SetActive(!pen);
            rightPen.SetActive(pen);
        }

        void UpdateDrawing()
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
            {
                SetLocalPose(NotebookPose.Hold);
                return;
            }

            bool overPage = TryGetPagePoint(_owner.playerCamera.ScreenPointToRay(Input.mousePosition), out Vector2 uv);
            if (overPage) _penUv = uv;
            SendPen();

            // Defter yerine oturmadan, ya da toolbar'a basınca çizgi başlamıyor.
            if (Input.GetMouseButtonDown(0) && _drawBlend >= DrawReadyBlend && !EventSystem.current.IsPointerOverGameObject())
            {
                _stroking = true;
                _hasLast = false;
            }

            if (Input.GetMouseButtonUp(0))
                _stroking = false;

            if (!_stroking) return;

            if (!overPage)
            {
                // Sayfadan taşınca çizgi kopuyor; dönünce yeni noktadan devam ediyor.
                _hasLast = false;
                return;
            }

            if (!_hasLast)
                page.LocalStroke(uv, uv, _color, _brush);
            else if ((uv - _last).sqrMagnitude >= MinStep * MinStep)
                page.LocalStroke(_last, uv, _color, _brush);
            else
                return;

            _hasLast = true;
            _last = uv;
        }

        void SendPen()
        {
            if (_penUv == _sentPen || Time.time < _nextPenSend) return;

            _sentPen = _penUv;
            _nextPenSend = Time.time + PenSendInterval;
            NetworkClient.Send(new NotebookPenMessage { uv = _penUv }, Channels.Unreliable);
        }

        // Sayfa tek bir düzlem: collider yerine doğrudan düzlemle kesişim.
        bool TryGetPagePoint(Ray ray, out Vector2 uv)
        {
            uv = default;
            Transform surface = page.Surface;

            // Sadece ön yüzden: Quad'ın görünen yüzü -Z tarafında.
            if (Vector3.Dot(ray.direction, surface.forward) <= 0f) return false;

            var plane = new Plane(surface.forward, surface.position);
            if (!plane.Raycast(ray, out float distance)) return false;

            Vector3 local = surface.InverseTransformPoint(ray.GetPoint(distance));
            uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        // ── GÖRÜNÜM (herkeste) ──────────────────────────────────────────────

        // LateUpdate: kafanın o karedeki dönüşü işlendikten sonra defter yerine gitsin.
        void LateUpdate()
        {
            if (_owner == null) return;

            Vector3 targetPosition;
            Vector3 targetRotation;
            switch (_pose)
            {
                case NotebookPose.Draw:
                    targetPosition = drawPosition;
                    targetRotation = drawRotation;
                    break;
                case NotebookPose.Show:
                    targetPosition = showPosition;
                    targetRotation = showRotation;
                    break;
                default:
                    targetPosition = holdPosition;
                    targetRotation = holdRotation;
                    break;
            }

            if (!_isLocal) targetPosition.z += remoteForward;
            Quaternion targetQuaternion = Quaternion.Euler(targetRotation);

            float blend = 1f - Mathf.Exp(-PoseSpeed * Time.deltaTime);
            if (!_placed)
            {
                _placed = true;
                _localPosition = targetPosition;
                _localRotation = targetQuaternion;
            }
            else
            {
                _localPosition = Vector3.Lerp(_localPosition, targetPosition, blend);
                _localRotation = Quaternion.Slerp(_localRotation, targetQuaternion, blend);
            }

            if (_pose == NotebookPose.Draw)
                _drawBlend = Mathf.Max(_drawBlend, 1f - Vector3.Distance(_localPosition, targetPosition) / 0.05f);

            Transform head = _owner.headPivot;
            notebook.SetPositionAndRotation(head.TransformPoint(_localPosition), head.rotation * _localRotation);

            UpdateRightHand();
        }

        void UpdateRightHand()
        {
            Vector3 target = _pose == NotebookPose.Draw
                ? new Vector3((_penUv.x - 0.5f) * pageSize.x, (_penUv.y - 0.5f) * pageSize.y, 0f) + penLift
                : rightGripPosition;

            float blend = 1f - Mathf.Exp(-HandSpeed * Time.deltaTime);
            rightHand.localPosition = Vector3.Lerp(rightHand.localPosition, target, blend);
        }

        // ── AĞ ──────────────────────────────────────────────────────────────

        public static void RegisterServerHandlers()
        {
            NetworkServer.RegisterHandler<NotebookPoseMessage>(OnServerPose);
            NetworkServer.RegisterHandler<NotebookPenMessage>(OnServerPen);
        }

        public static void RegisterClientHandlers()
        {
            NetworkClient.RegisterHandler<NotebookPoseMessage>(OnClientPose);
            NetworkClient.RegisterHandler<NotebookPenMessage>(OnClientPen);
        }

        static bool SenderCanDraw(NetworkConnectionToClient sender)
        {
            return sender.identity != null
                   && sender.identity.TryGetComponent(out DishasterPlayer player)
                   && MonkeyRoles.CanDraw(player.role);
        }

        static void OnServerPose(NetworkConnectionToClient sender, NotebookPoseMessage message)
        {
            if (!SenderCanDraw(sender)) return;

            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != sender)
                    connection.Send(message);
        }

        static void OnServerPen(NetworkConnectionToClient sender, NotebookPenMessage message)
        {
            if (!SenderCanDraw(sender)) return;

            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != sender)
                    connection.Send(message, Channels.Unreliable);
        }

        static void OnClientPose(NotebookPoseMessage message)
        {
            if (s_instance == null || s_instance._isLocal) return;
            s_instance.SetPose(message.pose);
        }

        static void OnClientPen(NotebookPenMessage message)
        {
            if (s_instance == null || s_instance._isLocal) return;
            s_instance._penUv = new Vector2(Mathf.Clamp01(message.uv.x), Mathf.Clamp01(message.uv.y));
        }
    }
}
