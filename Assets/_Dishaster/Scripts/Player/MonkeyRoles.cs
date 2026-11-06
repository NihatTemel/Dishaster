using UnityEngine;

namespace Dishaster
{
    /// <summary>Üç maymun. Lobide seçiliyor; her birinin bir eksiği var.</summary>
    public static class MonkeyRoles
    {
        public const int Count = 3;

        public const int LowVision = 0;
        public const int Deaf = 1;
        public const int Mute = 2;

        static readonly string[] Names = { "Görmedim", "Duymadım", "Bilmiyorum" };
        static readonly string[] Traits = { "Göremiyor", "Duyamıyor", "Konuşamıyor" };

        static readonly Color[] Colors =
        {
            new Color(0.93f, 0.42f, 0.30f),
            new Color(0.30f, 0.68f, 0.89f),
            new Color(0.49f, 0.78f, 0.36f),
        };

        public static string NameOf(int role) => Names[Mathf.Clamp(role, 0, Count - 1)];

        public static string TraitOf(int role) => Traits[Mathf.Clamp(role, 0, Count - 1)];

        public static Color ColorOf(int role) => Colors[Mathf.Clamp(role, 0, Count - 1)];

        /// <summary>Konuşamayan oyuncu anlatmak için elindeki deftere çiziyor.</summary>
        public static bool CanDraw(int role) => role == Mute;
    }
}
