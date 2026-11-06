using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Görmeyenin kamerasının çizmediği katman: kaplardaki dolum ve dökülen akıntı. Diğer oyuncular görüyor.
    /// Katman projeye editörde ekleniyor (BlindHiddenLayerInstaller).
    /// </summary>
    public static class BlindHiddenLayer
    {
        public const string Name = "BlindHidden";

        static int s_layer = -2;

        /// <summary>Katman yoksa -1.</summary>
        public static int Layer
        {
            get
            {
                if (s_layer == -2) s_layer = LayerMask.NameToLayer(Name);
                return s_layer;
            }
        }

        public static void Apply(Transform target)
        {
            if (target != null && Layer >= 0) target.gameObject.layer = Layer;
        }

        public static void HideFrom(Camera camera)
        {
            if (camera != null && Layer >= 0) camera.cullingMask &= ~(1 << Layer);
        }
    }
}
