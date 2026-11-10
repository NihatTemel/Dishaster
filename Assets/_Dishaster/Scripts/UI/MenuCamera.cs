using UnityEngine;

namespace Dishaster
{
    /// <summary>Menü ve lobinin arkasındaki mutfak: kamera yavaşça süzülüyor (sahne kurucu ekliyor).</summary>
    public class MenuCamera : MonoBehaviour
    {
        [Tooltip("Sağa-sola ve yukarı-aşağı süzülme (metre).")]
        public float sway = 0.22f;
        [Tooltip("Bir gidip gelme (saniye).")]
        public float period = 22f;
        [Tooltip("Bakışın sağa-sola salınımı (derece).")]
        public float look = 2.5f;

        Vector3 _origin;
        Quaternion _rotation;

        void Start()
        {
            _origin = transform.position;
            _rotation = transform.rotation;
        }

        void Update()
        {
            float t = Time.time * 2f * Mathf.PI / Mathf.Max(1f, period);
            transform.position = _origin + _rotation * new Vector3(Mathf.Sin(t) * sway, Mathf.Sin(t * 0.7f) * sway * 0.3f, 0f);
            transform.rotation = _rotation * Quaternion.Euler(Mathf.Sin(t * 0.6f) * look * 0.4f, -Mathf.Sin(t) * look, 0f);
        }
    }
}
