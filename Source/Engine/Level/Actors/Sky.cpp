// Copyright (c) Wojciech Figat. All rights reserved.

#include "Sky.h"
#include "DirectionalLight.h"
#include "Engine/Core/Math/Color.h"
#include "Engine/Content/Content.h"
#include "Engine/Renderer/RenderList.h"
#include "Engine/Renderer/Renderer.h"
#include "Engine/Renderer/GBufferPass.h"
#include "Engine/Renderer/Lighting/AtmospherePreCompute.h"
#include "Engine/Graphics/RenderBuffers.h"
#include "Engine/Graphics/RenderView.h"
#include "Engine/Graphics/RenderContext.h"
#include "Engine/Graphics/RenderTask.h"
#include "Engine/Graphics/RenderTargetPool.h"
#include "Engine/Graphics/GPUContext.h"
#include "Engine/Graphics/GPUDevice.h"
#include "Engine/Graphics/Shaders/GPUConstantBuffer.h"
#include "Engine/Graphics/Shaders/GPUShader.h"
#include "Engine/Serialization/Serialization.h"
#include "Engine/Level/Scene/SceneRendering.h"
#if USE_EDITOR
#include "Engine/Renderer/Lightmaps.h"
#endif

GPU_CB_STRUCT(Data {
    Matrix WorldViewProjection;
    Matrix InvViewProjection;
    Float3 ViewOffset;
    float NoiseScale;
    ShaderGBufferData GBuffer;
    ShaderAtmosphericFogData Fog;
    });

Sky::Sky(const SpawnParams& params)
    : Actor(params)
{
    _drawNoCulling = 1;
    _drawCategory = SceneRendering::PreRender;

    // Load shader
    _shader = Content::LoadAsyncInternal<Shader>(TEXT("Shaders/Lighting/Sky"));
    if (_shader == nullptr)
    {
        LOG(Fatal, "Cannot load sky shader.");
        return;
    }
#if COMPILE_WITH_DEV_ENV
    _shader.Get()->Reloading.Bind<Sky, &Sky::OnShaderReloading>(this);
#endif
}

Sky::~Sky()
{
    SAFE_DELETE_GPU_RESOURCE(_psSky);
}

void Sky::InitConfig(ShaderAtmosphericFogData& config) const
{
    config.AtmosphericFogDensityScale = 1.0f;
    config.AtmosphericFogSunDiscScale = SunDiscScale;
    config.AtmosphericFogDistanceScale = 1;
    config.AtmosphericFogGroundOffset = 0;

    config.AtmosphericFogAltitudeScale = 1;
    config.AtmosphericFogStartDistance = 0;
    config.AtmosphericFogPower = 1;
    config.AtmosphericFogDistanceOffset = 0;

    config.AtmosphericFogSunPower = SunPower;
    config.AtmosphericFogDensityOffset = 0.0f;
#if USE_EDITOR
    if (IsRunningRadiancePass)
    config.AtmosphericFogSunPower *= IndirectLightingIntensity;
#endif

    if (SunLight)
    {
        config.AtmosphericFogSunDirection = -SunLight->GetForward();
        config.AtmosphericFogSunColor = SunLight->Color.ToFloat3() * SunLight->Color.A;
    }
    else
    {
        config.AtmosphericFogSunDirection = Float3::UnitY;
        config.AtmosphericFogSunColor = Float3::One;
    }
}

