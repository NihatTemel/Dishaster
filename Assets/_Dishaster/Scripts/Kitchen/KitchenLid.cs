using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Kavanozdan düşen kapak: elden ayrılıp fizikle düşüyor, nereye düşerse orada kalıyor. El ışını ona takılmıyor
    /// (Ignore Raycast), tutulamıyor. Bir yere çarpınca tıkırdıyor (KitchenFx).
    /// </summary>
    public class KitchenLid : MonoBehaviour
    {
        const int IgnoreRaycastLayer = 2;
        const float Mass = 0.05f;
        const float Spin = 10f;
        const float MinHitSpeed = 0.4f;
        const float HitSoundGap = 0.12f;

        /// <summary>Kapak bir yere çarptı: yer ve çarpma hızı.</summary>
        public static event System.Action<Vector3, float> Hit;

        float _nextSound;

        /// <summary>Kapağı kavanozdan ayırıp verilen hızla fırlatıyor (herkeste aynı hız; düştüğü yer biraz farklı olabilir).</summary>
        public static void Release(Transform lid, Vector3 velocity)
        {
            lid.SetParent(null, true);
            foreach (Transform part in lid.GetComponentsInChildren<Transform>(true))
                part.gameObject.layer = IgnoreRaycastLayer;

            Bounds bounds = LocalBounds(lid);
            var box = lid.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.01f);

            var body = lid.gameObject.AddComponent<Rigidbody>();
            body.mass = Mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.velocity = velocity;
            body.angularVelocity = new Vector3(velocity.z, 0.3f, -velocity.x).normalized * Spin;

            lid.gameObject.AddComponent<KitchenLid>();
        }

        // Kapağın mesh'lerinin kendi uzayındaki kutusu.
        static Bounds LocalBounds(Transform lid)
        {
            bool any = false;
            var bounds = new Bounds(Vector3.zero, Vector3.one * 0.05f);
            foreach (MeshFilter filter in lid.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;

                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i % 2 == 0 ? mesh.min.x : mesh.max.x, (i / 2) % 2 == 0 ? mesh.min.y : mesh.max.y, i / 4 == 0 ? mesh.min.z : mesh.max.z);
                    Vector3 point = lid.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (any)
                    {
                        bounds.Encapsulate(point);
                    }
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }

            return bounds;
        }

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed < MinHitSpeed || Time.time < _nextSound || collision.contactCount == 0) return;

            _nextSound = Time.time + HitSoundGap;
            Hit?.Invoke(collision.GetContact(0).point, speed);
        }
    }
}
