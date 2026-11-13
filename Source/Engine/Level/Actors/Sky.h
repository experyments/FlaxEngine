// Copyright (c) Wojciech Figat. All rights reserved.

#pragma once

#include "../Actor.h"
#include "Engine/Content/AssetReference.h"
#include "Engine/Content/Assets/Shader.h"
#include "Engine/Content/Assets/CubeTexture.h"
#include "Engine/Content/Assets/MaterialBase.h"
#include "Engine/Scripting/ScriptingObjectReference.h"
#include "Engine/Renderer/Config.h"
#include "Engine/Renderer/DrawCall.h"

class GPUPipelineState;

/// <summary>
/// Sky actor renders atmosphere around the scene with fog and sky.
/// </summary>
API_CLASS(Attributes="ActorContextMenu(\"New/Visuals/Sky/Sky\"), ActorToolbox(\"Visuals\")")
class FLAXENGINE_API Sky : public Actor, public IAtmosphericFogRenderer, public ISkyRenderer
{
    DECLARE_SCENE_OBJECT(Sky);
private:
    AssetReference<Shader> _shader;
    GPUPipelineState* _psSky = nullptr;
    int32 _sceneRenderingKey = -1;

public:
    ~Sky();

public:
    /// <summary>
    /// Directional light that is used to simulate the sun.
    /// </summary>
    API_FIELD(Attributes="EditorOrder(10), EditorDisplay(\"Sky\")")
    ScriptingObjectReference<DirectionalLight> SunLight;

    /// <summary>
    /// The sun disc scale.
    /// </summary>
    API_FIELD(Attributes="EditorOrder(20), EditorDisplay(\"Sky\"), Limit(0, 100, 0.01f)")
    float SunDiscScale = 2.0f;

    /// <summary>
    /// The sun power.
    /// </summary>
    API_FIELD(Attributes="EditorOrder(30), EditorDisplay(\"Sky\"), Limit(0, 1000, 0.01f)")
    float SunPower = 8.0f;

    /// <summary>
    /// Controls how much sky will contribute indirect lighting. When set to 0, there is no GI from the sky. The default value is 1.
    /// </summary>
    API_FIELD(Attributes="EditorOrder(40), Limit(0, 100, 0.1f), EditorDisplay(\"Sky\")")
    float IndirectLightingIntensity = 1.0f;

private:
#if COMPILE_WITH_DEV_ENV
    void OnShaderReloading(Asset* obj)
    {
        _psSky = nullptr;
    }
#endif
    void InitConfig(ShaderAtmosphericFogData& config) const;

public:
    // [Actor]
#if USE_EDITOR
    BoundingBox GetEditorBoundingBox() const override
    {
        const Vector3 size(50);
        return BoundingBox(_transform.Translation - size, _transform.Translation + size);
    }
#endif
    void Draw(RenderContextBatch& renderContextBatch) override;
    void Serialize(SerializeStream& stream, const void* otherObj) override;
    void Deserialize(DeserializeStream& stream, ISerializeModifier* modifier) override;
    bool HasContentLoaded() const override;
    bool IntersectsItself(const Ray& ray, Real& distance, Vector3& normal) override;

    // [IAtmosphericFogRenderer]
    void DrawFog(GPUContext* context, RenderContext& renderContext, GPUTextureView* output) override;

    // [ISkyRenderer]
    bool IsDynamicSky() const override;
    float GetIndirectLightingIntensity() const override;
    void ApplySky(GPUContext* context, RenderContext& renderContext, const Matrix& world) override;

protected:
    // [Actor]
    void EndPlay() override;
    void OnEnable() override;
    void OnDisable() override;
    void OnTransformChanged() override;
};

/// <summary>
/// Raymarched clouds in a spherical layer around a planet. An active planet sky supplies the center, radius, and sun.
/// Clouds are composited against opaque scene depth and are not included in sky captures or cloud shadows.
/// </summary>
API_CLASS(Attributes="ActorContextMenu(\"New/Visuals/Sky/Planet Volumetric Clouds\"), ActorToolbox(\"Visuals\")")
class FLAXENGINE_API PlanetVolumetricClouds : public Actor, public IAtmosphericFogRenderer
{
    DECLARE_SCENE_OBJECT(PlanetVolumetricClouds);
private:
    AssetReference<Shader> _shader;
    GPUPipelineState* _pipeline = nullptr;
    GPUPipelineState* _raymarchPipeline = nullptr;
    int32 _sceneRenderingKey = -1;

public:
    ~PlanetVolumetricClouds();

