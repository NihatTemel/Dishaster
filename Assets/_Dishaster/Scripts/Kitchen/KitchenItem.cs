using UnityEngine;

namespace Dishaster
{
    public enum KitchenItemKind
    {
        Generic,
        Knife,
    }

    /// <summary>Elle alınıp bırakılabilen eşya. Origin tabanında; numarasını KitchenCounter veriyor.</summary>
    public class KitchenItem : MonoBehaviour
    {
        public KitchenItemKind kind;

        [Tooltip("El bu kadar yakındayken (metre) eşya alınabiliyor.")]
        public float grabRadius = 0.08f;

        [Tooltip("Eşyanın yüksekliği.")]
        public float height = 0.1f;

        [Tooltip("Avuçta tutulurken elin ne kadar önünde duruyor.")]
        public float holdForward = 0.03f;

        [Tooltip("Tutulurken el bakılan noktanın bu kadar gerisinde kalıyor: bıçağın ağzı hedefe denk gelsin.")]
        public float reachBack;

        [Tooltip("Dilimlenenlerde dilim başına bir parça; kesim baştan sona doğru ilerliyor.")]
        public GameObject[] segments;

        [Tooltip("Kesilince çıkan dilim.")]
        public KitchenItem slicePrefab;

        [Tooltip("Parça başına dilimin ölçeği: uçlardan küçük dilim çıkıyor.")]
        public float[] sliceScales;

        BoxCollider _collider;
        int _cuts;

        public int Id { get; set; }

        /// <summary>İçinde malzeme taşıyorsa (süt kutusu, bardak, kase).</summary>
        public KitchenContainer Container { get; private set; }

        /// <summary>Kaseye tane tane giriyorsa (yumurta).</summary>
        public KitchenIngredientItem Ingredient { get; private set; }

        public bool IsCuttable => segments != null && segments.Length > 0;

        public float NextSliceScale => sliceScales[_cuts];

        public Vector3 Center => transform.position + Vector3.up * (height / 2f);

        void Awake()
        {
            _collider = GetComponent<BoxCollider>();
            Container = GetComponent<KitchenContainer>();
            Ingredient = GetComponent<KitchenIngredientItem>();
        }

        /// <summary>Tutulurken kapalı: el ışını kendi tuttuğu eşyaya çarpmasın.</summary>
        public void SetTouchable(bool touchable)
        {
            _collider.enabled = touchable;
        }

        /// <summary>Bir dilim eksiltiyor; son dilim de kesilince eşya kalmıyor.</summary>
        public void CutOnce()
        {
            segments[_cuts].SetActive(false);
            _cuts++;

            if (_cuts >= segments.Length)
            {
                gameObject.SetActive(false);
                return;
            }

            FitColliderToRemaining();
        }

        void FitColliderToRemaining()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer part in renderers)
                bounds.Encapsulate(part.bounds);

            _collider.center = transform.InverseTransformPoint(bounds.center);
            _collider.size = bounds.size;
        }
    }
}
