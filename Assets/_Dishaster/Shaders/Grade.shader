// Görenlerin kamerasına son dokunuş: sıcak ton, biraz kontrast ve doygunluk, köşelerde yumuşak vinyet.
Shader "Hidden/Dishaster/Grade"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Warmth ("Warmth", Range(-1, 1)) = 0.12
        _Contrast ("Contrast", Range(0.5, 1.5)) = 1.08
        _Saturation ("Saturation", Range(0, 2)) = 1.12
        _Vignette ("Vignette", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Warmth;
            float _Contrast;
            float _Saturation;
            float _Vignette;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);
                float3 c = color.rgb;

                c *= float3(1.0 + _Warmth * 0.25, 1.0 + _Warmth * 0.05, 1.0 - _Warmth * 0.2);
                float luma = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(luma.xxx, c, _Saturation);
                c = (c - 0.5) * _Contrast + 0.5;

                float2 d = i.uv - 0.5;
                float vignette = 1.0 - _Vignette * smoothstep(0.25, 0.75, dot(d, d) * 2.2);
                c *= vignette;

                return fixed4(saturate(c), color.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