    /// <summary>Planet radius in world units when no planet atmosphere is active. The actor position is the planet center.</summary>
    API_FIELD(Attributes="EditorOrder(10), DefaultValue(60000000.0f), Limit(1), EditorDisplay(\"Clouds\")")
    float PlanetRadius = 60000000.0f;

    /// <summary>Height of the bottom of the cloud layer above the planet surface, in world units.</summary>
    API_FIELD(Attributes="EditorOrder(20), DefaultValue(200000.0f), Limit(0), EditorDisplay(\"Clouds\")")
    float CloudBaseHeight = 200000.0f;

    /// <summary>Thickness of the cloud layer in world units.</summary>
    API_FIELD(Attributes="EditorOrder(30), DefaultValue(300000.0f), Limit(1), EditorDisplay(\"Clouds\")")
    float CloudThickness = 300000.0f;

    /// <summary>Cloud coverage from clear to overcast.</summary>
    API_FIELD(Attributes="EditorOrder(40), DefaultValue(0.55f), Limit(0, 1, 0.01f), EditorDisplay(\"Clouds\")")
    float Coverage = 0.55f;

    /// <summary>Cloud extinction per kilometer.</summary>
    API_FIELD(Attributes="EditorOrder(50), DefaultValue(1.0f), Limit(0), EditorDisplay(\"Clouds\")")
    float Density = 1.0f;

    /// <summary>Size of cloud formations in world units.</summary>
    API_FIELD(Attributes="EditorOrder(60), DefaultValue(800000.0f), Limit(1), EditorDisplay(\"Clouds\")")
    float CloudScale = 800000.0f;

    /// <summary>Cloud tint.</summary>
    API_FIELD(Attributes="EditorOrder(70), DefaultValue(typeof(Color), \"1,1,1,1\"), EditorDisplay(\"Clouds\")")
    Color CloudColor = Color::White;

    /// <summary>Fallback sun when no planet atmosphere is active.</summary>
    API_FIELD(Attributes="EditorOrder(80), EditorDisplay(\"Clouds\")")
    ScriptingObjectReference<DirectionalLight> SunLight;

    /// <summary>Strength of the active planet atmosphere's color influence on cloud lighting. Zero disables the influence.</summary>
    API_FIELD(Attributes="EditorOrder(90), DefaultValue(0.5f), Limit(0, 1, 0.01f), EditorDisplay(\"Clouds\")")
    float AtmosphereColorInfluence = 0.5f;

    /// <summary>Strength of the captured planet atmosphere's brightness contribution to cloud lighting. Zero disables it.</summary>
    API_FIELD(Attributes="EditorOrder(100), DefaultValue(1.0f), Limit(0), EditorDisplay(\"Clouds\")")
    float AtmosphereBrightnessInfluence = 1.0f;

    /// <summary>Overall cloud lighting brightness multiplier. Does not change cloud opacity.</summary>
    API_FIELD(Attributes="EditorOrder(110), DefaultValue(1.0f), Limit(0), EditorDisplay(\"Clouds\")")
    float CloudBrightness = 1.0f;

    /// <summary>Cloud emission at night, faded out as the sun rises. Tinted by Cloud Color and scaled by Cloud Brightness; zero disables it.</summary>
    API_FIELD(Attributes="EditorOrder(115), DefaultValue(0.05f), Limit(0), EditorDisplay(\"Clouds\")")
    float NightEmission = 0.05f;

    /// <summary>Number of raymarch samples per cloud layer segment. Lower values improve performance but can miss detail.</summary>
    API_FIELD(Attributes="EditorOrder(120), DefaultValue(48), Limit(1, 128), EditorDisplay(\"Clouds\")")
    int32 RaymarchSteps = 48;

    /// <summary>Number of consecutive lit cloud samples that share a shadow sample. One evaluates every sample.</summary>
    API_FIELD(Attributes="EditorOrder(130), DefaultValue(1), Limit(1, 16), EditorDisplay(\"Clouds\")")
    int32 ShadowSampleFrequency = 1;

