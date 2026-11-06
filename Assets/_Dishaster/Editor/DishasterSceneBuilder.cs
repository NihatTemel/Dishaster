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
        const string BuilderVersion = "4";
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
        static readonly Color PanelColor = new Color(0.19f, 0.18f, 0.22f);
        static readonly Color ButtonColor = new Color(0.96f, 0.73f, 0.25f);
        static readonly Color ButtonTextColor = new Color(0.14f, 0.11f, 0.08f);
        static readonly Color SecondaryColor = new Color(0.35f, 0.34f, 0.40f);
        static readonly Color TextColor = new Color(0.96f, 0.94f, 0.90f);

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
            NotebookInstaller.PlaceInGameScene();
            KitchenCounterInstaller.PlaceInGameScene();
            PantryInstaller.PlaceInGameScene();
            LowVisionInstaller.PlaceInGameScene();
            FridgeInstaller.PlaceInGameScene();
            RecipeHudInstaller.PlaceInGameScene();
            InspectInstaller.PlaceInGameScene();
            VoiceChatInstaller.PlaceInGameScene();
            CharactersInstaller.PlaceInGameScene();

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
            controller.radius = 0.5f;

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
            camera.clearFlags = CameraClearFlags.SolidColor;
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
            AddCamera(Background);

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

            // UI
            Transform canvas = AddCanvas();

            AddText(canvas, "Title", "DISHASTER", 120, TextColor, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1200f, 160f));
            AddText(canvas, "Subtitle", "Görmedim · Duymadım · Bilmiyorum", 38, ButtonColor, new Vector2(0.5f, 1f), new Vector2(0f, -265f), new Vector2(1200f, 60f));

            RectTransform panel = AddPanel(canvas, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(640f, 560f));

            var ui = canvas.gameObject.AddComponent<MainMenuUI>();
            ui.hostButton = AddButton(panel, "HostButton", "HOST", ButtonColor, ButtonTextColor, new Vector2(0f, 200f), new Vector2(520f, 70f));
            ui.roomCodeInput = AddInputField(panel, "RoomCodeInput", "ENTER CODE", new Vector2(-135f, 110f), new Vector2(250f, 70f));
            ui.joinButton = AddButton(panel, "JoinButton", "JOIN", ButtonColor, ButtonTextColor, new Vector2(135f, 110f), new Vector2(250f, 70f));
            ui.hostLocalButton = AddButton(panel, "HostLocalButton", "LOCAL HOST", SecondaryColor, TextColor, new Vector2(-135f, 10f), new Vector2(250f, 70f));
            ui.joinLocalButton = AddButton(panel, "JoinLocalButton", "LOCAL JOIN", SecondaryColor, TextColor, new Vector2(135f, 10f), new Vector2(250f, 70f));
            ui.quitButton = AddButton(panel, "QuitButton", "QUIT", SecondaryColor, TextColor, new Vector2(0f, -90f), new Vector2(520f, 70f));
            ui.statusText = AddText(panel, "Status", "", 28, TextColor, new Vector2(0.5f, 0.5f), new Vector2(0f, -195f), new Vector2(600f, 90f));

            AddEventSystem();
            SaveScene(MainMenuPath);
        }

        // ── LOBBY ───────────────────────────────────────────────────────────

        static void BuildLobbyScene()
        {
            NewScene();
            AddCamera(Background);

            Transform canvas = AddCanvas();
            var ui = canvas.gameObject.AddComponent<LobbyUI>();

            AddText(canvas, "Title", "LOBBY", 80, TextColor, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1000f, 110f));
            ui.roomCodeText = AddText(canvas, "RoomCode", "", 40, ButtonColor, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1000f, 60f));

            ui.slots = new LobbyUI.SlotView[MonkeyRoles.Count];
            for (int i = 0; i < ui.slots.Length; i++)
                ui.slots[i] = AddSlot(canvas, i, new Vector2(-480f + 480f * i, 40f));

            ui.statusText = AddText(canvas, "Status", "", 34, TextColor, new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(1000f, 60f));

            ui.leaveButton = AddButton(canvas, "LeaveButton", "LEAVE", SecondaryColor, TextColor, new Vector2(-360f, -340f), new Vector2(320f, 90f));
            ui.readyButton = AddButton(canvas, "ReadyButton", "READY", ButtonColor, ButtonTextColor, new Vector2(0f, -340f), new Vector2(320f, 90f));
            ui.readyLabel = ui.readyButton.GetComponentInChildren<TMP_Text>();
            ui.startButton = AddButton(canvas, "StartButton", "START", ButtonColor, ButtonTextColor, new Vector2(360f, -340f), new Vector2(320f, 90f));

            AddEventSystem();
            SaveScene(LobbyPath);
        }

        static LobbyUI.SlotView AddSlot(Transform canvas, int index, Vector2 position)
        {
            RectTransform panel = AddPanel(canvas, "Slot" + (index + 1), new Vector2(0.5f, 0.5f), position, new Vector2(440f, 320f));

            // Kartın tamamı tip seçme butonu.
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel.GetComponent<Image>();

            var bar = new GameObject("ColorBar").AddComponent<Image>();
            bar.transform.SetParent(panel, false);
            bar.raycastTarget = false;
            SetRect(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(440f, 20f));

            return new LobbyUI.SlotView
            {
                button = button,
                background = panel.GetComponent<Image>(),
                colorBar = bar,
                roleText = AddText(panel, "Role", "", 40, TextColor, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(400f, 60f)),
                nameText = AddText(panel, "Name", "", 32, TextColor, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(400f, 60f)),
                readyText = AddText(panel, "Ready", "", 34, ButtonColor, new Vector2(0.5f, 0.5f), new Vector2(0f, -85f), new Vector2(400f, 60f)),
            };
        }

        // ── GAME ────────────────────────────────────────────────────────────

        static void BuildGameScene()
        {
            NewScene();

            // Oyuncu doğana kadar görüntü veren kamera; DishasterPlayer yerel oyuncuda kapatıyor.
            Camera camera = AddCamera(Color.black);
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.7f, -6.5f), Quaternion.identity);

            // Kapalı oda: ışık tavandaki lambalardan ve düz ortam ışığından geliyor.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.40f, 0.38f);
            RenderSettings.skybox = null;

            const float halfWidth = 9f;
            const float halfDepth = 7f;
            const float height = 3f;
            const float thickness = 0.4f;
            const float wallCenterX = halfWidth + thickness / 2f;
            const float wallCenterZ = halfDepth + thickness / 2f;
            float outerWidth = (halfWidth + thickness) * 2f;

            Material floor = GetMaterial("M_Floor", new Color(0.55f, 0.47f, 0.38f));
            Material wall = GetMaterial("M_Wall", new Color(0.78f, 0.74f, 0.66f));
            Material ceiling = GetMaterial("M_Ceiling", new Color(0.88f, 0.87f, 0.84f));
            Material door = GetMaterial("M_Door", new Color(0.30f, 0.20f, 0.14f));
            Material lamp = GetMaterial("M_Lamp", Color.white);
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor", Color.white * 1.5f);

            var room = new GameObject("Room").transform;
            AddBlock(room, "Floor", new Vector3(0f, -thickness / 2f, 0f), new Vector3(outerWidth, thickness, (halfDepth + thickness) * 2f), floor);
            AddBlock(room, "Ceiling", new Vector3(0f, height + thickness / 2f, 0f), new Vector3(outerWidth, thickness, (halfDepth + thickness) * 2f), ceiling);
            AddBlock(room, "WallNorth", new Vector3(0f, height / 2f, wallCenterZ), new Vector3(outerWidth, height, thickness), wall);
            AddBlock(room, "WallSouth", new Vector3(0f, height / 2f, -wallCenterZ), new Vector3(outerWidth, height, thickness), wall);
            AddBlock(room, "WallEast", new Vector3(wallCenterX, height / 2f, 0f), new Vector3(thickness, height, halfDepth * 2f), wall);
            AddBlock(room, "WallWest", new Vector3(-wallCenterX, height / 2f, 0f), new Vector3(thickness, height, halfDepth * 2f), wall);
            AddBlock(room, "Door", new Vector3(0f, 1.05f, -halfDepth + 0.03f), new Vector3(1.1f, 2.1f, 0.06f), door);
            LowVisionOutlineInstaller.MarkRoomWalls(room);

            var lamps = new GameObject("Lamps").transform;
            lamps.SetParent(room, false);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -4.5f : 4.5f;
                float z = i < 2 ? -3.5f : 3.5f;
                AddBlock(lamps, "LampPanel" + (i + 1), new Vector3(x, height - 0.03f, z), new Vector3(1.4f, 0.06f, 0.5f), lamp);

                var lightObject = new GameObject("Light" + (i + 1));
                lightObject.transform.SetParent(lamps, false);
                lightObject.transform.position = new Vector3(x, height - 0.25f, z);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 11f;
                light.intensity = 1.35f;
                light.color = new Color(1f, 0.95f, 0.86f);
                light.shadows = LightShadows.Soft;
            }

            var spawnRoot = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < MonkeyRoles.Count; i++)
            {
                var spawn = new GameObject("Spawn" + (i + 1));
                spawn.transform.SetParent(spawnRoot, false);
                spawn.transform.position = new Vector3(-2.5f + 2.5f * i, 1.05f, -5f);
                spawn.AddComponent<NetworkStartPosition>();
            }

            Transform canvas = AddCanvas();
            var hud = canvas.gameObject.AddComponent<GameHud>();

            var crosshair = new GameObject("Crosshair").AddComponent<Image>();
            crosshair.transform.SetParent(canvas, false);
            crosshair.raycastTarget = false;
            crosshair.color = new Color(1f, 1f, 1f, 0.8f);
            SetRect(crosshair.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            hud.crosshair = crosshair.gameObject;

            RectTransform pause = AddPanel(canvas, "PausePanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 340f));
            AddText(pause, "Title", "PAUSE", 54, TextColor, new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(480f, 80f));
            hud.resumeButton = AddButton(pause, "ResumeButton", "RESUME", ButtonColor, ButtonTextColor, new Vector2(0f, 0f), new Vector2(400f, 70f));
            hud.leaveButton = AddButton(pause, "LeaveButton", "LEAVE", SecondaryColor, TextColor, new Vector2(0f, -95f), new Vector2(400f, 70f));
            hud.pausePanel = pause.gameObject;
            pause.gameObject.SetActive(false);

            AddEventSystem();
            SaveScene(GamePath);
        }

        static GameObject AddBlock(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.position = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
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

        static RectTransform AddPanel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = PanelColor;
            SetRect(image.rectTransform, anchor, position, size);
            return image.rectTransform;
        }

        internal static TMP_Text AddText(Transform parent, string name, string text, float size, Color color, Vector2 anchor, Vector2 position, Vector2 rectSize)
        {
            var label = new GameObject(name).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            SetRect(label.rectTransform, anchor, position, rectSize);
            return label;
        }

        internal static Button AddButton(Transform parent, string name, string text, Color color, Color textColor, Vector2 position, Vector2 size)
        {
            GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, size);
            go.GetComponent<Image>().color = color;

            var label = go.GetComponentInChildren<TMP_Text>();
            label.text = text;
            label.fontSize = 30f;
            label.fontStyle = FontStyles.Bold;
            label.color = textColor;

            return go.GetComponent<Button>();
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
            input.pointSize = 30f;
            input.textComponent.alignment = TextAlignmentOptions.Center;

            var placeholderText = (TMP_Text)input.placeholder;
            placeholderText.text = placeholder;
            placeholderText.alignment = TextAlignmentOptions.Center;

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