void Sky::Draw(RenderContextBatch& renderContextBatch)
{
    const RenderContext& renderContext = renderContextBatch.GetMainContext();
    if (HasContentLoaded() && EnumHasAnyFlags(renderContext.View.Flags, ViewFlags::Sky))
    {
        // Ensure to have pipeline state cache created
        if (_psSky == nullptr)
        {
            const auto shader = _shader->GPU;

            // Create pipeline states
            if (_psSky == nullptr)
            {
                _psSky = GPUDevice::Instance->CreatePipelineState();
                auto psDesc = GPUPipelineState::Description::Default;
                psDesc.VS = shader->GetVS("VS");
                psDesc.PS = shader->GetPS("PS_Sky");
                psDesc.CullMode = CullMode::Inverted;
                psDesc.DepthWriteEnable = false;
                psDesc.DepthClipEnable = false;
                psDesc.DepthFunc = ComparisonFunc::DefaultEqual;
                if (_psSky->Init(psDesc))
                {
                    LOG(Warning, "Cannot create graphics pipeline state object for '{0}'.", ToString());
                }
            }
        }

        // Register for the sky and fog pass
        renderContext.List->Sky = this;
        //if(renderContext.View.Flags & ViewFlags::Fog) != 0)
        //renderContext.List->AtmosphericFog = this; // TODO: finish atmosphere fog
    }
}

void Sky::Serialize(SerializeStream& stream, const void* otherObj)
{
    Actor::Serialize(stream, otherObj);

    SERIALIZE_GET_OTHER_OBJ(Sky);

    SERIALIZE_MEMBER(Sun, SunLight);
    SERIALIZE(SunDiscScale);
    SERIALIZE(SunPower);
    SERIALIZE(IndirectLightingIntensity);
}

void Sky::Deserialize(DeserializeStream& stream, ISerializeModifier* modifier)
{
    Actor::Deserialize(stream, modifier);

    DESERIALIZE_MEMBER(Sun, SunLight);
    DESERIALIZE(SunDiscScale);
    DESERIALIZE(SunPower);
    DESERIALIZE(IndirectLightingIntensity);
}

bool Sky::HasContentLoaded() const
{
    return _shader && _shader->IsLoaded() && AtmospherePreCompute::GetCache(nullptr);
}

bool Sky::IntersectsItself(const Ray& ray, Real& distance, Vector3& normal)
{
    return false;
}

void Sky::DrawFog(GPUContext* context, RenderContext& renderContext, GPUTextureView* output)
{
    MISSING_CODE("sky fog");
}

bool Sky::IsDynamicSky() const
{
    return !IsStatic() || (SunLight && !SunLight->IsStatic());
}

float Sky::GetIndirectLightingIntensity() const
{
    return IndirectLightingIntensity;
}

void Sky::ApplySky(GPUContext* context, RenderContext& renderContext, const Matrix& world)
{
    // Get precomputed cache and bind it to the pipeline
    AtmosphereCache cache;
    if (!AtmospherePreCompute::GetCache(&cache))
        return;
    context->BindSR(4, cache.Transmittance);
    context->BindSR(5, cache.Irradiance);
    context->BindSR(6, cache.Inscatter->ViewVolume());

    // Setup constants data
    Matrix m;
    Data data;
    Matrix::Multiply(world, renderContext.View.ViewProjection(), m);
    Matrix::Transpose(m, data.WorldViewProjection);
    Matrix::Transpose(renderContext.View.IVP, data.InvViewProjection);
    GBufferPass::SetInputs(renderContext.View, data.GBuffer);
    data.ViewOffset = renderContext.View.Origin + GetPosition();
    data.NoiseScale = renderContext.View.IsSingleFrame ? 0.01f : 0.03f;
    InitConfig(data.Fog);
    //data.Fog.AtmosphericFogSunPower *= SunLight ? SunLight->Brightness : 1.0f;
    if (EnumHasNoneFlags(renderContext.View.Flags, ViewFlags::SpecularLight))
    {
        // Hide sun disc if specular light is disabled
        data.Fog.AtmosphericFogSunDiscScale = 0;
    }

    // Bind pipeline
    auto cb = _shader->GPU->GetCB(0);
    context->UpdateCB(cb, &data);
    context->BindCB(0, cb);
    context->SetState(_psSky);
}

void Sky::EndPlay()
{
    SAFE_DELETE_GPU_RESOURCE(_psSky);

    Actor::EndPlay();
}

void Sky::OnEnable()
{
    GetSceneRendering()->AddActor(this, _sceneRenderingKey);
#if USE_EDITOR
    GetSceneRendering()->AddViewportIcon(this);
#endif

    Actor::OnEnable();
}

