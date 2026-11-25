// Pavajul de piatră din fața palatelor, în Trecut: o fâșie de pietre cubice care se estompează spre piață, ca să
// nu aibă o margine tăiată. v = 0 lângă zid, v = 1 la marginea dinspre piață.
Shader "ThreeTimes/PastPaving"
{
    Properties
    {
        _BaseMap ("Pietre", 2D) = "white" {}
        _Tint ("Culoare", Color) = (0.95, 0.92, 0.86, 1)
        _Alpha ("Acoperire", Range(0, 1)) = 0.8
        _VScale ("Repetări pe lățime", Float) = 2.7
        _Reveal ("Developare (0..1)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "PastPaving"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Alpha;
                float _VScale;
                float _Reveal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half3 stone = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2(i.uv.x, i.uv.y * _VScale)).rgb;
                half fade = smoothstep(0.0, 0.06, i.uv.y) * (1.0 - smoothstep(0.55, 1.0, i.uv.y));
                return half4(stone * _Tint.rgb, _Alpha * fade * _Reveal);
            }
            ENDHLSL
        }
    }
}
