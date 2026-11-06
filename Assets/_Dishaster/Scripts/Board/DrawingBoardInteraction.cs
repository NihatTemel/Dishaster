using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dishaster
{
    /// <summary>
    /// Konuşamayan oyuncunun tahtayı kullanması: tahtaya bakıp E ile çizim moduna giriyor, kamera tahtanın
    /// önüne geliyor, imleçle çiziyor. E veya ESC ile çıkıyor. Diğer tipler tahtayı sadece görüyor.
    /// </summary>
    public class DrawingBoardInteraction : MonoBehaviour
    {
        // Çok yakın noktalar için mesaj gönderilmesin (UV cinsinden).
        const float MinStep = 0.002f;

        public DrawingBoard board;
        public Transform viewPoint;
        public float reachDistance = 3.5f;

        [Header("UI")]
        public GameObject prompt;
        public GameObject toolbar;
        public Button[] colorButtons;
        public GameObject[] colorSelections;
        public Button[] brushButtons;
        public GameObject[] brushSelections;
        public Button clearButton;
        public Button closeButton;

        DishasterPlayer _player;
        bool _drawing;
        bool _stroking;
        bool _hasLast;
        Vector2 _last;
        int _color;
        int _brush = 1;

        void Start()
        {
            for (int i = 0; i < colorButtons.Length; i++)
            {
                int index = i;
                colorButtons[i].onClick.AddListener(() => SelectColor(index));
            }

            for (int i = 0; i < brushButtons.Length; i++)
            {
                int index = i;
                brushButtons[i].onClick.AddListener(() => SelectBrush(index));
            }

            clearButton.onClick.AddListener(() => board.LocalClear());
            closeButton.onClick.AddListener(ExitDrawing);

            SelectColor(_color);
            SelectBrush(_brush);
        }

        void SelectColor(int index)
        {
            _color = index;
            for (int i = 0; i < colorSelections.Length; i++)
                colorSelections[i].SetActive(i == index);
        }

        void SelectBrush(int index)
        {
            _brush = index;
            for (int i = 0; i < brushSelections.Length; i++)
                brushSelections[i].SetActive(i == index);
        }

        void Update()
        {
            if (_player == null && NetworkClient.localPlayer != null)
                NetworkClient.localPlayer.TryGetComponent(out _player);

            if (_player == null || !MonkeyRoles.CanDraw(_player.role)) return;

            if (_drawing)
            {
                UpdateDrawing();
                return;
            }

            // İmleç serbestse ESC menüsü açık demek.
            bool canUse = Cursor.lockState == CursorLockMode.Locked && AimingAtBoard();

            prompt.SetActive(canUse);
            if (canUse && Input.GetKeyDown(KeyCode.E))
                EnterDrawing();
        }

        bool AimingAtBoard()
        {
            Transform view = _player.playerCamera.transform;
            return TryGetBoardPoint(new Ray(view.position, view.forward), reachDistance, out _);
        }

        // Tahta tek bir düzlem olduğu için collider yerine doğrudan düzlemle kesişim yeterli.
        bool TryGetBoardPoint(Ray ray, float maxDistance, out Vector2 uv)
        {
            uv = default;
            Transform surface = board.Surface;

            // Sadece ön yüzden bakınca: Quad'ın görünen yüzü -Z tarafında.
            if (Vector3.Dot(ray.direction, surface.forward) <= 0f) return false;

            var plane = new Plane(surface.forward, surface.position);
            if (!plane.Raycast(ray, out float distance) || distance > maxDistance) return false;

            Vector3 local = surface.InverseTransformPoint(ray.GetPoint(distance));
            uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        void EnterDrawing()
        {
            _drawing = true;
            _stroking = false;
            prompt.SetActive(false);
            toolbar.SetActive(true);
            _player.SetCameraOverride(viewPoint);
            GameHud.Instance.SetModal(true);
        }

        void ExitDrawing()
        {
            if (!_drawing) return;

            _drawing = false;
            _stroking = false;
            toolbar.SetActive(false);
            _player.SetCameraOverride(null);
            GameHud.Instance.SetModal(false);
        }

        void UpdateDrawing()
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
            {
                ExitDrawing();
                return;
            }

            // Toolbar'a basmak çizgi başlatmasın.
            if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            {
                _stroking = true;
                _hasLast = false;
            }

            if (Input.GetMouseButtonUp(0))
                _stroking = false;

            if (!_stroking) return;

            Ray ray = _player.playerCamera.ScreenPointToRay(Input.mousePosition);
            if (!TryGetBoardPoint(ray, Mathf.Infinity, out Vector2 uv))
            {
                // Tahtadan taşınca çizgi kopuyor; dönünce yeni noktadan devam ediyor.
                _hasLast = false;
                return;
            }

            if (!_hasLast)
            {
                board.LocalStroke(uv, uv, _color, _brush);
            }
            else if ((uv - _last).sqrMagnitude >= MinStep * MinStep)
            {
                board.LocalStroke(_last, uv, _color, _brush);
            }
            else
            {
                return;
            }

            _hasLast = true;
            _last = uv;
        }
    }
}