void Sky::OnDisable()
{
#if USE_EDITOR
    GetSceneRendering()->RemoveViewportIcon(this);
#endif
    GetSceneRendering()->RemoveActor(this, _sceneRenderingKey);

    Actor::OnDisable();
}

void Sky::OnTransformChanged()
{
    Actor::OnTransformChanged();

    _box = BoundingBox(_transform.Translation);
    _sphere = BoundingSphere(_transform.Translation, 0.0f);
}

GPU_CB_STRUCT(PlanetSkyData {
    Float4 CameraFromCenter;
    Float4 Geometry;
    Float4 Scattering;
    Float4 SunDirection;
    Float4 SunColor;
    Float4 Tint;
    Float4 DayOffset;
    Float4 DuskOffset;
    Float4 NightOffset;
    Float4 SkyboxColor;
    Float4 SkyboxRotation;
    Float4 SunDisk;
    });

PlanetSkyAtmosphere::PlanetSkyAtmosphere(const SpawnParams& params)
    : Actor(params)
{
    _drawNoCulling = 1;
    _drawCategory = SceneRendering::PreRender;
    _shader = Content::LoadAsyncInternal<Shader>(TEXT("Shaders/Lighting/Sky"));
#if COMPILE_WITH_DEV_ENV
    if (_shader)
        _shader.Get()->Reloading.Bind<PlanetSkyAtmosphere, &PlanetSkyAtmosphere::OnShaderReloading>(this);
#endif
}

PlanetSkyAtmosphere::~PlanetSkyAtmosphere()
{
    SAFE_DELETE_GPU_RESOURCE(_psSky);
    RenderTargetPool::Release(_sunDiskTexture);
}

#if COMPILE_WITH_DEV_ENV
void PlanetSkyAtmosphere::OnShaderReloading(Asset* obj)
{
    SAFE_DELETE_GPU_RESOURCE(_psSky);
}
#endif

void PlanetSkyAtmosphere::Draw(RenderContextBatch& renderContextBatch)
{
    const RenderContext& renderContext = renderContextBatch.GetMainContext();
    // Keep rendering the atmosphere while an optional background is loading.
    if (!_shader || !_shader->IsLoaded() || !EnumHasAnyFlags(renderContext.View.Flags, ViewFlags::Sky))
        return;
    if (!_psSky)
    {
        auto desc = GPUPipelineState::Description::Default;
        desc.VS = _shader->GPU->GetVS("VS");
        desc.PS = _shader->GPU->GetPS("PS_PlanetSky");
        desc.CullMode = CullMode::Inverted;
        desc.DepthWriteEnable = false;
        desc.DepthClipEnable = false;
        desc.DepthFunc = ComparisonFunc::DefaultEqual;
        _psSky = GPUDevice::Instance->CreatePipelineState();
        if (_psSky->Init(desc))
        {
            LOG(Warning, "Cannot create graphics pipeline state object for '{0}'.", ToString());
            SAFE_DELETE_GPU_RESOURCE(_psSky);
            return;
        }
    }
    renderContext.List->Sky = this;
}

void PlanetSkyAtmosphere::Serialize(SerializeStream& stream, const void* otherObj)
{
    Actor::Serialize(stream, otherObj);
    SERIALIZE_GET_OTHER_OBJ(PlanetSkyAtmosphere);
    SERIALIZE(SunLight);
    SERIALIZE(PlanetRadius);
    SERIALIZE(AtmosphereHeight);
    SERIALIZE(RayleighScaleHeight);
    SERIALIZE(RayleighScattering);
    SERIALIZE(ScatteringStrength);
    SERIALIZE(SunIntensity);
    SERIALIZE(ScatteringTint);
    SERIALIZE(DayColorOffset);
    SERIALIZE(DuskColorOffset);
    SERIALIZE(NightColorOffset);
    SERIALIZE(DuskTransitionWidth);
    SERIALIZE(IndirectLightingIntensity);
    SERIALIZE(SkyboxTexture);
    SERIALIZE(SkyboxTint);
    SERIALIZE(SkyboxExposure);
    SERIALIZE(SkyboxRotation);
    SERIALIZE(SunDiskDiameter);
    SERIALIZE(SunDiskIntensity);
    SERIALIZE(SunDiskMaterial);
}

