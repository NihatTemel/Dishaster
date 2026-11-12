using System.IO;
using kcp2k;
using Mirror;
using Mirror.FizzySteam;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// MainMenu, Lobby ve Game sahnelerini, oyuncu prefab'larını ve Build Settings'i kurar.
    /// Sahneler yoksa ya da BuilderVersion değiştiyse derleme sonrası kendiliğinden çalışır;
    /// menü sadece yeniden kurmak için. Bundan sonra eklenenler buraya değil, prefab olarak eklenecek.
    /// </summary>
    [InitializeOnLoad]
    public static class DishasterSceneBuilder
    {
        const string BuilderVersion = "5";
        const string VersionFile = "ProjectSettings/DishasterSceneBuilder.version";

        // Sadece oyuncu prefab'ı değişince: sahneler yeniden kurulmadan bu yenileniyor.
        const string PlayerVersion = "2";
        const string PlayerVersionFile = "ProjectSettings/DishasterGamePlayer.version";

        const string Root = "Assets/_Dishaster";
        const string MainMenuPath = Root + "/Scenes/MainMenu.unity";
        const string LobbyPath = Root + "/Scenes/Lobby.unity";
        const string GamePath = Root + "/Scenes/Game.unity";
        const string RoomPlayerPath = Root + "/Prefabs/RoomPlayer.prefab";
        const string GamePlayerPath = Root + "/Prefabs/GamePlayer.prefab";

        static readonly Color Background = new Color(0.12f, 0.11f, 0.14f);

        static readonly System.Globalization.CultureInfo Turkish = new System.Globalization.CultureInfo("tr-TR");

        static DishasterSceneBuilder()
        {
            EditorApplication.delayCall += AutoBuild;
        }

        static void AutoBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

            if (File.Exists(MainMenuPath) && IsCurrent(VersionFile, BuilderVersion))
            {
                if (!IsCurrent(PlayerVersionFile, PlayerVersion)) BuildGamePlayerPrefab();
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!SceneManager.GetSceneAt(i).isDirty) continue;

                Debug.LogWarning("[Dishaster] Açık sahnede kaydedilmemiş değişiklik var; sahneler kurulmadı. " +
                                 "Tools/Dishaster/1 Build Scenes ile kur.");
                return;
            }

            BuildAll();
        }

        static bool IsCurrent(string versionFile, string version) => File.Exists(versionFile) && File.ReadAllText(versionFile) == version;

        [MenuItem("Tools/Dishaster/1 Build Scenes", priority = 1)]
        static void BuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                BuildAll();
        }

        static void BuildAll()
        {
            GameObject roomPlayer = BuildRoomPlayerPrefab();
            GameObject gamePlayer = BuildGamePlayerPrefab();

            BuildGameScene();
            BuildLobbyScene();
            BuildMainMenuScene(roomPlayer, gamePlayer);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuPath, true),
                new EditorBuildSettingsScene(LobbyPath, true),
                new EditorBuildSettingsScene(GamePath, true),
            };

            // Eski kapalı alan (v1) malzemeleri.
            foreach (string oldName in new[] { "M_WallEast", "M_WallNorth", "M_WallSouth", "M_WallWest" })
                AssetDatabase.DeleteAsset($"{Root}/Materials/{oldName}.mat");

            AssetDatabase.SaveAssets();
            File.WriteAllText(VersionFile, BuilderVersion);

            // Prefab olarak eklenenler; Game yeniden yaratilinca geri konuyor.
            KitchenInstaller.PlaceInGameScene();
            NotebookInstaller.PlaceInGameScene();
            KitchenCounterInstaller.PlaceInGameScene();
            PantryInstaller.PlaceInGameScene();
            LowVisionInstaller.PlaceInGameScene();
            FridgeInstaller.PlaceInGameScene();
            RecipeHudInstaller.PlaceInGameScene();
            InspectInstaller.PlaceInGameScene();
            VoiceChatInstaller.PlaceInGameScene();
            CharactersInstaller.PlaceInGameScene();
            FxInstaller.PlaceInGameScene();

            Debug.Log($"[Dishaster] Sahneler kuruldu. RoomPlayer assetId={roomPlayer.GetComponent<NetworkIdentity>().assetId}, " +
                      $"GamePlayer assetId={gamePlayer.GetComponent<NetworkIdentity>().assetId}");
        }

        // ── PREFABS ─────────────────────────────────────────────────────────

        static GameObject BuildRoomPlayerPrefab()
        {
            var go = new GameObject("RoomPlayer");
            go.AddComponent<NetworkIdentity>();
            go.AddComponent<DishasterRoomPlayer>().showRoomGUI = false;
            return SavePrefab(go, RoomPlayerPath);
        }

        static GameObject BuildGamePlayerPrefab()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "GamePlayer";
            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());

            go.AddComponent<NetworkIdentity>();

            var controller = go.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.3f;

            // Bakış: gövde sağa-sola (root), kafa yukarı-aşağı (HeadPivot) dönüyor.
            var headObject = new GameObject("HeadPivot");
            headObject.transform.SetParent(go.transform, false);
            headObject.transform.localPosition = new Vector3(0f, 0.7f, 0f);

            // Sadece yerel oyuncuda açılıyor.
            var cameraObject = new GameObject("PlayerCamera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(headObject.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.05f;
            // Pencereden gökyüzü görünüyor; görmeyenin efekti onu da karartıyor.
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.SetActive(false);

            // Diğer oyuncuların nereye baktığı görünsün diye.
            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            Object.DestroyImmediate(visor.GetComponent<BoxCollider>());
            visor.transform.SetParent(headObject.transform, false);
            visor.transform.localPosition = new Vector3(0f, 0f, 0.46f);
            visor.transform.localScale = new Vector3(0.55f, 0.14f, 0.2f);
            visor.GetComponent<Renderer>().sharedMaterial = GetMaterial("M_Visor", new Color(0.08f, 0.08f, 0.10f));

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(go.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.4f, 0f);

            var label = labelObject.AddComponent<TextMeshPro>();
            if (UiStyle.Heading != null) label.font = UiStyle.Heading;
            label.fontSize = 2.5f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.rectTransform.sizeDelta = new Vector2(4f, 1f);

            // İkisi de sahibinden dağıtılıyor: biri gövdeyi, diğeri sadece kafanın dönüşünü.
            var bodySync = go.AddComponent<NetworkTransformReliable>();
            bodySync.syncDirection = SyncDirection.ClientToServer;

            var headSync = go.AddComponent<NetworkTransformReliable>();
            headSync.syncDirection = SyncDirection.ClientToServer;
            headSync.target = headObject.transform;
            headSync.syncPosition = false;
            headSync.syncRotation = true;

            var player = go.AddComponent<DishasterPlayer>();
            player.bodyRenderer = go.GetComponent<Renderer>();
            player.visorRenderer = visor.GetComponent<Renderer>();
            player.headPivot = headObject.transform;
            player.playerCamera = camera;
            player.label = label;

            GameObject prefab = SavePrefab(go, GamePlayerPath);
            File.WriteAllText(PlayerVersionFile, PlayerVersion);
            return prefab;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            // NetworkIdentity assetId'yi sadece prefab asset'i üstünde yazabiliyor;
            // ilk kayıt sırasında obje henüz sahnede olduğu için ikinci kez kaydediliyor.
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            PrefabUtility.SavePrefabAsset(asset);
            return asset;
        }

        // ── MAIN MENU ───────────────────────────────────────────────────────

        static void BuildMainMenuScene(GameObject roomPlayer, GameObject gamePlayer)
        {
            NewScene();
            AddBackdrop(new Vector3(3.4f, 1.6f, -2.55f), new Vector3(-1.2f, 1.05f, 1.6f));

            // Network Manager
            var managerObject = new GameObject("NetworkManager");
            var manager = managerObject.AddComponent<DishasterNetworkManager>();

            var localObject = new GameObject("LocalTransport");
            localObject.transform.SetParent(managerObject.transform, false);
            var kcp = localObject.AddComponent<KcpTransport>();

            var steamObject = new GameObject("SteamTransport");
            steamObject.transform.SetParent(managerObject.transform, false);
            var fizzy = steamObject.AddComponent<FizzySteamworks>();
            steamObject.SetActive(false);

            manager.transport = kcp;
            manager.localTransport = kcp;
            manager.steamTransport = fizzy;
            manager.offlineScene = MainMenuPath;
            manager.onlineScene = LobbyPath;
            manager.RoomScene = LobbyPath;
            manager.GameplayScene = GamePath;
            manager.maxConnections = DishasterNetworkManager.MaxPlayers;
            manager.minPlayers = DishasterNetworkManager.MaxPlayers;
            manager.roomPlayerPrefab = roomPlayer.GetComponent<NetworkRoomPlayer>();
            manager.playerPrefab = gamePlayer;
            manager.showRoomGUI = false;

            // UI: solda krem kart, arkasında mutfak (sağa doğru açılan karartma).
            Transform canvas = AddCanvas();
            Image fade = UiStyle.Overlay(canvas, "Fade", new Color(0.10f, 0.07f, 0.06f, 0.82f));
            fade.sprite = UiStyle.Fade;
            fade.raycastTarget = false;
            fade.rectTransform.anchorMax = new Vector2(0.72f, 1f);

            Image card = UiStyle.Card(canvas, "Panel", UiStyle.Cream, new Vector2(0f, 0.5f), new Vector2(410f, 0f), new Vector2(620f, 880f));
            RectTransform panel = card.rectTransform;

            TMP_Text title = AddText(panel, "Title", "DISHASTER", 92, UiStyle.Ink, new Vector2(0.5f, 0.5f), new Vector2(0f, 345f), new Vector2(580f, 110f), true);
            title.characterSpacing = 6f;
            AddText(panel, "Tagline", "Üç maymun, tek mutfak.", 28, UiStyle.InkSoft, new Vector2(0.5f, 0.5f), new Vector2(0f, 278f), new Vector2(560f, 44f));

            for (int i = 0; i < MonkeyRoles.Count; i++)
            {
                Image pill = UiStyle.Card(panel, "Role" + (i + 1), MonkeyRoles.ColorOf(i), new Vector2(0.5f, 0.5f), new Vector2(-180f + 180f * i, 222f), new Vector2(168f, 42f), false);
                TMP_Text name = AddText(pill.transform, "Label", MonkeyRoles.NameOf(i).ToUpper(Turkish), 19, UiStyle.Cream, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 40f), true);
                name.characterSpacing = 3f;
            }

            UiStyle.Card(panel, "Divider", UiStyle.Mustard, new Vector2(0.5f, 0.5f), new Vector2(0f, 172f), new Vector2(520f, 5f), false);

            var ui = card.gameObject.AddComponent<MainMenuUI>();
            ui.hostButton = AddButton(panel, "HostButton", "HOST", UiStyle.Kind.Primary, new Vector2(0f, 95f), new Vector2(520f, 88f));
            ui.roomCodeInput = AddInputField(panel, "RoomCodeInput", "ODA KODU", new Vector2(-133f, -10f), new Vector2(254f, 80f));
            ui.joinButton = AddButton(panel, "JoinButton", "JOIN", UiStyle.Kind.Accent, new Vector2(133f, -10f), new Vector2(254f, 80f));
            AddText(panel, "LocalLabel", "Aynı ağda", 22, UiStyle.InkSoft, new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(520f, 32f));
            ui.hostLocalButton = AddButton(panel, "HostLocalButton", "LOCAL HOST", UiStyle.Kind.Quiet, new Vector2(-133f, -142f), new Vector2(254f, 70f));
            ui.joinLocalButton = AddButton(panel, "JoinLocalButton", "LOCAL JOIN", UiStyle.Kind.Quiet, new Vector2(133f, -142f), new Vector2(254f, 70f));
            ui.quitButton = AddButton(panel, "QuitButton", "QUIT", UiStyle.Kind.Ghost, new Vector2(0f, -236f), new Vector2(520f, 62f));
            ui.statusText = AddText(panel, "Status", "", 24, UiStyle.Ink, new Vector2(0.5f, 0.5f), new Vector2(0f, -340f), new Vector2(540f, 110f));

            AddEventSystem();
            SaveScene(MainMenuPath);
        }

        // ── LOBBY ───────────────────────────────────────────────────────────

        static void BuildLobbyScene()
        {
            NewScene();
            AddBackdrop(new Vector3(-3.3f, 1.7f, -2.55f), new Vector3(0.6f, 1.0f, 1.3f));

            Transform canvas = AddCanvas();
            UiStyle.Overlay(canvas, "Shade", new Color(0.10f, 0.07f, 0.06f, 0.55f)).raycastTarget = false;
            var ui = canvas.gameObject.AddComponent<LobbyUI>();
            ui.cardColor = UiStyle.Cream;

            TMP_Text title = AddText(canvas, "Title", "LOBBY", 76, UiStyle.Cream, new Vector2(0.5f, 1f), new Vector2(0f, -105f), new Vector2(1000f, 100f), true);
            title.characterSpacing = 8f;
            Image code = UiStyle.Card(canvas, "RoomCodePill", UiStyle.Mustard, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(460f, 62f));
            ui.roomCodeText = AddText(code.transform, "RoomCode", "", 30, UiStyle.Ink, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 60f), true);
            ui.roomCodeText.characterSpacing = 4f;

            ui.slots = new LobbyUI.SlotView[MonkeyRoles.Count];
            for (int i = 0; i < ui.slots.Length; i++)
                ui.slots[i] = AddSlot(canvas, i, new Vector2(-480f + 480f * i, 20f));

            ui.statusText = AddText(canvas, "Status", "", 32, UiStyle.Cream, new Vector2(0.5f, 0.5f), new Vector2(0f, -240f), new Vector2(1000f, 60f));

            ui.leaveButton = AddButton(canvas, "LeaveButton", "LEAVE", UiStyle.Kind.GhostLight, new Vector2(-370f, -360f), new Vector2(320f, 88f));
            ui.readyButton = AddButton(canvas, "ReadyButton", "READY", UiStyle.Kind.Primary, new Vector2(0f, -360f), new Vector2(320f, 88f));
            ui.readyLabel = ui.readyButton.GetComponentInChildren<TMP_Text>();
            ui.startButton = AddButton(canvas, "StartButton", "START", UiStyle.Kind.Accent, new Vector2(370f, -360f), new Vector2(320f, 88f));

            AddEventSystem();
            SaveScene(LobbyPath);
        }

        // Tip kartı: üstte rolün renk şeridi, rol adı ve özelliği, oyuncunun adı, hazırsa rozet. Kartın tamamı buton.
        static LobbyUI.SlotView AddSlot(Transform canvas, int index, Vector2 position)
        {
            Image card = UiStyle.Card(canvas, "Slot" + (index + 1), UiStyle.Cream, new Vector2(0.5f, 0.5f), position, new Vector2(430f, 400f));
            card.raycastTarget = true;
            RectTransform panel = card.rectTransform;

            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = card;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = Color.white;
            button.colors = colors;

            Image bar = UiStyle.Card(panel, "ColorBar", Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(386f, 18f), false);

            TMP_Text ready = AddText(panel, "Ready", "", 26, UiStyle.Sage, new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(380f, 44f), true);
            ready.characterSpacing = 6f;

            return new LobbyUI.SlotView
            {
                button = button,
                background = card,
                colorBar = bar,
                roleText = AddText(panel, "Role", "", 44, UiStyle.Ink, new Vector2(0.5f, 0.5f), new Vector2(0f, 82f), new Vector2(390f, 130f), true),
                nameText = AddText(panel, "Name", "", 30, UiStyle.Ink, new Vector2(0.5f, 0.5f), new Vector2(0f, -42f), new Vector2(390f, 56f)),
                readyText = ready,
            };
        }

        /// <summary>
        /// Menü ve lobinin arkasında mutfak: oda, ada, buzdolabı, tahtada birkaç yiyecek (oyun eşyası değil, sadece
        /// görüntü), yavaşça süzülen kamera.
        /// </summary>
        static void AddBackdrop(Vector3 cameraPosition, Vector3 lookAt)
        {
            KitchenInstaller.ApplyRenderSettings();

            Camera camera = AddCamera(Background);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 55f;
            camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(lookAt - cameraPosition));
            camera.gameObject.AddComponent<MenuCamera>();

            var root = new GameObject("Backdrop").transform;
            GameObject kitchen = KitchenInstaller.EnsurePrefab();
            if (kitchen != null) PrefabUtility.InstantiatePrefab(kitchen, root);

            GameObject fridge = KitchenLayout.Art(KitchenLayout.FurnitureArt + "PF_Buzdolabi_100");
            if (fridge != null) KitchenLayout.PlaceAlong(root, fridge, KitchenLayout.Frame(KitchenLayout.Side.North), KitchenLayout.FridgeCursor, 1f);

            GameObject islandArt = KitchenLayout.Art(KitchenLayout.FurnitureArt + "PF_Ada_200");
            if (islandArt == null) return;

            var islandRoot = new GameObject("Island").transform;
            islandRoot.SetParent(root, false);
            islandRoot.position = KitchenLayout.IslandCenter;
            GameObject island = KitchenLayout.Instantiate(islandArt, islandRoot);
            island.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Bounds bounds = KitchenLayout.MeshBounds(island.transform, islandRoot);
            island.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            KitchenLayout.Paint(island, MobilyaRengi.Renk.Adacayi);

            float top = KitchenLayout.SurfaceBelow(island, islandRoot.position + Vector3.up * 2f, 3f, out Vector3 surface) ? surface.y : 0.9f;
            (string path, Vector3 spot, float yaw)[] props =
            {
                (KitchenLayout.ToolsArt + "PF_CuttingBoard_01", new Vector3(0.32f, 0f, -0.3f), 0f),
                (KitchenLayout.ToolsArt + "PF_MixingBowl_01", new Vector3(-0.42f, 0f, -0.2f), 0f),
                (KitchenLayout.ToolsArt + "PF_Knife_Chef_01", new Vector3(0.3f, 0.05f, 0.2f), 0f),
                ("Assets/ART/3D/Fruit_Vegetables/Prefabs/PF_Bread_Loaf_01", new Vector3(-0.45f, 0f, 0.4f), 15f),
                ("Assets/ART/3D/Fruit_Vegetables/Prefabs/PF_Tomato_01", new Vector3(0.18f, 0.058f, -0.28f), 0f),
                ("Assets/ART/3D/Fruit_Vegetables/Prefabs/PF_Tomato_01", new Vector3(0.36f, 0.058f, -0.36f), 70f),
                ("Assets/ART/3D/Fruit_Vegetables/Prefabs/PF_Carrot_01", new Vector3(0.52f, 0.058f, -0.24f), 30f),
                ("Assets/ART/3D/Fruit_Vegetables/Prefabs/PF_Strawberry_01", new Vector3(-0.85f, 0f, -0.35f), 0f),
                ("Assets/ART/3D/Fruit_Vegetables/Prefabs/PF_Strawberry_01", new Vector3(-0.78f, 0f, -0.45f), 50f),
                (KitchenLayout.PantryArt + "PF_MilkCarton_01", new Vector3(-0.8f, 0f, 0.15f), 20f),
            };

            foreach ((string path, Vector3 spot, float yaw) in props)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
                if (prefab == null) continue;

                GameObject prop = KitchenLayout.Instantiate(prefab, islandRoot);
                prop.transform.localPosition = new Vector3(spot.x, top - islandRoot.position.y + spot.y, spot.z);
                prop.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        // ── GAME ────────────────────────────────────────────────────────────

        static void BuildGameScene()
        {
            NewScene();

            // Oyuncu doğana kadar görüntü veren kamera; DishasterPlayer yerel oyuncuda kapatıyor.
            Camera camera = AddCamera(Color.black);
            camera.clearFlags = CameraClearFlags.Skybox;
            var cameraPosition = new Vector3(0f, 1.65f, -2.7f);
            camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(KitchenLayout.IslandCenter + Vector3.up * 0.9f - cameraPosition));

            // Oda, mobilya ve ışıklar Kitchen prefab'ında (KitchenInstaller); burada sadece sahnenin ortam ışığı.
            KitchenInstaller.ApplyRenderSettings();

            var spawnRoot = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < KitchenLayout.Spawns.Length; i++)
            {
                var spawn = new GameObject("Spawn" + (i + 1));
                spawn.transform.SetParent(spawnRoot, false);
                spawn.transform.position = KitchenLayout.Spawns[i];
                spawn.AddComponent<NetworkStartPosition>();
            }

            Transform canvas = AddCanvas();
            var hud = canvas.gameObject.AddComponent<GameHud>();

            Image crosshair = UiStyle.Card(canvas, "Crosshair", new Color(1f, 1f, 1f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f), false);
            var outline = crosshair.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
            outline.effectDistance = new Vector2(1f, -1f);
            hud.crosshair = crosshair.gameObject;

            // ESC: ekran kararıyor, ortada krem kart.
            Image pause = UiStyle.Overlay(canvas, "PausePanel", UiStyle.Shade);
            Image card = UiStyle.Card(pause.transform, "Card", UiStyle.Cream, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 400f));
            TMP_Text title = AddText(card.transform, "Title", "PAUSE", 60, UiStyle.Ink, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(500f, 80f), true);
            title.characterSpacing = 8f;
            hud.resumeButton = AddButton(card.transform, "ResumeButton", "RESUME", UiStyle.Kind.Primary, new Vector2(0f, 10f), new Vector2(420f, 80f));
            hud.leaveButton = AddButton(card.transform, "LeaveButton", "LEAVE", UiStyle.Kind.Quiet, new Vector2(0f, -100f), new Vector2(420f, 72f));
            hud.pausePanel = pause.gameObject;
            GameUiInstaller.StylePause(hud);
            pause.gameObject.SetActive(false);

            AddEventSystem();
            SaveScene(GamePath);
        }

        internal static Material GetMaterial(string name, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ── ORTAK ───────────────────────────────────────────────────────────

        static void NewScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void SaveScene(string path)
        {
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
        }

        static Camera AddCamera(Color background)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            go.AddComponent<AudioListener>();
            return camera;
        }

        static void AddEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        internal static Transform AddCanvas()
        {
            var go = new GameObject("Canvas");
            go.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return go.transform;
        }

        internal static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        internal static TMP_Text AddText(Transform parent, string name, string text, float size, Color color, Vector2 anchor, Vector2 position, Vector2 rectSize)
        {
            return AddText(parent, name, text, size, color, anchor, position, rectSize, false);
        }

        internal static TMP_Text AddText(Transform parent, string name, string text, float size, Color color, Vector2 anchor, Vector2 position, Vector2 rectSize, bool heading)
        {
            var label = new GameObject(name).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            UiStyle.Style(label, heading);
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            SetRect(label.rectTransform, anchor, position, rectSize);
            return label;
        }

        /// <summary>Eski imza (renk elle): diğer kurulumlar kullanıyor; görünüm yine ortak (yuvarlak, yazı tipi).</summary>
        internal static Button AddButton(Transform parent, string name, string text, Color color, Color textColor, Vector2 position, Vector2 size)
        {
            Button button = AddButton(parent, name, text, UiStyle.Kind.Quiet, position, size);
            button.GetComponent<Image>().color = color;
            button.GetComponentInChildren<TMP_Text>().color = textColor;
            return button;
        }

        internal static Button AddButton(Transform parent, string name, string text, UiStyle.Kind kind, Vector2 position, Vector2 size)
        {
            GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, size);

            var label = go.GetComponentInChildren<TMP_Text>();
            label.text = text;
            label.fontSize = 30f;
            label.fontStyle = FontStyles.Normal;

            var button = go.GetComponent<Button>();
            UiStyle.Style(button, kind);
            return button;
        }

        static TMP_InputField AddInputField(Transform parent, string name, string placeholder, Vector2 position, Vector2 size)
        {
            GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, size);

            var input = go.GetComponent<TMP_InputField>();
            input.characterLimit = 5;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            UiStyle.Style(input);
            input.pointSize = 32f;
            input.textComponent.alignment = TextAlignmentOptions.Center;

            var placeholderText = (TMP_Text)input.placeholder;
            placeholderText.text = placeholder;
            placeholderText.alignment = TextAlignmentOptions.Center;
            placeholderText.fontSize = 24f;
            placeholderText.characterSpacing = 3f;

            return input;
        }
    }

    /// <summary>Build'de exe'nin yanına steam_appid.txt koyar; yoksa Steam oyunu kapatıp Spacewar'ı açabilir.</summary>
    class SteamAppIdBuildStep : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;

            string folder = Path.GetDirectoryName(report.summary.outputPath);
            File.WriteAllText(Path.Combine(folder, "steam_appid.txt"), DishasterNetworkManager.SteamAppId.ToString());
        }
    }
}
