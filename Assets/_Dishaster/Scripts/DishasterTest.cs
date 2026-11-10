namespace Dishaster
{
    /// <summary>
    /// Geçici test anahtarları. Oyun hazır olunca hepsi false yapılacak (build'de de geçerli).
    /// </summary>
    public static class DishasterTest
    {
        /// <summary>
        /// true: Görmedim normal görüyor (siyah ekran, hatlar ve gizli katman kapalı; tarif kartını, nişangahı ve
        /// tıklama ipucunu da görüyor). false (oyunun asıl hali): ekran siyah, sadece elinin değdiği yerde ve duvar/masa
        /// hatlarında gri-beyaz çizgiler; hiç renk ve arayüz yok, yakınlaştırma ve inceleme yok.
        /// </summary>
        public const bool LowVisionSeesEverything = false;
    }
}
