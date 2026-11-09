using Mirror;
using Steamworks;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// MainMenu → Lobby → Game akışı. Sahne ve hazır-olma işini NetworkRoomManager yapıyor;
    /// burada Steam lobisi (oda kodu) ve local (KCP) bağlantı ekleniyor.
    /// UI referansı tutmuyor: manager DontDestroyOnLoad, UI ise her sahnede yeniden yaratılıyor.
    /// </summary>
    public class DishasterNetworkManager : NetworkRoomManager
    {
        // 480 = Spacewar, Valve'ın test App ID'si. Dishaster'ın kendi ID'si alınınca burası ve
        // proje kökündeki steam_appid.txt birlikte değişecek.
        public const uint SteamAppId = 480;

        // Spacewar'ı herkes kullanıyor; lobi aramasında sadece bu oyunun lobileri gelsin.
        const string LobbyGameTag = "Dishaster";

        const string KeyGame = "Game";
        const string KeyVersion = "GameVersion";
        const string KeyHostAddress = "HostAddress";
        const string KeyRoomCode = "RoomCode";
        const string KeyLocked = "LobbyLocked";

        public const int MaxPlayers = 3;

        [Header("Transports")]
        public Transport steamTransport;
        public Transport localTransport;

        [Header("Test")]
        [Tooltip("Editörde Play'e basınca oyunu başlatmak için gereken oyuncu sayısı. Build'de her zaman 3.")]
        public int editorMinPlayers = 1;

        public static DishasterNetworkManager Instance => singleton as DishasterNetworkManager;

        public bool SteamReady { get; private set; }

        /// <summary>
        /// Steam açık ve sunuculara bağlı (giriş yapılmış). Steam istemcisi çalışırken de bağlantı kopabiliyor (ör.
        /// hesap başka yerde açılınca "Session Replaced" ile çıkış); o zaman lobi kurulamıyor, katılınamıyor.
        /// </summary>
        public bool SteamOnline => SteamReady && SteamUser.BLoggedOn();

        public const string SteamOfflineText = "Steam çevrimdışı: Steam'de yeniden giriş yap (ya da Steam'i yeniden başlat). Local oynanabilir.";
        public bool IsBusy { get; private set; }
        public string RoomCode { get; private set; } = "";

        // Static: Mirror oturum bitince manager'ı yok edip MainMenu'dekini kullanıyor;
        // "Bağlantı kesildi" gibi mesajlar yeni instance'a geçebilmeli.
        public string Status
        {
            get => s_status;
            private set => s_status = value;
        }
        static string s_status = "";

        CSteamID _currentLobby = CSteamID.Nil;
        bool _leaving;

        Callback<LobbyCreated_t> _lobbyCreated;
        Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        Callback<LobbyEnter_t> _lobbyEntered;
        Callback<LobbyMatchList_t> _lobbyMatchList;

        public override void Awake()
        {
            System.Environment.SetEnvironmentVariable("SteamAppId", SteamAppId.ToString());
            System.Environment.SetEnvironmentVariable("SteamGameId", SteamAppId.ToString());

            base.Awake();

            // MainMenu'ye dönüşte sahnedeki kopya base.Awake içinde yok ediliyor.
            if (singleton != this) return;

            maxConnections = MaxPlayers;
            minPlayers = Application.isEditor ? Mathf.Clamp(editorMinPlayers, 1, MaxPlayers) : MaxPlayers;

            SteamReady = SteamManager.Initialized;
            if (!SteamReady)
            {
                if (string.IsNullOrEmpty(Status)) Status = "Steam kapalı: sadece local oynanabilir.";
                return;
            }

            // Fizzy, OnEnable'da Steam API'ye dokunuyor; Steam yokken hiç açılmamalı.
            steamTransport.gameObject.SetActive(true);

            _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            // Kapatılmazsa eski manager sonraki oturumda da Steam olaylarını yakalar.
            _lobbyCreated?.Dispose();
            _lobbyJoinRequested?.Dispose();
            _lobbyEntered?.Dispose();
            _lobbyMatchList?.Dispose();
        }

        // ── LOCAL ───────────────────────────────────────────────────────────

        public void HostLocal()
        {
            BeginSession(localTransport);
            RoomCode = "LOCAL";
            networkAddress = "localhost";
            StartHost();
        }

        public void JoinLocal()
        {
            BeginSession(localTransport);
            RoomCode = "LOCAL";
            networkAddress = "localhost";
            StartClient();
            Status = "Local sunucuya bağlanılıyor...";
        }

        // ── STEAM ───────────────────────────────────────────────────────────

        public void HostSteam()
        {
            if (!SteamReady) return;
            if (!SteamOnline)
            {
                Status = SteamOfflineText;
                return;
            }

            BeginSession(steamTransport);
            IsBusy = true;
            RoomCode = Random.Range(10000, 100000).ToString();
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, MaxPlayers);
            Status = "Lobi oluşturuluyor...";
        }

        public void JoinSteam(string code)
        {
            if (!SteamReady) return;
            if (!SteamOnline)
            {
                Status = SteamOfflineText;
                return;
            }

            code = code == null ? "" : code.Trim();
            if (code.Length == 0)
            {
                Status = "Oda kodu gir.";
                return;
            }

            BeginSession(steamTransport);
            IsBusy = true;
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyGame, LobbyGameTag, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyVersion, Application.version, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyRoomCode, code, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.RequestLobbyList();
            Status = "Oda aranıyor: " + code;
        }

        void OnLobbyCreated(LobbyCreated_t callback)
        {
            IsBusy = false;

            if (callback.m_eResult != EResult.k_EResultOK)
            {
                Status = callback.m_eResult == EResult.k_EResultNoConnection
                    ? SteamOfflineText
                    : "Lobi oluşturulamadı: " + callback.m_eResult;
                Debug.LogWarning("[Dishaster] Steam lobisi oluşturulamadı: " + callback.m_eResult);
                return;
            }

            _currentLobby = new CSteamID(callback.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_currentLobby, KeyGame, LobbyGameTag);
            SteamMatchmaking.SetLobbyData(_currentLobby, KeyVersion, Application.version);
            SteamMatchmaking.SetLobbyData(_currentLobby, KeyHostAddress, SteamUser.GetSteamID().ToString());
            SteamMatchmaking.SetLobbyData(_currentLobby, KeyRoomCode, RoomCode);

            StartHost();
        }

        void OnLobbyMatchList(LobbyMatchList_t callback)
        {
            for (int i = 0; i < callback.m_nLobbiesMatching; i++)
            {
                CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
                if (SteamMatchmaking.GetLobbyData(lobby, KeyLocked) == "true") continue;

                SteamMatchmaking.JoinLobby(lobby);
                Status = "Lobi bulundu, katılınıyor...";
                return;
            }

            IsBusy = false;
            Status = "Oda bulunamadı. Kod doğru mu?";
        }

        void OnLobbyJoinRequested(GameLobbyJoinRequested_t callback)
        {
            // Steam daveti: zaten bir oturumdaysak önce çıkılması gerekiyor.
            if (NetworkClient.active || NetworkServer.active) return;

            BeginSession(steamTransport);
            IsBusy = true;
            SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
            Status = "Davete katılınıyor...";
        }

        void OnLobbyEntered(LobbyEnter_t callback)
        {
            CSteamID lobby = new CSteamID(callback.m_ulSteamIDLobby);

            // Host da kendi lobisine giriyor; onun bağlantısı OnLobbyCreated'da başladı.
            if (SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID()) return;

            IsBusy = false;

            if (callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = "Lobiye girilemedi: " + (EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse;
                return;
            }

            _currentLobby = lobby;
            RoomCode = SteamMatchmaking.GetLobbyData(lobby, KeyRoomCode);
            networkAddress = SteamMatchmaking.GetLobbyData(lobby, KeyHostAddress);
            StartClient();
            Status = "Host'a bağlanılıyor...";
        }

        void LeaveSteamLobby()
        {
            if (_currentLobby == CSteamID.Nil) return;

            SteamMatchmaking.LeaveLobby(_currentLobby);
            _currentLobby = CSteamID.Nil;
        }

        // ── OTURUM ──────────────────────────────────────────────────────────

        void BeginSession(Transport selected)
        {
            transport = selected;
            Transport.active = selected;
            _leaving = false;
            Status = "";
        }

        /// <summary>Host oyunu başlatır. Herkes hazır ve oyuncu sayısı tamam olmalı.</summary>
        public void StartGame()
        {
            if (!NetworkServer.active || !allPlayersReady) return;

            if (_currentLobby != CSteamID.Nil)
            {
                SteamMatchmaking.SetLobbyJoinable(_currentLobby, false);
                SteamMatchmaking.SetLobbyData(_currentLobby, KeyLocked, "true");
            }

            ServerChangeScene(GameplayScene);
        }

        /// <summary>Oturumdan çık; offlineScene (MainMenu) otomatik yükleniyor.</summary>
        public void Leave()
        {
            _leaving = true;
            Status = "";

            if (NetworkServer.active) StopHost();
            else if (NetworkClient.active) StopClient();
        }

        // Base sınıf otomatik başlatıyor; Lobby'de host'un Start butonunu bekliyoruz.
        public override void OnRoomServerPlayersReady() { }

        // Tipsiz oyuncu varken oyun başlamaz. Hazır butonu zaten tipsiz kapalı; bu sunucu tarafı garantisi.
        // readyToBegin burada sıfırlanamıyor: Mirror başka assembly'den SyncVar değiştirmeye izin vermiyor.
        public override void ReadyStatusChanged()
        {
            foreach (NetworkRoomPlayer player in roomSlots)
            {
                if (player is DishasterRoomPlayer roomPlayer && roomPlayer.monkeyRole == DishasterRoomPlayer.NoRole)
                {
                    allPlayersReady = false;
                    return;
                }
            }

            base.ReadyStatusChanged();
        }

        public override bool OnRoomServerSceneLoadedForPlayer(NetworkConnectionToClient conn, GameObject roomPlayer, GameObject gamePlayer)
        {
            var room = roomPlayer.GetComponent<DishasterRoomPlayer>();
            gamePlayer.GetComponent<DishasterPlayer>().ServerInit(room.monkeyRole, room.DisplayName);
            return true;
        }

        public override void OnRoomClientDisconnect()
        {
            IsBusy = false;
            if (!_leaving) Status = "Bağlantı kesildi.";
        }

        public override void OnRoomStartServer()
        {
            DrawingBoard.RegisterServerHandlers();
            NotebookRole.RegisterServerHandlers();
            VoiceChat.RegisterServerHandlers();
            LowVisionRole.RegisterServerHandlers();
            KitchenCounter.RegisterServerHandlers();
        }

        public override void OnRoomStartClient()
        {
            DrawingBoard.RegisterClientHandlers();
            NotebookRole.RegisterClientHandlers();
            VoiceChat.RegisterClientHandlers();
            LowVisionRole.RegisterClientHandlers();
            KitchenCounter.RegisterClientHandlers();
        }

        public override void OnRoomStopClient() => EndSession();

        // StopHost'ta client tarafının stop callback'i çalışmıyor; host buradan çıkıyor.
        public override void OnRoomStopServer() => EndSession();

        void EndSession()
        {
            IsBusy = false;
            RoomCode = "";
            LeaveSteamLobby();
        }
    }
}
