using UnityEngine;

namespace Dishaster
{
    /// <summary>Tane tane eklenen malzeme (yumurta gibi): kasenin üstüne bırakılınca içine giriyor (bkz. KitchenCounter).</summary>
    [RequireComponent(typeof(KitchenItem))]
    public class KitchenIngredientItem : MonoBehaviour
    {
        public Ingredient ingredient;
        public float amount = 1f;
    }
}
