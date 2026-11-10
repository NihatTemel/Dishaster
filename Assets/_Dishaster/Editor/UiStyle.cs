using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Arayüzün tek görünümü: art'ın paletinden renkler (krem, mürekkep kahvesi, adaçayı, hardal, terrakota),
    /// yuvarlak köşeli kartlar ve butonlar (üretilen 9-dilim sprite), Spartan yazı tipi (OFL; projede Mirror'ın
    /// editör klasöründe duruyordu, build'e girsin diye UI/Fonts'a kopyalanıp TMP font asset'i kuruluyor).
    /// Sahne kurucu ve UI kuran installer'lar bunu kullanıyor.
    /// </summary>
    internal static class UiStyle
    {
        const string Folder = "Assets/_Dishaster/UI";
        const string RoundedPath = Folder + "/UI_Rounded.png";
        const string FadePath = Folder + "/UI_Fade.png";
        const string FontFolder = Folder + "/Fonts";
        const string SourceFonts = "Assets/Mirror/Hosting/Edgegap/Editor/Fonts/Src/Spartan/";

        // Önceden atlasa konan karakterler (Türkçe dahil); kalanlar oyunda kendiliğinden ekleniyor.
        const string Characters = " !\"#%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]_abcdefghijklmnopqrstuvwxyz{|}·•–—…ÇĞİÖŞÜçğıöşü°×";

        public static readonly Color Cream = new Color(0.965f, 0.933f, 0.875f);
        public static readonly Color CreamDark = new Color(0.898f, 0.847f, 0.761f);
        public static readonly Color Ink = new Color(0.231f, 0.173f, 0.141f);
        public static readonly Color InkSoft = new Color(0.231f, 0.173f, 0.141f, 0.65f);
        public static readonly Color Terracotta = new Color(0.776f, 0.420f, 0.290f);
        public static readonly Color Mustard = new Color(0.890f, 0.690f, 0.290f);
        public static readonly Color Sage = new Color(0.471f, 0.573f, 0.443f);
        public static readonly Color DustyBlue = new Color(0.498f, 0.596f, 0.682f);
        public static readonly Color Shade = new Color(0.10f, 0.07f, 0.06f, 0.6f);

        public enum Kind { Primary, Accent, Quiet, Ghost, GhostLight }

        static TMP_FontAsset s_heading;
        static TMP_FontAsset s_body;

        public static TMP_FontAsset Heading => s_heading != null ? s_heading : s_heading = FontAsset("Spartan-SemiBold.ttf", "Spartan SemiBold SDF");
        public static TMP_FontAsset Body => s_body != null ? s_body : s_body = FontAsset("Spartan-Regular.ttf", "Spartan Regular SDF");

        public static Sprite Rounded => SpriteAt(RoundedPath, WriteRounded, new Vector4(24f, 24f, 24f, 24f));
        public static Sprite Fade => SpriteAt(FadePath, WriteFade, Vector4.zero);

        // ── PARÇALAR ────────────────────────────────────────────────────────

        /// <summary>Yuvarlak köşeli kart, altında hafif gölge.</summary>
        public static Image Card(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size, bool shadow = true)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = Rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            DishasterSceneBuilder.SetRect(image.rectTransform, anchor, position, size);

            if (shadow)
            {
                var drop = image.gameObject.AddComponent<Shadow>();
                drop.effectColor = new Color(0f, 0f, 0f, 0.28f);
                drop.effectDistance = new Vector2(0f, -7f);
            }

            return image;
        }

        /// <summary>Bütün ekranı kaplayan düz renk (menüde arka planı karartmak için).</summary>
        public static Image Overlay(Transform parent, string name, Color color)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            Stretch(image.rectTransform);
            return image;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void Style(TMP_Text text, bool heading)
        {
            TMP_FontAsset font = heading ? Heading : Body;
            if (font != null) text.font = font;
        }

        /// <summary>Butonun görünümü: renk ve yazı türe göre, köşeler yuvarlak, üstüne gelince/basınca koyulaşıyor.</summary>
        public static void Style(Button button, Kind kind)
        {
            var image = button.GetComponent<Image>();
            image.sprite = Rounded;
            image.type = Image.Type.Sliced;

            Color background;
            Color text;
            switch (kind)
            {
                case Kind.Primary: background = Terracotta; text = Cream; break;
                case Kind.Accent: background = Mustard; text = Ink; break;
                case Kind.Quiet: background = CreamDark; text = Ink; break;
                case Kind.Ghost: background = new Color(Ink.r, Ink.g, Ink.b, 0.08f); text = Ink; break;
                default: background = new Color(Cream.r, Cream.g, Cream.b, 0.16f); text = Cream; break;
            }

            image.color = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (kind == Kind.Primary || kind == Kind.Accent)
            {
                var drop = button.gameObject.AddComponent<Shadow>();
                drop.effectColor = new Color(0f, 0f, 0f, 0.22f);
                drop.effectDistance = new Vector2(0f, -4f);
            }

            var label = button.GetComponentInChildren<TMP_Text>();
            if (label == null) return;
            Style(label, true);
            label.color = text;
            label.characterSpacing = 4f;
        }

        public static void Style(TMP_InputField input)
        {
            var image = input.GetComponent<Image>();
            image.sprite = Rounded;
            image.type = Image.Type.Sliced;
            image.color = CreamDark;

            Style(input.textComponent, true);
            input.textComponent.color = Ink;
            input.fontAsset = Heading != null ? Heading : input.fontAsset;
            input.caretColor = Ink;
            input.selectionColor = new Color(Mustard.r, Mustard.g, Mustard.b, 0.5f);

            if (input.placeholder is TMP_Text placeholder)
            {
                Style(placeholder, false);
                placeholder.color = InkSoft;
                placeholder.fontStyle = FontStyles.Normal;
            }
        }

        // ── ASSET'LER ───────────────────────────────────────────────────────

        static Sprite SpriteAt(string path, System.Action<string> write, Vector4 border)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            Directory.CreateDirectory(Folder);
            write(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // Beyaz, köşe yarıçapı 22 px, kenarı yumuşak: 9 dilim (24 px kenar) her boyda aynı köşe.
        static void WriteRounded(string path)
        {
            const int size = 64;
            const float radius = 22f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float dx = Mathf.Max(radius - px, px - (size - radius), 0f);
                    float dy = Mathf.Max(radius - py, py - (size - radius), 0f);
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        // Soldan sağa yumuşakça kaybolan beyaz (menüde kartın arkasındaki karartma).
        static void WriteFade(string path)
        {
            const int width = 256;
            const int height = 4;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float t = x / (width - 1f);
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, t));
                for (int y = 0; y < height; y++)
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        static TMP_FontAsset FontAsset(string file, string assetName)
        {
            string assetPath = $"{FontFolder}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            Directory.CreateDirectory(FontFolder);
            string fontPath = $"{FontFolder}/{file}";
            if (!File.Exists(fontPath))
            {
                if (!File.Exists(SourceFonts + file)) return null;
                File.Copy(SourceFonts + file, fontPath);
                AssetDatabase.ImportAsset(fontPath, ImportAssetOptions.ForceSynchronousImport);
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) return null;

            TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            if (created == null) return null;

            created.name = assetName;
            AssetDatabase.CreateAsset(created, assetPath);
            created.atlasTextures[0].name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(created.atlasTextures[0], created);
            created.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(created.material, created);

            created.TryAddCharacters(Characters);
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
