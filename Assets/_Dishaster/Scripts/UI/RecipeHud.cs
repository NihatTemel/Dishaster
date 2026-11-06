using System.Text;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster
{
    /// <summary>
    /// Tarif kartı, tarif bozulunca ya da tamamlanınca çıkan sonuç paneli ve dökme göstergesi (prefab). Kartı sadece Bilmiyorum görüyor: deftere
    /// çizip Duymadım'a anlatıyor, o da Görmedim'e söylüyor. Uyarıyı herkes görüyor. Dökme göstergesi kaba ne kadar
    /// girdiğini gösteriyor; görmeyen hiç görmüyor.
    /// </summary>
    public class RecipeHud : MonoBehaviour
    {
        const float PourShowSeconds = 2.5f;

        public GameObject recipePanel;
        public TMP_Text recipeText;
        public GameObject failPanel;
        public TMP_Text failText;
        public GameObject pourPanel;
        public TMP_Text pourText;

        readonly StringBuilder _pour = new StringBuilder();
        int _shownVersion = -1;
        bool _roleResolved;
        bool _showRecipe;
        bool _isLowVision;
        string _shownPour;
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
            pourPanel.SetActive(false);
        }

        void Update()
        {
            if (!_roleResolved)
            {
                if (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.TryGetComponent(out DishasterPlayer player)) return;

                _roleResolved = true;
                // Test: Görmedim de kartı ve dökme göstergesini görüyor (bkz. DishasterTest).
                bool testSees = DishasterTest.LowVisionSeesEverything && player.role == MonkeyRoles.LowVision;
                _showRecipe = player.role == MonkeyRoles.Mute || testSees;
                _isLowVision = player.role == MonkeyRoles.LowVision && !testSees;
                _shownVersion = -1;
            }

            KitchenCounter counter = KitchenCounter.Instance;
            if (counter == null) return;

            UpdateRecipe(counter);
            UpdatePour(counter);
        }

        void UpdateRecipe(KitchenCounter counter)
        {
            if (counter.Recipe.Version == _shownVersion) return;

            Recipe recipe = counter.Recipe;
            _shownVersion = recipe.Version;

            recipePanel.SetActive(_showRecipe && recipe.HasTargets);
            if (recipe.HasTargets) recipeText.text = recipe.Describe();

            failPanel.SetActive(recipe.Failed || recipe.Completed);
            if (_resultImage != null) _resultImage.color = recipe.Completed ? CompletedColor : _failColor;
            if (recipe.Failed) failText.text = "<b>TARİF BOZULDU</b>\n<size=70%>" + recipe.FailReason + "</size>";
            else if (recipe.Completed) failText.text = "<b>TARİF TAMAMLANDI</b>\n<size=70%>Afiyet olsun!</size>";
        }

        void UpdatePour(KitchenCounter counter)
        {
            bool visible = !_isLowVision && Time.time - counter.PourTime < PourShowSeconds;
            if (pourPanel.activeSelf != visible) pourPanel.SetActive(visible);
            if (!visible) return;

            string text = DescribePour(counter);
            if (text == _shownPour) return;

            _shownPour = text;
            pourText.text = text;
        }

        string DescribePour(KitchenCounter counter)
        {
            KitchenContainer source = counter.PourSource;
            KitchenContainer target = counter.PourTarget;
            string from = source != null && !string.IsNullOrEmpty(source.displayName) ? source.displayName : Ingredients.NameOf(counter.PourIngredient);

            _pour.Clear();
            if (target == null)
            {
                _pour.Append("<b>").Append(from).Append(" → yere</b>\n");
                _pour.Append("<size=150%>").Append(Mathf.RoundToInt(counter.PourSpilled)).Append(" ml</size> döküldü");
                return _pour.ToString();
            }

            string to = string.IsNullOrEmpty(target.displayName) ? "kap" : target.displayName;
            _pour.Append("<b>").Append(from).Append(" → ").Append(to).Append("</b>\n");
            _pour.Append("<size=150%>").Append(Mathf.RoundToInt(target.Volume)).Append(" ml</size> / ").Append(Mathf.RoundToInt(target.capacity)).Append(" ml");

            // İçinde birden çok malzeme varsa tek tek.
            int kinds = 0;
            for (int i = 0; i < Ingredients.Count; i++)
                if (target.AmountOf((Ingredient)i) > 0.5f) kinds++;

            if (kinds > 1)
            {
                _pour.Append("\n<size=75%>");
                bool first = true;
                for (int i = 0; i < Ingredients.Count; i++)
                {
                    var ingredient = (Ingredient)i;
                    float amount = target.AmountOf(ingredient);
                    if (amount <= 0.5f) continue;

                    if (!first) _pour.Append("  ·  ");
                    first = false;
                    _pour.Append(Ingredients.NameOf(ingredient)).Append(' ').Append(Recipe.Format(ingredient, amount));
                }

                _pour.Append("</size>");
            }

            return _pour.ToString();
        }
    }
}
