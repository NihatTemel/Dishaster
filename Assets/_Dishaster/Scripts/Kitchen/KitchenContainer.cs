using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// İçinde malzeme taşıyan eşya: süt kutusu, un torbası, ölçü bardağı, kase. Elde tutulup sağ tık basılı tutulunca
    /// baktığı kaba dökülüyor (bkz. KitchenCounter.ServerPour). Miktarlar tarif biriminde (ml ya da adet).
    /// İçindeki seviye ve döküntü BlindHidden katmanında: görmeyenin kamerası çizmiyor, miktarı göremiyor.
    /// </summary>
    [RequireComponent(typeof(KitchenItem))]
    public class KitchenContainer : MonoBehaviour
    {
        const float PourVisibleSeconds = 0.25f;
        const float TiltSpeed = 8f;
        const float StreamLength = 0.3f;

        [Tooltip("Dökme göstergesinde görünen ad. Boşsa içindeki malzemenin adı.")]
        public string displayName;

        [Header("Hacim")]
        public float capacity = 1000f;
        public bool canPour;
        public bool canReceive;
        [Tooltip("Tarifin hamuru bu kapta hazırlanıyor.")]
        public bool isMixingBowl;
        [Tooltip("ml/saniye.")]
        public float pourRate = 150f;

        [Header("Başlangıç")]
        public Ingredient startIngredient;
        public float startAmount;

        [Header("Görünüm (görmeyenin kamerası çizmiyor)")]
        [Tooltip("İçerik: silindir. Disk ise sadece yüzeyi çiziliyor (kase gibi eğimli kaplar).")]
        public Transform fill;
        public bool fillIsDisc;
        public float fillBottom;
        public float fillTop = 0.1f;
        public float fillRadiusBottom = 0.05f;
        public float fillRadiusTop = 0.05f;
        public Transform stream;
        [Tooltip("Dökerken öne eğilme açısı.")]
        public float pourTilt = 75f;

        readonly float[] _amounts = new float[Ingredients.Count];
        Renderer _fillRenderer;
        Renderer _streamRenderer;
        float _pourUntil;
        float _tilt;
        int _shownVersion = -1;
        int _version;

        public KitchenItem Item { get; private set; }

        public float Volume
        {
            get
            {
                float volume = 0f;
                for (int i = 0; i < _amounts.Length; i++)
                    volume += _amounts[i] * Ingredients.VolumeOf((Ingredient)i);
                return volume;
            }
        }

        public bool IsEmpty => Volume <= 0.001f;

        public float AmountOf(Ingredient ingredient) => _amounts[(int)ingredient];

        void Awake()
        {
            Item = GetComponent<KitchenItem>();
            if (startAmount > 0f) _amounts[(int)startIngredient] = startAmount;

            BlindHiddenLayer.Apply(fill);
            BlindHiddenLayer.Apply(stream);

            if (fill != null) _fillRenderer = fill.GetComponent<Renderer>();
            if (stream != null)
            {
                _streamRenderer = stream.GetComponent<Renderer>();
                stream.gameObject.SetActive(false);
            }
        }

        /// <summary>Hacme sığdığı kadar ekliyor; taşan kayboluyor (dökülmüş sayılıyor).</summary>
        public void Add(Ingredient ingredient, float amount)
        {
            float volumePerUnit = Ingredients.VolumeOf(ingredient);
            float room = Mathf.Max(0f, capacity - Volume) / volumePerUnit;
            _amounts[(int)ingredient] += Mathf.Min(amount, room);
            _version++;
        }

        public void AddMix(float[] amounts)
        {
            for (int i = 0; i < amounts.Length; i++)
                if (amounts[i] > 0f)
                    Add((Ingredient)i, amounts[i]);
        }

        /// <summary>Verilen hacmi, içindekilerin oranında çıkarıyor.</summary>
        public float[] Take(float volume)
        {
            var taken = new float[Ingredients.Count];
            float total = Volume;
            if (total <= 0f) return taken;

            float share = Mathf.Clamp01(volume / total);
            for (int i = 0; i < _amounts.Length; i++)
            {
                taken[i] = _amounts[i] * share;
                _amounts[i] -= taken[i];
            }

            _version++;
            return taken;
        }

        /// <summary>Dökme olayı geldikçe çağrılıyor: kısa süre eğik duruyor ve akıntı görünüyor.</summary>
        public void MarkPouring()
        {
            _pourUntil = Time.time + PourVisibleSeconds;
        }

        void Update()
        {
            bool pouring = Time.time < _pourUntil;
            UpdateTilt(pouring);
            UpdateStream(pouring);

            if (_shownVersion != _version) UpdateFill();
        }

        void UpdateTilt(bool pouring)
        {
            // Sadece eldeyken: tezgahta duran kap eğilmiyor.
            Transform hand = LowVisionRole.Hand;
            if (hand == null || transform.parent != hand) return;

            _tilt = Mathf.Lerp(_tilt, pouring ? pourTilt : 0f, 1f - Mathf.Exp(-TiltSpeed * Time.deltaTime));
            transform.localRotation = Quaternion.Euler(_tilt, 0f, 0f);
        }

        void UpdateStream(bool pouring)
        {
            if (stream == null) return;

            bool visible = pouring && !IsEmpty;
            if (stream.gameObject.activeSelf != visible) stream.gameObject.SetActive(visible);
            if (!visible) return;

            // Ağız: kabın üstü, eğilince öne gidiyor. Akıntı oradan dümdüz aşağı.
            Vector3 spout = transform.TransformPoint(Vector3.up * Item.height);
            stream.SetPositionAndRotation(spout + Vector3.down * (StreamLength / 2f), Quaternion.identity);
            stream.localScale = Vector3.one;
            Vector3 lossy = stream.lossyScale;
            stream.localScale = new Vector3(0.012f / lossy.x, StreamLength / 2f / lossy.y, 0.012f / lossy.z);
            _streamRenderer.material.color = MainColor();
        }

        void UpdateFill()
        {
            _shownVersion = _version;
            if (fill == null) return;

            float level = Mathf.Clamp01(Volume / capacity);
            bool visible = level > 0.001f;
            _fillRenderer.enabled = visible;
            if (!visible) return;

            float height = (fillTop - fillBottom) * level;
            float radius = Mathf.Lerp(fillRadiusBottom, fillRadiusTop, level);

            fill.localPosition = new Vector3(0f, fillIsDisc ? fillBottom + height : fillBottom + height / 2f, 0f);
            fill.localScale = fillIsDisc
                ? new Vector3(radius * 2f, 0.002f, radius * 2f)
                : new Vector3(radius * 2f, height / 2f, radius * 2f);
            _fillRenderer.material.color = MixedColor();
        }

        Color MixedColor()
        {
            Color color = Color.black;
            float total = 0f;

            for (int i = 0; i < _amounts.Length; i++)
            {
                float volume = _amounts[i] * Ingredients.VolumeOf((Ingredient)i);
                color += Ingredients.ColorOf((Ingredient)i) * volume;
                total += volume;
            }

            return total > 0f ? color / total : Color.white;
        }

        Color MainColor() => Ingredients.ColorOf(Ingredients.MainOf(_amounts));
    }
}
