using System;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dishaster
{
    /// <summary>Her kart bir tip (maymun): tıklayan oyuncu o tipi seçer, sunucu çakışmayı engeller.</summary>
    public class LobbyUI : MonoBehaviour
    {
        [Serializable]
        public class SlotView
        {
            public Button button;
            public Image background;
            public Image colorBar;
            public TMP_Text nameText;
            public TMP_Text roleText;
            public TMP_Text readyText;
        }

        public TMP_Text roomCodeText;
        public TMP_Text statusText;
        public SlotView[] slots;
        public Color cardColor = new Color(0.19f, 0.18f, 0.22f);
        public Button readyButton;
        public TMP_Text readyLabel;
        public Button startButton;
        public Button leaveButton;

        readonly DishasterRoomPlayer[] _byRole = new DishasterRoomPlayer[MonkeyRoles.Count];

        DishasterNetworkManager Manager => DishasterNetworkManager.Instance;

        void Start()
        {
            // Sahne doğrudan Play'lenirse manager yok; oturum MainMenu'den başlıyor.
            if (Manager == null)
            {
                SceneManager.LoadScene("MainMenu");
                return;
            }

            readyButton.onClick.AddListener(ToggleReady);
            startButton.onClick.AddListener(() => Manager.StartGame());
            leaveButton.onClick.AddListener(() => Manager.Leave());

            for (int i = 0; i < slots.Length; i++)
            {
                int role = i;
                slots[i].button.onClick.AddListener(() => SelectRole(role));
                slots[i].colorBar.color = MonkeyRoles.ColorOf(i);
                slots[i].roleText.text = MonkeyRoles.NameOf(i) + "\n<size=60%>" + MonkeyRoles.TraitOf(i) + "</size>";
            }
        }

        void SelectRole(int role)
        {
            var local = LocalPlayer();
            if (local != null) local.CmdSelectRole(role);
        }

        void ToggleReady()
        {
            var local = LocalPlayer();
            if (local != null) local.CmdChangeReadyState(!local.readyToBegin);
        }

        static DishasterRoomPlayer LocalPlayer()
        {
            return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<DishasterRoomPlayer>() : null;
        }

        void Update()
        {
            if (Manager == null) return;

            Array.Clear(_byRole, 0, _byRole.Length);
            int playerCount = 0;
            int readyCount = 0;

            foreach (NetworkRoomPlayer player in Manager.roomSlots)
            {
                if (!(player is DishasterRoomPlayer roomPlayer)) continue;

                playerCount++;
                if (roomPlayer.readyToBegin) readyCount++;
                if (roomPlayer.monkeyRole >= 0 && roomPlayer.monkeyRole < _byRole.Length)
                    _byRole[roomPlayer.monkeyRole] = roomPlayer;
            }

            var local = LocalPlayer();

            for (int i = 0; i < slots.Length; i++)
            {
                DishasterRoomPlayer owner = _byRole[i];
                bool mine = owner != null && owner == local;

                slots[i].nameText.text = owner != null ? owner.DisplayName : "Boş";
                slots[i].readyText.text = owner != null && owner.readyToBegin ? "HAZIR" : "";
                slots[i].background.color = mine ? Color.Lerp(cardColor, MonkeyRoles.ColorOf(i), 0.35f) : cardColor;
                slots[i].button.interactable = local != null && !local.readyToBegin && (owner == null || mine);
            }

            bool hasRole = local != null && local.monkeyRole != DishasterRoomPlayer.NoRole;

            roomCodeText.text = "ODA KODU: " + Manager.RoomCode;
            statusText.text = hasRole
                ? $"Oyuncu {playerCount}/{DishasterNetworkManager.MaxPlayers}  ·  Hazır {readyCount}"
                : "Bir tip seç";

            readyButton.interactable = hasRole;
            readyLabel.text = local != null && local.readyToBegin ? "CANCEL" : "READY";

            startButton.gameObject.SetActive(NetworkServer.active);
            startButton.interactable = Manager.allPlayersReady;
        }
    }
}
