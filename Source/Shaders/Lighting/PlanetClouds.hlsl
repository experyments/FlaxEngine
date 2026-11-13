// Copyright (c) Wojciech Figat. All rights reserved.

#ifndef __PLANET_CLOUDS__
#define __PLANET_CLOUDS__

float CloudHash(float3 p)
{
    p = frac(p * 0.1031f);
    p += dot(p, p.yzx + 33.33f);
    return frac((p.x + p.y) * p.z);
}

float CloudNoise(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0f - 2.0f * f);
    float a = lerp(CloudHash(cell), CloudHash(cell + float3(1, 0, 0)), f.x);
    float b = lerp(CloudHash(cell + float3(0, 1, 0)), CloudHash(cell + float3(1, 1, 0)), f.x);
    float c = lerp(CloudHash(cell + float3(0, 0, 1)), CloudHash(cell + float3(1, 0, 1)), f.x);
    float d = lerp(CloudHash(cell + float3(0, 1, 1)), CloudHash(cell + float3(1, 1, 1)), f.x);
    return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
}

float CloudAltitude(float3 camera, float cameraLength, float altitude, float3 offset)
{
    float3 end = camera + offset;
    return altitude + dot(offset, camera + end) / max(cameraLength + length(end), 0.000001f);
}

float PlanetCloudDensity(float3 position, float altitude, float4 geometry, float coverage, float density)
{
    float h = (altitude - geometry.y) / geometry.z;
    if (h <= 0.0f || h >= 1.0f)
        return 0.0f;
    float layer = smoothstep(0.0f, 0.15f, h) * (1.0f - smoothstep(0.7f, 1.0f, h));
    float3 p = position / geometry.w;
    float shape = CloudNoise(p) * 0.75f + CloudNoise(p * 2.03f + 17.0f) * 0.25f;
    return saturate((shape - (1.0f - coverage)) * 4.0f) * layer * density;
}

#endif
