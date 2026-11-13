// Copyright (c) Wojciech Figat. All rights reserved.

#ifndef __EXPONENTIAL_HEIGHT_FOG__
#define __EXPONENTIAL_HEIGHT_FOG__

#include "./Flax/Common.hlsl"
#include "./Flax/Math/Math.hlsl"

// Structure that contains information about exponential height fog
struct ExponentialHeightFogData
{
    float3 FogInscatteringColor;
    float FogMinOpacity;

    float FogDensity;
    float FogHeight;
    float FogHeightFalloff;
    float FogAtViewPosition;

    float3 InscatteringLightDirection;
    float ApplyDirectionalInscattering;

    float3 DirectionalInscatteringColor;
    float DirectionalInscatteringExponent;

    float FogCutoffDistance;
    float VolumetricFogMaxDistance;
    float DirectionalInscatteringStartDistance;
    float StartDistance;

    float4 RadialCameraPosition;
    float4 RadialParameters;
};

// Compute altitude relative to the camera without subtracting two planet-sized floats.
float GetRadialFogAltitude(float4 cameraFromCenter, float3 cameraToPosition)
{
    float3 endFromCenter = cameraFromCenter.xyz + cameraToPosition;
    float denominator = length(endFromCenter) + length(cameraFromCenter.xyz);
    return cameraFromCenter.w + dot(cameraToPosition, cameraFromCenter.xyz + endFromCenter) / max(denominator, 0.0001f);
}

float GetRadialFogSegmentIntegral(float density, float falloff, float altitude0, float altitude1, float segmentLength)
{
    float exponent0 = clamp(-falloff * altitude0, -80.0f, 80.0f);
    float exponent1 = clamp(-falloff * altitude1, -80.0f, 80.0f);
    float delta = exponent0 - exponent1;
    float average = abs(delta) > 0.01f ? (exp2(exponent0) - exp2(exponent1)) / delta : exp2((exponent0 + exponent1) * 0.5f) * log(2.0f);
    return density * segmentLength * average;
}

float4 GetRadialExponentialHeightFog(ExponentialHeightFogData fog, float3 cameraToPos, float skipDistance, float sceneDistance)
{
    float rayLength = length(cameraToPos);
    if (rayLength <= 0.0001f || (fog.FogCutoffDistance > 0 && sceneDistance > fog.FogCutoffDistance))
        return float4(0, 0, 0, 1);

    float3 direction = cameraToPos / rayLength;
    float start = min(max(skipDistance, fog.StartDistance), rayLength);
    float end = rayLength;

    // Clip away the region where density is less than 2^-32 of its surface value.
    float shellHeight = 32.0f / max(fog.FogHeightFalloff, 0.0000001f);
    float altitude = fog.RadialCameraPosition.w;
    float b = dot(fog.RadialCameraPosition.xyz, direction);
    float c = (altitude - shellHeight) * (2.0f * fog.RadialParameters.x + altitude + shellHeight);
    float discriminant = b * b - c;
    if (discriminant <= 0)
        return float4(0, 0, 0, 1);
    float root = sqrt(discriminant);
    float farRoot = -b + root;
    float nearRoot = -b - root;
    if (abs(farRoot) > abs(nearRoot) && abs(farRoot) > 0.0001f)
        nearRoot = c / farRoot;
    else if (abs(nearRoot) > 0.0001f)
        farRoot = c / nearRoot;
    start = max(start, nearRoot);
    end = min(end, farRoot);
    if (end <= start)
        return float4(0, 0, 0, 1);

    const int steps = 32;
    float stepLength = (end - start) / steps;
    float integral = 0;
    float directionalIntegral = 0;
    float previousAltitude = GetRadialFogAltitude(fog.RadialCameraPosition, direction * start);
    for (int i = 0; i < steps; i++)
    {
        float segmentStart = start + i * stepLength;
        float nextAltitude = GetRadialFogAltitude(fog.RadialCameraPosition, direction * (segmentStart + stepLength));
        integral += GetRadialFogSegmentIntegral(fog.FogDensity, fog.FogHeightFalloff, previousAltitude, nextAltitude, stepLength);
        float directionalStart = max(segmentStart, max(skipDistance, fog.StartDistance) + fog.DirectionalInscatteringStartDistance);
        if (directionalStart < segmentStart + stepLength)
        {
            float fraction = (directionalStart - segmentStart) / stepLength;
            float directionalAltitude = lerp(previousAltitude, nextAltitude, fraction);
            directionalIntegral += GetRadialFogSegmentIntegral(fog.FogDensity, fog.FogHeightFalloff, directionalAltitude, nextAltitude, segmentStart + stepLength - directionalStart);
        }
        previousAltitude = nextAltitude;
    }
    float transmittance = max(saturate(exp2(-integral)), fog.FogMinOpacity);
    float3 directional = fog.DirectionalInscatteringColor * pow(saturate(dot(direction, fog.InscatteringLightDirection)), fog.DirectionalInscatteringExponent);
    directional *= fog.ApplyDirectionalInscattering * (1.0f - saturate(exp2(-directionalIntegral)));
    return float4(fog.FogInscatteringColor * (1.0f - transmittance) + directional, transmittance);
}