void PlanetSkyAtmosphere::Deserialize(DeserializeStream& stream, ISerializeModifier* modifier)
{
    Actor::Deserialize(stream, modifier);
    DESERIALIZE(SunLight);
    DESERIALIZE(PlanetRadius);
    DESERIALIZE(AtmosphereHeight);
    DESERIALIZE(RayleighScaleHeight);
    DESERIALIZE(RayleighScattering);
    DESERIALIZE(ScatteringStrength);
    DESERIALIZE(SunIntensity);
    DESERIALIZE(ScatteringTint);
    DESERIALIZE(DayColorOffset);
    DESERIALIZE(DuskColorOffset);
    DESERIALIZE(NightColorOffset);
    DESERIALIZE(DuskTransitionWidth);
    DESERIALIZE(IndirectLightingIntensity);
    DESERIALIZE(SkyboxTexture);
    DESERIALIZE(SkyboxTint);
    DESERIALIZE(SkyboxExposure);
    DESERIALIZE(SkyboxRotation);
    DESERIALIZE(SunDiskDiameter);
    DESERIALIZE(SunDiskIntensity);
    DESERIALIZE(SunDiskMaterial);
}

bool PlanetSkyAtmosphere::HasContentLoaded() const
{
    return _shader && _shader->IsLoaded() && (!SkyboxTexture || SkyboxTexture->IsLoaded());
}

bool PlanetSkyAtmosphere::IntersectsItself(const Ray& ray, Real& distance, Vector3& normal)
{
    return false;
}

bool PlanetSkyAtmosphere::IsDynamicSky() const
{
    return !IsStatic() || (SunLight && !SunLight->IsStatic()) || SunDiskMaterial;
}

float PlanetSkyAtmosphere::GetIndirectLightingIntensity() const
{
    return IndirectLightingIntensity;
}

void PlanetSkyAtmosphere::PrepareSky(GPUContext* context, RenderContext& renderContext)
{
    _sunDiskMaterialReady = false;
    if (!SunDiskMaterial || !SunDiskMaterial->IsLoaded() || !SunDiskMaterial->IsReady() || !SunDiskMaterial->IsPostFx() ||
        SunDiskDiameter <= 0.0f || SunDiskIntensity <= 0.0f || EnumHasNoneFlags(renderContext.View.Flags, ViewFlags::SpecularLight))
        return;

    constexpr int32 resolution = 256;
    if (!_sunDiskTexture)
    {
        _sunDiskTexture = RenderTargetPool::Get(GPUTextureDescription::New2D(resolution, resolution, PixelFormat::R16G16B16A16_Float));
        if (!_sunDiskTexture)
            return;
        RENDER_TARGET_POOL_SET_NAME(_sunDiskTexture, "PlanetSky.SunDisk");
    }

    context->ResetSR();
    context->Clear(_sunDiskTexture->View(), Color::Transparent);
    context->SetViewportAndScissors(resolution, resolution);
    RenderContext materialContext = renderContext;
    materialContext.View.ScreenSize = Float4((float)resolution, (float)resolution, 1.0f / resolution, 1.0f / resolution);
    Renderer::DrawPostFxMaterial(context, materialContext, SunDiskMaterial.Get(), _sunDiskTexture, nullptr);
    context->ResetSR();
    _sunDiskMaterialReady = true;
}

void PlanetSkyAtmosphere::ApplySky(GPUContext* context, RenderContext& renderContext, const Matrix& world)
{
    ApplySkyInternal(context, renderContext, world, true);
}

void PlanetSkyAtmosphere::ApplyAtmosphereOnly(GPUContext* context, RenderContext& renderContext, const Matrix& world)
{
    ApplySkyInternal(context, renderContext, world, false);
}

