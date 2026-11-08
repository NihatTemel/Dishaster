using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Bilmiyorum'un defterini (NotebookRole prefab: defter, onu tutan sol el, kalem tutan sağ el, çizim toolbar'ı) oluşturur ve
    /// Game sahnesine koyar. Eski duvar tahtasını (DrawingBoard prefab) sahneden ve projeden kaldırır.
    /// Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class NotebookInstaller
    {
        const string Version = "4";
        const string VersionFile = "ProjectSettings/DishasterNotebook.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/NotebookRole.prefab";
        const string PageMaterialPath = "Assets/_Dishaster/Materials/M_NotebookPage.mat";

        // Eski tahta: kaldırılıyor.
        const string OldBoardPath = "Assets/_Dishaster/Prefabs/DrawingBoard.prefab";
        const string OldBoardVersionFile = "ProjectSettings/DishasterDrawingBoard.version";
        static readonly string[] OldBoardMaterials =
        {
            "Assets/_Dishaster/Materials/M_BoardFrame.mat",
            "Assets/_Dishaster/Materials/M_BoardSurface.mat",
        };

        // Texture ile aynı oran (2:1).
        static readonly Vector2 PageSize = new Vector2(0.5f, 0.25f);
        const float CoverThickness = 0.012f;

        static readonly Color ToolbarColor = new Color(0.19f, 0.18f, 0.22f);
        static readonly Color SelectionColor = new Color(0.96f, 0.73f, 0.25f);
        static readonly Color ButtonColor = new Color(0.35f, 0.34f, 0.40f);
        static readonly Color TextColor = new Color(0.96f, 0.94f, 0.90f);

        static NotebookInstaller()
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

        [MenuItem("Tools/Dishaster/2 Install Notebook", priority = 2)]
        static void Install()
        {
            if (!RemoveOldBoard()) return;

            BuildPrefab();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity);
        }

        static bool RemoveOldBoard()
        {
            if (File.Exists(OldBoardPath))
            {
                if (!GameScenePrefabs.Remove(OldBoardPath)) return false;
                AssetDatabase.DeleteAsset(OldBoardPath);
            }

            foreach (string material in OldBoardMaterials)
                if (File.Exists(material)) AssetDatabase.DeleteAsset(material);

            if (File.Exists(OldBoardVersionFile)) File.Delete(OldBoardVersionFile);
            return true;
        }

        static void BuildPrefab()
        {
            var root = new GameObject("NotebookRole");
            var role = root.AddComponent<NotebookRole>();
            // Kök hep açık: defter gizliyken de gelen çizgiler sayfaya işleniyor.
            var page = root.AddComponent<DrawingBoard>();
            role.page = page;
            role.pageSize = PageSize;

            var rig = new GameObject("Rig");
            rig.transform.SetParent(root.transform, false);
            role.rig = rig.transform;

            var notebook = new GameObject("Notebook");
            notebook.transform.SetParent(rig.transform, false);
            role.notebook = notebook.transform;

            BuildNotebook(notebook.transform, page);

            Material skin = DishasterSceneBuilder.GetMaterial("M_Hand", new Color(0.85f, 0.70f, 0.58f));

            // Defteri tek başına tutan sol el: sol kenarın ortasından.
            role.leftHand = BuildGripHand(notebook.transform, "LeftHand", -1f, skin).transform;
            role.leftHand.localPosition = new Vector3(-PageSize.x / 2f, 0f, 0f);

            // Sağ el sadece çizerken açılıyor; kök kalemin ucu.
            var rightHand = new GameObject("RightHand");
            rightHand.transform.SetParent(notebook.transform, false);
            rightHand.transform.localPosition = role.penRest;
            role.rightHand = rightHand.transform;
            BuildPenHand(rightHand.transform, skin);

            Transform canvas = DishasterSceneBuilder.AddCanvas();
            canvas.name = "Canvas";
            canvas.SetParent(root.transform, false);
            canvas.GetComponent<Canvas>().sortingOrder = 10;

            TMP_Text prompt = DishasterSceneBuilder.AddText(canvas, "Prompt", "[E] Deftere çiz     [Sağ tık basılı] Göster", 28f, TextColor,
                new Vector2(0f, 0f), new Vector2(360f, 50f), new Vector2(680f, 50f));
            prompt.gameObject.SetActive(false);
            role.prompt = prompt.gameObject;

            BuildToolbar(canvas, role);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        // Kök sayfanın ortası; sayfanın görünen yüzü -Z (kameraya bakan) tarafta, kapak arkada.
        static void BuildNotebook(Transform notebook, DrawingBoard page)
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Page";
            Object.DestroyImmediate(surface.GetComponent<MeshCollider>());
            surface.transform.SetParent(notebook, false);
            surface.transform.localScale = new Vector3(PageSize.x, PageSize.y, 1f);
            var surfaceRenderer = surface.GetComponent<Renderer>();
            surfaceRenderer.sharedMaterial = GetPageMaterial();
            page.surface = surfaceRenderer;

            Material cover = DishasterSceneBuilder.GetMaterial("M_NotebookCover", new Color(0.55f, 0.16f, 0.15f));
            AddPart(notebook, "Cover", new Vector3(0f, 0f, CoverThickness / 2f + 0.0005f),
                new Vector3(PageSize.x + 0.02f, PageSize.y + 0.02f, CoverThickness), Quaternion.identity, cover);

            Material binding = DishasterSceneBuilder.GetMaterial("M_NotebookBinding", new Color(0.62f, 0.62f, 0.65f));
            AddPart(notebook, "Binding", new Vector3(0f, PageSize.y / 2f + 0.012f, CoverThickness / 2f),
                new Vector3(PageSize.x, 0.014f, 0.02f), Quaternion.identity, binding);
        }

        /// <summary>Defterin kenarını kavrayan el: avuç kenarın dışında, parmaklar kapağın arkasında, başparmak sayfanın önünde.</summary>
        static GameObject BuildGripHand(Transform parent, string name, float side, Material skin)
        {
            // side: +1 sağ kenar (dış taraf +X), -1 sol kenar.
            var hand = new GameObject(name);
            hand.transform.SetParent(parent, false);

            float behind = CoverThickness + 0.012f;
            AddPart(hand.transform, "Palm", new Vector3(0.035f * side, 0f, behind - 0.004f), new Vector3(0.06f, 0.09f, 0.03f), Quaternion.identity, skin);
            for (int i = 0; i < 4; i++)
                AddPart(hand.transform, "Finger" + (i + 1), new Vector3(-0.025f * side, -0.033f + 0.022f * i, behind),
                    new Vector3(0.075f, 0.017f, 0.017f), Quaternion.identity, skin);
            AddPart(hand.transform, "Thumb", new Vector3(-0.008f * side, 0.02f, -0.012f), new Vector3(0.05f, 0.018f, 0.018f), Quaternion.identity, skin);

            return hand;
        }

        /// <summary>Kalem tutan sağ el. Kök kalemin ucu; kalem sayfadan kameraya doğru, sağ aşağıya uzanıyor.</summary>
        static GameObject BuildPenHand(Transform parent, Material skin)
        {
            var hand = new GameObject("Pen");
            hand.transform.SetParent(parent, false);

            Vector3 direction = new Vector3(0.45f, -0.5f, -0.75f).normalized;
            Quaternion along = Quaternion.LookRotation(direction, Vector3.up);

            Material pen = DishasterSceneBuilder.GetMaterial("M_Pen", new Color(0.12f, 0.12f, 0.14f));
            GameObject stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stick.name = "Pencil";
            Object.DestroyImmediate(stick.GetComponent<CapsuleCollider>());
            stick.transform.SetParent(hand.transform, false);
            stick.transform.localPosition = direction * 0.065f;
            stick.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            stick.transform.localScale = new Vector3(0.009f, 0.065f, 0.009f);
            stick.GetComponent<Renderer>().sharedMaterial = pen;

            // Yumruk kalemi ortasından tutuyor; işaret ve başparmak uca doğru.
            AddPart(hand.transform, "Fist", direction * 0.095f + new Vector3(0.012f, 0f, 0f), new Vector3(0.07f, 0.065f, 0.08f), along, skin);
            AddPart(hand.transform, "IndexFinger", direction * 0.05f + new Vector3(0.004f, 0.006f, 0f), new Vector3(0.017f, 0.017f, 0.06f), along, skin);
            AddPart(hand.transform, "Thumb", direction * 0.055f + new Vector3(-0.012f, -0.004f, 0f), new Vector3(0.018f, 0.018f, 0.05f), along, skin);

            return hand;
        }

        static void AddPart(Transform parent, string name, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<BoxCollider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void BuildToolbar(Transform canvas, NotebookRole role)
        {
            var panel = new GameObject("Toolbar").AddComponent<Image>();
            panel.transform.SetParent(canvas, false);
            panel.color = ToolbarColor;
            DishasterSceneBuilder.SetRect(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 85f), new Vector2(1420f, 130f));

            int colorCount = DrawingBoard.Palette.Length;
            role.colorButtons = new Button[colorCount];
            role.colorSelections = new GameObject[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                role.colorButtons[i] = AddToolButton(panel.transform, "Color" + (i + 1), new Vector2(-640f + 90f * i, 0f), DrawingBoard.Palette[i], out GameObject selection);
                role.colorSelections[i] = selection;
            }

            // Fırça boyu: nokta büyüklüğü fırçayı gösteriyor.
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            float[] dotSizes = { 14f, 28f, 50f };
            int brushCount = DrawingBoard.BrushRadii.Length;
            role.brushButtons = new Button[brushCount];
            role.brushSelections = new GameObject[brushCount];
            for (int i = 0; i < brushCount; i++)
            {
                Button button = AddToolButton(panel.transform, "Brush" + (i + 1), new Vector2(-90f + 90f * i, 0f), ButtonColor, out GameObject selection);
                role.brushButtons[i] = button;
                role.brushSelections[i] = selection;

                var dot = new GameObject("Dot").AddComponent<Image>();
                dot.transform.SetParent(button.transform, false);
                dot.sprite = knob;
                dot.raycastTarget = false;
                DishasterSceneBuilder.SetRect(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(dotSizes[i], dotSizes[i]));
            }

            role.clearButton = DishasterSceneBuilder.AddButton(panel.transform, "ClearButton", "SİL", ButtonColor, TextColor, new Vector2(290f, 0f), new Vector2(190f, 80f));
            role.closeButton = DishasterSceneBuilder.AddButton(panel.transform, "CloseButton", "KAPAT", ButtonColor, TextColor, new Vector2(550f, 0f), new Vector2(190f, 80f));

            panel.gameObject.SetActive(false);
            role.toolbar = panel.gameObject;
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

        // Boyama texture'a yapılıyor; ışıktan etkilenmesin diye Unlit.
        static Material GetPageMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PageMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Unlit/Texture"));
                AssetDatabase.CreateAsset(material, PageMaterialPath);
            }

            return material;
        }
    }
}
