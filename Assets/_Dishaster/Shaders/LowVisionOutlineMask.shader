// LowVisionOutline objelerini iki dokuya çiziyor (bkz. LowVisionEffect.RenderOutlineMask):
//  0. geçiş: kameraya uzaklık ve hat parlaklığı tek sayıda paketli (bkz. LowVision.shader DecodeMark). Derinlik tamponu
//     yok: Min karışımıyla en yakın yüzey kalıyor.
//  1. geçiş: o en yakın yüzeyin kamera uzayındaki yönü. Eşyanın tezgaha değen alt kenarı tezgahla aynı uzaklıkta;
//     ayırt etmek için LowVision shader'ı yönü de karşılaştırıyor.
// Matrisler LowVisionEffect'ten geliyor, Unity'nin o anki kamera durumuna bağlı değil. Sahnedeki diğer objeler hesaba
// katılmıyor; önlerinde bir şey var mı LowVision shader'ı karşılaştırıp anlıyor.
Shader "Hidden/Dishaster/LowVisionOutlineMask"
{
    CGINCLUDE
    #include "UnityCG.cginc"

    // Properties'te yok: LowVisionEffect global olarak veriyor.
    float4x4 _LVMaskViewProj;
    float4x4 _LVWorldToView;
    float3 _LVCameraPosition;
    float3 _LVCameraForward;
    float _OutlineLevel;

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float depth : TEXCOORD0;
        float3 viewNormal : TEXCOORD1;
    };

    v2f vert(appdata v)
    {
        float4 world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1));

        v2f o;
        o.pos = mul(_LVMaskViewProj, world);
        o.depth = dot(world.xyz - _LVCameraPosition, _LVCameraForward);
        o.viewNormal = mul((float3x3)_LVWorldToView, UnityObjectToWorldNormal(v.normal));
        return o;
    }

    // Milimetre adımlı uzaklık * 16 + parlaklık (0-15). 30 m'de 491520, float'ta tam sayı olarak kesin.
    float Pack(float depth)
    {
        return floor(max(depth, 0) * 1024) * 16 + round(saturate(_OutlineLevel) * 15);
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            BlendOp Min
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                return float4(Pack(i.depth), 0, 0, 0);
            }
            ENDCG
        }

        Pass
        {
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _LVMaskDepth;
            float2 _LVMaskTexel;

            float4 frag(v2f i) : SV_Target
            {
                // Aynı boyutta, aynı izdüşümle çizilmiş doku: aynı piksel.
                float nearest = floor(tex2Dlod(_LVMaskDepth, float4(i.pos.xy * _LVMaskTexel, 0, 0)).r / 16);
                float own = floor(Pack(i.depth) / 16);
                if (own > nearest + 1) discard;

                return float4(normalize(i.viewNormal), 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
