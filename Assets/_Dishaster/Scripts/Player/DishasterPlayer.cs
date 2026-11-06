using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dishaster
{
    /// <summary>Game sahnesindeki FPS oyuncu. Konum ve bakışı kendi makinesi yönetiyor, NetworkTransform'lar dağıtıyor.</summary>
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

        [Header("Referanslar")]
        public Renderer bodyRenderer;
        public Renderer visorRenderer;
        public Transform headPivot;
        public Camera playerCamera;
        public TMP_Text label;

        const float CameraBlendSpeed = 10f;

        CharacterController _controller;
        Camera _viewCamera;
        Transform _cameraOverride;
        float _pitch;
        float _verticalVelocity;

        [Server]
        public void ServerInit(int monkeyRole, string displayName)
        {
            role = monkeyRole;
            playerName = displayName;
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
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

            // Kendi gövdemiz görünmesin ama gölgesi kalsın.
            bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            visorRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            label.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!isLocalPlayer) return;

            // İmleç serbestse (ESC menüsü) oyuncu kontrolü durur, yerçekimi devam eder.
            bool hasControl = Cursor.lockState == CursorLockMode.Locked;

            if (hasControl) Look();
            Move(hasControl);
        }

        void Look()
        {
            transform.Rotate(0f, Input.GetAxis("Mouse X") * mouseSensitivity, 0f);

            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -maxPitch, maxPitch);
            headPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move(bool hasControl)
        {
            Vector3 horizontal = Vector3.zero;

            if (hasControl)
            {
                var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
                float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
                horizontal = transform.TransformDirection(Vector3.ClampMagnitude(input, 1f)) * speed;
            }

            if (_controller.isGrounded)
            {
                // Küçük negatif değer: yerde kalmayı (isGrounded) garanti ediyor.
                _verticalVelocity = -1f;
                if (hasControl && Input.GetButtonDown("Jump"))
                    _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            else
            {
                _verticalVelocity -= gravity * Time.deltaTime;
            }

            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
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
                view.localPosition = Vector3.Lerp(view.localPosition, Vector3.zero, blend);
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