void PlanetSkyAtmosphere::ApplySkyInternal(GPUContext* context, RenderContext& renderContext, const Matrix& world, bool includeSkybox)
{
    Data data{};
    Matrix matrix;
    Matrix::Multiply(world, renderContext.View.ViewProjection(), matrix);
    Matrix::Transpose(matrix, data.WorldViewProjection);
    Matrix::Transpose(renderContext.View.IVP, data.InvViewProjection);
    GBufferPass::SetInputs(renderContext.View, data.GBuffer);
    data.NoiseScale = renderContext.View.IsSingleFrame ? 0.01f : 0.03f;

    const Vector3 cameraFromCenter = renderContext.View.Origin + Vector3(renderContext.View.Position) - GetPosition();
    const float radius = Math::Max(PlanetRadius, 1.0f);
    PlanetSkyData planet;
    planet.CameraFromCenter = Float4((Float3)(cameraFromCenter * 0.00001), (float)((cameraFromCenter.Length() - radius) * 0.00001));
    planet.Geometry = Float4(radius * 0.00001f, Math::Max(AtmosphereHeight, 1.0f) * 0.00001f, Math::Max(RayleighScaleHeight, 1.0f) * 0.00001f, Math::Max(SunIntensity, 0.0f));
#if USE_EDITOR
    if (IsRunningRadiancePass)
        planet.Geometry.W *= IndirectLightingIntensity;
#endif
    planet.Scattering = Float4(RayleighScattering * Math::Max(ScatteringStrength, 0.0f), Math::Clamp(DuskTransitionWidth, 0.001f, 1.0f));
    planet.SunDirection = Float4(SunLight ? -SunLight->GetForward() : Float3::UnitY, 0.0f);
    planet.SunColor = Float4(SunLight ? SunLight->Color.ToFloat3() * SunLight->Color.A : Float3::One, 0.0f);
    planet.Tint = Float4(ScatteringTint.ToFloat3() * ScatteringTint.A, 0.0f);
    planet.DayOffset = Float4(DayColorOffset.ToFloat3() * DayColorOffset.A, 0.0f);
    planet.DuskOffset = Float4(DuskColorOffset.ToFloat3() * DuskColorOffset.A, 0.0f);
    planet.NightOffset = Float4(NightColorOffset.ToFloat3() * NightColorOffset.A, 0.0f);

    GPUTexture* skybox = nullptr;
    if (includeSkybox && SkyboxTexture && SkyboxTexture->IsLoaded())
    {
        SkyboxTexture->SetStreamingVisible();
        GPUTexture* texture = SkyboxTexture->GetTexture();
        if (texture && texture->ResidentMipLevels() > 0)
            skybox = texture;
    }
    planet.SkyboxColor = Float4(SkyboxTint.ToFloat3() * (SkyboxTint.A * Math::Exp2(SkyboxExposure)), skybox ? 1.0f : 0.0f);
    const float skyboxAngle = SkyboxRotation * DegreesToRadians;
    planet.SkyboxRotation = Float4(Math::Cos(skyboxAngle), Math::Sin(skyboxAngle), 0.0f, 0.0f);
    float diskIntensity = includeSkybox && EnumHasAnyFlags(renderContext.View.Flags, ViewFlags::SpecularLight) ? Math::Max(SunDiskIntensity, 0.0f) : 0.0f;
#if USE_EDITOR
    if (IsRunningRadiancePass)
        diskIntensity *= IndirectLightingIntensity;
#endif
    const float diskRadius = Math::Sin(Math::Clamp(SunDiskDiameter, 0.0f, 90.0f) * DegreesToRadians * 0.5f);
    planet.SunDisk = Float4(diskRadius, diskIntensity, 0.0f, _sunDiskMaterialReady ? 1.0f : 0.0f);

    auto cb = _shader->GPU->GetCB(0);
    context->UpdateCB(cb, &data);
    context->BindCB(0, cb);
    cb = _shader->GPU->GetCB(1);
    context->UpdateCB(cb, &planet);
    context->BindCB(1, cb);
    context->BindSR(7, skybox);
    context->BindSR(8, _sunDiskMaterialReady ? _sunDiskTexture : nullptr);
    context->SetState(_psSky);
}

