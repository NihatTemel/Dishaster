using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dishaster
{
    /// <summary>Nişangah ve ESC menüsü. İmleç kilidi oyuncu kontrolünün de anahtarı (bkz. DishasterPlayer).</summary>
    public class GameHud : MonoBehaviour
    {
        public GameObject crosshair;
        public GameObject pausePanel;
        public Button resumeButton;
        public Button leaveButton;

        public static GameHud Instance { get; private set; }

        bool _modal;
        int _modalEndFrame = -1;
        bool _crosshairHidden;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            // Sahne doğrudan Play'lenirse manager yok; oturum MainMenu'den başlıyor.
            if (DishasterNetworkManager.Instance == null)
            {
                SceneManager.LoadScene("MainMenu");
                return;
            }

            resumeButton.onClick.AddListener(() => SetPaused(false));
            leaveButton.onClick.AddListener(() => DishasterNetworkManager.Instance.Leave());
            SetPaused(false);
        }

        void Update()
        {
            // Modal açıkken (ve kapandığı karede) ESC onun: aynı basış menüyü de açmasın.
            if (_modal || Time.frameCount == _modalEndFrame) return;

            if (Input.GetKeyDown(KeyCode.Escape))
                SetPaused(!pausePanel.activeSelf);
        }

        /// <summary>Nişangahı olmayan roller için (görmeyen eli yönlendiriyor).</summary>
        public void HideCrosshair()
        {
            _crosshairHidden = true;
            crosshair.SetActive(false);
        }

        /// <summary>Çizim gibi imleç isteyen ekranlar: oyuncu kontrolü durur, ESC menüsü açılmaz.</summary>
        public void SetModal(bool modal)
        {
            _modal = modal;

            if (modal)
            {
                crosshair.SetActive(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                _modalEndFrame = Time.frameCount;
                SetPaused(false);
            }
        }

        void SetPaused(bool paused)
        {
            pausePanel.SetActive(paused);
            crosshair.SetActive(!paused && !_crosshairHidden);
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Menü ve lobi imleçsiz kalmasın.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
