using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dishaster
{
    public class MainMenuUI : MonoBehaviour
    {
        public Button hostButton;
        public Button joinButton;
        public Button hostLocalButton;
        public Button joinLocalButton;
        public Button quitButton;
        public TMP_InputField roomCodeInput;
        public TMP_Text statusText;

        DishasterNetworkManager Manager => DishasterNetworkManager.Instance;

        void Start()
        {
            hostButton.onClick.AddListener(() => Manager.HostSteam());
            joinButton.onClick.AddListener(() => Manager.JoinSteam(roomCodeInput.text));
            hostLocalButton.onClick.AddListener(() => Manager.HostLocal());
            joinLocalButton.onClick.AddListener(() => Manager.JoinLocal());
            quitButton.onClick.AddListener(Application.Quit);
        }

        void Update()
        {
            bool idle = !Manager.IsBusy && !NetworkClient.active && !NetworkServer.active;
            bool online = Manager.SteamOnline;

            hostButton.interactable = idle && online;
            joinButton.interactable = idle && online;
            roomCodeInput.interactable = idle && online;
            hostLocalButton.interactable = idle;
            joinLocalButton.interactable = idle;

            // Steam açık ama sunuculara bağlı değilse (lobi kurulamaz) bunu söyle; bağlantı gelince yazı kalkıyor.
            string status = Manager.Status;
            if (Manager.SteamReady && !online && idle && string.IsNullOrEmpty(status)) status = DishasterNetworkManager.SteamOfflineText;
            if (online && status == DishasterNetworkManager.SteamOfflineText) status = "";
            statusText.text = status;
        }
    }
}