void PlanetSkyAtmosphere::EndPlay()
{
    SAFE_DELETE_GPU_RESOURCE(_psSky);
    RenderTargetPool::Release(_sunDiskTexture);
    _sunDiskTexture = nullptr;
    _sunDiskMaterialReady = false;
    Actor::EndPlay();
}

void PlanetSkyAtmosphere::OnEnable()
{
    GetSceneRendering()->AddActor(this, _sceneRenderingKey);
#if USE_EDITOR
    GetSceneRendering()->AddViewportIcon(this);
#endif
    Actor::OnEnable();
}

void PlanetSkyAtmosphere::OnDisable()
{
#if USE_EDITOR
    GetSceneRendering()->RemoveViewportIcon(this);
#endif
    GetSceneRendering()->RemoveActor(this, _sceneRenderingKey);
    Actor::OnDisable();
}

void PlanetSkyAtmosphere::OnTransformChanged()
{
    Actor::OnTransformChanged();
    _box = BoundingBox(_transform.Translation);
    _sphere = BoundingSphere(_transform.Translation, 0.0f);
}

GPU_CB_STRUCT(PlanetCloudData {
    ShaderGBufferData GBuffer;
    Float4 CameraFromCenter;
    Float4 Geometry;
    Float4 Settings;
    Float4 SunDirection;
    Float4 SunColor;
    Float4 CloudColor;
    Float4 RenderSize;
    Float4 Quality;
    });

PlanetVolumetricClouds::PlanetVolumetricClouds(const SpawnParams& params)
    : Actor(params)
{
    _drawNoCulling = 1;
    _drawCategory = SceneRendering::PreRender;
    _shader = Content::LoadAsyncInternal<Shader>(TEXT("Shaders/Lighting/Fog"));
#if COMPILE_WITH_DEV_ENV
    if (_shader)
        _shader.Get()->Reloading.Bind<PlanetVolumetricClouds, &PlanetVolumetricClouds::OnShaderReloading>(this);
#endif
}

PlanetVolumetricClouds::~PlanetVolumetricClouds()
{
    SAFE_DELETE_GPU_RESOURCE(_pipeline);
    SAFE_DELETE_GPU_RESOURCE(_raymarchPipeline);
}

#if COMPILE_WITH_DEV_ENV
void PlanetVolumetricClouds::OnShaderReloading(Asset* obj)
{
    SAFE_DELETE_GPU_RESOURCE(_pipeline);
    SAFE_DELETE_GPU_RESOURCE(_raymarchPipeline);
}
#endif

void PlanetVolumetricClouds::Draw(RenderContextBatch& renderContextBatch)
{
    const RenderContext& renderContext = renderContextBatch.GetMainContext();
    if (!HasContentLoaded() || !renderContext.View.IsPerspectiveProjection() ||
        EnumHasNoneFlags(renderContext.View.Flags, ViewFlags::Sky) ||
        EnumHasNoneFlags(renderContext.View.Pass, DrawPass::GBuffer))
        return;
    if (!_pipeline)
    {
        if (!_shader->GPU->HasShader("PS_PlanetClouds") || !_shader->GPU->HasShader("PS_PlanetCloudsComposite") || !_shader->GPU->GetCB(1))
            return;
        auto desc = GPUPipelineState::Description::DefaultFullscreenTriangle;
        desc.PS = _shader->GPU->GetPS("PS_PlanetClouds");
        _raymarchPipeline = GPUDevice::Instance->CreatePipelineState();
        if (_raymarchPipeline->Init(desc))
        {
            LOG(Warning, "Cannot create graphics pipeline state object for '{0}'.", ToString());
            SAFE_DELETE_GPU_RESOURCE(_raymarchPipeline);
            return;
        }
        desc.PS = _shader->GPU->GetPS("PS_PlanetCloudsComposite");
        desc.BlendMode.BlendEnable = true;
        desc.BlendMode.SrcBlend = BlendingMode::Blend::One;
        desc.BlendMode.DestBlend = BlendingMode::Blend::SrcAlpha;
        desc.BlendMode.BlendOp = BlendingMode::Operation::Add;
        desc.BlendMode.SrcBlendAlpha = BlendingMode::Blend::One;
        desc.BlendMode.DestBlendAlpha = BlendingMode::Blend::Zero;
        desc.BlendMode.BlendOpAlpha = BlendingMode::Operation::Add;
        desc.BlendMode.RenderTargetWriteMask = BlendingMode::ColorWrite::RGB;
        _pipeline = GPUDevice::Instance->CreatePipelineState();
        if (_pipeline->Init(desc))
        {
            LOG(Warning, "Cannot create graphics pipeline state object for '{0}'.", ToString());
            SAFE_DELETE_GPU_RESOURCE(_pipeline);
            SAFE_DELETE_GPU_RESOURCE(_raymarchPipeline);
            return;
        }
    }
    renderContext.List->PlanetClouds = this;
}

