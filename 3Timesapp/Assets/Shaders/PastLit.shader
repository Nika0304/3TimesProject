// Clădirile din Trecut (Opera din 1900, palatele, Catedrala): lumină simplă (soarele + lumina ambientală, un luciu
// mic pentru țigla smălțuită) și „developarea”: cu _Reveal de la 0 la 1, clădirea apare din puncte de granulație care
// se înmulțesc, întâi jos, apoi tot mai sus, cu o margine aurie acolo unde se formează, ca o fotografie veche care
// apare pe hârtie. La _Reveal = 1 se vede întreagă.
Shader "ThreeTimes/PastLit"
{
    Properties
    {
        _BaseColor ("Culoare", Color) = (1, 1, 1, 1)
        _BaseMap ("Textură", 2D) = "white" {}
        _Smoothness ("Luciu", Range(0, 1)) = 0.1
        _Reveal ("Developare (0..1)", Range(0, 1)) = 1
        _RevealHeight ("Înălțimea pentru developare (m, în obiect)", Float) = 30
        _EdgeColor ("Marginea care se developează", Color) = (1, 0.82, 0.45, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Fețe", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "PastLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
                float _Reveal;
                float _RevealHeight;
                half4 _EdgeColor;
                float _Cull;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float heightOS : TEXCOORD3;
            };

            Varyings vert (Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.heightOS = input.positionOS.y;
                return o;
            }

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 frag (Varyings i) : SV_Target
            {
                half emission = 0;
                if (_Reveal < 0.999)
                {
                    // Pete mari (forma developării) + granulație fină care pâlpâie ca filmul (18 cadre/s).
                    float frame = floor(_Time.y * 18.0);
                    float blot = Noise3(i.positionWS * 0.45);
                    float grain = Hash3(floor(i.positionWS * 9.0) + frame);
                    float h = saturate(i.heightOS / max(1.0, _RevealHeight));
                    float v = blot * 0.45 + h * 0.4 + grain * 0.15;
                    float t = _Reveal * 1.2 - 0.1;
                    clip(t - v);
                    emission = 1.0 - saturate((t - v) / 0.07);
                }

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 n = normalize(i.normalWS);
                Light sun = GetMainLight();
                half ndl = saturate(dot(n, sun.direction));
                half3 ambient = SampleSH(n);
                half3 color = albedo * (ambient + sun.color * ndl);
                if (_Smoothness > 0.01)
                {
                    float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                    float3 hv = normalize(sun.direction + view);
                    half spec = pow(saturate(dot(n, hv)), lerp(8.0, 96.0, _Smoothness)) * _Smoothness;
                    color += sun.color * spec * 0.6;
                }
                color = lerp(color, _EdgeColor.rgb * 1.6, emission);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
