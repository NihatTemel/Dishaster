using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster
{
    /// <summary>
    /// Tarif kartı ve tarif bozulunca ya da tamamlanınca çıkan sonuç şeridi (prefab). Kartı sadece Bilmiyorum
    /// görüyor: deftere çizip Duymadım'a anlatıyor, o da Görmedim'e söylüyor. Sonucu herkes görüyor.
    /// Kart ve şerit içeriğe göre uzuyor (yazılar kaydırılıyor, taşmıyor). Adımların yanında daire: bekliyorsa
    /// numara, yapılınca yeşil tik, fazlaysa kırmızı ünlem.
    /// Dökülen miktar hiçbir arayüzde yazmıyor: ölçü bardağının çizgilerinden okunuyor (yakından bakmak için Z).
    /// </summary>
    public class RecipeHud : MonoBehaviour
    {
        const float PopSeconds = 0.3f;

        [Header("Tarif kartı (sadece Bilmiyorum)")]
        public GameObject recipePanel;
        public TMP_Text titleText;
        public TMP_Text subtitleText;
        public RectTransform stepsRoot;
        [Tooltip("Adım satırı kalıbı: Badge (Image) > Number (TMP) ve Check (Image); Label (TMP).")]
        public GameObject stepTemplate;

        [Header("Sonuç (herkes)")]
        public GameObject resultPanel;
        public Image resultBackground;
        public TMP_Text resultTitle;
        public TMP_Text resultText;

        struct Row
        {
            public GameObject root;
            public Image badge;
            public TMP_Text number;
            public GameObject check;
            public TMP_Text label;
        }

        readonly List<Row> _rows = new List<Row>();
        int _shownVersion = -1;
        bool _roleResolved;
        bool _showRecipe;
        float _resultShownAt = float.NegativeInfinity;

        void Awake()
        {
            recipePanel.SetActive(false);
            resultPanel.SetActive(false);
            stepTemplate.SetActive(false);
        }

        void Update()
        {
            AnimateResult();

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

            _shownVersion = counter.Recipe.Version;
            Refresh(counter.Recipe);
        }

        void Refresh(Recipe recipe)
        {
            recipePanel.SetActive(_showRecipe && recipe.HasTargets);
            if (recipe.HasTargets)
            {
                titleText.text = recipe.Title;
                subtitleText.text = recipe.Subtitle;
                ShowSteps(recipe.Steps());
            }

            bool result = recipe.Failed || recipe.Completed;
            if (result && !resultPanel.activeSelf) _resultShownAt = Time.unscaledTime;
            resultPanel.SetActive(result);
            if (recipe.Failed)
            {
                resultBackground.color = HudPalette.Terracotta;
                resultTitle.text = "TARİF BOZULDU";
                resultText.text = recipe.FailReason;
            }
            else if (recipe.Completed)
            {
                resultBackground.color = HudPalette.Sage;
                resultTitle.text = "TARİF TAMAMLANDI";
                resultText.text = "Afiyet olsun!";
            }

            if (recipePanel.activeSelf) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)recipePanel.transform);
            if (resultPanel.activeSelf) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)resultPanel.transform);
        }

        void ShowSteps(List<(string text, RecipeStepState state)> steps)
        {
            while (_rows.Count < steps.Count) _rows.Add(MakeRow());

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                bool used = i < steps.Count;
                row.root.SetActive(used);
                if (used) SetRow(row, i + 1, steps[i].text, steps[i].state);
            }
        }

        Row MakeRow()
        {
            GameObject root = Instantiate(stepTemplate, stepsRoot);
            root.SetActive(true);
            Transform badge = root.transform.Find("Badge");
            return new Row
            {
                root = root,
                badge = badge.GetComponent<Image>(),
                number = badge.Find("Number").GetComponent<TMP_Text>(),
                check = badge.Find("Check").gameObject,
                label = root.transform.Find("Label").GetComponent<TMP_Text>(),
            };
        }

        static void SetRow(Row row, int index, string text, RecipeStepState state)
        {
            switch (state)
            {
                case RecipeStepState.Done:
                    row.badge.color = HudPalette.Sage;
                    row.check.SetActive(true);
                    row.number.text = "";
                    row.label.color = HudPalette.SageDark;
                    row.label.text = text;
                    break;

                case RecipeStepState.Over:
                    row.badge.color = HudPalette.Terracotta;
                    row.check.SetActive(false);
                    row.number.color = HudPalette.Cream;
                    row.number.text = "!";
                    row.label.color = HudPalette.Terracotta;
                    row.label.text = text + "  <size=80%>(fazla)</size>";
                    break;

                default:
                    row.badge.color = HudPalette.CreamDark;
                    row.check.SetActive(false);
                    row.number.color = HudPalette.Ink;
                    row.number.text = index.ToString();
                    row.label.color = HudPalette.Ink;
                    row.label.text = text;
                    break;
            }
        }

        /// <summary>Sonuç şeridi belirirken hafifçe büyüyüp yerine oturuyor.</summary>
        void AnimateResult()
        {
            if (!resultPanel.activeSelf) return;

            float t = Mathf.Clamp01((Time.unscaledTime - _resultShownAt) / PopSeconds);
            float overshoot = 1f + 0.08f * Mathf.Sin(t * Mathf.PI);
            float scale = Mathf.Lerp(0.85f, 1f, 1f - (1f - t) * (1f - t)) * overshoot;
            resultPanel.transform.localScale = Vector3.one * (t >= 1f ? 1f : scale);
        }
    }
}
