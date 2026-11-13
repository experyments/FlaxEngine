// Copyright (c) Wojciech Figat. All rights reserved.

#define NO_GBUFFER_SAMPLING

#include "./Flax/Common.hlsl"
#include "./Flax/GBufferCommon.hlsl"
#include "./Flax/GBuffer.hlsl"
#include "./Flax/Lighting/VolumetricFog.hlsl"
#include "./Flax/Lighting/ExponentialHeightFog.hlsl"
#include "./Flax/Lighting/PlanetClouds.hlsl"

// Disable Volumetric Fog if is not supported
#if VOLUMETRIC_FOG && !CAN_USE_COMPUTE_SHADER
#undef VOLUMETRIC_FOG
#define VOLUMETRIC_FOG 0
#endif

META_CB_BEGIN(0, Data)
GBufferData GBuffer;
ExponentialHeightFogData ExponentialHeightFog;
VolumetricFogData VolumetricFog;
float4 TemporalAAJitter;
META_CB_END

META_CB_BEGIN(1, PlanetCloudData)
GBufferData CloudGBuffer;
float4 CloudCameraFromCenter;
float4 CloudGeometry;
float4 CloudSettings;
float4 CloudSunDirection;
float4 CloudSunColor;
float4 CloudTint;
float4 CloudRenderSize;
float4 CloudQuality; // Raymarch steps, shadow sample frequency, night emission
META_CB_END

DECLARE_GBUFFERDATA_ACCESS(GBuffer)

Texture2D Depth : register(t0);
#if VOLUMETRIC_FOG
Texture3D VolumetricFogTexture : register(t1);
#endif
TextureCube AtmosphereSky : register(t2);
Texture2D PlanetCloudTexture : register(t3);

