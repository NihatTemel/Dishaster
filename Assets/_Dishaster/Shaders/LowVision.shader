// Görmeyen oyuncunun görüntüsü: her yer siyah, sadece elin gezdiği yerde gri hatlar. Renk yok.
// Duvar ve masa gibi LowVisionOutline objelerinin hatları el değmeden de sürekli görünüyor.
// Bkz. LowVisionEffect.
Shader "Hidden/Dishaster/LowVision"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Hatlar (derinlik ve yüzey yönü farkından) ve hafif gri gölge, elin yakınından geçtiği noktalarla sınırlı.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _CameraDepthNormalsTexture;

            // xyz dünya konumu, w gücü (bkz. LowVisionEffect.TrailLength).
            float4 _TouchPoints[40];
            float _TouchRadius;
            float4x4 _ViewToWorld;
            float2 _FrustumScale;
            float _FarClip;

            float _EdgeWidth;
            float _EdgeStrength;
            float _DepthThreshold;
            float _NormalThreshold;
            float _Fill;

            // LowVisionOutlineMask: işaretli yüzeyin kameraya uzaklığı ve hat parlaklığı tek sayıda paketli.
            sampler2D _OutlineMask;
            // En yakın işaretli yüzeyin kamera uzayındaki yönü; a=0 ise yok.
            sampler2D _OutlineNormals;
            float _OutlineDepthThreshold;

            void Sample(float2 uv, out float depth, out float3 normal)
            {
                DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, uv), depth, normal);
                depth *= _FarClip;
            }

            // DepthNormals'ta derinlik iki 8 bitlik kanala kodlu: iki pikselin arası okunursa karışıp bozuluyor
            // (yüzeylerde çizgili desen). Hep piksel merkezinden ve tam piksel adımla okunuyor.
            float2 SnapToTexel(float2 uv)
            {
                float2 size = abs(_MainTex_TexelSize.zw);
                return (floor(uv * size) + 0.5) / size;
            }

            float2 EdgeStep()
            {
                return abs(_MainTex_TexelSize.xy) * max(1, floor(_EdgeWidth));
            }

            // Dokunma: derinlik ve yüzey yönü farkı.
            float TouchEdge(float4 d, float3 n0, float3 n1, float3 n2, float3 n3)
            {
                // Uzaklaştıkça aynı yüzeyde de derinlik farkı büyüyor; eşik mesafeyle ölçekleniyor.
                float distanceToView = (d.x + d.y + d.z + d.w) * 0.25;
                float depthEdge = (abs(d.x - d.y) + abs(d.z - d.w)) / (_DepthThreshold * distanceToView);
                float normalEdge = (length(n0 - n1) + length(n2 - n3)) / _NormalThreshold;

                return saturate(max(depthEdge, normalEdge) - 1);
            }

            // Sürekli hat: 1/derinlik düz yüzeyde ekranda doğrusal, ikinci farkı sıfır. Böylece yandan bakılan uzun
            // duvar boydan boya parlamıyor; sadece kopukluk (silüet) ve kıvrım (köşe) hat veriyor.
            float OutlineEdge(float dc, float4 inv, float3 nc, float3 n0, float3 n1, float3 n2, float3 n3)
            {
                float invc = 1 / dc;
                float depthEdge = (abs(inv.x + inv.y - 2 * invc) + abs(inv.z + inv.w - 2 * invc)) * dc / _OutlineDepthThreshold;
                float normalEdge = (length(n0 - n1) + length(n2 - n3)) / _NormalThreshold;

                return saturate(max(depthEdge, normalEdge) - 1);
            }

            // x uzaklık (işaretli obje yoksa çok büyük), y parlaklık.
            float2 DecodeMark(float2 uv)
            {
                float packed = tex2D(_OutlineMask, uv).r;
                float levelStep = fmod(packed, 16);
                return float2((packed - levelStep) / 16 / 1024, levelStep / 15);
            }

            float OutlineEps(float depth)
            {
                // Maske milimetreye yuvarlı, DepthNormals yarım milimetre adımlı: ince dilimler (1 cm) de önde sayılsın.
                return 0.003 + depth * 0.001;
            }

            // Bu pikselde görünen gerçekten işaretli yüzey mi: uzaklık ve yön ikisi de tutmalı. Sadece uzaklık yetmiyor:
            // eşyanın tezgaha değen alt kenarı tezgahla aynı uzaklıkta, yan yüzü ise hat veriyordu.
            bool IsMarkedSurface(float2 uv, float2 mark, float depth, float3 normal)
            {
                float4 markNormal = tex2D(_OutlineNormals, uv);
                return mark.x < 1000
                    && markNormal.a > 0.5
                    && abs(depth - mark.x) < OutlineEps(mark.x)
                    && dot(normal, markNormal.xyz) > 0.85;
            }

            // İşaretli yüzeyin önünde başka bir şey (eşya, kasa, oyuncu) var: onun silüeti çıkmamalı.
            bool Occluded(float2 uv, float depth, float3 normal)
            {
                float2 mark = DecodeMark(uv);
                return mark.x < 1000 && !IsMarkedSurface(uv, mark, depth, normal);
            }

            float TouchMask(float3 worldPosition)
            {
                float mask = 0;
                for (int i = 0; i < 40; i++)
                {
                    float touch = 1 - smoothstep(_TouchRadius * 0.5, _TouchRadius, distance(worldPosition, _TouchPoints[i].xyz));
                    mask = max(mask, touch * _TouchPoints[i].w);
                }
                return mask;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                // Kaynak ters gelirse ikinci dokular da aynı yöne çevrilmeli.
                float2 otherUv = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                    otherUv.y = 1 - otherUv.y;
                #endif

                otherUv = SnapToTexel(otherUv);

                float depth;
                float3 normal;
                Sample(otherUv, depth, normal);

                float2 step = EdgeStep();
                float2 uv0 = otherUv + step * float2(1, 1);
                float2 uv1 = otherUv + step * float2(-1, -1);
                float2 uv2 = otherUv + step * float2(-1, 1);
                float2 uv3 = otherUv + step * float2(1, -1);

                float4 d;
                float3 n0, n1, n2, n3;
                Sample(uv0, d.x, n0);
                Sample(uv1, d.y, n1);
                Sample(uv2, d.z, n2);
                Sample(uv3, d.w, n3);

                // Elin açtığı alan.
                float touchGrey = 0;
                float3 viewPosition = float3((otherUv * 2 - 1) * _FrustumScale, -1) * depth; // kamera uzayında ileri -z
                float touch = TouchMask(mul(_ViewToWorld, float4(viewPosition, 1)).xyz);
                if (touch > 0.003)
                {
                    float luma = dot(tex2D(_MainTex, i.uv).rgb, float3(0.299, 0.587, 0.114));
                    touchGrey = saturate(TouchEdge(d, n0, n1, n2, n3) * _EdgeStrength + luma * _Fill) * touch;
                }

                // Sürekli hat: sadece işaretli yüzey burada görünüyorsa.
                float outlineGrey = 0;
                float2 mark = DecodeMark(otherUv);
                if (mark.y > 0 && IsMarkedSurface(otherUv, mark, depth, normal))
                {
                    float4 inv = 1 / max(d, 0.0001);
                    float invc = 1 / max(depth, 0.0001);

                    // Önündeki eşyanın silüeti duvarda/masada çıkmasın: o örnek yerine karşı örneğin aynası alınıyor.
                    bool o0 = Occluded(uv0, d.x, n0);
                    bool o1 = Occluded(uv1, d.y, n1);
                    bool o2 = Occluded(uv2, d.z, n2);
                    bool o3 = Occluded(uv3, d.w, n3);

                    if (o0 && o1) { inv.x = invc; inv.y = invc; n0 = normal; n1 = normal; }
                    else if (o0) { inv.x = 2 * invc - inv.y; n0 = normal; }
                    else if (o1) { inv.y = 2 * invc - inv.x; n1 = normal; }

                    if (o2 && o3) { inv.z = invc; inv.w = invc; n2 = normal; n3 = normal; }
                    else if (o2) { inv.z = 2 * invc - inv.w; n2 = normal; }
                    else if (o3) { inv.w = 2 * invc - inv.z; n3 = normal; }

                    outlineGrey = saturate(OutlineEdge(max(depth, 0.0001), inv, normal, n0, n1, n2, n3) * _EdgeStrength) * mark.y;
                }

                float grey = max(touchGrey, outlineGrey);
                return fixed4(grey, grey, grey, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
