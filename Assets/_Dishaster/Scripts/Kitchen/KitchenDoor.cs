using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Menteşeli kapak (buzdolabı). Görmeyen eliyle kapağa dokunup sol tıklayınca açılıp kapanıyor; kararı
    /// KitchenCounter veriyor, herkeste aynı olay oynuyor.
    /// </summary>
    public class KitchenDoor : MonoBehaviour
    {
        const float TouchDistance = 0.06f;

        [Tooltip("Dönen parça; kapak bunun altında.")]
        public Transform hinge;
        public Collider panel;
        [Tooltip("Açıkken menteşenin y açısı.")]
        public float openAngle = -100f;
        [Tooltip("Derece/saniye.")]
        public float swingSpeed = 320f;

        bool _open;
        float _angle;

        public int Id { get; set; }

        public bool IsOpen => _open;

        public void Toggle()
        {
            _open = !_open;
        }

        public bool IsTouched(Vector3 point)
        {
            return (panel.ClosestPoint(point) - point).sqrMagnitude <= TouchDistance * TouchDistance;
        }

        void Update()
        {
            float target = _open ? openAngle : 0f;
            if (Mathf.Approximately(_angle, target)) return;

            _angle = Mathf.MoveTowards(_angle, target, swingSpeed * Time.deltaTime);
            hinge.localRotation = Quaternion.Euler(0f, _angle, 0f);
        }
    }
}