float3 GetAtmosphereFogDirection(float3 cameraToPosition, float4 cameraFromCenter, float radius)
{
	float3 up = dot(cameraFromCenter.xyz, cameraFromCenter.xyz) > 0.0001f ? normalize(cameraFromCenter.xyz) : float3(0, 1, 0);
	float3 direction = dot(cameraToPosition, cameraToPosition) > 0.0001f ? normalize(cameraToPosition) : up;
	float elevation = dot(direction, up);

	// Stay clear of the planet silhouette, including the linear-filter footprint
	// of the low-resolution sky capture. Use an angular margin so it also works
	// when the horizon is well below the local horizontal at high altitude.
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

float3 SampleAtmosphereFogColor(float3 direction, float4 cameraFromCenter)
{
	float3 up = dot(cameraFromCenter.xyz, cameraFromCenter.xyz) > 0.0001f ? normalize(cameraFromCenter.xyz) : float3(0, 1, 0);
	float elevation = dot(direction, up);
	float3 horizontal = direction - up * elevation;
	float3 side = cross(up, horizontal);
	// Rotate around local up so all taps keep the same safe elevation.
	float3 left = up * elevation + horizontal * 0.9982005f - side * 0.0599640f;
	float3 right = up * elevation + horizontal * 0.9982005f + side * 0.0599640f;
	return AtmosphereSky.SampleLevel(SamplerLinearClamp, direction, 0).rgb * 0.5f
		+ (AtmosphereSky.SampleLevel(SamplerLinearClamp, left, 0).rgb + AtmosphereSky.SampleLevel(SamplerLinearClamp, right, 0).rgb) * 0.25f;
}

META_PS(true, FEATURE_LEVEL_ES2)
META_PERMUTATION_1(VOLUMETRIC_FOG=0)
META_PERMUTATION_1(VOLUMETRIC_FOG=1)
float4 PS_Fog(Quad_VS2PS input) : SV_Target0
{
    // Get world space position at given pixel coordinate
	float rawDepth = SAMPLE_RT_DEPTH(Depth, input.TexCoord);
	GBufferData gBufferData = GetGBufferData();
	float3 viewPos = GetViewPos(gBufferData, input.TexCoord, rawDepth);
	float3 worldPos = mul(float4(viewPos, 1), gBufferData.InvViewMatrix).xyz;

    float skipDistance = 0;
#if VOLUMETRIC_FOG
	skipDistance = max(ExponentialHeightFog.VolumetricFogMaxDistance - 100, 0);
#endif

	// Match the radial fog to the planet atmosphere without changing its opacity
	// or directional-light inscattering.
	ExponentialHeightFogData fogData = ExponentialHeightFog;
	float influence = saturate(fogData.RadialParameters.z);
	float3 atmosphereColor = 1.0f;
	if (influence > 0.0f && fogData.RadialParameters.y > 0.0f)
	{
		float3 cameraToPosition = worldPos - GBuffer.ViewPos;
		float3 cameraFromCenter = fogData.RadialCameraPosition.xyz;
		float3 up = dot(cameraFromCenter, cameraFromCenter) > 0.0001f ? normalize(cameraFromCenter) : float3(0, 1, 0);
		float3 atmosphereDirection = cameraToPosition - 2.0f * min(dot(cameraToPosition, up), 0.0f) * up;
		float3 skyDirection = GetAtmosphereFogDirection(atmosphereDirection, fogData.RadialCameraPosition, fogData.RadialParameters.x);
		atmosphereColor = SampleAtmosphereFogColor(skyDirection, fogData.RadialCameraPosition);
		fogData.FogInscatteringColor = lerp(fogData.FogInscatteringColor, atmosphereColor, influence);
	}
	float4 fog = GetExponentialHeightFog(fogData, worldPos, GBuffer.ViewPos, skipDistance, viewPos.z);

#if VOLUMETRIC_FOG
    // Sample volumetric fog and mix it in
	float4 volumetricFog = SampleVolumetricFog(VolumetricFogTexture, VolumetricFog, worldPos - GBuffer.ViewPos, input.TexCoord, TemporalAAJitter);
	// Tint the accumulated volumetric lighting, retaining its light and shadow
	// structure and its transmittance rather than replacing it with sky color.
	volumetricFog.rgb *= lerp(float3(1, 1, 1), atmosphereColor, influence);
	fog = CombineVolumetricFog(fog, volumetricFog);
#endif

	return fog;
}

float2 CloudSphereIntersections(float3 camera, float altitude, float height, float3 direction)
{
	float b = dot(camera, direction);
	float c = (altitude - height) * (2.0f * CloudGeometry.x + altitude + height);
	float discriminant = b * b - c;
	if (discriminant < 0.0f)
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

float CloudDensityAt(float3 position, float altitude)
{
	return PlanetCloudDensity(position, altitude, CloudGeometry, CloudSettings.x, CloudSettings.y);
}

float4 RaymarchPlanetClouds(Quad_VS2PS input)
{
	if (CloudSettings.x <= 0.0f || CloudSettings.y <= 0.0f || CloudCameraFromCenter.w < 0.0f)
		return float4(0, 0, 0, 1);
	float rawDepth = SAMPLE_RT_DEPTH(Depth, input.TexCoord);
#if REVERSE_Z
	bool background = rawDepth <= 0.0f;
#else
	bool background = rawDepth >= 1.0f;
#endif
	float3 viewPos = GetViewPos(CloudGBuffer, input.TexCoord, background ? 0.5f : rawDepth);
	float3 worldPos = mul(float4(viewPos, 1), CloudGBuffer.InvViewMatrix).xyz;
	float3 ray = worldPos - CloudGBuffer.ViewPos;
	float3 direction = normalize(ray);
	float maxDistance = background ? 1e20f : length(ray) * 0.00001f;
	float3 camera = CloudCameraFromCenter.xyz;
	float altitude = CloudCameraFromCenter.w;
	float2 outer = CloudSphereIntersections(camera, altitude, CloudGeometry.y + CloudGeometry.z, direction);
	float start = max(outer.x, 0.0f);
	float end = min(outer.y, maxDistance);
	float2 ground = CloudSphereIntersections(camera, altitude, 0.0f, direction);
	if (ground.x > 0.00001f && ground.x <= ground.y)
		end = min(end, ground.x);
	else if (altitude <= 0.00001f && dot(camera, direction) < 0.0f)
		end = start;
	if (end <= start)
		return float4(0, 0, 0, 1);

	float2 inner = CloudSphereIntersections(camera, altitude, CloudGeometry.y, direction);
	float3 sun = normalize(CloudSunDirection.xyz);
	float cameraLength = length(camera);
	float phase = 0.6f + 0.4f * pow(saturate(dot(direction, sun)), 8.0f);
	int stepCount = (int)CloudQuality.x;
	int shadowFrequency = (int)CloudQuality.y;
	float3 light = 0.0f;
	float nightEmission = 0.0f;
	float transmittance = 1.0f;
	for (int segment = 0; segment < 2; segment++)
	{
		float segmentStart = start;
		float segmentEnd = end;
		if (inner.x <= inner.y)
		{
			if (segment == 0)
				segmentEnd = min(end, inner.x);
			else
				segmentStart = max(start, inner.y);
		}
		else if (segment == 1)
			break;
		if (segmentEnd <= segmentStart)
			continue;
		float stepLength = (segmentEnd - segmentStart) / stepCount;
		int shadowSamplesRemaining = 0;
		float sunVisibility = 1.0f;
		[loop]
		for (int i = 0; i < stepCount; i++)
		{
			float3 offset = direction * (segmentStart + (i + 0.5f) * stepLength);
			float sampleAltitude = CloudAltitude(camera, cameraLength, altitude, offset);
			float3 position = camera + offset;
			float density = CloudDensityAt(position, sampleAltitude);
			if (density <= 0.0f)
			{
				shadowSamplesRemaining = 0;
				continue;
			}
			float extinction = exp(-density * stepLength);
			float sunElevation = dot(normalize(position), sun);
			float daylight = smoothstep(-0.02f, 0.08f, sunElevation);
			float3 illumination = 0.04f;
			if (daylight > 0.0f)
			{
				if (shadowSamplesRemaining == 0)
				{
					float sunDistance = CloudGeometry.z * 0.35f / max(sunElevation, 0.2f);
					float shadowAltitude = CloudAltitude(camera, cameraLength, altitude, offset + sun * sunDistance);
					float shadowDensity = CloudDensityAt(position + sun * sunDistance, shadowAltitude);
					sunVisibility = exp(-shadowDensity * sunDistance);
					shadowSamplesRemaining = shadowFrequency;
				}
				shadowSamplesRemaining--;
				illumination += CloudSunColor.rgb * daylight * phase * sunVisibility;
			}
			else
				shadowSamplesRemaining = 0;
			float sampleWeight = transmittance * (1.0f - extinction);
			light += sampleWeight * CloudTint.rgb * illumination;
			nightEmission += sampleWeight * (1.0f - daylight);
			transmittance *= extinction;
			if (transmittance < 0.01f)
				break;
		}
		if (transmittance < 0.01f)
			break;
	}
	if (CloudSettings.z > 0.0f || CloudSettings.w > 0.0f)
	{
		// Reflect downward views above the local horizon so the sky lookup remains
		// continuous at nadir instead of stretching a horizon ring into radial stripes.
		float3 up = camera / max(cameraLength, 0.000001f);
		float3 atmosphereDirection = direction - 2.0f * min(dot(direction, up), 0.0f) * up;
		float3 skyDirection = GetAtmosphereFogDirection(atmosphereDirection, CloudCameraFromCenter, CloudGeometry.x);
		float3 skyColor = max(SampleAtmosphereFogColor(skyDirection, CloudCameraFromCenter), 0.0f);
		float peak = max(skyColor.r, max(skyColor.g, skyColor.b));
		float3 skyTint = peak > 0.0001f ? skyColor / peak : float3(1, 1, 1);
		light += (1.0f - transmittance) * CloudTint.rgb * peak * CloudSettings.w;
		light *= lerp(float3(1, 1, 1), skyTint, saturate(CloudSettings.z));
	}
	light += nightEmission * CloudQuality.z * CloudTint.rgb;
	return float4(light * CloudTint.w, transmittance);
}

META_PS(true, FEATURE_LEVEL_ES2)
float4 PS_PlanetClouds(Quad_VS2PS input) : SV_Target0
{
	return RaymarchPlanetClouds(input);
}

float CloudDepthMatch(float referenceDepth, float sampleDepth)
{
#if REVERSE_Z
	bool referenceBackground = referenceDepth <= 0.0f;
	bool sampleBackground = sampleDepth <= 0.0f;
#else
	bool referenceBackground = referenceDepth >= 1.0f;
	bool sampleBackground = sampleDepth >= 1.0f;
#endif
	if (referenceBackground || sampleBackground)
		return referenceBackground && sampleBackground ? 1.0f : 0.0f;
	float referenceLinear = abs(LinearizeZ(CloudGBuffer, referenceDepth));
	float sampleLinear = abs(LinearizeZ(CloudGBuffer, sampleDepth));
	return abs(sampleLinear - referenceLinear) <= max(referenceLinear * 0.02f, 1.0f) ? 1.0f : 0.0f;
}

META_PS(true, FEATURE_LEVEL_ES2)
float4 PS_PlanetCloudsComposite(Quad_VS2PS input) : SV_Target0
{
	float2 size = CloudRenderSize.xy;
	float2 texel = CloudRenderSize.zw;
	float2 position = input.TexCoord * size - 0.5f;
	float2 base = floor(position);
	float2 fraction = position - base;
	float2 minimumUV = 0.5f * texel;
	float2 maximumUV = 1.0f - minimumUV;
	float2 uv00 = clamp((base + float2(0.5f, 0.5f)) * texel, minimumUV, maximumUV);
	float2 uv10 = clamp((base + float2(1.5f, 0.5f)) * texel, minimumUV, maximumUV);
	float2 uv01 = clamp((base + float2(0.5f, 1.5f)) * texel, minimumUV, maximumUV);
	float2 uv11 = clamp((base + float2(1.5f, 1.5f)) * texel, minimumUV, maximumUV);
	float depth = SAMPLE_RT_DEPTH(Depth, input.TexCoord);
	float4 weights = float4((1.0f - fraction.x) * (1.0f - fraction.y), fraction.x * (1.0f - fraction.y),
		(1.0f - fraction.x) * fraction.y, fraction.x * fraction.y);
	weights *= float4(CloudDepthMatch(depth, SAMPLE_RT_DEPTH(Depth, uv00)), CloudDepthMatch(depth, SAMPLE_RT_DEPTH(Depth, uv10)),
		CloudDepthMatch(depth, SAMPLE_RT_DEPTH(Depth, uv01)), CloudDepthMatch(depth, SAMPLE_RT_DEPTH(Depth, uv11)));
	float weightSum = dot(weights, float4(1, 1, 1, 1));
	[branch]
	if (weightSum < 0.0001f)
		return RaymarchPlanetClouds(input);
	return (SAMPLE_RT(PlanetCloudTexture, uv00) * weights.x + SAMPLE_RT(PlanetCloudTexture, uv10) * weights.y
		+ SAMPLE_RT(PlanetCloudTexture, uv01) * weights.z + SAMPLE_RT(PlanetCloudTexture, uv11) * weights.w) / weightSum;
}
