// Copyright (c) Wojciech Figat. All rights reserved.

#include "./Flax/Common.hlsl"
#include "./Flax/MaterialCommon.hlsl"
#include "./Flax/GBuffer.hlsl"
#include "./Flax/Math/Noise.hlsl"
#include "./Flax/Lighting/AtmosphereFog.hlsl"

META_CB_BEGIN(0, Data)
float4x4 WorldViewProjection;
float4x4 InvViewProjection;
float3 ViewOffset;
float NoiseScale;
GBufferData GBuffer;
AtmosphericFogData AtmosphericFog;
META_CB_END

META_CB_BEGIN(1, PlanetSkyData)
float4 PlanetCameraFromCenter;
float4 PlanetGeometry;
float4 PlanetScattering;
float4 PlanetSunDirection;
float4 PlanetSunColor;
float4 PlanetTint;
float4 PlanetDayOffset;
float4 PlanetDuskOffset;
float4 PlanetNightOffset;
float4 PlanetSkyboxColor;
float4 PlanetSkyboxRotation;
float4 PlanetSunDisk;
META_CB_END

DECLARE_GBUFFERDATA_ACCESS(GBuffer)

TextureCube PlanetSkybox : register(t7);
Texture2D PlanetSunDiskTexture : register(t8);

struct MaterialInput
{
	float4 Position : SV_Position;
	float4 ScreenPos : TEXCOORD0;
};

// Vertex Shader function for GBuffer Pass
META_VS(true, FEATURE_LEVEL_ES2)
META_VS_IN_ELEMENT(POSITION, 0, R32G32B32_FLOAT, 0, 0, PER_VERTEX, 0, true)
MaterialInput VS(ModelInput_PosOnly input)
{
	MaterialInput output;

	// Compute vertex position
	output.Position = PROJECT_POINT(float4(input.Position.xyz, 1), WorldViewProjection);
	output.ScreenPos = output.Position;

	return output;
}

float PlanetAltitude(float3 fromCenter, float altitude, float3 offset)
{
	float3 end = fromCenter + offset;
	return altitude + dot(offset, fromCenter + end) / max(length(fromCenter) + length(end), 0.000001f);
}

float2 PlanetSphereIntersections(float3 fromCenter, float altitude, float radius, float height, float3 direction)
{
	float b = dot(fromCenter, direction);
	float c = (altitude - height) * (2.0f * radius + altitude + height);
	float discriminant = b * b - c;
	if (discriminant < 0.0f)
		// A miss is represented by an empty interval (near > far).
		return float2(1.0f, -1.0f);
	float root = sqrt(discriminant);
	float nearRoot = -b - root;
	float farRoot = -b + root;
	if (abs(farRoot) > abs(nearRoot) && abs(farRoot) > 0.000001f)
		nearRoot = c / farRoot;
	else if (abs(nearRoot) > 0.000001f)
		farRoot = c / nearRoot;
	return float2(nearRoot, farRoot);
}

float PlanetSunOpticalDepth(float3 fromCenter, float altitude, float3 sunDirection)
{
	float2 ground = PlanetSphereIntersections(fromCenter, altitude, PlanetGeometry.x, 0.0f, sunDirection);
	if (ground.x > 0.00001f && ground.y > ground.x)
		return -1.0f;
	float2 atmosphere = PlanetSphereIntersections(fromCenter, altitude, PlanetGeometry.x, PlanetGeometry.y, sunDirection);
	float lengthToSpace = max(atmosphere.y, 0.0f);
	float stepLength = lengthToSpace / 8.0f;
	float depth = 0.0f;
	for (int i = 0; i < 8; i++)
	{
		float sampleAltitude = PlanetAltitude(fromCenter, altitude, sunDirection * ((i + 0.5f) * stepLength));
		depth += exp(-max(sampleAltitude, 0.0f) / PlanetGeometry.z) * stepLength;
	}
	return depth;
}

