using System.Text;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Çilekli kek tarifi. Miktarları sunucu seçiyor, herkese gidiyor (bkz. KitchenCounter). Kaseye konan her
    /// malzeme herkeste aynı olaylarla geldiği için kontrol her makinede ayrı yapılıyor, sonuç aynı çıkıyor.
    /// Şimdilik sadece fazlası kontrol ediliyor: hedefin %20'sinden fazla konursa tarif bozuluyor. Eksik olan
    /// karıştırma adımında kontrol edilecek.
    /// </summary>
    public class CakeRecipe
    {
        public const float Tolerance = 0.2f;

        readonly int[] _targets = new int[Ingredients.Count];

        public bool HasTargets { get; private set; }
        public bool Failed { get; private set; }
        public string FailReason { get; private set; }

        /// <summary>Her değişiklikte artıyor: arayüz buna bakıp yeniliyor.</summary>
        public int Version { get; private set; }

        public int TargetOf(Ingredient ingredient) => _targets[(int)ingredient];

        public int[] Targets => (int[])_targets.Clone();

        /// <summary>Sunucuda: ölçü bardağı 1 L, miktarlar 100 ml adımlı.</summary>
        public static int[] Roll()
        {
            var targets = new int[Ingredients.Count];
            targets[(int)Ingredient.Milk] = Random.Range(2, 7) * 100;
            targets[(int)Ingredient.Flour] = Random.Range(3, 8) * 100;
            targets[(int)Ingredient.Sugar] = Random.Range(1, 4) * 100;
            targets[(int)Ingredient.BakingPowder] = Random.Range(1, 3) * 100;
            targets[(int)Ingredient.Egg] = Random.Range(1, 4);
            targets[(int)Ingredient.Strawberry] = Random.Range(3, 6);
            return targets;
        }

        public void SetTargets(int[] targets)
        {
            for (int i = 0; i < _targets.Length && i < targets.Length; i++)
                _targets[i] = targets[i];

            HasTargets = true;
            Version++;
        }

        /// <summary>Kaseye bir şey girdikçe çağrılıyor.</summary>
        public void Check(KitchenContainer bowl)
        {
            if (Failed || !HasTargets) return;

            for (int i = 0; i < _targets.Length; i++)
            {
                var ingredient = (Ingredient)i;
                float amount = bowl.AmountOf(ingredient);
                if (amount <= 0.5f || amount <= _targets[i] * (1f + Tolerance) + 0.001f) continue;

                Failed = true;
                FailReason = _targets[i] > 0
                    ? $"{Ingredients.NameOf(ingredient)} fazla: {Format(ingredient, amount)} kondu, tarif {Format(ingredient, _targets[i])}"
                    : $"{Ingredients.NameOf(ingredient)} tarifte yok";
                Version++;
                return;
            }
        }

        public static string Format(Ingredient ingredient, float amount)
        {
            return $"{Mathf.RoundToInt(amount)} {Ingredients.UnitOf(ingredient)}";
        }

        /// <summary>Bilmiyorum'un gördüğü tarif kartı.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine("<b>ÇİLEKLİ KEK</b>");
            text.AppendLine("<size=80%>Ölçü bardağı 1 L. Hata payı %20.</size>");
            text.AppendLine();
            text.AppendLine($"1. Çilekleri doğra: {Format(Ingredient.Strawberry, TargetOf(Ingredient.Strawberry))}");
            text.AppendLine("2. Kaseye koy:");
            foreach (Ingredient ingredient in new[] { Ingredient.Flour, Ingredient.Milk, Ingredient.Sugar, Ingredient.BakingPowder, Ingredient.Egg })
                text.AppendLine($"    {Ingredients.NameOf(ingredient)}: {Format(ingredient, TargetOf(ingredient))}");
            text.AppendLine("<color=#888>3. Karıştır</color>");
            text.AppendLine("<color=#888>4. Çilekleri ekle, kalıba dök</color>");
            text.AppendLine("<color=#888>5. Fırında pişir</color>");
            text.Append("<color=#888>6. Servis et</color>");
            return text.ToString();
        }
    }
}
