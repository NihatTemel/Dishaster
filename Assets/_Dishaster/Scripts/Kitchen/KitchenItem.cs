using UnityEngine;

namespace Dishaster
{
    public enum KitchenItemKind
    {
        Generic,
        Knife,
    }

    /// <summary>Elde nasıl duruyor (HandGoals): bıçak sapından yumrukla, kap ve uzun şeyler yandan, küçükler avuçta.</summary>
    public enum KitchenGrip
    {
        Knife,
        Side,
        Palm,
    }

    /// <summary>Elle alınıp bırakılabilen eşya. Origin tabanında; numarasını KitchenCounter veriyor.</summary>
    public class KitchenItem : MonoBehaviour
    {
        // Elin (LowVisionRole.Hand) üstünde eşyanın tabanı; avuçta taşınanlar biraz daha yukarıda (avuç altında).
        const float PalmTop = 0.016f;
        const float PalmCarryLift = 0.035f;
        // Boyu eninin bu katından uzunsa (kavanoz, şişe) yandan tutuluyor; biber gibi biraz uzun olanlar avuçta.
        const float TallRatio = 1.5f;
        // Bıçak elde dik: ağız aşağı, üst kenarı hafif dışa yatık (ağız içe), uç biraz aşağıda.
        const float KnifeInwardLean = 12f;
        const float KnifePitch = 6f;
        // Bir yere uzanınca ağzın ortası bakılan noktanın bu kadar üstünde (kesince iniyor).
        const float KnifeEdgeClearance = 0.025f;
        const float PalmAboveSurface = 0.02f;

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

        [Header("Art yiyecekleri (YIYECEKLER.md: Whole / Slices / Slice_NN)")]
        [Tooltip("Doluysa her kesimde sıradaki dilimin kendi prefab'ı çıkıyor (ölçek yok); slicePrefab yerine.")]
        public KitchenItem[] slicePrefabs;
        [Tooltip("Bütün hal: ilk kesimde kapanıp dilimli hali açılıyor.")]
        public GameObject whole;
        public GameObject slicesRoot;

        BoxCollider _collider;
        Transform _lid;
        int _cuts;

        public int Id { get; set; }

        /// <summary>İçinde malzeme taşıyorsa (süt kutusu, bardak, kase).</summary>
        public KitchenContainer Container { get; private set; }

        /// <summary>Kaseye tane tane giriyorsa (yumurta).</summary>
        public KitchenIngredientItem Ingredient { get; private set; }

        /// <summary>Tabaksa: üstüne dilimler konuyor.</summary>
        public KitchenPlate Plate { get; private set; }

        public bool IsCuttable => segments != null && segments.Length > 0;

        public float NextSliceScale => sliceScales != null && _cuts < sliceScales.Length ? sliceScales[_cuts] : 1f;

        /// <summary>Bu kesimde çıkacak dilim ve ölçeği.</summary>
        public KitchenItem NextSlice(out float scale)
        {
            if (slicePrefabs != null && _cuts < slicePrefabs.Length && slicePrefabs[_cuts] != null)
            {
                scale = 1f;
                return slicePrefabs[_cuts];
            }

            scale = NextSliceScale;
            return slicePrefab;
        }

        public Vector3 Center => transform.position + Vector3.up * (height / 2f);

        /// <summary>Elde nasıl tutuluyor.</summary>
        public KitchenGrip Grip { get; private set; } = KitchenGrip.Palm;

        /// <summary>Tutulurken ele göre yeri ve dönüşü (KitchenCounter alırken koyuyor; bıçak inişi de buradan).</summary>
        public Vector3 HoldPosition { get; private set; }
        public Quaternion HoldRotation { get; private set; } = Quaternion.identity;

        /// <summary>Bir yere uzanırken el bakılan noktanın bu kadar üstünde (bıçakta ağız noktanın hemen üstüne gelsin).</summary>
        public float ReachUp { get; private set; } = PalmAboveSurface;

        /// <summary>Bıçak: ağzın baktığı yön (eşyanın kendi ekseninde; elde aşağı).</summary>
        public Vector3 EdgeLocal { get; private set; } = Vector3.down;

        /// <summary>Eşyanın kendi eksenlerinde kutusu (tetik collider'ı görünen hacme oturuyor, kesildikçe küçülüyor).</summary>
        public Bounds LocalBox => _collider != null
            ? new Bounds(_collider.center, _collider.size)
            : new Bounds(Vector3.up * height / 2f, new Vector3(0.06f, height, 0.06f));

