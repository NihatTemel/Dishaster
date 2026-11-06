using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// DrawingBoard prefab'ını oluşturur ve Game sahnesine koyar. Bir kez kendiliğinden çalışır;
    /// sonrasında prefab elle düzenlenebilir, sahnedeki yeri de elle değiştirilebilir.
    /// </summary>
    [InitializeOnLoad]
    public static class DrawingBoardInstaller
    {
        const string Version = "1";
        const string VersionFile = "ProjectSettings/DishasterDrawingBoard.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/DrawingBoard.prefab";
        const string SurfaceMaterialPath = "Assets/_Dishaster/Materials/M_BoardSurface.mat";

        // Odanın kuzey duvarı z=7'de.
        static readonly Vector3 BoardPosition = new Vector3(0f, 1.6f, 6.96f);

        static readonly Color ToolbarColor = new Color(0.19f, 0.18f, 0.22f);
        static readonly Color SelectionColor = new Color(0.96f, 0.73f, 0.25f);
        static readonly Color ButtonColor = new Color(0.35f, 0.34f, 0.40f);
        static readonly Color TextColor = new Color(0.96f, 0.94f, 0.90f);

        static DrawingBoardInstaller()
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

        [MenuItem("Tools/Dishaster/2 Install Drawing Board", priority = 2)]
        static void Install()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            if (PlaceInGameScene()) File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde tahta yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, BoardPosition, Quaternion.identity);
        }

        static void BuildPrefab()
        {
            var root = new GameObject("DrawingBoard");
            var board = root.AddComponent<DrawingBoard>();
            var interaction = root.AddComponent<DrawingBoardInteraction>();
            interaction.board = board;

            // Duvara yaslanan çerçeve: root duvardan 4 cm önde, çerçeve arkaya doğru duruyor.
            GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Object.DestroyImmediate(frame.GetComponent<BoxCollider>());
            frame.transform.SetParent(root.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            frame.transform.localScale = new Vector3(3.36f, 1.76f, 0.04f);
            frame.GetComponent<Renderer>().sharedMaterial = DishasterSceneBuilder.GetMaterial("M_BoardFrame", new Color(0.25f, 0.18f, 0.13f));

            // Oranı texture ile aynı (2:1). Boyama texture'a yapılıyor, ışıktan etkilenmesin diye Unlit.
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            Object.DestroyImmediate(surface.GetComponent<MeshCollider>());
            surface.transform.SetParent(root.transform, false);
            surface.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            surface.transform.localScale = new Vector3(3.2f, 1.6f, 1f);
            var surfaceRenderer = surface.GetComponent<Renderer>();
            surfaceRenderer.sharedMaterial = GetSurfaceMaterial();
            board.surface = surfaceRenderer;

            // Çizerken kamera burada. Biraz aşağıda: tahta ekranda yukarı kayıyor, alt tarafta toolbar'a yer kalıyor.
            var viewPoint = new GameObject("ViewPoint");
            viewPoint.transform.SetParent(root.transform, false);
            viewPoint.transform.localPosition = new Vector3(0f, -0.2f, -1.6f);
            interaction.viewPoint = viewPoint.transform;

            Transform canvas = DishasterSceneBuilder.AddCanvas();
            canvas.name = "Canvas";
            canvas.SetParent(root.transform, false);
            canvas.GetComponent<Canvas>().sortingOrder = 10;

            TMP_Text prompt = DishasterSceneBuilder.AddText(canvas, "Prompt", "[E] Tahtaya çiz", 34, TextColor,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(600f, 60f));
            prompt.gameObject.SetActive(false);
            interaction.prompt = prompt.gameObject;

            BuildToolbar(canvas, interaction);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        static void BuildToolbar(Transform canvas, DrawingBoardInteraction interaction)
        {
            var panel = new GameObject("Toolbar").AddComponent<Image>();
            panel.transform.SetParent(canvas, false);
            panel.color = ToolbarColor;
            DishasterSceneBuilder.SetRect(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 85f), new Vector2(1420f, 130f));

            // Renkler
            int colorCount = DrawingBoard.Palette.Length;
            interaction.colorButtons = new Button[colorCount];
            interaction.colorSelections = new GameObject[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                Button button = AddToolButton(panel.transform, "Color" + (i + 1), new Vector2(-640f + 90f * i, 0f), DrawingBoard.Palette[i], out GameObject selection);
                interaction.colorButtons[i] = button;
                interaction.colorSelections[i] = selection;
            }

            // Fırça boyu: nokta büyüklüğü fırçayı gösteriyor.
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            float[] dotSizes = { 14f, 28f, 50f };
            int brushCount = DrawingBoard.BrushRadii.Length;
            interaction.brushButtons = new Button[brushCount];
            interaction.brushSelections = new GameObject[brushCount];
            for (int i = 0; i < brushCount; i++)
            {
                Button button = AddToolButton(panel.transform, "Brush" + (i + 1), new Vector2(-90f + 90f * i, 0f), ButtonColor, out GameObject selection);
                interaction.brushButtons[i] = button;
                interaction.brushSelections[i] = selection;

                var dot = new GameObject("Dot").AddComponent<Image>();
                dot.transform.SetParent(button.transform, false);
                dot.sprite = knob;
                dot.raycastTarget = false;
                DishasterSceneBuilder.SetRect(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(dotSizes[i], dotSizes[i]));
            }

            interaction.clearButton = DishasterSceneBuilder.AddButton(panel.transform, "ClearButton", "CLEAR", ButtonColor, TextColor, new Vector2(290f, 0f), new Vector2(190f, 80f));
            interaction.closeButton = DishasterSceneBuilder.AddButton(panel.transform, "CloseButton", "CLOSE", ButtonColor, TextColor, new Vector2(550f, 0f), new Vector2(190f, 80f));

            panel.gameObject.SetActive(false);
            interaction.toolbar = panel.gameObject;
        }

        static Button AddToolButton(Transform parent, string name, Vector2 position, Color color, out GameObject selection)
        {
            // Seçim çerçevesi butonun arkasında, ondan biraz büyük.
            var frame = new GameObject(name + "Selection").AddComponent<Image>();
            frame.transform.SetParent(parent, false);
            frame.color = SelectionColor;
            frame.raycastTarget = false;
            DishasterSceneBuilder.SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(90f, 90f));
            selection = frame.gameObject;

            var image = new GameObject(name).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            DishasterSceneBuilder.SetRect(image.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(76f, 76f));

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        static Material GetSurfaceMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SurfaceMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Unlit/Texture"));
                AssetDatabase.CreateAsset(material, SurfaceMaterialPath);
            }

            return material;
        }
    }
}
