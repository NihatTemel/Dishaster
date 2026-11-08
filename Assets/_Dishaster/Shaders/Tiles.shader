// Mutfak karosu: dünya koordinatında, yüzeyin baktığı eksene göre (duvar da zemin de aynı malzemeyle doğru ölçekte).
// Derz çizgileri, karolar arasında hafif ton farkı ve parlaklık; zeminde dama deseni seçilebiliyor.
// RenderType Opaque: görmeyenin DepthNormals dokusuna giriyor, hatları eskisi gibi çıkıyor.
Shader "Dishaster/Tiles"
{
    Properties
    {
        _Color ("Tile", Color) = (0.9, 0.88, 0.82, 1)
        _AltColor ("Alternate Tile", Color) = (0.86, 0.84, 0.78, 1)
        _GroutColor ("Grout", Color) = (0.62, 0.6, 0.56, 1)
        _TileSize ("Tile Size (m)", Float) = 0.3
        _GroutWidth ("Grout Width (m)", Float) = 0.006
        _Checker ("Checker (0/1)", Range(0, 1)) = 0
        _Variation ("Per-tile Variation", Range(0, 0.2)) = 0.04
        _Glossiness ("Smoothness", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _Color;
        fixed4 _AltColor;
        fixed4 _GroutColor;
        float _TileSize;
        float _GroutWidth;
        float _Checker;
        float _Variation;
        half _Glossiness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Yüzeyin baktığı eksen düzleminde karo koordinatı.
            float3 n = abs(IN.worldNormal);
            float2 uv = n.y > max(n.x, n.z) ? IN.worldPos.xz : (n.x > n.z ? IN.worldPos.zy : IN.worldPos.xy);
            float2 cell = uv / _TileSize;
            float2 id = floor(cell);
            float2 local = frac(cell) * _TileSize;

            float2 edge = min(local, _TileSize - local);
            float grout = step(min(edge.x, edge.y), _GroutWidth * 0.5);

            float checker = fmod(abs(id.x + id.y), 2.0);
            fixed3 tile = lerp(_Color.rgb, _AltColor.rgb, checker * _Checker);
            tile *= 1.0 + (Hash(id) - 0.5) * _Variation * 2.0;

            o.Albedo = lerp(tile, _GroutColor.rgb, grout);
            o.Smoothness = lerp(_Glossiness, 0.05, grout);
            o.Metallic = 0;
        }
        ENDCG
    }

    Fallback "Diffuse"
}
