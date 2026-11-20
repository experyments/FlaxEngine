// Copyright (c) Wojciech Figat. All rights reserved.

#include "Engine/Core/Math/Vector3.h"
#include "Engine/Core/Types/String.h"
#include "Engine/Core/Types/StringView.h"
#include "Engine/Core/ScopeExit.h"
#include "Engine/Graphics/RenderView.h"
#include "Engine/Level/Actors/ExponentialHeightFog.h"
#include "Engine/Level/Actors/Sky.h"
#include "Engine/Level/LargeWorlds.h"
#include "Engine/Level/Tags.h"
#include <ThirdParty/catch2/catch.hpp>
#include <limits>

TEST_CASE("Planet cloud defaults match the atmosphere")
{
    auto* sky = ScriptingObject::NewObject<PlanetSkyAtmosphere>();
    auto* clouds = ScriptingObject::NewObject<PlanetVolumetricClouds>();
    REQUIRE(sky);
    REQUIRE(clouds);
    SCOPE_EXIT { sky->DeleteObject(); clouds->DeleteObject(); };
    CHECK(clouds->PlanetRadius == sky->PlanetRadius);
    CHECK(clouds->CloudBaseHeight >= 0.0f);
    CHECK(clouds->CloudThickness > 0.0f);
    CHECK(clouds->Coverage > 0.0f);
    CHECK(clouds->Density > 0.0f);
    CHECK(clouds->AtmosphereColorInfluence == 0.5f);
    CHECK(clouds->AtmosphereBrightnessInfluence == 1.0f);
    CHECK(clouds->CloudBrightness == 1.0f);
    CHECK(clouds->RenderResolutionScale == 0.5f);
}

TEST_CASE("Planet cloud render resolution")
{
    auto* clouds = ScriptingObject::NewObject<PlanetVolumetricClouds>();
    REQUIRE(clouds);
    SCOPE_EXIT { clouds->DeleteObject(); };

    CHECK(clouds->GetCloudRenderSize(1921, 1081) == Int2(961, 541));
    clouds->RenderResolutionScale = 0.25f;
    CHECK(clouds->GetCloudRenderSize(1921, 1081) == Int2(481, 271));
    clouds->RenderResolutionScale = 1.0f;
    CHECK(clouds->GetCloudRenderSize(1921, 1081) == Int2(1921, 1081));
    clouds->RenderResolutionScale = 0.75f;
    CHECK(clouds->GetCloudRenderSize(100, 100) == Int2(75, 75));
    clouds->RenderResolutionScale = 0.0f;
    CHECK(clouds->GetCloudRenderSize(1, 0) == Int2(1, 1));
    CHECK(clouds->GetCloudRenderSize(100, 100) == Int2(25, 25));
    clouds->RenderResolutionScale = 2.0f;
    CHECK(clouds->GetCloudRenderSize(100, 100) == Int2(100, 100));
    clouds->RenderResolutionScale = std::numeric_limits<float>::quiet_NaN();
    CHECK(clouds->GetCloudRenderSize(100, 100) == Int2(50, 50));
    clouds->RenderResolutionScale = std::numeric_limits<float>::infinity();
    CHECK(clouds->GetCloudRenderSize(100, 100) == Int2(100, 100));
}

TEST_CASE("Radial exponential fog")
{
    auto* fog = ScriptingObject::NewObject<RadialExponentialHeightFog>();
    REQUIRE(fog);
    SCOPE_EXIT { fog->DeleteObject(); };
    CHECK(fog->Radius == 60000000.0f);
    fog->SetPosition(Vector3(100000000, 200000000, -300000000));
    fog->FogDensity = 0.1f;
    fog->VolumetricFogEnable = true;

    RenderView view;
    view.Origin = fog->GetPosition() + Vector3(60000000, 0, 0);
    view.Position = Float3(25, 0, 0);
    ShaderExponentialHeightFogData surface;
    VolumetricFogOptions volume;
    fog->GetExponentialHeightFogData(view, surface);
    fog->GetVolumetricFogOptions(view, volume);
    CHECK(surface.RadialParameters.X == 60000000.0f);
    CHECK(surface.RadialParameters.Y == 1.0f);
    CHECK(surface.RadialCameraPosition.W == Approx(25.0f));
    CHECK(volume.RadialCameraPosition == surface.RadialCameraPosition);
    CHECK(volume.RadialParameters == surface.RadialParameters);
    CHECK(volume.Enable);
    CHECK(volume.FogParameters.X == Approx(0.0001f));
}

TEST_CASE("LargeWorlds")
{
    SECTION("UpdateOrigin")
    {
        LargeWorlds::Enable = true;
        Vector3 origin = Vector3::Zero;
        LargeWorlds::UpdateOrigin(origin, Vector3::Zero);
        CHECK(origin == Vector3::Zero);
        LargeWorlds::UpdateOrigin(origin, Vector3(LargeWorlds::ChunkSize * 0.5, LargeWorlds::ChunkSize * 1.0001, LargeWorlds::ChunkSize * 1.5));
        CHECK(origin == Vector3(0, 0, LargeWorlds::ChunkSize * 1));
    }
}

TEST_CASE("Tags")
{
    SECTION("Tag")
    {
        auto prevTags = Tags::List;

        Tags::List = Array<String>({ TEXT("A"), TEXT("A.1"), TEXT("B"), TEXT("B.1"), });

        auto a = Tags::Get(TEXT("A"));
        auto a1 = Tags::Get(TEXT("A.1"));
        auto b = Tags::Get(TEXT("B"));
        auto b1 = Tags::Get(TEXT("B.1"));
        auto c = Tags::Get(TEXT("C"));
        CHECK(a.Index == 1);
        CHECK(a1.Index == 2);
        CHECK(b.Index == 3);
        CHECK(b1.Index == 4);
        CHECK(c.Index == 5);

        Tags::List = prevTags;
    }

    SECTION("Tags")
    {
        auto prevTags = Tags::List;

        Tags::List = Array<String>({ TEXT("A"), TEXT("A.1"), TEXT("B"), TEXT("B.1"), });

        auto a = Tags::Get(TEXT("A"));
        auto a1 = Tags::Get(TEXT("A.1"));
        auto b = Tags::Get(TEXT("B"));
        auto b1 = Tags::Get(TEXT("B.1"));
        auto c = Tags::Get(TEXT("C"));

        Array<Tag> list = { a1, b1 };

        CHECK(Tags::HasTag(list, Tag()) == false);
        CHECK(Tags::HasTag(list, a1) == true);
        CHECK(Tags::HasTag(list, a) == true);
        CHECK(Tags::HasTag(list, c) == false);

        CHECK(Tags::HasTagExact(list, a1) == true);
        CHECK(Tags::HasTagExact(list, a) == false);
        CHECK(Tags::HasTagExact(list, c) == false);

        Tags::List = prevTags;
    }
}
