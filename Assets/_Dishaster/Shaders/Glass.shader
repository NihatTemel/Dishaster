// Ölçü bardağının cami: diğer oyuncular içindeki seviyeyi dışarıdan görüyor.
// RenderType Opaque bilerek: görmeyenin DepthNormals dokusuna giriyor, eline aldığında bardağın hatlarını seçiyor
// ama içindekini (zaten ona çizilmiyor) göremiyor. Ana kamerada saydam sırada çiziliyor.
Shader "Dishaster/Glass"
{
    Properties
    {
        _Color ("Color", Color) = (0.85, 0.92, 1, 0.25)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        ZWrite Off

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        fixed4 _Color;
        half _Glossiness;

        struct Input
        {
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            o.Alpha = _Color.a;
        }
        ENDCG
    }

    Fallback Off
}
