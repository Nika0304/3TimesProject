// „Cerul pictat” din modul Trecut: acoperă părțile clădirii de azi care ies din conturul fațadei vechi.
// Culoare de cer în degradeu pe verticala ecranului. Se desenează înaintea fațadei și nu scrie în adâncime,
// așa că fațada veche (și urmele) apar mereu peste el. Cu _Reveal < 1 se „developează” din granulație, odată cu
// fațada (vezi PastLit).
Shader "ThreeTimes/SkyMask"
{
    Properties
    {
        _TopColor ("Cer sus", Color) = (0.78, 0.84, 0.92, 1)
        _HorizonColor ("Cer la orizont", Color) = (0.92, 0.92, 0.90, 1)
        _Reveal ("Developare (0..1)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SkyMask"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                float _Reveal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return o;
            }

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag (Varyings i) : SV_Target
            {
                if (_Reveal < 0.999)
                {
                    float frame = floor(_Time.y * 18.0);
                    float v = Hash3(floor(i.positionWS * 1.2)) * 0.7 + Hash3(floor(i.positionWS * 9.0) + frame) * 0.3;
                    clip(_Reveal * 1.1 - v);
                }
                float y = saturate(i.screenPos.y / i.screenPos.w);
                return lerp(_HorizonColor, _TopColor, smoothstep(0.25, 1.0, y));
            }
            ENDHLSL
        }
    }
}
