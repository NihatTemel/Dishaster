using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster
{
    /// <summary>
    /// Tarif kartı ve tarif bozulunca ya da tamamlanınca çıkan sonuç paneli (prefab). Kartı sadece Bilmiyorum
    /// görüyor: deftere çizip Duymadım'a anlatıyor, o da Görmedim'e söylüyor. Sonucu herkes görüyor.
    /// Dökülen miktar hiçbir arayüzde yazmıyor: ölçü bardağının çizgilerinden okunuyor (yakından bakmak için Z).
    /// </summary>
    public class RecipeHud : MonoBehaviour
    {
        public GameObject recipePanel;
        public TMP_Text recipeText;
        public GameObject failPanel;
        public TMP_Text failText;

        int _shownVersion = -1;
        bool _roleResolved;
        bool _showRecipe;
        Image _resultImage;
        Color _failColor;

        static readonly Color CompletedColor = new Color(0.20f, 0.55f, 0.28f, 0.92f);

        void Awake()
        {
            // Sonuç paneli: bozulunca kırmızı, tamamlanınca yeşil.
            _resultImage = failPanel.GetComponent<Image>();
            if (_resultImage != null) _failColor = _resultImage.color;

            recipePanel.SetActive(false);
            failPanel.SetActive(false);
        }

        void Update()
        {
            if (!_roleResolved)
            {
                if (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.TryGetComponent(out DishasterPlayer player)) return;

                _roleResolved = true;
                // Test: Görmedim de kartı görüyor (bkz. DishasterTest).
                bool testSees = DishasterTest.LowVisionSeesEverything && player.role == MonkeyRoles.LowVision;
                _showRecipe = player.role == MonkeyRoles.Mute || testSees;
                _shownVersion = -1;
            }

            KitchenCounter counter = KitchenCounter.Instance;
            if (counter == null || counter.Recipe.Version == _shownVersion) return;

            Recipe recipe = counter.Recipe;
            _shownVersion = recipe.Version;

            recipePanel.SetActive(_showRecipe && recipe.HasTargets);
            if (recipe.HasTargets) recipeText.text = recipe.Describe();

            failPanel.SetActive(recipe.Failed || recipe.Completed);
            if (_resultImage != null) _resultImage.color = recipe.Completed ? CompletedColor : _failColor;
            if (recipe.Failed) failText.text = "<b>TARİF BOZULDU</b>\n<size=70%>" + recipe.FailReason + "</size>";
            else if (recipe.Completed) failText.text = "<b>TARİF TAMAMLANDI</b>\n<size=70%>Afiyet olsun!</size>";
        }
    }
}