META_PS(true, FEATURE_LEVEL_ES2)
GBufferOutput PS_PlanetSky(MaterialInput input)
{
	GBufferOutput output;
	float4 clipPos = float4(input.ScreenPos.xy / input.ScreenPos.w, 1.0f, 1.0f);
	clipPos = PROJECT_POINT(clipPos, InvViewProjection);
	float3 direction = normalize(clipPos.xyz / clipPos.w - GBuffer.ViewPos);
	float3 camera = PlanetCameraFromCenter.xyz;
	float cameraAltitude = PlanetCameraFromCenter.w;
	float3 sunDirection = normalize(PlanetSunDirection.xyz);
	float2 atmosphere = PlanetSphereIntersections(camera, cameraAltitude, PlanetGeometry.x, PlanetGeometry.y, direction);
	float start = max(atmosphere.x, 0.0f);
	float end = atmosphere.y;
	float2 ground = PlanetSphereIntersections(camera, cameraAltitude, PlanetGeometry.x, 0.0f, direction);
	bool groundHit = cameraAltitude < 0.0f;
	if (ground.x <= ground.y)
	{
		if (ground.x > 0.00001f)
		{
			end = min(end, ground.x);
			groundHit = true;
		}
		else if (cameraAltitude <= 0.00001f && ground.y > 0.0f && dot(camera, direction) < 0.0f)
		{
			end = start;
			groundHit = true;
		}
	}

	float3 color = 0.0f;
	float3 transmittance = 1.0f;
	if (end > start && cameraAltitude >= 0.0f)
	{
		float stepLength = (end - start) / 16.0f;
		float viewDepth = 0.0f;
		float3 scattered = 0.0f;
		float3 colorOffset = 0.0f;
		float3 beta = max(PlanetScattering.xyz, 0.0f);
		for (int i = 0; i < 16; i++)
		{
			float3 offset = direction * (start + (i + 0.5f) * stepLength);
			float3 samplePosition = camera + offset;
			float sampleAltitude = PlanetAltitude(camera, cameraAltitude, offset);
			float density = exp(-max(sampleAltitude, 0.0f) / PlanetGeometry.z);
			float sampleDepth = density * stepLength;
			float sunDepth = PlanetSunOpticalDepth(samplePosition, sampleAltitude, sunDirection);
			if (sunDepth >= 0.0f)
				scattered += exp(-beta * (viewDepth + sampleDepth * 0.5f + sunDepth)) * sampleDepth;
			viewDepth += sampleDepth;

			float elevation = dot(normalize(samplePosition), sunDirection);
			float dusk = 1.0f - smoothstep(0.0f, PlanetScattering.w, abs(elevation));
			float day = (1.0f - dusk) * step(0.0f, elevation);
			float night = (1.0f - dusk) * step(elevation, 0.0f);
			colorOffset += (PlanetDayOffset.rgb * day + PlanetDuskOffset.rgb * dusk + PlanetNightOffset.rgb * night) * sampleDepth / PlanetGeometry.z;
		}
		float mu = dot(direction, sunDirection);
		float phase = (3.0f / (16.0f * PI)) * (1.0f + mu * mu);
		color = scattered * beta * phase * PlanetGeometry.w * PlanetSunColor.rgb * PlanetTint.rgb + colorOffset;
		transmittance = exp(-beta * viewDepth);
	}
	if (PlanetSkyboxColor.w > 0.0f && !groundHit)
	{
		float c = PlanetSkyboxRotation.x;
		float s = PlanetSkyboxRotation.y;
		float3 skyDirection = float3(c * direction.x - s * direction.z, direction.y, s * direction.x + c * direction.z);
		color += PlanetSkybox.SampleLevel(SamplerLinearClamp, skyDirection, 0).rgb * PlanetSkyboxColor.rgb * transmittance;
	}
	float3 referenceUp = abs(sunDirection.y) > 0.99f ? float3(1, 0, 0) : float3(0, 1, 0);
	float3 sunRight = normalize(cross(referenceUp, sunDirection));
	float3 sunUp = cross(sunDirection, sunRight);
	float2 diskPosition = float2(dot(direction, sunRight), dot(direction, sunUp));
	float diskDistance = length(diskPosition);
	float edgeWidth = max(fwidth(diskDistance), 0.0000001f);
	if (PlanetSunDisk.x > 0.0f && PlanetSunDisk.y > 0.0f && !groundHit && dot(direction, sunDirection) > 0.0f)
	{
		float diskMask = 1.0f - smoothstep(PlanetSunDisk.x - edgeWidth, PlanetSunDisk.x + edgeWidth, diskDistance);
		float3 diskColor = PlanetSunColor.rgb * PlanetSunDisk.y;
		if (PlanetSunDisk.w > 0.0f)
		{
			float2 diskUV = float2(diskPosition.x, -diskPosition.y) / PlanetSunDisk.x * 0.5f + 0.5f;
			float4 materialColor = PlanetSunDiskTexture.SampleLevel(SamplerLinearClamp, diskUV, 0);
			diskColor *= materialColor.rgb * materialColor.a;
		}
		color += diskColor * diskMask * transmittance;
	}

	output.Light = float4(max(color, 0.0f), 1.0f);
	output.RT0 = float4(0, 0, 0, 0);
	output.RT1 = float4(1, 0, 0, SHADING_MODEL_UNLIT);
	output.RT2 = float4(0, 0, 0, 0);
	output.RT3 = float4(0, 0, 0, 0);
	return output;
}

// Pixel Shader function for GBuffer Pass
META_PS(true, FEATURE_LEVEL_ES2)
GBufferOutput PS_Sky(MaterialInput input)
{
	GBufferOutput output;

    // Calculate view vector (unproject at the far plane)
	GBufferData gBufferData = GetGBufferData();
	float4 clipPos = float4(input.ScreenPos.xy / input.ScreenPos.w, 1.0, 1.0);
	clipPos = PROJECT_POINT(clipPos, InvViewProjection);
	float3 worldPos = clipPos.xyz / clipPos.w;
    float3 viewVector = normalize(worldPos - gBufferData.ViewPos);

	// Sample atmosphere color
    float4 color = GetAtmosphericFog(AtmosphericFog, gBufferData.ViewFar, gBufferData.ViewPos + ViewOffset, viewVector, gBufferData.ViewFar, float3(0, 0, 0));

    // Apply dithering to hide banding artifacts
    float2 uv = ProjectClipToUV(input.ScreenPos.xy / input.ScreenPos.w);
    float luminance = Luminance(saturate(color.rgb));
    color.rgb += rand2dTo1d(uv) * luminance * NoiseScale;

	// Pack GBuffer
	output.Light = color;
	output.RT0 = float4(0, 0, 0, 0);
	output.RT1 = float4(1, 0, 0, SHADING_MODEL_UNLIT);
	output.RT2 = float4(0, 0, 0, 0);
	output.RT3 = float4(0, 0, 0, 0);

	return output;
}
