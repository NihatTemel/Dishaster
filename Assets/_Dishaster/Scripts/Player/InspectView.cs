using Mirror;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// İncele (prefab): gören oyuncu bir kaba (ölçü bardağı, kase) bakıp F'ye basınca kamera kabın önüne geliyor,
    /// çizgiler ve içindeki seviye net okunuyor. Fareyle kabın etrafında dönülüyor, tekerlekle yaklaşılıyor;
    /// F ya da ESC ile çıkılıyor. Kap elde taşınıyorsa görüntü onu izliyor. Görmeyen inceleyemiyor (test anahtarı hariç).
    /// Sadece yerel görüntü: ağa bir şey gitmiyor.
    /// </summary>
    public class InspectView : MonoBehaviour
    {
        const KeyCode InspectKey = KeyCode.F;
        const float AimRange = 4f;

        public GameObject prompt;
        public GameObject help;

        [Header("Görüntü (metre, derece)")]
        public float startDistance = 0.26f;
        public float minDistance = 0.14f;
        public float maxDistance = 0.6f;
        public float startPitch = 12f;
        public float minPitch = -20f;
        public float maxPitch = 75f;
        public float orbitSpeed = 3f;
        public float scrollSpeed = 0.15f;

        DishasterPlayer _player;
        bool _allowed;
        KitchenItem _target;
        Transform _view;
        float _yaw;
        float _pitch;
        float _distance;

        void Awake()
        {
            _view = new GameObject("InspectViewPoint").transform;
            _view.SetParent(transform, false);
            prompt.SetActive(false);
            help.SetActive(false);
        }

        void OnDestroy()
        {
            if (_target != null) End();
        }

        void Update()
        {
            if (_player == null)
            {
                if (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.TryGetComponent(out _player)) return;
                _allowed = _player.role != MonkeyRoles.LowVision || DishasterTest.LowVisionSeesEverything;
            }

            if (!_allowed) return;

            if (_target != null)
            {
                UpdateInspecting();
                return;
            }

            // İmleç serbestse ESC menüsü ya da defter çizimi açık.
            KitchenItem aimed = Cursor.lockState == CursorLockMode.Locked ? FindAimed() : null;
            if (prompt.activeSelf != (aimed != null)) prompt.SetActive(aimed != null);

            if (aimed != null && Input.GetKeyDown(InspectKey))
                Begin(aimed);
        }

        /// <summary>Bakışın en yakından geçtiği kap. Küçük olduğu için uzaklaştıkça tolerans artıyor.</summary>
        KitchenItem FindAimed()
        {
            KitchenCounter counter = KitchenCounter.Instance;
            if (counter == null) return null;

            Transform view = _player.playerCamera.transform;
            KitchenItem best = null;
            float bestOffset = float.PositiveInfinity;

            foreach (KitchenItem item in counter.Items)
            {
                if (item == null || item.Container == null || !item.Container.canReceive || !item.gameObject.activeInHierarchy) continue;

                Vector3 toItem = item.Center - view.position;
                float along = Vector3.Dot(toItem, view.forward);
                if (along <= 0f || along > AimRange) continue;

                float offset = (toItem - view.forward * along).magnitude;
                float tolerance = Mathf.Max(item.grabRadius, item.height / 2f) + 0.03f * along;
                if (offset > tolerance || offset >= bestOffset) continue;

                best = item;
                bestOffset = offset;
            }

            return best;
        }

        void Begin(KitchenItem item)
        {
            _target = item;

            // Oyuncunun durduğu taraftan başlıyor.
            Vector3 fromItem = _player.playerCamera.transform.position - item.Center;
            _yaw = Mathf.Atan2(fromItem.x, fromItem.z) * Mathf.Rad2Deg;
            _pitch = startPitch;
            _distance = startDistance;
            PlaceView();

            _player.ControlBlocked = true;
            _player.SetCameraOverride(_view);
            prompt.SetActive(false);
            help.SetActive(true);
        }

        void End()
        {
            _target = null;
            help.SetActive(false);

            if (_player == null) return;
            _player.ControlBlocked = false;
            _player.SetCameraOverride(null);
        }

        void UpdateInspecting()
        {
            // Kap kaseye girdiyse / gizlendiyse, F'ye basıldıysa ya da ESC menüsü açıldıysa çıkılıyor.
            if (!_target.gameObject.activeInHierarchy || Input.GetKeyDown(InspectKey) || Cursor.lockState != CursorLockMode.Locked)
            {
                End();
                return;
            }

            _yaw += Input.GetAxis("Mouse X") * orbitSpeed;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * orbitSpeed, minPitch, maxPitch);
            _distance = Mathf.Clamp(_distance - Input.mouseScrollDelta.y * scrollSpeed * _distance, minDistance, maxDistance);
        }

        // LateUpdate: kap eldeyse o karedeki yerine göre.
        void LateUpdate()
        {
            if (_target != null) PlaceView();
        }

        void PlaceView()
        {
            Vector3 center = _target.Center;
            Vector3 offset = Quaternion.Euler(-_pitch, _yaw, 0f) * Vector3.forward * _distance;
            _view.position = center + offset;
            _view.rotation = Quaternion.LookRotation(center - _view.position, Vector3.up);
        }
    }
}
