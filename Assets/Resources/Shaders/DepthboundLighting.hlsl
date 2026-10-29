// Shared URP lighting for Depthbound's shaders: Lambert diffuse from the main light (with shadows)
// plus all additional lights, and a flat ambient color driven by the game (_DepthboundAmbient).
#ifndef DEPTHBOUND_LIGHTING_INCLUDED
#define DEPTHBOUND_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

half4 _DepthboundAmbient;

half3 DepthboundLambert(float3 positionWS, half3 normalWS, float4 positionCS, half3 albedo)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    half3 lighting = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation)
                   * saturate(dot(normalWS, mainLight.direction));

#if defined(_ADDITIONAL_LIGHTS)
    // InputData is required by the clustered (Forward+) light loop macros.
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    half4 shadowMask = half4(1, 1, 1, 1);
    uint lightsCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(lightsCount)
        Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
        lighting += light.color * (light.distanceAttenuation * light.shadowAttenuation)
                  * saturate(dot(normalWS, light.direction));
    LIGHT_LOOP_END
#endif

    return albedo * (lighting + _DepthboundAmbient.rgb);
}

// ---- Shadow caster / depth helpers ----
float3 _LightDirection;
float3 _LightPosition;

float4 DepthboundShadowPositionCS(float3 positionOS, float3 normalOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

#endif