void PlanetVolumetricClouds::Serialize(SerializeStream& stream, const void* otherObj)
{
    Actor::Serialize(stream, otherObj);
    SERIALIZE_GET_OTHER_OBJ(PlanetVolumetricClouds);
    SERIALIZE(PlanetRadius);
    SERIALIZE(CloudBaseHeight);
    SERIALIZE(CloudThickness);
    SERIALIZE(Coverage);
    SERIALIZE(Density);
    SERIALIZE(CloudScale);
    SERIALIZE(CloudColor);
    SERIALIZE(SunLight);
    SERIALIZE(AtmosphereColorInfluence);
    SERIALIZE(AtmosphereBrightnessInfluence);
    SERIALIZE(CloudBrightness);
    SERIALIZE(NightEmission);
    SERIALIZE(RaymarchSteps);
    SERIALIZE(ShadowSampleFrequency);
    SERIALIZE(VolumetricFogContribution);
}

void PlanetVolumetricClouds::Deserialize(DeserializeStream& stream, ISerializeModifier* modifier)
{
    Actor::Deserialize(stream, modifier);
    DESERIALIZE(PlanetRadius);
    DESERIALIZE(CloudBaseHeight);
    DESERIALIZE(CloudThickness);
    DESERIALIZE(Coverage);
    DESERIALIZE(Density);
    DESERIALIZE(CloudScale);
    DESERIALIZE(CloudColor);
    DESERIALIZE(SunLight);
    DESERIALIZE(AtmosphereColorInfluence);
    DESERIALIZE(AtmosphereBrightnessInfluence);
    DESERIALIZE(CloudBrightness);
    DESERIALIZE(NightEmission);
    DESERIALIZE(RaymarchSteps);
    DESERIALIZE(ShadowSampleFrequency);
    DESERIALIZE(VolumetricFogContribution);
}

bool PlanetVolumetricClouds::HasContentLoaded() const
{
    return _shader && _shader->IsLoaded();
}

bool PlanetVolumetricClouds::IntersectsItself(const Ray& ray, Real& distance, Vector3& normal)
{
    return false;
}

