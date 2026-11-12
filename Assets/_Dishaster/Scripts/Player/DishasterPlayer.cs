using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dishaster
{
    /// <summary>
    /// Game sahnesindeki FPS oyuncu. Konum ve bakışı kendi makinesi yönetiyor, NetworkTransform'lar dağıtıyor.
    /// Ctrl basılıyken çömeliyor: kafa pivotu (kamera) iniyor, kapsül kısalıyor, yavaş yürüyor, zıplamıyor; tavana
    /// değecekse kalkmıyor. Çömelme herkese gidiyor (karakter herkeste çömeliyor, bkz. CharacterBody).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DishasterPlayer : NetworkBehaviour
    {
        [SyncVar] public int role;
        [SyncVar] public string playerName;

        [Header("Hareket")]
        public float walkSpeed = 4f;
        public float sprintSpeed = 6.5f;
        public float jumpHeight = 1f;
        public float gravity = 20f;

        [Header("Bakış")]
        public float mouseSensitivity = 2f;
        public float maxPitch = 85f;

        [Header("Çömelme (Ctrl basılıyken)")]
        public float crouchSpeed = 2f;
        [Tooltip("Çömelince kapsülün boyu (ayakta 2 m).")]
        public float crouchHeight = 1.45f;
        [Tooltip("Ayakta ↔ çömelik geçiş süresi (saniye).")]
        public float crouchSeconds = 0.22f;

        [Header("Yakından bakma (Z basılıyken; görmeyen hariç)")]
        public float zoomFieldOfView = 22f;
        public float zoomSpeed = 12f;

        [Header("Referanslar")]
        public Renderer bodyRenderer;
        public Renderer visorRenderer;
        public Transform headPivot;
        public Camera playerCamera;
        public TMP_Text label;

        const float CameraBlendSpeed = 10f;
        const KeyCode ZoomKey = KeyCode.Z;

        // Sahibi Ctrl'e basınca sunucuya bildiriyor; diğerleri buradan görüyor.
        [SyncVar] bool _crouching;
        bool _crouchInput;
        bool _crouchForTest;
        float _crouchBlend;
        float _standHeight;
        Vector3 _standCenter;
        float _standEyeHeight;

        CharacterController _controller;
        Camera _viewCamera;
        Transform _cameraOverride;
        float _pitch;
        float _verticalVelocity;
        float _baseFieldOfView;

        /// <summary>İnceleme gibi kamerayı başka yere alan modlar: yürüme, bakış ve el/defter girişleri duruyor.</summary>
        public bool ControlBlocked { get; set; }

        /// <summary>Kameranın kafa pivotuna göre kayması: uzanıp eğilince bakış yönünde öne (nişangah aynı yerde). CharacterBody veriyor.</summary>
        public Vector3 CameraOffset { get; set; }

        /// <summary>Çömeliyor mu (kendi makinesinde tuşa göre, diğerlerinde sunucudan gelen).</summary>
        public bool IsCrouching => isLocalPlayer ? _crouchInput : _crouching;

        /// <summary>0 ayakta, 1 tam çömelik; yumuşak geçiş (herkeste aynı hızda).</summary>
        public float CrouchAmount => Mathf.SmoothStep(0f, 1f, _crouchBlend);

        /// <summary>Tam çömelince gözlerin (kafa pivotunun) ne kadar indiği: karakterin bacak boyundan (CharacterBody).</summary>
        public float CrouchDrop { get; set; } = 0.4f;

        /// <summary>Ayaktayken kafa pivotunun (gözlerin) yüksekliği; karakter giydirilince gözlerine göre (CharacterVisuals).</summary>
        public float StandEyeHeight
        {
            get => _standEyeHeight;
            set
            {
                _standEyeHeight = value;
                ApplyHeadHeight();
            }
        }

        [Server]
        public void ServerInit(int monkeyRole, string displayName)
        {
            role = monkeyRole;
            playerName = displayName;
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _standHeight = _controller.height;
            _standCenter = _controller.center;
            _standEyeHeight = headPivot.localPosition.y;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Değerler spawn'dan önce set edildiği için burada hazır ve oyun boyunca değişmiyor.
            bodyRenderer.material.color = MonkeyRoles.ColorOf(role);
            label.text = playerName + "\n<size=70%>" + MonkeyRoles.NameOf(role) + "</size>";
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            // Sahne kamerası sadece oyuncu doğana kadar görüntü veriyor.
            Camera sceneCamera = Camera.main;
            if (sceneCamera != null) sceneCamera.gameObject.SetActive(false);

            playerCamera.gameObject.SetActive(true);
            _baseFieldOfView = playerCamera.fieldOfView;

            // Kendi gövdemiz görünmesin ama gölgesi kalsın.
            bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            visorRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            label.gameObject.SetActive(false);
        }

        void Update()
        {
            if (isLocalPlayer)
            {
                // İmleç serbestse (ESC menüsü) oyuncu kontrolü durur, yerçekimi devam eder.
                bool hasControl = Cursor.lockState == CursorLockMode.Locked && !ControlBlocked;

                UpdateCrouchInput(hasControl);
                UpdateZoom(hasControl);
                if (hasControl) Look();
                Move(hasControl);
            }

            UpdateCrouch();
        }

        // ── ÇÖMELME ─────────────────────────────────────────────────────────

        void UpdateCrouchInput(bool hasControl)
        {
            bool wanted = _crouchForTest || hasControl && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            // Kalkınca kafası bir yere değecekse çömelik kalıyor.
            if (!wanted && _crouchInput && !CanStand()) wanted = true;
            if (wanted == _crouchInput) return;

            _crouchInput = wanted;
            CmdSetCrouch(wanted);
        }

        [Command]
        void CmdSetCrouch(bool crouching)
        {
            _crouching = crouching;
        }

        /// <summary>Otomatik test için: Ctrl'e basılı tutmak gibi.</summary>
        public void SetCrouchForTest(bool crouching)
        {
            _crouchForTest = crouching;
        }

        bool CanStand()
        {
            float radius = _controller.radius * 0.95f;
            Vector3 top = transform.TransformPoint(_controller.center + Vector3.up * (_controller.height / 2f - _controller.radius));
            float rise = _standHeight - _controller.height;
            return rise <= 0.01f || !Physics.SphereCast(top, radius, Vector3.up, out _, rise, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Herkeste: geçiş, kafa pivotunun yüksekliği ve kapsül (ayaklar yerinde kalıyor).</summary>
        void UpdateCrouch()
        {
            float target = IsCrouching ? 1f : 0f;
            if (Mathf.Approximately(_crouchBlend, target)) return;

            _crouchBlend = Mathf.MoveTowards(_crouchBlend, target, Time.deltaTime / Mathf.Max(0.01f, crouchSeconds));
            ApplyHeadHeight();

            float height = Mathf.Lerp(_standHeight, crouchHeight, CrouchAmount);
            _controller.height = height;
            _controller.center = _standCenter + Vector3.down * ((_standHeight - height) / 2f);
        }

        void ApplyHeadHeight()
        {
            Vector3 local = headPivot.localPosition;
            headPivot.localPosition = new Vector3(local.x, _standEyeHeight - CrouchAmount * CrouchDrop, local.z);
        }

        /// <summary>Uzaktaki küçük şeyler (ölçü bardağının çizgileri) okunabilsin diye. Görmeyen yakınlaştıramıyor.</summary>
        bool CanZoom => role != MonkeyRoles.LowVision || DishasterTest.LowVisionSeesEverything;

        void UpdateZoom(bool hasControl)
        {
            bool zooming = hasControl && CanZoom && Input.GetKey(ZoomKey);
            float target = zooming ? zoomFieldOfView : _baseFieldOfView;
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, target, 1f - Mathf.Exp(-zoomSpeed * Time.deltaTime));
        }

        void Look()
        {
            // Yakınlaşınca bakış da aynı oranda yavaşlıyor.
            float sensitivity = mouseSensitivity * playerCamera.fieldOfView / _baseFieldOfView;
            transform.Rotate(0f, Input.GetAxis("Mouse X") * sensitivity, 0f);

            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * sensitivity, -maxPitch, maxPitch);
            headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move(bool hasControl)
        {
            Vector3 horizontal = Vector3.zero;

            if (hasControl)
            {
                var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
                float speed = IsCrouching ? crouchSpeed : Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
                horizontal = transform.TransformDirection(Vector3.ClampMagnitude(input, 1f)) * speed;
            }

            if (_controller.isGrounded)
            {
                // Küçük negatif değer: yerde kalmayı (isGrounded) garanti ediyor.
                _verticalVelocity = -1f;
                if (hasControl && !IsCrouching && Input.GetButtonDown("Jump"))
                    _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            else
            {
                _verticalVelocity -= gravity * Time.deltaTime;
            }

            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        /// <summary>Otomatik test için: gövdeyi ve kafayı bir noktaya çevirir (fare ile bakmak gibi).</summary>
        public void FaceTowards(Vector3 point)
        {
            Vector3 direction = point - headPivot.position;
            float flat = new Vector2(direction.x, direction.z).magnitude;
            transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 0f);
            _pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, flat) * Mathf.Rad2Deg, -maxPitch, maxPitch);
            headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>Kamerayı geçici olarak bir noktaya taşır (çizim tahtası gibi); null ile kafaya döner.</summary>
        public void SetCameraOverride(Transform target)
        {
            _cameraOverride = target;
        }

        void UpdateCameraOverride()
        {
            Transform view = playerCamera.transform;
            float blend = 1f - Mathf.Exp(-CameraBlendSpeed * Time.deltaTime);

            if (_cameraOverride != null)
            {
                view.position = Vector3.Lerp(view.position, _cameraOverride.position, blend);
                view.rotation = Quaternion.Slerp(view.rotation, _cameraOverride.rotation, blend);
            }
            else
            {
                view.localPosition = Vector3.Lerp(view.localPosition, CameraOffset, blend);
                view.localRotation = Quaternion.Slerp(view.localRotation, Quaternion.identity, blend);
            }
        }

        void LateUpdate()
        {
            if (isLocalPlayer)
            {
                UpdateCameraOverride();
                return;
            }

            // İsim tabelası izleyen oyuncunun kamerasına dönük.
            if (_viewCamera == null) _viewCamera = Camera.main;
            if (_viewCamera != null) label.transform.rotation = _viewCamera.transform.rotation;
        }
    }
}
