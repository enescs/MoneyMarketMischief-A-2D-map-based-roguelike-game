Shader "Custom/OceanWave"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskTex ("Land Mask (R=land)", 2D) = "white" {}
        _WaveColor1 ("Wave Color Light", Color) = (0.3, 0.5, 0.7, 0.2)
        _WaveColor2 ("Wave Color Dark", Color) = (0.05, 0.12, 0.25, 0.15)
        _FoamColor ("Foam Color", Color) = (0.8, 0.9, 1.0, 0.3)
        _WaveScale1 ("Wave Scale 1", Float) = 8.0
        _WaveScale2 ("Wave Scale 2", Float) = 15.0
        _WaveSpeed1 ("Wave Speed 1", Vector) = (0.06, 0.04, 0, 0)
        _WaveSpeed2 ("Wave Speed 2", Vector) = (-0.04, 0.06, 0, 0)
        _FoamScale ("Foam Scale", Float) = 25.0
        _FoamSpeed ("Foam Speed", Vector) = (0.01, -0.015, 0, 0)
        _FoamThreshold ("Foam Threshold", Range(0.5, 0.95)) = 0.72
        _Intensity ("Overall Intensity", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _MaskTex;
            float4 _WaveColor1;
            float4 _WaveColor2;
            float4 _FoamColor;
            float _WaveScale1;
            float _WaveScale2;
            float4 _WaveSpeed1;
            float4 _WaveSpeed2;
            float _FoamScale;
            float4 _FoamSpeed;
            float _FoamThreshold;
            float _Intensity;

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash(i);
                float b = hash(i + float2(1, 0));
                float c = hash(i + float2(0, 1));
                float d = hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                float2 shift = float2(100, 100);
                for (int i = 0; i < 4; i++)
                {
                    v += amp * noise(p);
                    p = p * 2.0 + shift;
                    amp *= 0.5;
                }
                return v;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //mask: R=1 kara, R=0 su
                fixed4 mask = tex2D(_MaskTex, i.uv);
                float waterMask = 1.0 - mask.r;

                if (waterMask < 0.01) return fixed4(0, 0, 0, 0);

                float t = _Time.y;

                //dalga katmanı 1
                float2 waveUV1 = i.uv * _WaveScale1 + _WaveSpeed1.xy * t;
                float wave1 = fbm(waveUV1);

                //dalga katmanı 2
                float2 waveUV2 = i.uv * _WaveScale2 + _WaveSpeed2.xy * t;
                float wave2 = fbm(waveUV2);

                //dalga rengi — yarı saydam overlay
                float waveMix = wave1 * 0.6 + wave2 * 0.4;
                fixed4 waveCol = lerp(_WaveColor1, _WaveColor2, waveMix);

                //köpük
                float2 foamUV = i.uv * _FoamScale + _FoamSpeed.xy * t;
                float foam = fbm(foamUV);
                float foamMask = smoothstep(_FoamThreshold, 1.0, foam);
                fixed4 foamCol = _FoamColor * foamMask;

                //birleştir — yarı saydam overlay olarak
                fixed4 result = waveCol + foamCol;
                result.a *= waterMask * _Intensity;

                return result;
            }
            ENDCG
        }
    }
}
