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
    /// defter görünüyor. Defteri hep sol el, sol kenarından tek başına tutuyor: normalde aşağıda; E ile yüze
    /// kaldırılıp imleçle çiziliyor (sağ el kalemle gelip çiziyor); sağ tık basılıyken sol el defteri çevirip
    /// karşıya gösteriyor (defter elin etrafında dönüyor). Sağ el sadece çizerken görünüyor.
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
        // Gösterirken el defterle birlikte bu hızla dönüyor (saniyede, üstel): dönüş görünsün.
        const float TurnSpeed = 7f;

        static NotebookRole s_instance;

        public static NotebookRole Instance => s_instance;

        /// <summary>Defteri tutan Bilmiyorum (henüz doğmadıysa ya da düştüyse null).</summary>
        public DishasterPlayer Owner => _owner;

        public bool IsDrawing => _owner != null && _pose == NotebookPose.Draw;

        /// <summary>Şimdiki duruş (tut, çiz, göster); uzaktakilerde mesajla geliyor.</summary>
        public NotebookPose Pose => _pose;

        /// <summary>
        /// Sol avucun yeri (defterin uzayında): sol kenarın hemen dışı, ortanın biraz altı. El kenarı kavrıyor: avuç
        /// kenara bakıyor, parmaklar yukarı-arkaya kapanıp kapağın arkasını sarıyor, başparmak sayfanın önünde.
        /// Defter bu noktanın etrafında dönüyor (gösterirken el de onunla).
        /// </summary>
        public Vector3 GripLocal => new Vector3(-pageSize.x * 0.5f - 0.025f, -0.04f, 0.008f);

        public Vector3 LeftGripPoint => notebook.TransformPoint(GripLocal);

        /// <summary>Kalemi tutan avucun yeri: kalemin ortasından biraz yukarısı.</summary>
        public Vector3 PenGripPoint => _pencil != null ? _pencil.position + _pencil.up * 0.035f : rightHand.position;

        /// <summary>Kalemin ucu (sayfaya değen nokta).</summary>
        public Vector3 PenTip => rightHand.position;

        [Header("Referanslar")]
        public Transform rig;
        public Transform notebook;
        public DrawingBoard page;
        public Transform leftHand;
        public Transform rightHand;

        [Header("Defter (metre)")]
        public Vector2 pageSize = new Vector2(0.5f, 0.25f);

        [Header("Duruşlar: sol elin yeri (kafaya göre) ve defterin dönüşü")]
        public Vector3 holdHand = new Vector3(-0.22f, -0.05f, 0.48f);
        public Vector3 holdRotation = new Vector3(40f, 0f, 0f);
        public Vector3 drawHand = new Vector3(-0.25f, 0.03f, 0.42f);
        public Vector3 drawRotation = Vector3.zero;
        [Tooltip("Gösterirken el defteri kendi etrafında saat yönünün tersine (üstten bakınca) çeviriyor: defter elin soluna geçiyor, el biraz sağa geliyor ki defter göğsün önünde kalsın.")]
        public Vector3 showHand = new Vector3(0.08f, -0.25f, 0.45f);
        [Tooltip("Y: dönüş açısı; eksi = üstten bakınca saat yönünün tersi (sağ kenar öne gelip karşıya döner).")]
        public Vector3 showRotation = new Vector3(0f, -180f, 0f);
        [Tooltip("Diğer oyuncularda defter bu kadar öne (eksi: geri) alınıyor: kendi ekranındaki yer kol boyunun ucunda, dışarıdan dirsek bükük, göğüs önünde.")]
        public float remoteForward = -0.12f;
        [Tooltip("Diğer oyuncularda defter bu kadar aşağıda: kendi ekranında görünsün diye göze yakın tutuluyor, dışarıdan göğüs/karın hizasında (kol aşağıda, ön kol yatay).")]
        public float remoteDown = 0.48f;
        [Tooltip("Gösterirken dışarıdan biraz daha yukarıda: karşıdakinin göğsü hizasında.")]
        public float remoteShowUp = 0.16f;
        [Tooltip("Diğer oyuncularda defter bakışın eğimini bu kadar izliyor: yukarı bakınca defter kafanın üstüne çıkmasın.")]
        [Range(0f, 1f)] public float remotePitchFollow = 0.4f;

        [Header("Sağ el (deftere göre)")]
        public Vector3 penLift = new Vector3(0f, 0f, -0.004f);
        [Tooltip("Çizime başlarken sağ el buradan (sağ alttan) gelip kaleme gidiyor.")]
        public Vector3 penRest = new Vector3(0.4f, -0.25f, -0.12f);

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
        bool _testOverride;
        Transform _pencil;
        bool _isLocal;
        NotebookPose _pose = NotebookPose.Hold;
        Vector2 _penUv = new Vector2(0.5f, 0.5f);
        Vector3 _handPosition;
        Quaternion _localRotation;
        // Dönüş ikiye ayrık: eğim (x) ve çevirme açısı (y). Açı sayı olarak yumuşuyor ki çevirme hep aynı yönden olsun
        // (180 derecede iki yol eşit, kuaterniyon rastgele seçiyordu).
        Quaternion _tilt = Quaternion.identity;
        float _yaw;
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
            rightHand.gameObject.SetActive(false);

            // Küp eller artık sadece hedef: görünen eller karakterin kendisi (IK) ve FPS elleri. Kalem kalıyor.
            _pencil = CharacterBody.FindBone(rightHand, "Pencil");
            foreach (Renderer part in leftHand.GetComponentsInChildren<Renderer>(true))
                part.enabled = false;
            foreach (Renderer part in rightHand.GetComponentsInChildren<Renderer>(true))
                if (_pencil == null || part.transform != _pencil) part.enabled = false;
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
            if (!ResolveOwner() || !_isLocal || _testOverride) return;

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

        /// <summary>Otomatik test için: duruşu Bilmiyorum'un tuşlarına basmış gibi değiştiriyor.</summary>
        public void SetPoseForTest(NotebookPose pose)
        {
            if (!_isLocal) return;
            _testOverride = true;
            SetLocalPose(pose);
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
            bool wasDrawing = _pose == NotebookPose.Draw;
            _pose = pose;
            if (pose != NotebookPose.Draw) _drawBlend = 0f;

            // Sağ el sadece çizerken: sağ alttan gelip kaleme gidiyor.
            bool drawing = pose == NotebookPose.Draw;
            if (drawing && !wasDrawing) rightHand.localPosition = penRest;
            rightHand.gameObject.SetActive(drawing);
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

            Vector3 targetHand;
            Vector3 targetRotation;
            switch (_pose)
            {
                case NotebookPose.Draw:
                    targetHand = drawHand;
                    targetRotation = drawRotation;
                    break;
                case NotebookPose.Show:
                    targetHand = showHand;
                    targetRotation = showRotation;
                    break;
                default:
                    targetHand = holdHand;
                    targetRotation = holdRotation;
                    break;
            }

            if (!_isLocal)
            {
                targetHand.z += remoteForward;
                targetHand.y -= remoteDown;
                if (_pose == NotebookPose.Show) targetHand.y += remoteShowUp;
            }
            Quaternion targetTilt = Quaternion.Euler(targetRotation.x, 0f, targetRotation.z);
            float targetYaw = targetRotation.y;
            Quaternion targetQuaternion = Quaternion.Euler(0f, targetYaw, 0f) * targetTilt;

            // Sol el ve defterin dönüşü ayrı yumuşuyor: çevirirken defter elin etrafında dönüyor.
            float blend = 1f - Mathf.Exp(-PoseSpeed * Time.deltaTime);
            float turn = 1f - Mathf.Exp(-TurnSpeed * Time.deltaTime);
            if (!_placed)
            {
                _placed = true;
                _handPosition = targetHand;
                _tilt = targetTilt;
                _yaw = targetYaw;
            }
            else
            {
                _handPosition = Vector3.Lerp(_handPosition, targetHand, blend);
                _tilt = Quaternion.Slerp(_tilt, targetTilt, blend);
                _yaw = Mathf.Lerp(_yaw, targetYaw, turn);
            }

            _localRotation = Quaternion.Euler(0f, _yaw, 0f) * _tilt;

            if (_pose == NotebookPose.Draw)
            {
                float settle = Mathf.Max(Vector3.Distance(_handPosition, targetHand) / 0.05f, Quaternion.Angle(_localRotation, targetQuaternion) / 10f);
                _drawBlend = Mathf.Max(_drawBlend, 1f - settle);
            }

            // Defter, tutma noktası (sol elin yeri) elde olacak şekilde konuyor. Kendi ekranında bakışa tam bağlı;
            // dışarıdan eğimi azaltılmış (yukarı/aşağı bakınca kol omuzdan dönüp durmasın).
            Vector3 localPosition = _handPosition - _localRotation * GripLocal;
            Transform head = _owner.headPivot;
            Quaternion frame = head.rotation;
            if (!_isLocal)
            {
                float pitch = Mathf.DeltaAngle(0f, head.localEulerAngles.x);
                frame = _owner.transform.rotation * Quaternion.Euler(pitch * remotePitchFollow, 0f, 0f);
            }

            notebook.SetPositionAndRotation(head.position + frame * localPosition, frame * _localRotation);

            UpdateRightHand();
        }

        void UpdateRightHand()
        {
            if (_pose != NotebookPose.Draw) return;

            Vector3 target = new Vector3((_penUv.x - 0.5f) * pageSize.x, (_penUv.y - 0.5f) * pageSize.y, 0f) + penLift;

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
