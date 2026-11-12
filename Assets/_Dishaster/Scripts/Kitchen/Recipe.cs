using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Dishaster
{
    public enum RecipeKind
    {
        StrawberryMilk,
        StrawberryCake,
        TomatoPlate,
    }

    /// <summary>Tarif kartındaki bir adımın durumu: yapılınca yeşil tik, fazlaysa kırmızı.</summary>
    public enum RecipeStepState
    {
        Pending,
        Done,
        Over,
    }

    /// <summary>
    /// Oynanan tarif. Miktarları sunucu seçiyor, herkese gidiyor (bkz. KitchenCounter). Mutfak olayları herkeste
    /// aynı geldiği için kontrol her makinede ayrı yapılıyor, sonuç aynı çıkıyor.
    /// İlk tarif domatesli tabak: domates tahtada doğranıyor, bir tabak adaya konuyor, üstüne tam 3 dilim domates.
    /// Fazlası ya da başka bir şey tabakta kırmızı görünüyor, elle geri alınabiliyor (tarif bozulmuyor).
    /// Çilekli sütte bir malzeme hedefin %20'sinden fazla kaseye konursa (tarifte olmayan bir şey konursa da) tarif bozuluyor.
    /// </summary>
    public class Recipe
    {
        public const float Tolerance = 0.2f;

        /// <summary>Oynanan tarif: ilk tarif domatesli tabak. Çilekli kekin karıştırma/kalıp/fırın/servis adımları henüz yok.</summary>
        public const RecipeKind Current = RecipeKind.TomatoPlate;

        const int TomatoSlices = 3;

        readonly int[] _targets = new int[Ingredients.Count];
        readonly int[] _slicesCut = new int[Ingredients.Count];
        readonly RecipeStepState[] _steps = new RecipeStepState[4];

        /// <summary>Bu tarif hangisi (oyunda Current; testler başkasını da kurabiliyor).</summary>
        public RecipeKind Kind { get; }

        public Recipe() : this(Current) { }

        public Recipe(RecipeKind kind)
        {
            Kind = kind;
        }

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
                case RecipeKind.TomatoPlate:
                    targets[(int)Ingredient.Tomato] = TomatoSlices;
                    break;

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

        /// <summary>Tarif kaseyle mi yapılıyor (dökülen ve kaseye atılan miktarlar sayılıyor).</summary>
        bool UsesBowl => Kind == RecipeKind.StrawberryMilk || Kind == RecipeKind.StrawberryCake;

        /// <summary>Kaseye bir kaptan döküldüğünde, eklenenler kaseye girmeden önce.</summary>
        public void CheckPour(KitchenContainer source, float[] poured)
        {
            if (Failed || Completed || !HasTargets || !UsesBowl) return;

            // Süt ölçülmeden kaseye gitmesin: önce ölçü bardağına (hem alan hem döken kap).
            if (Kind == RecipeKind.StrawberryMilk && !source.canReceive && poured[(int)Ingredient.Milk] > 0.5f)
                Fail("Süt doğrudan kaseye döküldü: önce ölçü bardağına dökülmeli");
        }

        /// <summary>Kesilen her dilimde (kaseye tane tane girenler).</summary>
        public void NoteSlice(Ingredient ingredient)
        {
            _slicesCut[(int)ingredient]++;
        }

        public RecipeStepState StepState(int index) => _steps[index];

        /// <summary>Her mutfak olayından sonra: adımların o anki durumu (herkeste aynı olaylarla aynı çıkıyor).</summary>
        public void UpdateSteps(KitchenCounter counter)
        {
            if (!HasTargets || counter == null) return;

            if (Kind == RecipeKind.TomatoPlate)
            {
                if (UpdatePlateSteps(counter)) Version++;
                return;
            }

            UpdateSteps(counter.Cup, counter.Bowl);
        }

        /// <summary>Kaseyle yapılan tariflerde: bardak ve kase (testler doğrudan çağırıyor).</summary>
        public void UpdateSteps(KitchenContainer cup, KitchenContainer bowl)
        {
            if (!HasTargets || Kind != RecipeKind.StrawberryMilk) return;
            if (UpdateMilkSteps(cup, bowl)) Version++;
        }

        /// <summary>
        /// Domatesli tabak: doğrandı mı, adada tabak var mı, adadaki tabakta tam 3 dilim domates (başka bir şey yok)
        /// mu. Tamamlanınca öyle kalıyor.
        /// </summary>
        bool UpdatePlateSteps(KitchenCounter counter)
        {
            int slices = TargetOf(Ingredient.Tomato);
            KitchenPlate plate = ServedPlate(counter, out bool onIsland);
            int tomatoes = plate != null ? plate.CountOf(Ingredient.Tomato) : 0;
            int others = plate != null ? plate.Pieces.Count - tomatoes : 0;

            bool changed = false;
            changed |= SetStep(0, _slicesCut[(int)Ingredient.Tomato] >= slices ? RecipeStepState.Done : RecipeStepState.Pending);
            changed |= SetStep(1, onIsland ? RecipeStepState.Done : RecipeStepState.Pending);
            changed |= SetStep(2, tomatoes > slices || others > 0 ? RecipeStepState.Over : tomatoes == slices ? RecipeStepState.Done : RecipeStepState.Pending);

            if (!Completed && !Failed && onIsland && tomatoes == slices && others == 0)
            {
                Completed = true;
                changed = true;
            }

            return changed;
        }

        /// <summary>Sayılan tabak: adada (elde olmayan) üstünde en çok şey olan; adada yoksa üstünde en çok şey olan.</summary>
        static KitchenPlate ServedPlate(KitchenCounter counter, out bool onIsland)
        {
            KitchenPlate best = null;
            onIsland = false;
            foreach (KitchenPlate plate in counter.Plates)
            {
                if (plate == null || !plate.gameObject.activeInHierarchy || plate.Item == counter.Held) continue;

                bool island = counter.IsOnIsland(plate.transform.position);
                bool better = best == null
                    || (island && !onIsland)
                    || (island == onIsland && plate.Pieces.Count > best.Pieces.Count);
                if (!better) continue;

                best = plate;
                onIsland = island;
            }

            return best;
        }

        /// <summary>Çilekli süt. Ölçü bardağı adımı süt kaseye geçince de tamam sayılıyor.</summary>
        bool UpdateMilkSteps(KitchenContainer cup, KitchenContainer bowl)
        {
            if (bowl == null) return false;

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
            return changed;
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

        /// <summary>Kaseye bir şey girdikçe çağrılıyor (kaseyle yapılan tariflerde).</summary>
        public void Check(KitchenContainer bowl)
        {
            if (Failed || Completed || !HasTargets || !UsesBowl) return;

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

            if (Kind == RecipeKind.StrawberryMilk && IsEnough(bowl, Ingredient.Milk) && IsEnough(bowl, Ingredient.Strawberry))
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

        // ── KART ────────────────────────────────────────────────────────────

        /// <summary>Kartın başlığı.</summary>
        public string Title
        {
            get
            {
                switch (Kind)
                {
                    case RecipeKind.TomatoPlate: return "DOMATESLİ TABAK";
                    case RecipeKind.StrawberryMilk: return "ÇİLEKLİ SÜT";
                    default: return "ÇİLEKLİ KEK";
                }
            }
        }

        /// <summary>Başlığın altındaki kısa not.</summary>
        public string Subtitle
        {
            get
            {
                switch (Kind)
                {
                    case RecipeKind.TomatoPlate: return "1. tarif · Sunum adada";
                    default: return "Ölçü bardağı 1 L · Hata payı %20";
                }
            }
        }

        /// <summary>Adımlar, sırayla: metin ve durum.</summary>
        public List<(string text, RecipeStepState state)> Steps()
        {
            var steps = new List<(string, RecipeStepState)>();
            switch (Kind)
            {
                case RecipeKind.TomatoPlate:
                    int slices = TargetOf(Ingredient.Tomato);
                    steps.Add(($"Domatesi doğra: {slices} dilim", _steps[0]));
                    steps.Add(("Bir tabağı adaya koy", _steps[1]));
                    steps.Add(($"{slices} dilim domatesi tabağa koy", _steps[2]));
                    break;

                case RecipeKind.StrawberryMilk:
                    steps.Add(($"Çileği doğra: {Format(Ingredient.Strawberry, TargetOf(Ingredient.Strawberry))}", _steps[0]));
                    steps.Add(($"Sütü ölçü bardağına dök: {Format(Ingredient.Milk, TargetOf(Ingredient.Milk))}", _steps[1]));
                    steps.Add(("Ölçü bardağındaki sütü kaseye dök", _steps[2]));
                    steps.Add(("Çilek dilimlerini kaseye ekle", _steps[3]));
                    break;

                case RecipeKind.StrawberryCake:
                    steps.Add(($"Çilekleri doğra: {Format(Ingredient.Strawberry, TargetOf(Ingredient.Strawberry))}", RecipeStepState.Pending));
                    var bowl = new StringBuilder("Kaseye koy:");
                    foreach (Ingredient ingredient in new[] { Ingredient.Flour, Ingredient.Milk, Ingredient.Sugar, Ingredient.BakingPowder, Ingredient.Egg })
                        bowl.Append($"\n{Ingredients.NameOf(ingredient)}: {Format(ingredient, TargetOf(ingredient))}");
                    steps.Add((bowl.ToString(), RecipeStepState.Pending));
                    steps.Add(("Karıştır", RecipeStepState.Pending));
                    steps.Add(("Çilekleri ekle, kalıba dök", RecipeStepState.Pending));
                    steps.Add(("Fırında pişir", RecipeStepState.Pending));
                    steps.Add(("Servis et", RecipeStepState.Pending));
                    break;
            }

            return steps;
        }

        /// <summary>Kartın tamamı tek metin (test ipucu ve kayıt için).</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Title);
            text.AppendLine(Subtitle);
            List<(string text, RecipeStepState state)> steps = Steps();
            for (int i = 0; i < steps.Count; i++)
                text.AppendLine($"{i + 1}. {steps[i].text} [{steps[i].state}]");

            return text.ToString();
        }
    }
}