    /// <summary>Strength of the additional cloud density in volumetric fog. Zero disables the contribution; the cloud raymarch remains unchanged.</summary>
    API_FIELD(Attributes="EditorOrder(140), DefaultValue(1.0f), Limit(0), EditorDisplay(\"Clouds\")")
    float VolumetricFogContribution = 1.0f;

private:
#if COMPILE_WITH_DEV_ENV
    void OnShaderReloading(Asset* obj);
#endif
public:
#if USE_EDITOR
    BoundingBox GetEditorBoundingBox() const override
    {
        const Vector3 size(50);
        return BoundingBox(_transform.Translation - size, _transform.Translation + size);
    }
#endif
    void Draw(RenderContextBatch& renderContextBatch) override;
    void Serialize(SerializeStream& stream, const void* otherObj) override;
    void Deserialize(DeserializeStream& stream, ISerializeModifier* modifier) override;
    bool HasContentLoaded() const override;
    bool IntersectsItself(const Ray& ray, Real& distance, Vector3& normal) override;
    void DrawFog(GPUContext* context, RenderContext& renderContext, GPUTextureView* output) override;

protected:
    void EndPlay() override;
    void OnEnable() override;
    void OnDisable() override;
    void OnTransformChanged() override;
};

/// <summary>
/// A planet-centered sky with Rayleigh scattering and sun-driven color controls.
/// </summary>
API_CLASS(Attributes="ActorContextMenu(\"New/Visuals/Sky/Planet Sky Atmosphere\"), ActorToolbox(\"Visuals\")")
class FLAXENGINE_API PlanetSkyAtmosphere : public Actor, public ISkyRenderer
{
    DECLARE_SCENE_OBJECT(PlanetSkyAtmosphere);
private:
    AssetReference<Shader> _shader;
    GPUPipelineState* _psSky = nullptr;
    GPUTexture* _sunDiskTexture = nullptr;
    bool _sunDiskMaterialReady = false;
    int32 _sceneRenderingKey = -1;

public:
    ~PlanetSkyAtmosphere();

    /// <summary>The directional light used as the sun.</summary>
    API_FIELD(Attributes="EditorOrder(10), EditorDisplay(\"Atmosphere\")")
    ScriptingObjectReference<DirectionalLight> SunLight;

    /// <summary>Planet radius in world units. The actor position is the planet center.</summary>
    API_FIELD(Attributes="EditorOrder(20), DefaultValue(60000000.0f), Limit(1), EditorDisplay(\"Atmosphere\")")
    float PlanetRadius = 60000000.0f;

    /// <summary>Atmosphere thickness in world units.</summary>
    API_FIELD(Attributes="EditorOrder(30), DefaultValue(6000000.0f), Limit(1), EditorDisplay(\"Atmosphere\")")
    float AtmosphereHeight = 6000000.0f;

    /// <summary>Rayleigh density scale height in world units.</summary>
    API_FIELD(Attributes="EditorOrder(40), DefaultValue(800000.0f), Limit(1), EditorDisplay(\"Atmosphere\")")
    float RayleighScaleHeight = 800000.0f;

    /// <summary>Rayleigh scattering coefficients per kilometer.</summary>
    API_FIELD(Attributes="EditorOrder(50), EditorDisplay(\"Atmosphere\")")
    Float3 RayleighScattering = Float3(0.0058f, 0.0135f, 0.0331f);

    /// <summary>Scales the strength of the scattering coefficients.</summary>
    API_FIELD(Attributes="EditorOrder(60), DefaultValue(1.0f), Limit(0), EditorDisplay(\"Atmosphere\")")
    float ScatteringStrength = 1.0f;

    /// <summary>Scales the sun's contribution to the atmosphere.</summary>
    API_FIELD(Attributes="EditorOrder(70), DefaultValue(20.0f), Limit(0), EditorDisplay(\"Atmosphere\")")
    float SunIntensity = 20.0f;

    /// <summary>Overall tint applied to scattered light.</summary>
    API_FIELD(Attributes="EditorOrder(80), DefaultValue(typeof(Color), \"1,1,1,1\"), EditorDisplay(\"Artistic Controls\")")
    Color ScatteringTint = Color::White;

    /// <summary>Color added to the atmosphere in daylight.</summary>
    API_FIELD(Attributes="EditorOrder(90), DefaultValue(typeof(Color), \"0,0,0,1\"), EditorDisplay(\"Artistic Controls\")")
    Color DayColorOffset = Color::Black;

    /// <summary>Color added near sunrise and sunset.</summary>
    API_FIELD(Attributes="EditorOrder(100), DefaultValue(typeof(Color), \"0.03,0.01,0.002,1\"), EditorDisplay(\"Artistic Controls\")")
    Color DuskColorOffset = Color(0.03f, 0.01f, 0.002f);

