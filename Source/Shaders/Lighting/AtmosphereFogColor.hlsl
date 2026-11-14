// Copyright (c) Wojciech Figat. All rights reserved.

#ifndef __ATMOSPHERE_FOG_COLOR__
#define __ATMOSPHERE_FOG_COLOR__

#include "./Flax/Lighting/ExponentialHeightFog.hlsl"

float3 GetAtmosphereFogDirection(float3 cameraToPosition, float4 cameraFromCenter, float radius)
{
    float3 up = dot(cameraFromCenter.xyz, cameraFromCenter.xyz) > 0.0001f ? normalize(cameraFromCenter.xyz) : float3(0, 1, 0);
    float3 direction = dot(cameraToPosition, cameraToPosition) > 0.0001f ? normalize(cameraToPosition) : up;
    float elevation = dot(direction, up);

    // Stay clear of the planet silhouette, including the linear-filter footprint
    // of the low-resolution sky capture, even at high altitude.
    float altitude = max(cameraFromCenter.w, 0.0f);
    float distanceFromCenter = max(radius + altitude, 0.0001f);
    float horizon = -sqrt(saturate(altitude * (2.0f * radius + altitude) / (distanceFromCenter * distanceFromCenter)));
    float minimumElevation = horizon * 0.9800666f + sqrt(saturate(1.0f - horizon * horizon)) * 0.1986693f; // 0.2 radians above the horizon
    if (elevation < minimumElevation)
    {
        float3 horizontal = direction - up * elevation;
        if (dot(horizontal, horizontal) < 0.0001f)
            horizontal = cross(up, abs(up.y) < 0.9f ? float3(0, 1, 0) : float3(1, 0, 0));
        direction = normalize(horizontal) * sqrt(saturate(1.0f - minimumElevation * minimumElevation)) + up * minimumElevation;
    }
    return direction;
}

float3 SampleAtmosphereFogColor(TextureCube atmosphereSky, float3 direction, float4 cameraFromCenter)
{
    float3 up = dot(cameraFromCenter.xyz, cameraFromCenter.xyz) > 0.0001f ? normalize(cameraFromCenter.xyz) : float3(0, 1, 0);
    float elevation = dot(direction, up);
    float3 horizontal = direction - up * elevation;
    float3 side = cross(up, horizontal);
    // Rotate around local up so all taps keep the same safe elevation.
    float3 left = up * elevation + horizontal * 0.9982005f - side * 0.0599640f;
    float3 right = up * elevation + horizontal * 0.9982005f + side * 0.0599640f;
    return atmosphereSky.SampleLevel(SamplerLinearClamp, direction, 0).rgb * 0.5f
        + (atmosphereSky.SampleLevel(SamplerLinearClamp, left, 0).rgb + atmosphereSky.SampleLevel(SamplerLinearClamp, right, 0).rgb) * 0.25f;
}

// Tint radial fog without changing opacity or directional-light inscattering.
float3 ApplyAtmosphereFogColor(TextureCube atmosphereSky, inout ExponentialHeightFogData fog, float3 cameraToPosition)
{
    float influence = saturate(fog.RadialParameters.z);
    float3 atmosphereColor = 1.0f;
    if (influence > 0.0f && fog.RadialParameters.y > 0.0f)
    {
        float3 cameraFromCenter = fog.RadialCameraPosition.xyz;
        float3 up = dot(cameraFromCenter, cameraFromCenter) > 0.0001f ? normalize(cameraFromCenter) : float3(0, 1, 0);
        float3 atmosphereDirection = cameraToPosition - 2.0f * min(dot(cameraToPosition, up), 0.0f) * up;
        float3 skyDirection = GetAtmosphereFogDirection(atmosphereDirection, fog.RadialCameraPosition, fog.RadialParameters.x);
        atmosphereColor = SampleAtmosphereFogColor(atmosphereSky, skyDirection, fog.RadialCameraPosition);
        fog.FogInscatteringColor = lerp(fog.FogInscatteringColor, atmosphereColor, influence);
    }
    return atmosphereColor;
}

#endif