void PlanetVolumetricClouds::DrawFog(GPUContext* context, RenderContext& renderContext, GPUTextureView* output)
{
    if (Coverage <= 0.0f || Density <= 0.0f)
        return;

    const auto sky = dynamic_cast<PlanetSkyAtmosphere*>(renderContext.List->Sky);
    const Vector3 center = sky ? sky->GetPosition() : GetPosition();
    const float radius = Math::Max(sky ? sky->PlanetRadius : PlanetRadius, 1.0f);
    const Vector3 camera = renderContext.View.Origin + Vector3(renderContext.View.Position) - center;
    const DirectionalLight* sun = sky ? sky->SunLight.Get() : SunLight.Get();

    GPUTextureView* atmosphereSky = nullptr;
    if (sky && (AtmosphereColorInfluence > 0.0f || AtmosphereBrightnessInfluence > 0.0f))
    {
        atmosphereSky = GBufferPass::Instance()->RenderSkybox(renderContext, context, true);
        context->SetViewportAndScissors(renderContext.Task ? renderContext.Task->GetViewport() : renderContext.Buffers->GetViewport());
        context->ResetSR();
    }

    const int32 width = Math::Max((renderContext.Buffers->GetWidth() + 1) / 2, 1);
    const int32 height = Math::Max((renderContext.Buffers->GetHeight() + 1) / 2, 1);
    auto cloudBuffer = RenderTargetPool::Get(GPUTextureDescription::New2D(width, height, PixelFormat::R16G16B16A16_Float));
    if (!cloudBuffer)
        return;
    RENDER_TARGET_POOL_SET_NAME(cloudBuffer, "PlanetClouds");

    PlanetCloudData data;
    GBufferPass::SetInputs(renderContext.View, data.GBuffer);
    data.CameraFromCenter = Float4((Float3)(camera * 0.00001), (float)((camera.Length() - radius) * 0.00001));
    data.Geometry = Float4(radius * 0.00001f, Math::Max(CloudBaseHeight, 0.0f) * 0.00001f, Math::Max(CloudThickness, 1.0f) * 0.00001f, Math::Max(CloudScale, 1.0f) * 0.00001f);
    data.Settings = Float4(Math::Saturate(Coverage), Math::Max(Density, 0.0f), atmosphereSky ? Math::Saturate(AtmosphereColorInfluence) : 0.0f, atmosphereSky ? Math::Max(AtmosphereBrightnessInfluence, 0.0f) : 0.0f);
    data.SunDirection = Float4(sun ? -sun->GetForward() : Float3::UnitY, 0.0f);
    data.SunColor = Float4(sun ? sun->Color.ToFloat3() * sun->Color.A : Float3::One, 0.0f);
    data.CloudColor = Float4(CloudColor.ToFloat3() * CloudColor.A, Math::Max(CloudBrightness, 0.0f));
    data.RenderSize = Float4((float)width, (float)height, 1.0f / width, 1.0f / height);
    data.Quality = Float4((float)Math::Clamp(RaymarchSteps, 1, 128), (float)Math::Clamp(ShadowSampleFrequency, 1, 16), Math::Max(NightEmission, 0.0f), 0.0f);
    auto cb = _shader->GPU->GetCB(1);
    context->UpdateCB(cb, &data);
    context->BindCB(1, cb);
    context->ResetRenderTarget();
    context->BindSR(0, renderContext.Buffers->DepthBuffer);
    context->BindSR(2, atmosphereSky);
    context->SetViewportAndScissors((float)width, (float)height);
    context->SetRenderTarget(cloudBuffer->View());
    context->SetState(_raymarchPipeline);
    context->DrawFullscreenTriangle();

    context->ResetRenderTarget();
    context->SetViewportAndScissors(renderContext.Task ? renderContext.Task->GetViewport() : renderContext.Buffers->GetViewport());
    context->BindSR(3, cloudBuffer);
    context->SetRenderTarget(output);
    context->SetState(_pipeline);
    context->DrawFullscreenTriangle();
    context->ResetSR();
    RenderTargetPool::Release(cloudBuffer);
}

void PlanetVolumetricClouds::EndPlay()
{
    SAFE_DELETE_GPU_RESOURCE(_pipeline);
    SAFE_DELETE_GPU_RESOURCE(_raymarchPipeline);
    Actor::EndPlay();
}

void PlanetVolumetricClouds::OnEnable()
{
    GetSceneRendering()->AddActor(this, _sceneRenderingKey);
#if USE_EDITOR
    GetSceneRendering()->AddViewportIcon(this);
#endif
    Actor::OnEnable();
}

void PlanetVolumetricClouds::OnDisable()
{
#if USE_EDITOR
    GetSceneRendering()->RemoveViewportIcon(this);
#endif
    GetSceneRendering()->RemoveActor(this, _sceneRenderingKey);
    Actor::OnDisable();
}

void PlanetVolumetricClouds::OnTransformChanged()
{
    Actor::OnTransformChanged();
    _box = BoundingBox(_transform.Translation);
    _sphere = BoundingSphere(_transform.Translation, 0.0f);
}