    /// <summary>Color added when the sun is below the horizon.</summary>
    API_FIELD(Attributes="EditorOrder(110), DefaultValue(typeof(Color), \"0.001,0.002,0.006,1\"), EditorDisplay(\"Artistic Controls\")")
    Color NightColorOffset = Color(0.001f, 0.002f, 0.006f);

    /// <summary>Width of the dusk transition in sun elevation sine units.</summary>
    API_FIELD(Attributes="EditorOrder(120), DefaultValue(0.15f), Limit(0.001f, 1.0f), EditorDisplay(\"Artistic Controls\")")
    float DuskTransitionWidth = 0.15f;

    /// <summary>Scales the sky contribution to indirect lighting.</summary>
    API_FIELD(Attributes="EditorOrder(130), DefaultValue(1.0f), Limit(0), EditorDisplay(\"Atmosphere\")")
    float IndirectLightingIntensity = 1.0f;

    /// <summary>Optional space background, attenuated by the atmosphere and occluded by the planet.</summary>
    API_FIELD(Attributes="EditorOrder(140), DefaultValue(null), EditorDisplay(\"Skybox\")")
    AssetReference<CubeTexture> SkyboxTexture;

    /// <summary>Tint applied to the space background.</summary>
    API_FIELD(Attributes="EditorOrder(150), DefaultValue(typeof(Color), \"1,1,1,1\"), EditorDisplay(\"Skybox\")")
    Color SkyboxTint = Color::White;

    /// <summary>Space background exposure in stops.</summary>
    API_FIELD(Attributes="EditorOrder(160), DefaultValue(0.0f), Limit(-100, 100, 0.01f), EditorDisplay(\"Skybox\")")
    float SkyboxExposure = 0.0f;

    /// <summary>Rotation of the space background around the world Y axis, in degrees.</summary>
    API_FIELD(Attributes="EditorOrder(170), DefaultValue(0.0f), EditorDisplay(\"Skybox\")")
    float SkyboxRotation = 0.0f;

    /// <summary>Apparent angular diameter of the sun disk, in degrees. Set to zero to hide the disk.</summary>
    API_FIELD(Attributes="EditorOrder(180), DefaultValue(0.5332f), Limit(0, 90, 0.001f), EditorDisplay(\"Sun Disk\"), ValueCategory(Utils.ValueCategory.Angle)")
    float SunDiskDiameter = 0.5332f;

    /// <summary>Brightness of the sun disk before atmospheric attenuation.</summary>
    API_FIELD(Attributes="EditorOrder(190), DefaultValue(10.0f), Limit(0), EditorDisplay(\"Sun Disk\")")
    float SunDiskIntensity = 10.0f;

    /// <summary>Optional post-process material drawn across the disk. Emissive and opacity modulate its color; the output is automatically masked to the circular disk. Scene color input is unavailable.</summary>
    API_FIELD(Attributes="EditorOrder(200), DefaultValue(null), EditorDisplay(\"Sun Disk\")")
    AssetReference<MaterialBase> SunDiskMaterial;

private:
#if COMPILE_WITH_DEV_ENV
    void OnShaderReloading(Asset* obj);
#endif
    void ApplySkyInternal(GPUContext* context, RenderContext& renderContext, const Matrix& world, bool includeSkybox);
public:
#if USE_EDITOR
    BoundingBox GetEditorBoundingBox() const override
    {
        const Vector3 size(50);
        return BoundingBox(_transform.Translation - size, _transform.Translation + size);
    }
#endif
    void Draw(RenderContextBatch& renderContextBatch) override;
    void Serialize(SerializeStream& stream, const void* otherObj) override;
    void Deserialize(DeserializeStream& stream, ISerializeModifier* modifier) override;
    bool HasContentLoaded() const override;
    bool IntersectsItself(const Ray& ray, Real& distance, Vector3& normal) override;
    bool IsDynamicSky() const override;
    float GetIndirectLightingIntensity() const override;
    void PrepareSky(GPUContext* context, RenderContext& renderContext) override;
    void ApplySky(GPUContext* context, RenderContext& renderContext, const Matrix& world) override;
    void ApplyAtmosphereOnly(GPUContext* context, RenderContext& renderContext, const Matrix& world);

protected:
    void EndPlay() override;
    void OnEnable() override;
    void OnDisable() override;
    void OnTransformChanged() override;
};
