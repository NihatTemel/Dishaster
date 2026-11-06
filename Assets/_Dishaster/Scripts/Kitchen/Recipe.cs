using System.Text;
using UnityEngine;

namespace Dishaster
{
    public enum RecipeKind
    {
        StrawberryMilk,
        StrawberryCake,
    }

    /// <summary>Tarif kartındaki bir adımın durumu: yapılınca yeşil tik, fazlaysa kırmızı.</summary>
    public enum RecipeStepState
    {
        Pending,
        Done,
        Over,
    }

    /// <summary>
    /// Oynanan tarif. Miktarları sunucu seçiyor, herkese gidiyor (bkz. KitchenCounter). Kaseye konan her malzeme
    /// herkeste aynı olaylarla geldiği için kontrol her makinede ayrı yapılıyor, sonuç aynı çıkıyor.
    /// Bir malzeme hedefin %20'sinden fazla konursa (tarifte olmayan bir şey konursa da) tarif bozuluyor.
    /// </summary>
    public class Recipe
    {
        public const float Tolerance = 0.2f;

        /// <summary>Şimdilik oynanan tarif. Çilekli kekin karıştırma/kalıp/fırın/servis adımları henüz yok.</summary>
        public const RecipeKind Current = RecipeKind.StrawberryMilk;

        readonly int[] _targets = new int[Ingredients.Count];
        readonly int[] _slicesCut = new int[Ingredients.Count];
        readonly RecipeStepState[] _steps = new RecipeStepState[4];

        public bool HasTargets { get; private set; }
        public bool Failed { get; private set; }
        public string FailReason { get; private set; }
        public bool Completed { get; private set; }

        /// <summary>Her değişiklikte artıyor: arayüz buna bakıp yeniliyor.</summary>
        public int Version { get; private set; }

        public int TargetOf(Ingredient ingredient) => _targets[(int)ingredient];

        public int[] Targets => (int[])_targets.Clone();

        /// <summary>Sunucuda: ölçü bardağı 1 L, sıvılar 100 ml adımlı.</summary>
        public static int[] Roll()
        {
            var targets = new int[Ingredients.Count];
            switch (Current)
            {
                case RecipeKind.StrawberryMilk:
                    targets[(int)Ingredient.Milk] = Random.Range(2, 6) * 100;
                    // Bir çilek 4 dilim çıkarıyor.
                    targets[(int)Ingredient.Strawberry] = Random.Range(2, 5);
                    break;

                case RecipeKind.StrawberryCake:
                    targets[(int)Ingredient.Milk] = Random.Range(2, 7) * 100;
                    targets[(int)Ingredient.Flour] = Random.Range(3, 8) * 100;
                    targets[(int)Ingredient.Sugar] = Random.Range(1, 4) * 100;
                    targets[(int)Ingredient.BakingPowder] = Random.Range(1, 3) * 100;
                    targets[(int)Ingredient.Egg] = Random.Range(1, 4);
                    targets[(int)Ingredient.Strawberry] = Random.Range(3, 6);
                    break;
            }

            return targets;
        }

        public void SetTargets(int[] targets)
        {
            for (int i = 0; i < _targets.Length && i < targets.Length; i++)
                _targets[i] = targets[i];

            HasTargets = true;
            Version++;
        }

        void Fail(string reason)
        {
            if (Failed || Completed) return;

            Failed = true;
            FailReason = reason;
            Version++;
        }

        /// <summary>Kaseye bir kaptan döküldüğünde, eklenenler kaseye girmeden önce.</summary>
        public void CheckPour(KitchenContainer source, float[] poured)
        {
            if (Failed || Completed || !HasTargets) return;

            // Süt ölçülmeden kaseye gitmesin: önce ölçü bardağına (hem alan hem döken kap).
            if (Current == RecipeKind.StrawberryMilk && !source.canReceive && poured[(int)Ingredient.Milk] > 0.5f)
                Fail("Süt doğrudan kaseye döküldü: önce ölçü bardağına dökülmeli");
        }

        /// <summary>Kesilen her dilimde (kaseye tane tane girenler).</summary>
        public void NoteSlice(Ingredient ingredient)
        {
            _slicesCut[(int)ingredient]++;
        }

        public RecipeStepState StepState(int index) => _steps[index];

