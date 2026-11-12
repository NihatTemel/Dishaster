using System.Collections.Generic;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Tabak: tane tane eklenenler (dilimler) üstünde görünür duruyor ve sayılıyor; elle geri alınabiliyor. Tabak
    /// taşınınca üstündekiler de onunla gidiyor. Tarif tabaktakine bakıyor (bkz. Recipe). Yerleri KitchenCounter'ın
    /// olaylarıyla herkeste aynı.
    /// </summary>
    [RequireComponent(typeof(KitchenItem))]
    public class KitchenPlate : MonoBehaviour
    {
        // Dilimler tabağın iç yüzeyinde, ortanın çevresinde bir halkada; halka dolunca ikinci kat.
        const int RingSlots = 6;
        const float LayerHeight = 0.014f;

        [Tooltip("Tabağın iç yüzeyinin tabandan yüksekliği (metre).")]
        public float surfaceHeight = 0.012f;
        [Tooltip("Dilimlerin dizildiği halkanın yarıçapı (metre).")]
        public float ringRadius = 0.095f;

        readonly List<KitchenItem> _pieces = new List<KitchenItem>();

        public KitchenItem Item { get; private set; }

        /// <summary>Üstündekiler, konma sırasıyla.</summary>
        public IReadOnlyList<KitchenItem> Pieces => _pieces;

        void Awake()
        {
            Item = GetComponent<KitchenItem>();
        }

        public bool Holds(KitchenItem piece) => _pieces.Contains(piece);

        public int CountOf(Ingredient ingredient)
        {
            int count = 0;
            foreach (KitchenItem piece in _pieces)
                if (piece.Ingredient != null && piece.Ingredient.ingredient == ingredient)
                    count++;

            return count;
        }

        /// <summary>Tabağa konuyor: ilk boş yere, tabağın çocuğu olarak (tabakla birlikte gidiyor).</summary>
        public void Add(KitchenItem piece)
        {
            if (_pieces.Contains(piece)) return;

            _pieces.Add(piece);
            piece.transform.SetParent(transform, false);
            piece.transform.localScale = Vector3.one;
            int slot = _pieces.Count - 1;
            float angle = (slot % RingSlots) * (360f / RingSlots) + 90f;
            Vector3 ring = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * ringRadius;
            piece.transform.localPosition = ring + Vector3.up * (surfaceHeight + slot / RingSlots * LayerHeight);
            piece.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            piece.SetTouchable(true);
        }

        /// <summary>Elle alındı: artık tabakta sayılmıyor.</summary>
        public void Remove(KitchenItem piece)
        {
            _pieces.Remove(piece);
        }

        /// <summary>Tabak elde taşınırken üstündekilere el değmesin (el ışını onlara çarpmasın).</summary>
        public void SetPiecesTouchable(bool touchable)
        {
            foreach (KitchenItem piece in _pieces)
                if (piece != null) piece.SetTouchable(touchable);
        }
    }
}
