using UnityEngine;
using UnityEngine.Rendering;

namespace Dishaster
{
    /// <summary>
    /// Görmeyen oyuncunun kamerasına eklenen görüntü efekti. Oyuncu yürüdüğü için dokunma izi dünya uzayında:
    /// elin geçtiği noktalar bir süre tutuluyor, shader her pikselin dünya konumunu bunlara göre açıyor.
    /// LowVisionOutline objeleri (duvar, masa) ayrı bir dokuya çiziliyor; onların hatları el değmeden de sürekli görünüyor.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LowVisionEffect : MonoBehaviour
    {
        // Shader'daki _TouchPoints dizisiyle aynı.
        const int TrailLength = 40;
        const float TrailStep = 0.03f;

        // Odanın köşegeni ~23 m: karşı duvarlar da sürekli hatla görünsün. 16 bit derinlik bu mesafede yarım milimetre.
        const float FarClip = 30f;

        // Maske dokusunda "işaretli obje yok" değeri (paketli); shader 1000 m'den uzağı boş sayıyor.
        const float NoOutline = 1e9f;

        readonly Vector3[] _trail = new Vector3[TrailLength];
        readonly float[] _trailTime = new float[TrailLength];
        readonly Vector4[] _touchPoints = new Vector4[TrailLength];

        LowVisionRole _settings;
        Camera _camera;
        Material _material;
        Material _maskMaterial;
        CommandBuffer _maskCommands;
        RenderTexture _mask;
        RenderTexture _maskNormals;
        int _head;

        static readonly int OutlineLevelId = Shader.PropertyToID("_OutlineLevel");
        static readonly int MaskViewProjId = Shader.PropertyToID("_LVMaskViewProj");
        static readonly int CameraPositionId = Shader.PropertyToID("_LVCameraPosition");
        static readonly int CameraForwardId = Shader.PropertyToID("_LVCameraForward");
        static readonly int WorldToViewId = Shader.PropertyToID("_LVWorldToView");
        static readonly int MaskTexelId = Shader.PropertyToID("_LVMaskTexel");
        static readonly int MaskDepthId = Shader.PropertyToID("_LVMaskDepth");

        public void Setup(LowVisionRole settings)
        {
            _settings = settings;
            _material = new Material(settings.effectShader);
            _maskMaterial = new Material(settings.outlineMaskShader);
            _maskCommands = new CommandBuffer { name = "LowVisionOutlineMask" };

            _camera = GetComponent<Camera>();
            _camera.depthTextureMode |= DepthTextureMode.DepthNormals;
            _camera.allowMSAA = false;
            _camera.farClipPlane = FarClip;

            for (int i = 0; i < TrailLength; i++)
                _trailTime[i] = float.NegativeInfinity;
        }

        void UpdateTrail()
        {
            Vector3 center = _settings.TouchCenter;

            if (Vector3.Distance(center, _trail[_head]) >= TrailStep)
                _head = (_head + 1) % TrailLength;

            _trail[_head] = center;
            _trailTime[_head] = Time.time;

            // Sürenin ilk yarısı tam görünüyor, ikinci yarısında kararıyor.
            float memory = Mathf.Max(0.01f, _settings.touchMemorySeconds);
            for (int i = 0; i < TrailLength; i++)
            {
                float strength = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(2f * (1f - (Time.time - _trailTime[i]) / memory)));
                _touchPoints[i] = new Vector4(_trail[i].x, _trail[i].y, _trail[i].z, strength);
            }
        }

        static RenderTexture Resize(RenderTexture texture, int width, int height, RenderTextureFormat format, string name)
        {
            if (texture != null && texture.width == width && texture.height == height) return texture;
            if (texture != null) texture.Release();

            return new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear) { name = name, filterMode = FilterMode.Point };
        }

        // 0. geçiş en yakın işaretli yüzeyin uzaklığını, 1. geçiş o yüzeyin yönünü çiziyor (bkz. LowVisionOutlineMask).
        void RenderOutlineMask(int width, int height)
        {
            _mask = Resize(_mask, width, height, RenderTextureFormat.RFloat, "LowVisionOutlineMask");
            _maskNormals = Resize(_maskNormals, width, height, RenderTextureFormat.ARGBHalf, "LowVisionOutlineNormals");

            _maskCommands.Clear();

            // Diğer dokular (DepthNormals) gibi dokuya çizim yönünde.
            Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, true) * _camera.worldToCameraMatrix;
            Transform view = _camera.transform;
            _maskCommands.SetGlobalMatrix(MaskViewProjId, viewProjection);
            _maskCommands.SetGlobalMatrix(WorldToViewId, _camera.worldToCameraMatrix);
            _maskCommands.SetGlobalVector(CameraPositionId, view.position);
            _maskCommands.SetGlobalVector(CameraForwardId, view.forward);
            _maskCommands.SetGlobalVector(MaskTexelId, new Vector4(1f / width, 1f / height, 0f, 0f));

            _maskCommands.SetRenderTarget(_mask);
            _maskCommands.ClearRenderTarget(false, true, new Color(NoOutline, 0f, 0f, 0f));
            DrawOutlines(0);

            _maskCommands.SetGlobalTexture(MaskDepthId, _mask);
            _maskCommands.SetRenderTarget(_maskNormals);
            _maskCommands.ClearRenderTarget(false, true, Color.clear);
            DrawOutlines(1);

            Graphics.ExecuteCommandBuffer(_maskCommands);
        }

        void DrawOutlines(int pass)
        {
            foreach (LowVisionOutline outline in LowVisionOutline.All)
            {
                _maskCommands.SetGlobalFloat(OutlineLevelId, _settings.OutlineLevel(outline.type));

                foreach (MeshRenderer meshRenderer in outline.Renderers)
                {
                    if (meshRenderer == null || !meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy) continue;

                    int subMeshes = meshRenderer.sharedMaterials.Length;
                    for (int i = 0; i < subMeshes; i++)
                        _maskCommands.DrawRenderer(meshRenderer, _maskMaterial, i, pass);
                }
            }
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_material == null || _settings == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            UpdateTrail();
            RenderOutlineMask(source.width, source.height);

            float tanHalfFov = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _material.SetVectorArray("_TouchPoints", _touchPoints);
            _material.SetFloat("_TouchRadius", _settings.TouchRadius);
            _material.SetMatrix("_ViewToWorld", _camera.cameraToWorldMatrix);
            _material.SetVector("_FrustumScale", new Vector4(tanHalfFov * _camera.aspect, tanHalfFov, 0f, 0f));
            _material.SetFloat("_FarClip", _camera.farClipPlane);

            // Her karede okunuyor: Play'de sahnedeki prefab'dan ayarlanabilsin.
            _material.SetFloat("_EdgeWidth", _settings.edgeWidth);
            _material.SetFloat("_EdgeStrength", _settings.edgeStrength);
            _material.SetFloat("_DepthThreshold", _settings.edgeDepthThreshold);
            _material.SetFloat("_NormalThreshold", _settings.edgeNormalThreshold);
            _material.SetFloat("_Fill", _settings.greyFill);
            _material.SetFloat("_OutlineDepthThreshold", _settings.outlineDepthThreshold);
            _material.SetTexture("_OutlineMask", _mask);
            _material.SetTexture("_OutlineNormals", _maskNormals);
            Graphics.Blit(source, destination, _material);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_maskMaterial != null) Destroy(_maskMaterial);
            if (_mask != null) _mask.Release();
            if (_maskNormals != null) _maskNormals.Release();
            _maskCommands?.Release();
        }
    }
}
