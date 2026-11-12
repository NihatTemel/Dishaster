using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Kilerde bir kasa ve içindeki yiyecekler. Her oyunda sunucu kasaların içeriğini karıştırıyor (KitchenCounter):
    /// yiyecekler kasaya göre aynı yerlerinde başka kasaya geçiyor, neyin nerede olduğu oyundan oyuna değişiyor.
    /// </summary>
    public class KitchenCrate : MonoBehaviour
    {
        public KitchenItem[] items;
    }
}
