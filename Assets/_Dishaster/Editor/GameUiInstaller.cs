using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Oyun içi arayüzün görünümü, yerinde: ESC menüsü (Game sahnesindeki GameHud; sahne yeniden kurulmuyor) ve
    /// defterin istemi/araç çubuğu (NotebookRole prefab'ı; defterin kendisi yeniden kurulmuyor). Art'ın paleti, yuvarlak
    /// köşeler, Türkçe yazılar; yazılar kutularına sığıyor. Sahne kurucu ve defter kurulumu da aynı görünümü buradan
    /// alıyor. Sürüm değişince bir kez çalışıyor; Game sahnesinde kaydedilmemiş değişiklik varsa dokunmuyor.
    /// </summary>
    [InitializeOnLoad]
    public static class GameUiInstaller
    {
        const string Version = "1";
        const string VersionFile = "ProjectSettings/DishasterGameUi.version";
        const string GamePath = "Assets/_Dishaster/Scenes/Game.unity";
        const string NotebookPath = "Assets/_Dishaster/Prefabs/NotebookRole.prefab";

        const string ControlsText = "<b>WASD</b> yürü    <b>Shift</b> koş    <b>Boşluk</b> zıpla\n<b>Ctrl</b> çömel    <b>Z</b> yakından bak";

        static GameUiInstaller()
        {
            EditorApplication.delayCall += AutoInstall;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) AutoInstall();
            };
        }

        static void AutoInstall()
        {
            if (File.Exists(VersionFile) && File.ReadAllText(VersionFile) == Version) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

            Install();
        }

        [MenuItem("Tools/Dishaster/10 Game UI", priority = 10)]
        static void Install()
        {
            Scene scene = SceneManager.GetSceneByPath(GamePath);
            bool wasOpen = scene.isLoaded;
            if (wasOpen && scene.isDirty)
            {
                Debug.LogWarning("[Dishaster] Game sahnesinde kaydedilmemiş değişiklik var; arayüz yenilenmedi. " +
                                 "Sahneyi kaydedince Tools/Dishaster/10 Game UI ile tekrar çalıştır.");
                return;
            }

            if (File.Exists(NotebookPath))
            {
                GameObject notebook = PrefabUtility.LoadPrefabContents(NotebookPath);
                try
                {
                    if (notebook.TryGetComponent(out NotebookRole role) && StyleNotebook(role))
                        PrefabUtility.SaveAsPrefabAsset(notebook, NotebookPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(notebook);
                }
            }

            if (!wasOpen) scene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);
            GameHud hud = scene.GetRootGameObjects().Select(root => root.GetComponentInChildren<GameHud>(true)).FirstOrDefault(found => found != null);
            if (hud != null)
            {
                StylePause(hud);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);

            File.WriteAllText(VersionFile, Version);
            Debug.Log("[Dishaster] Oyun içi arayüz yenilendi: ESC menüsü, defter istemi ve araç çubuğu.");
        }

        // ── ESC MENÜSÜ ──────────────────────────────────────────────────────

        /// <summary>Krem kart: başlık, iki buton, altta kontroller (yazılar sığıyor). Bağlar (GameHud) aynı kalıyor.</summary>
        internal static void StylePause(GameHud hud)
        {
            if (hud.pausePanel == null) return;

            Transform card = hud.pausePanel.transform.Find("Card");
            if (card == null) return;

            ((RectTransform)card).sizeDelta = new Vector2(600f, 470f);

            Transform titleObject = card.Find("Title");
            if (titleObject != null && titleObject.TryGetComponent(out TMP_Text title))
            {
                title.text = "DURAKLATILDI";
                title.fontSize = 52f;
                title.characterSpacing = 6f;
                DishasterSceneBuilder.SetRect(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(540f, 72f));
                UiStyle.Fit(title, 0.6f, false);
            }

            Label(hud.resumeButton, "DEVAM ET", new Vector2(0f, 60f), new Vector2(440f, 82f));
            Label(hud.leaveButton, "MENÜYE DÖN", new Vector2(0f, -40f), new Vector2(440f, 70f));

            Transform divider = card.Find("Divider");
            if (divider == null)
                divider = UiStyle.Card(card, "Divider", new Color(UiStyle.Ink.r, UiStyle.Ink.g, UiStyle.Ink.b, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one, false).transform;
            DishasterSceneBuilder.SetRect((RectTransform)divider, new Vector2(0.5f, 0.5f), new Vector2(0f, -112f), new Vector2(520f, 3f));

            Transform controlsObject = card.Find("Controls");
            TMP_Text controls = controlsObject != null ? controlsObject.GetComponent<TMP_Text>()
                : DishasterSceneBuilder.AddText(card, "Controls", "", 21f, UiStyle.InkSoft, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one);
            controls.text = ControlsText;
            controls.fontSize = 21f;
            controls.lineSpacing = 12f;
            DishasterSceneBuilder.SetRect(controls.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(530f, 92f));
            UiStyle.Fit(controls, 0.75f);
        }

        static void Label(Button button, string text, Vector2 position, Vector2 size)
        {
            if (button == null) return;

            DishasterSceneBuilder.SetRect((RectTransform)button.transform, new Vector2(0.5f, 0.5f), position, size);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            label.text = text;
            label.characterSpacing = 4f;
            UiStyle.Fit(label, 0.7f, false);
        }

        // ── DEFTER ──────────────────────────────────────────────────────────

        /// <summary>
        /// İstem koyu bir hapın içinde (mutfağın açık zemininde de okunsun); araç çubuğu mürekkep renginde, yuvarlak
        /// köşeli, renk butonları yuvarlak, seçim hardal. Değiştiyse true.
        /// </summary>
        internal static bool StyleNotebook(NotebookRole role)
        {
            if (role.prompt == null || role.toolbar == null) return false;

            StylePrompt(role);

            if (role.toolbar.TryGetComponent(out Image bar))
            {
                bar.sprite = UiStyle.Rounded;
                bar.type = Image.Type.Sliced;
                bar.color = new Color(UiStyle.Ink.r, UiStyle.Ink.g, UiStyle.Ink.b, 0.94f);
                if (!bar.TryGetComponent(out Shadow _))
                {
                    var drop = bar.gameObject.AddComponent<Shadow>();
                    drop.effectColor = new Color(0f, 0f, 0f, 0.3f);
                    drop.effectDistance = new Vector2(0f, -6f);
                }
            }

            foreach (GameObject frame in (role.colorSelections ?? new GameObject[0]).Concat(role.brushSelections ?? new GameObject[0]))
                if (frame != null && frame.TryGetComponent(out Image selection)) Round(selection, UiStyle.Mustard);

            foreach (Button button in (role.colorButtons ?? new Button[0]).Concat(role.brushButtons ?? new Button[0]))
                if (button != null && button.TryGetComponent(out Image swatch)) Round(swatch, swatch.color);

            foreach (Button brush in role.brushButtons ?? new Button[0])
                if (brush != null && brush.TryGetComponent(out Image swatch)) swatch.color = new Color(UiStyle.Cream.r, UiStyle.Cream.g, UiStyle.Cream.b, 0.16f);

            if (role.clearButton != null) UiStyle.Style(role.clearButton, UiStyle.Kind.GhostLight);
            if (role.closeButton != null) UiStyle.Style(role.closeButton, UiStyle.Kind.Accent);
            foreach (Button button in new[] { role.clearButton, role.closeButton })
            {
                TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
                if (label != null) UiStyle.Fit(label, 0.7f, false);
            }

            return true;
        }

        static void StylePrompt(NotebookRole role)
        {
            GameObject prompt = role.prompt;
            if (prompt.TryGetComponent(out Image _))
            {
                // Hap zaten var: sadece yazı.
                TMP_Text inside = prompt.GetComponentInChildren<TMP_Text>(true);
                if (inside != null) StylePromptText(inside);
                return;
            }

            TMP_Text text = prompt.GetComponent<TMP_Text>();
            if (text == null) return;

            // Hap: yazının yerinde, yazı onun içinde; istemi açıp kapatan hap.
            RectTransform textRect = text.rectTransform;
            Image pill = UiStyle.Card(textRect.parent, "PromptPill", new Color(UiStyle.Ink.r, UiStyle.Ink.g, UiStyle.Ink.b, 0.82f), Vector2.zero, Vector2.zero, Vector2.one, false);
            pill.transform.SetSiblingIndex(textRect.GetSiblingIndex());
            RectTransform pillRect = pill.rectTransform;
            pillRect.anchorMin = new Vector2(0.5f, 0f);
            pillRect.anchorMax = new Vector2(0.5f, 0f);
            pillRect.pivot = new Vector2(0.5f, 0.5f);
            pillRect.anchoredPosition = new Vector2(0f, 64f);
            pillRect.sizeDelta = new Vector2(720f, 60f);

            text.transform.SetParent(pill.transform, false);
            UiStyle.Stretch(textRect);
            textRect.offsetMin = new Vector2(24f, 0f);
            textRect.offsetMax = new Vector2(-24f, 0f);
            StylePromptText(text);

            pill.gameObject.SetActive(prompt.activeSelf);
            text.gameObject.SetActive(true);
            role.prompt = pill.gameObject;
        }

        static void StylePromptText(TMP_Text text)
        {
            UiStyle.Style(text, false);
            text.fontSize = 26f;
            text.text = "<b>[E]</b> Deftere çiz     <b>[Sağ tık basılı]</b> Göster";
            text.color = UiStyle.Cream;
            text.alignment = TextAlignmentOptions.Center;
            UiStyle.Fit(text, 0.7f, false);
        }

        static void Round(Image image, Color color)
        {
            image.sprite = UiStyle.Rounded;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2f;
            image.color = color;
        }
    }
}
