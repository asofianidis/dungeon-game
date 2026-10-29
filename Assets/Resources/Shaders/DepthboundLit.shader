// URP shader for characters and props. Characters use cel shading (_Toon), a rim light, and an
// inverted-hull outline (_OutlineWidth, in pixels). _Tint/_Flash are set per renderer via
// MaterialPropertyBlock for fog-of-war dimming, hit flashes and wind-up glow.
Shader "Depthbound/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _DetailTex ("Detail (0.5 = neutral)", 2D) = "gray" {}
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0
        _Emission ("Emission", Color) = (0,0,0,0)
        _Toon ("Toon", Range(0, 1)) = 0
        _RimColor ("Rim Color", Color) = (0,0,0,0)
        _OutlineWidth ("Outline Width (px)", Float) = 0
        _OutlineColor ("Outline Color", Color) = (0.05,0.03,0.06,1)
        _Tint ("Tint", Color) = (1,1,1,1)
        _Flash ("Flash", Color) = (0,0,0,0)
    }

    HLSLINCLUDE
    #include "DepthboundLighting.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_DetailTex);
    SAMPLER(sampler_DetailTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _DetailTex_ST;
        half _DetailStrength;
        half4 _Color;
        half4 _Emission;
        half _Toon;
        half4 _RimColor;
        float _OutlineWidth;
        half4 _OutlineColor;
        half4 _Tint;
        half4 _Flash;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
    };
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float2 detailUV : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.detailUV = TRANSFORM_TEX(v.uv, _DetailTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                half detail = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.detailUV).r * 2.0;
                c.rgb *= lerp(1.0, detail, _DetailStrength);
                half3 albedo = c.rgb * _Tint.rgb;
                half3 n = normalize(i.normalWS);
                half3 color = DepthboundLighting(i.positionWS, n, i.positionCS, albedo, _Toon);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half rim = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                color += _RimColor.rgb * rim * _Tint.rgb;
                color += _Emission.rgb * _Tint.rgb + _Flash.rgb;
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Inverted-hull outline, drawn by URP's default unlit pass alongside UniversalForward.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(Attributes v) : SV_POSITION
            {
                float4 positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                float len = max(length(normalCS), 1e-5);
                positionCS.xy += (normalCS / len) * (_OutlineWidth * 2.0 / _ScreenParams.xy) * positionCS.w;
                return positionCS;
            }

            half4 frag() : SV_Target
            {
                clip(_OutlineWidth - 0.001);
                return half4(_OutlineColor.rgb * _Tint.rgb + _Flash.rgb * 0.5, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float4 vert(Attributes v) : SV_POSITION { return DepthboundShadowPositionCS(v.positionOS.xyz, v.normalOS); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