float4 GetExponentialHeightFog(ExponentialHeightFogData exponentialHeightFog, float3 posWS, float3 camWS, float skipDistance, float sceneDistance)
{
    float3 cameraToPos = posWS - camWS;
    if (exponentialHeightFog.RadialParameters.y > 0)
        return GetRadialExponentialHeightFog(exponentialHeightFog, cameraToPos, skipDistance, sceneDistance);
    float cameraToPosSqr = dot(cameraToPos, cameraToPos);
    float cameraToPosLenInv = rsqrt(cameraToPosSqr);
    float cameraToPosLen = cameraToPosSqr * cameraToPosLenInv;
    float3 cameraToReceiverNorm = cameraToPos * cameraToPosLenInv;

    float rayOriginTerms = exponentialHeightFog.FogAtViewPosition;
    float rayLength = cameraToPosLen;
    float rayDirectionY = cameraToPos.y;

    // Apply start distance offset
    skipDistance = max(skipDistance, exponentialHeightFog.StartDistance);
    if (skipDistance > 0)
    {
        float excludeIntersectionTime = skipDistance * cameraToPosLenInv;
        float cameraToExclusionIntersectionY = excludeIntersectionTime * cameraToPos.y;
        float exclusionIntersectionY = camWS.y + cameraToExclusionIntersectionY;
        rayLength = (1.0f - excludeIntersectionTime) * cameraToPosLen;
        rayDirectionY = cameraToPos.y - cameraToExclusionIntersectionY;
        float exponent = exponentialHeightFog.FogHeightFalloff * (exclusionIntersectionY - exponentialHeightFog.FogHeight);
        rayOriginTerms = exponentialHeightFog.FogDensity * exp2(-exponent);
    }

    // Calculate the integral of the ray starting from the view to the object position with the fog density function
    float falloff = max(-127.0f, exponentialHeightFog.FogHeightFalloff * rayDirectionY);
    float lineIntegral = (1.0f - exp2(-falloff)) / falloff;
    float lineIntegralTaylor = log(2.0f) - (0.5f * Pow2(log(2.0f))) * falloff;
    float exponentialHeightLineIntegralCalc = rayOriginTerms * (abs(falloff) > 0.01f ? lineIntegral : lineIntegralTaylor);
    float exponentialHeightLineIntegral = exponentialHeightLineIntegralCalc * rayLength;

    // Calculate the light that went through the fog
    float expFogFactor = max(saturate(exp2(-exponentialHeightLineIntegral)), exponentialHeightFog.FogMinOpacity);

    // Calculate the directional light inscattering
    float3 inscatteringColor = exponentialHeightFog.FogInscatteringColor;
    float3 directionalInscattering = 0;
    BRANCH

    if (exponentialHeightFog.ApplyDirectionalInscattering > 0)
    {
        float3 directionalLightInscattering = exponentialHeightFog.DirectionalInscatteringColor * pow(saturate(dot(cameraToReceiverNorm, exponentialHeightFog.InscatteringLightDirection)), exponentialHeightFog.DirectionalInscatteringExponent);
        float dirExponentialHeightLineIntegral = exponentialHeightLineIntegralCalc * max(rayLength - exponentialHeightFog.DirectionalInscatteringStartDistance, 0.0f);
        float directionalInscatteringFogFactor = saturate(exp2(-dirExponentialHeightLineIntegral));
        directionalInscattering = directionalLightInscattering * (1.0f - directionalInscatteringFogFactor);
    }

    // Disable fog after a certain distance
    FLATTEN

    if (exponentialHeightFog.FogCutoffDistance > 0 && sceneDistance > exponentialHeightFog.FogCutoffDistance)
    {
        expFogFactor = 1;
        directionalInscattering = 0;
    }

    return float4(inscatteringColor * (1.0f - expFogFactor) + directionalInscattering, expFogFactor);
}

float4 GetExponentialHeightFog(ExponentialHeightFogData exponentialHeightFog, float3 posWS, float3 camWS, float skipDistance)
{
    return GetExponentialHeightFog(exponentialHeightFog, posWS, camWS, skipDistance, distance(posWS, camWS));
}

#endif
