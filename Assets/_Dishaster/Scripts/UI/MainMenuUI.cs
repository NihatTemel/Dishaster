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

            hostButton.interactable = idle && Manager.SteamReady;
            joinButton.interactable = idle && Manager.SteamReady;
            roomCodeInput.interactable = idle && Manager.SteamReady;
            hostLocalButton.interactable = idle;
            joinLocalButton.interactable = idle;

            statusText.text = Manager.Status;
        }
    }
}