        /// <summary>
        /// Her mutfak olayından sonra: adımların o anki durumu (herkeste aynı olaylarla aynı çıkıyor).
        /// Ölçü bardağı adımı süt kaseye geçince de tamam sayılıyor.
        /// </summary>
        public void UpdateSteps(KitchenContainer cup, KitchenContainer bowl)
        {
            if (!HasTargets || Current != RecipeKind.StrawberryMilk || bowl == null) return;

            int slices = TargetOf(Ingredient.Strawberry);
            float bowlMilk = bowl.AmountOf(Ingredient.Milk);
            float cupMilk = cup != null ? cup.AmountOf(Ingredient.Milk) : 0f;
            int bowlSlices = Mathf.RoundToInt(bowl.AmountOf(Ingredient.Strawberry));

            RecipeStepState pourBowl = AmountState(Ingredient.Milk, bowlMilk);
            RecipeStepState measure = pourBowl == RecipeStepState.Done ? RecipeStepState.Done : AmountState(Ingredient.Milk, cupMilk);

            bool changed = false;
            changed |= SetStep(0, _slicesCut[(int)Ingredient.Strawberry] >= slices ? RecipeStepState.Done : RecipeStepState.Pending);
            changed |= SetStep(1, measure);
            changed |= SetStep(2, pourBowl);
            changed |= SetStep(3, bowlSlices == slices ? RecipeStepState.Done : bowlSlices > slices ? RecipeStepState.Over : RecipeStepState.Pending);

            if (changed) Version++;
        }

        bool SetStep(int index, RecipeStepState state)
        {
            if (_steps[index] == state) return false;
            _steps[index] = state;
            return true;
        }

        /// <summary>Hata payı içinde tamam, üstünde fazla, altında daha bekliyor.</summary>
        RecipeStepState AmountState(Ingredient ingredient, float amount)
        {
            float target = TargetOf(ingredient);
            if (amount > target * (1f + Tolerance) + 0.001f) return RecipeStepState.Over;
            return amount >= target * (1f - Tolerance) - 0.001f ? RecipeStepState.Done : RecipeStepState.Pending;
        }

        /// <summary>Kaseye bir şey girdikçe çağrılıyor.</summary>
        public void Check(KitchenContainer bowl)
        {
            if (Failed || Completed || !HasTargets) return;

            for (int i = 0; i < _targets.Length; i++)
            {
                var ingredient = (Ingredient)i;
                float amount = bowl.AmountOf(ingredient);
                if (amount <= 0.5f || amount <= _targets[i] * (1f + Tolerance) + 0.001f) continue;

                Fail(_targets[i] > 0
                    ? $"{Ingredients.NameOf(ingredient)} fazla: {Format(ingredient, amount)} kondu, tarif {Format(ingredient, _targets[i])}"
                    : $"{Ingredients.NameOf(ingredient)} tarifte yok");
                return;
            }

            if (Current == RecipeKind.StrawberryMilk && IsEnough(bowl, Ingredient.Milk) && IsEnough(bowl, Ingredient.Strawberry))
            {
                Completed = true;
                Version++;
            }
        }

        /// <summary>Hedefin %20 eksiğine kadar yetiyor (fazlası Check'te bozuyor).</summary>
        bool IsEnough(KitchenContainer bowl, Ingredient ingredient)
        {
            return bowl.AmountOf(ingredient) >= TargetOf(ingredient) * (1f - Tolerance) - 0.001f;
        }

        public static string Format(Ingredient ingredient, float amount)
        {
            return $"{Mathf.RoundToInt(amount)} {Ingredients.UnitOf(ingredient)}";
        }

        // Yazı tipinde ✓ yok; √ tik olarak kullanılıyor.
        void AppendStep(StringBuilder text, int index, string line)
        {
            switch (_steps[index])
            {
                case RecipeStepState.Done:
                    text.AppendLine($"<color=#6FD86F><b>√</b>  {index + 1}. {line}</color>");
                    break;
                case RecipeStepState.Over:
                    text.AppendLine($"<color=#FF7060><b>!</b>  {index + 1}. {line}  (fazla)</color>");
                    break;
                default:
                    text.AppendLine($"<color=#9A968E>○</color>  {index + 1}. {line}");
                    break;
            }
        }

        /// <summary>Bilmiyorum'un gördüğü tarif kartı.</summary>
        public string Describe()
        {
            var text = new StringBuilder();

            switch (Current)
            {
                case RecipeKind.StrawberryMilk:
                    text.AppendLine("<b>ÇİLEKLİ SÜT</b>");
                    text.AppendLine("<size=80%>Ölçü bardağı 1 L. Hata payı %20.</size>");
                    text.AppendLine();
                    AppendStep(text, 0, $"Çileği doğra: {Format(Ingredient.Strawberry, TargetOf(Ingredient.Strawberry))}");
                    AppendStep(text, 1, $"Sütü ölçü bardağına dök: {Format(Ingredient.Milk, TargetOf(Ingredient.Milk))}");
                    AppendStep(text, 2, "Ölçü bardağındaki sütü kaseye dök");
                    AppendStep(text, 3, "Çilek dilimlerini kaseye ekle");
                    break;

                case RecipeKind.StrawberryCake:
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
                    break;
            }

            return text.ToString();
        }
    }
}
