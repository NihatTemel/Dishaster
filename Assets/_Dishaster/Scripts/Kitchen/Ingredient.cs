using UnityEngine;

namespace Dishaster
{
    /// <summary>Tariflerde geçen malzemeler. Sıra ağ mesajlarında ve dizilerde indeks: sona ekle.</summary>
    public enum Ingredient : byte
    {
        Milk,
        Flour,
        Sugar,
        BakingPowder,
        Egg,
        Strawberry,
    }

    public static class Ingredients
    {
        public static readonly int Count = System.Enum.GetValues(typeof(Ingredient)).Length;

        static readonly string[] Names = { "Süt", "Un", "Şeker", "Kabartma tozu", "Yumurta", "Çilek" };
        static readonly string[] Units = { "ml", "ml", "ml", "ml", "adet", "dilim" };

        // Kapta kapladığı hacim (ml): sayılanlarda tanesi başına.
        static readonly float[] Volumes = { 1f, 1f, 1f, 1f, 50f, 10f };

        static readonly Color[] Colors =
        {
            new Color(0.97f, 0.97f, 0.94f),
            new Color(0.93f, 0.90f, 0.84f),
            new Color(1.00f, 1.00f, 1.00f),
            new Color(0.95f, 0.95f, 0.97f),
            new Color(0.98f, 0.80f, 0.25f),
            new Color(0.85f, 0.15f, 0.18f),
        };

        public static string NameOf(Ingredient ingredient) => Names[(int)ingredient];

        public static string UnitOf(Ingredient ingredient) => Units[(int)ingredient];

        public static float VolumeOf(Ingredient ingredient) => Volumes[(int)ingredient];

        public static Color ColorOf(Ingredient ingredient) => Colors[(int)ingredient];

        /// <summary>Dökülebilenler ml ile ölçülüyor; diğerleri tane tane ekleniyor.</summary>
        public static bool IsPourable(Ingredient ingredient) => Volumes[(int)ingredient] == 1f;

        /// <summary>Miktar dizisinde en çok hacim kaplayan malzeme.</summary>
        public static Ingredient MainOf(float[] amounts)
        {
            int main = 0;
            for (int i = 1; i < amounts.Length; i++)
                if (amounts[i] * Volumes[i] > amounts[main] * Volumes[main])
                    main = i;

            return (Ingredient)main;
        }
    }
}
