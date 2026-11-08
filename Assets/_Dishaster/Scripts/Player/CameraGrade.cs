using UnityEngine;

namespace Dishaster
{
    /// <summary>Görenlerin kamerasında renk ve vinyet (Hidden/Dishaster/Grade). KitchenFx ekliyor; görmeyende yok.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraGrade : MonoBehaviour
    {
        public Material material;

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            Graphics.Blit(source, destination, material);
        }
    }
}
