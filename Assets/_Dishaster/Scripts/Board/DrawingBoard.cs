using Mirror;
using Unity.Collections;
using UnityEngine;

namespace Dishaster
{
    public struct BoardStrokeMessage : NetworkMessage
    {
        public Vector2 from;
        public Vector2 to;
        public byte color;
        public byte brush;
    }

    public struct BoardClearMessage : NetworkMessage { }

    /// <summary>
    /// Bilmiyorum'un defter sayfasının (bkz. NotebookRole) görüntüsü ve ağ senkronu. Çizen oyuncu kendi ekranına hemen çiziyor, sunucu tipini
    /// kontrol edip çizgiyi diğerlerine dağıtıyor. Sahne objesi olduğu için NetworkIdentity gerekmesin
    /// diye Mirror mesajları kullanılıyor; oyun başladıktan sonra odaya kimse katılamadığı için
    /// geç gelene geçmiş çizgileri göndermek gerekmiyor.
    /// </summary>
    public class DrawingBoard : MonoBehaviour
    {
        const int TextureWidth = 1024;
        const int TextureHeight = 512;

        public static readonly Color32[] Palette =
        {
            new Color32(20, 20, 20, 255),
            new Color32(255, 255, 255, 255),
            new Color32(220, 40, 40, 255),
            new Color32(40, 90, 220, 255),
            new Color32(40, 170, 70, 255),
            new Color32(245, 205, 40, 255),
        };

        // Piksel cinsinden yarıçap.
        public static readonly int[] BrushRadii = { 3, 8, 18 };

        static readonly Color32 BackgroundColor = new Color32(255, 255, 255, 255);

        static DrawingBoard s_instance;

        public Renderer surface;

        Texture2D _texture;
        NativeArray<Color32> _pixels;
        Material _material;
        bool _dirty;

        public Transform Surface => surface.transform;

        void Awake()
        {
            s_instance = this;

            _texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _pixels = _texture.GetRawTextureData<Color32>();

            _material = surface.material;
            _material.mainTexture = _texture;

            Clear();
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;

            Destroy(_texture);
            Destroy(_material);
        }

        void LateUpdate()
        {
            // Bir karede birden çok çizgi gelebilir; texture karede en fazla bir kez yükleniyor.
            if (!_dirty) return;

            _dirty = false;
            _texture.Apply(false);
        }

        // ── ÇİZEN OYUNCU ────────────────────────────────────────────────────

        public void LocalStroke(Vector2 from, Vector2 to, int color, int brush)
        {
            var message = new BoardStrokeMessage { from = from, to = to, color = (byte)color, brush = (byte)brush };
            Paint(message);
            NetworkClient.Send(message);
        }

        public void LocalClear()
        {
            Clear();
            NetworkClient.Send(new BoardClearMessage());
        }

        // ── AĞ ──────────────────────────────────────────────────────────────

        public static void RegisterServerHandlers()
        {
            NetworkServer.RegisterHandler<BoardStrokeMessage>(OnServerStroke);
            NetworkServer.RegisterHandler<BoardClearMessage>(OnServerClear);
        }

        public static void RegisterClientHandlers()
        {
            NetworkClient.RegisterHandler<BoardStrokeMessage>(OnClientStroke);
            NetworkClient.RegisterHandler<BoardClearMessage>(OnClientClear);
        }

        static void OnServerStroke(NetworkConnectionToClient sender, BoardStrokeMessage message)
        {
            if (SenderCanDraw(sender)) SendToOthers(sender, message);
        }

        static void OnServerClear(NetworkConnectionToClient sender, BoardClearMessage message)
        {
            if (SenderCanDraw(sender)) SendToOthers(sender, message);
        }

        static bool SenderCanDraw(NetworkConnectionToClient sender)
        {
            return sender.identity != null
                   && sender.identity.TryGetComponent(out DishasterPlayer player)
                   && MonkeyRoles.CanDraw(player.role);
        }

        static void SendToOthers<T>(NetworkConnectionToClient sender, T message) where T : struct, NetworkMessage
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != sender)
                    connection.Send(message);
        }

        static void OnClientStroke(BoardStrokeMessage message)
        {
            if (s_instance != null) s_instance.Paint(message);
        }

        static void OnClientClear(BoardClearMessage message)
        {
            if (s_instance != null) s_instance.Clear();
        }

        // ── TEXTURE ─────────────────────────────────────────────────────────

        void Clear()
        {
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = BackgroundColor;

            _dirty = true;
        }

        void Paint(BoardStrokeMessage message)
        {
            Color32 color = Palette[Mathf.Min(message.color, Palette.Length - 1)];
            int radius = BrushRadii[Mathf.Min(message.brush, BrushRadii.Length - 1)];

            float ax = Mathf.Clamp01(message.from.x) * (TextureWidth - 1);
            float ay = Mathf.Clamp01(message.from.y) * (TextureHeight - 1);
            float bx = Mathf.Clamp01(message.to.x) * (TextureWidth - 1);
            float by = Mathf.Clamp01(message.to.y) * (TextureHeight - 1);

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, bx)) - radius);
            int maxX = Mathf.Min(TextureWidth - 1, Mathf.CeilToInt(Mathf.Max(ax, bx)) + radius);
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, by)) - radius);
            int maxY = Mathf.Min(TextureHeight - 1, Mathf.CeilToInt(Mathf.Max(ay, by)) + radius);

            float abX = bx - ax;
            float abY = by - ay;
            float lengthSq = abX * abX + abY * abY;
            float radiusSq = radius * radius;

            // Kalınlığı olan bir çizgi: pikselin doğru parçasına uzaklığı yarıçaptan küçükse boyanıyor.
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float t = lengthSq > 0f ? Mathf.Clamp01(((x - ax) * abX + (y - ay) * abY) / lengthSq) : 0f;
                    float dx = x - (ax + t * abX);
                    float dy = y - (ay + t * abY);

                    if (dx * dx + dy * dy <= radiusSq)
                        _pixels[y * TextureWidth + x] = color;
                }
            }

            _dirty = true;
        }
    }
}
