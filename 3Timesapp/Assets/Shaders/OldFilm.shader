// „Filmul vechi” din modul Trecut: un strat peste toată imaginea, desenat după scenă (și înainte de sepia și
// granulație, care se aplică și peste el). Zgârieturi verticale care rămân câteva cadre, fire de praf care
// apar și dispar la fiecare cadru, pâlpâirea luminii proiectorului, margini care „respiră” și blițul cald de
// la pornirea proiectorului. Totul e calculat din timp (18 cadre/s, ca un film mut), fără texturi.
Shader "ThreeTimes/OldFilm"
{
    Properties
    {
        _Amount ("Cât de vechi (0..1)", Range(0, 1)) = 1
        _Flash ("Bliț de proiector (0..1)", Range(0, 1)) = 0
        _Flicker ("Pâlpâire în plus (0..1)", Range(0, 1)) = 0
        _FlashColor ("Culoarea blițului", Color) = (1, 0.9, 0.72, 1)
        _Fps ("Cadre pe secundă", Float) = 18
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+400" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "OldFilm"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Amount;
                float _Flash;
                float _Flicker;
                float4 _FlashColor;
                float _Fps;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes input)
            {
                // Pătratul acoperă tot ecranul, oriunde ar fi: poziția vine direct din UV.
                Varyings o;
                float z = (UNITY_NEAR_CLIP_VALUE + UNITY_RAW_FAR_CLIP_VALUE) * 0.5;
                o.positionCS = float4(input.uv * 2.0 - 1.0, z, 1.0);
                o.uv = input.uv;
                return o;
            }

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            // Un strat de culoare c, cu acoperirea a, peste ce s-a adunat deja (culori premultiplicate).
            float4 Over(float4 acc, float3 c, float a)
            {
                a = saturate(a);
                return float4(c * a + acc.rgb * (1.0 - a), a + acc.a * (1.0 - a));
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
                // Numărul cadrului, ținut mic (sin pe numere mari pierde precizie pe telefoane).
                float frame = fmod(floor(_Time.y * _Fps), 997.0);
                float4 acc = 0;

                // 1. Pâlpâirea proiectorului: imaginea se întunecă puțin, alt pic la fiecare cadru.
                float flick = Hash(frame * 1.31 + 0.17);
                acc = Over(acc, float3(0, 0, 0), _Amount * (0.012 + 0.03 * flick) + _Flicker * 0.25 * flick);

                // 2. Marginile cadrului, arse și „respirând”.
                float edge = smoothstep(0.42, 0.78, length(uv - 0.5));
                acc = Over(acc, float3(0.09, 0.055, 0.02), edge * _Amount * (0.14 + 0.06 * flick));

                // 3. Zgârieturi verticale: rămân câteva cadre, tremură puțin, unele luminoase, altele întunecate.
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    float kk = (float)k;
                    float seg = floor(frame / (3.0 + kk * 4.0));
                    float present = step(0.5, Hash(seg * 7.7 + kk * 19.3));
                    float x = Hash(seg * 3.1 + kk * 5.7);
                    x += (Hash(frame + kk * 13.0) - 0.5) * 0.004;
                    x += sin(uv.y * 5.0 + seg) * 0.004;
                    float w = 0.0008 + 0.0014 * Hash(seg + kk * 2.0);
                    float scratch = 1.0 - smoothstep(w * 0.3, w, abs(uv.x - x));
                    float y0 = Hash(seg * 1.7 + kk) * 0.6;
                    float y1 = y0 + 0.35 + 0.65 * Hash(seg * 2.3 + kk);
                    float along = smoothstep(y0 - 0.05, y0, uv.y) * (1.0 - smoothstep(y1, y1 + 0.05, uv.y));
                    float3 c = lerp(float3(0.1, 0.07, 0.04), float3(1.0, 0.95, 0.85), step(0.55, Hash(seg * 9.1 + kk)));
                    acc = Over(acc, c, scratch * along * present * _Amount * 0.4);
                }

                // 4. Fire de praf: pete mici, neregulate, altele la fiecare cadru.
                [unroll]
                for (int s = 0; s < 7; s++)
                {
                    float ss = (float)s;
                    float2 p = float2(Hash(frame * 0.37 + ss * 11.1), Hash(frame * 0.71 + ss * 3.3));
                    float on = step(0.45, Hash(frame * 1.9 + ss * 5.1));
                    float r = 0.003 + 0.012 * Hash(frame + ss * 7.0) * Hash(frame * 3.0 + ss);
                    float2 q = uv - p;
                    q.x *= aspect;
                    q.y *= 0.5 + Hash(ss + frame * 0.13);
                    float speck = 1.0 - smoothstep(r * 0.4, r, length(q));
                    float3 c = lerp(float3(0.05, 0.03, 0.02), float3(0.95, 0.9, 0.8), step(0.7, Hash(ss * 2.7 + frame)));
                    acc = Over(acc, c, speck * on * _Amount * 0.6);
                }

                // 5. Un fir de păr care trece rar prin cadru (o curbă subțire, câteva cadre).
                float hairSeg = floor(frame / 6.0);
                float hairOn = step(0.93, Hash(hairSeg * 4.3 + 1.0));
                float2 hc = float2(Hash(hairSeg * 2.1), Hash(hairSeg * 5.9));
                float2 hq = uv - hc;
                hq.x *= aspect;
                float hairR = 0.05 + 0.06 * Hash(hairSeg * 3.3);
                float ring = abs(length(hq) - hairR);
                float arc = step(0.0, hq.x + hq.y * (Hash(hairSeg) - 0.5));
                float hair = (1.0 - smoothstep(0.0002, 0.0009, ring)) * arc;
                acc = Over(acc, float3(0.06, 0.04, 0.02), hair * hairOn * _Amount * 0.35);

                // 6. Blițul cald de la pornirea proiectorului.
                acc = Over(acc, _FlashColor.rgb, _Flash);

                return half4(acc.rgb, acc.a);
            }
            ENDHLSL
        }
    }
}