        /// <summary>Dünyada kapladığı kutu (eşyanın kendi kutusunun köşelerinden): üst üste mi, arada yer var mı.</summary>
        public Bounds WorldBox
        {
            get
            {
                Bounds local = LocalBox;
                var world = new Bounds(transform.TransformPoint(local.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                    world.Encapsulate(transform.TransformPoint(local.min + Vector3.Scale(local.size, corner)));
                }

                return world;
            }
        }

        /// <summary>Kapağı hâlâ üstünde mi (baharat kavanozları; art'ta "Lid" parçası).</summary>
        public bool HasLid => _lid != null && _lid.IsChildOf(transform);

        void Awake()
        {
            _collider = GetComponent<BoxCollider>();
            Container = GetComponent<KitchenContainer>();
            Ingredient = GetComponent<KitchenIngredientItem>();
            Plate = GetComponent<KitchenPlate>();
            _lid = FindPart("Lid");
            SetupHold();
        }

        /// <summary>
        /// Kapak düşüyor (KitchenCounter'ın olayı; elde tutulurken bir ara). Kutu kavanozun gövdesine iniyor: el onu
        /// ortasından tutsun.
        /// </summary>
        public void DropLid(Vector3 velocity)
        {
            if (!HasLid) return;

            KitchenLid.Release(_lid, velocity);
            if (_collider != null) FitColliderToRemaining();
        }

        Transform FindPart(string partName)
        {
            foreach (Transform part in GetComponentsInChildren<Transform>(true))
                if (part != transform && part.name == partName)
                    return part;

            return null;
        }

        /// <summary>
        /// Tutuşu eşyanın şeklinden kuruyor: bıçak dik, kaplar ve uzun şeyler (kavanoz) yandan, kalan küçükler avuçta
        /// (el altında, parmaklar etrafına kıvrık).
        /// </summary>
        void SetupHold()
        {
            Bounds box = LocalBox;
            if (kind == KitchenItemKind.Knife)
            {
                SetupKnife(box);
                return;
            }

            float width = Mathf.Max(box.size.x, box.size.z);
            Grip = Container != null || box.size.y > width * TallRatio ? KitchenGrip.Side : KitchenGrip.Palm;
            HoldRotation = Quaternion.identity;
            HoldPosition = new Vector3(0f, Grip == KitchenGrip.Palm ? PalmCarryLift : PalmTop, holdForward);
            ReachUp = PalmAboveSurface;
        }

        /// <summary>
        /// Bıçak tezgahta yatık (ağzı yanda), elde dik: uzun ekseni (+z) etrafında ağız aşağı dönüyor. Ağzın hangi yanda
        /// olduğu kutudan: sap sırta yakın, ağız sapın altına doğru daha çok uzanıyor. Sap elin noktasında; bir yere
        /// uzanınca ağzın ortası bakılan noktanın hemen üstüne geliyor (reachBack ve ReachUp).
        /// </summary>
        void SetupKnife(Bounds box)
        {
            Grip = KitchenGrip.Knife;

            Transform handle = transform.Find("Handle");
            Vector3 grip = handle != null ? handle.localPosition : new Vector3(box.center.x, box.center.y, box.min.z + box.size.z * 0.2f);
            float edgeSign = box.center.x >= grip.x ? 1f : -1f;
            EdgeLocal = new Vector3(edgeSign, 0f, 0f);

            float roll = (edgeSign > 0f ? -90f : 90f) - KnifeInwardLean;
            HoldRotation = Quaternion.AngleAxis(KnifePitch, Vector3.right) * Quaternion.AngleAxis(roll, Vector3.forward);
            HoldPosition = new Vector3(0f, PalmTop, 0f) - HoldRotation * grip;

            // Ağzın ortası (sapın önünden ucuna kadar olan kısmın ortası), elin uzayında.
            float bladeStart = grip.z + box.size.z * 0.18f;
            var edge = new Vector3(box.center.x + edgeSign * box.extents.x, box.center.y, (bladeStart + box.max.z) / 2f);
            Vector3 edgeInHand = HoldPosition + HoldRotation * edge;
            reachBack = edgeInHand.z;
            ReachUp = KnifeEdgeClearance - edgeInHand.y;
        }

        /// <summary>Noktanın eşyanın yüzeyine (dokunma kutusuna) uzaklığı; kutu kapalıysa merkezine.</summary>
        public float SurfaceDistance(Vector3 point)
        {
            if (_collider == null || !_collider.enabled) return Vector3.Distance(Center, point);
            return Vector3.Distance(_collider.ClosestPoint(point), point);
        }

        /// <summary>Tutulurken kapalı: el ışını kendi tuttuğu eşyaya çarpmasın.</summary>
        public void SetTouchable(bool touchable)
        {
            _collider.enabled = touchable;
        }

        /// <summary>Bir dilim eksiltiyor; son dilim de kesilince eşya kalmıyor.</summary>
        public void CutOnce()
        {
            // Art yiyeceği: ilk kesimde bütün hal kapanıp dilimli hal açılıyor, sonra dilimler tek tek gidiyor.
            if (whole != null && whole.activeSelf)
            {
                whole.SetActive(false);
                if (slicesRoot != null) slicesRoot.SetActive(true);
            }

            segments[_cuts].SetActive(false);
            _cuts++;

            if (_cuts >= segments.Length)
            {
                gameObject.SetActive(false);
                return;
            }

            FitColliderToRemaining();
        }

        /// <summary>
        /// Dokunma kutusu kalan parçalara (açık olan mesh'ler) oturuyor, eşyanın kendi eksenlerinde. (Eskiden dünyadaki
        /// sınırlardan hesaplanıyordu: elde dönükken büyüyor, kapak düştüğü an tabanın altına kayabiliyordu; kavanoz
        /// bırakılınca tezgaha gömülüyordu.)
        /// </summary>
        void FitColliderToRemaining()
        {
            bool any = false;
            var local = new Bounds();
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out Renderer _)) continue;

                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                    Vector3 point = transform.InverseTransformPoint(filter.transform.TransformPoint(mesh.min + Vector3.Scale(mesh.size, corner)));
                    if (any)
                    {
                        local.Encapsulate(point);
                    }
                    else
                    {
                        local = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }

            if (!any) return;

            _collider.center = local.center;
            _collider.size = local.size;
        }
    }
}
