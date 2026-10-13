// Painted backdrop with a slow, subtle 2D wave: the UVs are nudged by two sines (one sideways along y, one up/down along x),
// so the picture sways like it is seen through water. Mobile cost: 1 texture fetch + 2 sin per pixel, no extra passes,
// no grab, no branches; everything else is computed per vertex. Unlit, alpha-blended like the plain sprite it replaces.
// Lives in Resources/ so Resources.Load<Shader> finds it in builds (Board.BuildVisuals).
Shader "BlobMerge/BackdropWave"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Amplitude ("Amplitude (UV)", Range(0, 0.02)) = 0.00271
        _Speed ("Speed", Range(0, 2)) = 1.2
        _Frequency ("Waves per height", Range(1, 20)) = 17
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half _Amplitude, _Speed, _Frequency;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 phase : TEXCOORD1; // x: time phase of the sideways wave, y: of the vertical one (per vertex, free)
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float t = _Time.y * _Speed;
                o.phase = float2(t, 1.3 * t + 1.7);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                // float (not half) for the sine arguments: half runs out of precision once the phase grows
                float dx = sin(uv.y * _Frequency + uv.x * 2.0 + i.phase.x);
                float dy = sin(uv.x * _Frequency * 0.75 + uv.y * 1.5 - i.phase.y);
                uv += float2(dx, dy * 0.7) * _Amplitude;
                return tex2D(_MainTex, uv) * i.color;
            }
            ENDCG
        }
    }
}
