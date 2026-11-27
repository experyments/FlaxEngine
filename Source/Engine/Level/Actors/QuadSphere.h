#pragma once
#include "../Actor.h"
#include "Engine/Content/Assets/MaterialBase.h"
#include "Engine/Content/Assets/Model.h"
#include "Engine/Content/JsonAsset.h"
#include "Engine/Level/Prefabs/Prefab.h"
#include "Engine/Scripting/ScriptingObjectReference.h"

class QuadSphereRuntime;

/// <summary>Operation performed by a quad-sphere terrain brush.</summary>
API_ENUM() enum class QuadSphereBrushMode
{
    Carve,
    Flatten,
    None,
    RemoveFoliage,
};

/// <summary>Biome layer applied by a quad-sphere brush.</summary>
API_ENUM() enum class QuadSphereBrushBiome
{
    None,
    Layer0,
    Layer1,
    Layer2,
    Layer3,
    Beach,
};

/// <summary>Shape of a quad-sphere terrain brush.</summary>
API_ENUM() enum class QuadSphereBrushShape
{
    Circle,
    Square,
};

/// <summary>Noise settings for one quad-sphere biome layer.</summary>
API_STRUCT() struct FLAXENGINE_API QuadSphereBiomeLayer : ISerializable
{
    API_AUTO_SERIALIZATION();
    DECLARE_SCRIPTING_TYPE_MINIMAL(QuadSphereBiomeLayer);

    /// <summary>Noise frequency on the unit sphere.</summary>
    API_FIELD(Attributes="Limit(0, 4096)") float NoiseFrequency = 0.0f;
    /// <summary>Number of noise octaves.</summary>
    API_FIELD(Attributes="Limit(1, 8)") int32 NoiseOctaves = 0;
    /// <summary>Offset added to the terrain noise seed.</summary>
    API_FIELD() int32 SeedOffset = 0;
    /// <summary>Scales this layer's noise score relative to other layers.</summary>
    API_FIELD(Attributes="Limit(0, 16)") float NoiseContrast = 0.0f;

    QuadSphereBiomeLayer() = default;
    explicit QuadSphereBiomeLayer(int32 seedOffset)
        : NoiseFrequency(4.0f)
        , NoiseOctaves(3)
        , SeedOffset(seedOffset)
        , NoiseContrast(2.0f)
    {
    }
};

/// <summary>Shadow casting selection for a quad-sphere foliage entry.</summary>
API_ENUM() enum class QuadSphereFoliageShadows
{
    Legacy,
    Enabled,
    Disabled,
};

/// <summary>Hemispheres eligible for placement, relative to the planet's local Y axis.</summary>
API_ENUM() enum class QuadSphereFoliageHemisphere
{
    Both,
    North,
    South,
};

/// <summary>Noise used to modulate foliage density and scale.</summary>
API_ENUM() enum class QuadSphereFoliageNoiseType
{
    Value,
    Perlin,
    RidgedPerlin,
};

/// <summary>Biome layers eligible for prefab or foliage placement.</summary>
API_ENUM(Attributes="Flags") enum class QuadSpherePrefabBiomes
{
    None = 0,
    Layer0 = 1,
    Layer1 = 2,
    Layer2 = 4,
    Layer3 = 8,
    Beach = 16,
    All = 31,
};
DECLARE_ENUM_OPERATORS(QuadSpherePrefabBiomes);

/// <summary>Deterministic placement settings for one prefab.</summary>
API_STRUCT() struct FLAXENGINE_API QuadSpherePrefabConfig : ISerializable
{
    API_AUTO_SERIALIZATION();
    DECLARE_SCRIPTING_TYPE_MINIMAL(QuadSpherePrefabConfig);

    /// <summary>Prefab to instantiate.</summary>
    API_FIELD() AssetReference<Prefab> Prefab;
    /// <summary>Eligible material and beach layers.</summary>
    API_FIELD() QuadSpherePrefabBiomes Biomes = QuadSpherePrefabBiomes::All;
    /// <summary>Prefab placement density, using the reference implementation's radius / 2,000,000 scale.</summary>
    API_FIELD(Attributes="Limit(0, 1000000)") float Density = 1.0f;
    /// <summary>Probability of accepting an eligible placement.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float Probability = 1.0f;
    /// <summary>Frequency of the optional placement noise on the unit sphere.</summary>
    API_FIELD(Attributes="Limit(0, 4096)") float NoiseFrequency = 4.0f;
    /// <summary>Number of placement noise octaves.</summary>
    API_FIELD(Attributes="Limit(1, 8)") int32 NoiseOctaves = 3;
    /// <summary>Offset added to the planet seed for placement noise.</summary>
    API_FIELD() int32 SeedOffset = 0;
    /// <summary>Strength of noise modulation; zero disables it.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float NoiseStrength = 0.0f;
    /// <summary>Minimum distance from the planet center; zero disables the limit.</summary>
    API_FIELD() double MinAltitude = 0.0;
    /// <summary>Maximum distance from the planet center; zero disables the limit.</summary>
    API_FIELD() double MaxAltitude = MAX_double;
    /// <summary>Distance inside either altitude boundary over which probability fades in.</summary>
    API_FIELD(Attributes="Limit(0, float.MaxValue)") float AltitudeFalloff = 0.0f;
    /// <summary>Minimum allowed terrain slope in degrees from radial up.</summary>
    API_FIELD(Attributes="Limit(0, 90)") float MinSlope = 0.0f;
    /// <summary>Maximum allowed terrain slope in degrees from radial up.</summary>
    API_FIELD(Attributes="Limit(0, 90)") float MaxSlope = 90.0f;
    /// <summary>Minimum temperature; 1 at the equator and 0 at the poles.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float MinTemperature = 0.0f;
    /// <summary>Maximum temperature; 1 at the equator and 0 at the poles.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float MaxTemperature = 1.0f;
    /// <summary>Hemisphere restriction.</summary>
    API_FIELD() QuadSphereFoliageHemisphere Hemisphere = QuadSphereFoliageHemisphere::Both;
    /// <summary>World-space streaming distance; zero disables spawning.</summary>
    API_FIELD(Attributes="Limit(0)") double SpawnDistance = 10000.0;
};

/// <summary>Deterministic instanced foliage placement settings.</summary>
API_STRUCT() struct FLAXENGINE_API QuadSphereFoliageConfig : ISerializable
{
    API_AUTO_SERIALIZATION();
    DECLARE_SCRIPTING_TYPE_MINIMAL(QuadSphereFoliageConfig);

    /// <summary>Model rendered by the foliage instances.</summary>
    API_FIELD() AssetReference<Model> Model;
    /// <summary>Shadow casting policy for this entry.</summary>
    API_FIELD() QuadSphereFoliageShadows Shadows = QuadSphereFoliageShadows::Legacy;
    /// <summary>Align instances to the ground normal instead of radial up.</summary>
    API_FIELD() bool AlignToGroundNormal = false;
    /// <summary>Eligible material and beach layers.</summary>
    API_FIELD() QuadSpherePrefabBiomes Biomes = QuadSpherePrefabBiomes::All;
    /// <summary>Placement attempts per million square local units.</summary>
    API_FIELD(Attributes="Limit(0, 1000000)") float Density = 1.0f;
    /// <summary>Probability of accepting an eligible placement.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float Probability = 1.0f;
    /// <summary>Noise type used for density and scale.</summary>
    API_FIELD() QuadSphereFoliageNoiseType NoiseType = QuadSphereFoliageNoiseType::Value;
    /// <summary>Frequency of foliage noise on the unit sphere.</summary>
    API_FIELD(Attributes="Limit(0, 4096)") float NoiseFrequency = 4.0f;
    /// <summary>Number of foliage noise octaves.</summary>
    API_FIELD(Attributes="Limit(1, 8)") int32 NoiseOctaves = 3;
    /// <summary>Offset added to the planet seed for foliage noise.</summary>
    API_FIELD() int32 NoiseSeedOffset = 0;
    /// <summary>Strength of noise modulation of placement probability.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float NoiseDensityStrength = 0.0f;
    /// <summary>Strength of noise modulation of instance scale.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float NoiseScaleStrength = 0.0f;
    /// <summary>Minimum radial altitude; zero disables the limit.</summary>
    API_FIELD() double MinAltitude = 0.0;
    /// <summary>Maximum radial altitude; zero disables the limit.</summary>
    API_FIELD() double MaxAltitude = MAX_double;
    /// <summary>Minimum allowed slope in degrees.</summary>
    API_FIELD(Attributes="Limit(0, 90)") float MinSlope = 0.0f;
    /// <summary>Maximum allowed slope in degrees.</summary>
    API_FIELD(Attributes="Limit(0, 90)") float MaxSlope = 90.0f;
    /// <summary>Fade density from full at minimum slope to zero at maximum slope.</summary>
    API_FIELD() bool SmoothSlope = false;
    /// <summary>Minimum temperature; 1 at the equator and 0 at the poles.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float MinTemperature = 0.0f;
    /// <summary>Maximum temperature; 1 at the equator and 0 at the poles.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float MaxTemperature = 1.0f;
    /// <summary>Hemisphere restriction.</summary>
    API_FIELD() QuadSphereFoliageHemisphere Hemisphere = QuadSphereFoliageHemisphere::Both;
    /// <summary>Minimum uniform instance scale.</summary>
    API_FIELD(Attributes="Limit(0.001f)") float MinScale = 1.0f;
    /// <summary>Maximum uniform instance scale.</summary>
    API_FIELD(Attributes="Limit(0.001f)") float MaxScale = 1.0f;
    /// <summary>World-space streaming distance; -1 uses the legacy actor setting.</summary>
    API_FIELD(Attributes="Limit(-1)") double SpawnDistance = -1.0;
    /// <summary>Maximum streamed cells; -1 uses only the actor-wide limit.</summary>
    API_FIELD(Attributes="Limit(-1, 4096)") int32 MaxCells = -1;
    /// <summary>World-space collision distance; -1 uses the legacy actor setting.</summary>
    API_FIELD(Attributes="Limit(-1)") double CollisionDistance = -1.0;
    /// <summary>Maximum active collision boxes; -1 uses the legacy actor setting.</summary>
    API_FIELD(Attributes="Limit(-1, 1024)") int32 MaxColliders = -1;
    /// <summary>Model-local collision box dimensions. A zero component disables collision.</summary>
    API_FIELD() Float3 CollisionSize = Float3::Zero;
    /// <summary>Model-local offset of the collision box center.</summary>
    API_FIELD() Float3 CollisionOffset = Float3::Zero;

    QuadSphereFoliageConfig() = default;
    explicit QuadSphereFoliageConfig(double spawnDistance)
        : SpawnDistance(spawnDistance)
    {
    }
};

/// <summary>Reusable prefab placement entry for a quad sphere.</summary>
API_CLASS(Attributes="ContentContextMenu(\"New/Terrain/Quad Sphere Prefab Entry\")") class FLAXENGINE_API QuadSpherePrefabAsset : public ScriptingObject, public ISerializable
{
    API_AUTO_SERIALIZATION();
    DECLARE_SCRIPTING_TYPE_WITH_CONSTRUCTOR_IMPL(QuadSpherePrefabAsset, ScriptingObject);

public:
    /// <summary>Placement and streaming settings for this prefab.</summary>
    API_FIELD() QuadSpherePrefabConfig Settings;
};

/// <summary>Reusable foliage placement entry for a quad sphere.</summary>
API_CLASS(Attributes="ContentContextMenu(\"New/Terrain/Quad Sphere Foliage Entry\")") class FLAXENGINE_API QuadSphereFoliageAsset : public ScriptingObject, public ISerializable
{
    API_AUTO_SERIALIZATION();
    DECLARE_SCRIPTING_TYPE_WITH_CONSTRUCTOR_IMPL(QuadSphereFoliageAsset, ScriptingObject);

public:
    /// <summary>Placement and streaming settings for this foliage.</summary>
    API_FIELD() QuadSphereFoliageConfig Settings = QuadSphereFoliageConfig(10000.0);
};

/// <summary>Quad-sphere terrain actor with an editor foliage preview.</summary>
API_CLASS(Attributes="ActorContextMenu(\"New/Terrain/Quad Sphere (Native)\"), ActorToolbox(\"Terrain\", \"Quad Sphere (Native)\")") class FLAXENGINE_API QuadSphereActor : public Actor
{
    API_AUTO_SERIALIZATION();
    DECLARE_SCENE_OBJECT(QuadSphereActor);

public:
    /// <summary>Actor used for LOD in play mode; defaults to the main camera when unset. In edit mode, the scene viewport is preferred.</summary>
    API_FIELD(Attributes="EditorOrder(300), EditorDisplay(\"LOD\")") ScriptingObjectReference<Actor> LodTarget;
    /// <summary>Material applied to terrain patches.</summary>
    API_FIELD(Attributes="EditorOrder(700), EditorDisplay(\"Rendering\")") AssetReference<MaterialBase> Material;
    /// <summary>Repeats per base UV unit used to rebase patch UVs; zero disables rebasing.</summary>
    API_FIELD(Attributes="EditorOrder(710), EditorDisplay(\"Rendering\")") float UvRebaseRepeat = 0.0f;
    /// <summary>Base planet radius in local units.</summary>
    API_FIELD(Attributes="EditorOrder(100), EditorDisplay(\"Planet\")") double Radius = 100000.0;
    /// <summary>Generate a sphere without terrain displacement or biome noise.</summary>
    API_FIELD(Attributes="EditorOrder(110), EditorDisplay(\"Planet\")") bool SimpleSphere = false;
    /// <summary>Whether to generate terrain collision in play mode.</summary>
    API_FIELD(Attributes="EditorOrder(120), EditorDisplay(\"Planet\")") bool GenerateCollision = true;
    /// <summary>Whether terrain patches cast shadows.</summary>
    API_FIELD(Attributes="EditorOrder(720), EditorDisplay(\"Rendering\")") bool CastShadows = true;
    /// <summary>Number of mesh subdivisions per side of a terrain patch.</summary>
    API_FIELD(Attributes="EditorOrder(200), EditorDisplay(\"Chunks\")") int32 PatchResolution = 16;
    /// <summary>Number of base chunks per side of each cube face.</summary>
    API_FIELD(Attributes="EditorOrder(210), EditorDisplay(\"Chunks\")") int32 ChunksPerPatch = 1;
    /// <summary>Maximum quadtree refinement level for terrain LOD.</summary>
    API_FIELD(Attributes="EditorOrder(310), EditorDisplay(\"LOD\"), Limit(0, 10)") int32 MaxLod = 5;
    /// <summary>Distance threshold for refinement as a multiple of chunk size.</summary>
    API_FIELD(Attributes="EditorOrder(320), EditorDisplay(\"LOD\")") float LodDistance = 2.0f;
    /// <summary>Interval in seconds between terrain LOD updates.</summary>
    API_FIELD(Attributes="EditorOrder(330), EditorDisplay(\"LOD\")") float LodUpdateInterval = 0.5f;
    /// <summary>Maximum concurrent geometry jobs; zero selects a worker count automatically.</summary>
    API_FIELD(Attributes="EditorOrder(800), EditorDisplay(\"Generation\"), Limit(0, 32)") int32 GenerationWorkers = 0;
    /// <summary>Approximate main-thread patch installation budget in milliseconds.</summary>
    API_FIELD(Attributes="EditorOrder(810), EditorDisplay(\"Generation\")") float PatchInstallBudgetMs = 3.0f;
    /// <summary>Maximum world-space distance to a base chunk's conservative bounds; zero disables render culling without affecting collision.</summary>
    API_FIELD(Attributes="EditorOrder(230), EditorDisplay(\"Chunks\")") double ChunkCullDistance = 0.0;
    /// <summary>Fraction of the culling distance added to the hide threshold to reduce flicker.</summary>
    API_FIELD(Attributes="EditorOrder(240), EditorDisplay(\"Chunks\"), Limit(0, 1, 0.01f)") double ChunkCullHysteresis = 0.05;
    /// <summary>Depth of patch edge skirts in local units.</summary>
    API_FIELD(Attributes="EditorOrder(220), EditorDisplay(\"Chunks\")") float SkirtDepth = 100.0f;
    /// <summary>Initial amplitude of terrain noise in local units.</summary>
    API_FIELD(Attributes="EditorOrder(400), EditorDisplay(\"Terrain Shape\")") float NoiseHeight = 1000.0f;
    /// <summary>Initial frequency of terrain noise.</summary>
    API_FIELD(Attributes="EditorOrder(410), EditorDisplay(\"Terrain Shape\")") float NoiseFrequency = 4.0f;
    /// <summary>Number of terrain noise octaves.</summary>
    API_FIELD(Attributes="EditorOrder(420), EditorDisplay(\"Terrain Shape\")") int32 NoiseOctaves = 4;
    /// <summary>Seed used to generate terrain noise.</summary>
    API_FIELD(Attributes="EditorOrder(430), EditorDisplay(\"Terrain Shape\")") int32 NoiseSeed = 12345;
    /// <summary>Number of generated biome layers stored in patch vertex colors.</summary>
    API_FIELD(Attributes="EditorOrder(500), EditorDisplay(\"Biomes\"), Limit(1, 4)") int32 BiomeLayerCount = 4;
    /// <summary>Noise settings for the first material layer.</summary>
    API_FIELD(Attributes="EditorOrder(520), EditorDisplay(\"Biomes\")") QuadSphereBiomeLayer BiomeLayer0 = QuadSphereBiomeLayer(0);
    /// <summary>Noise settings for the second material layer.</summary>
    API_FIELD(Attributes="EditorOrder(530), EditorDisplay(\"Biomes\")") QuadSphereBiomeLayer BiomeLayer1 = QuadSphereBiomeLayer(104729);
    /// <summary>Noise settings for the third material layer.</summary>
    API_FIELD(Attributes="EditorOrder(540), EditorDisplay(\"Biomes\")") QuadSphereBiomeLayer BiomeLayer2 = QuadSphereBiomeLayer(209458);
    /// <summary>Noise settings for the fourth material layer.</summary>
    API_FIELD(Attributes="EditorOrder(550), EditorDisplay(\"Biomes\")") QuadSphereBiomeLayer BiomeLayer3 = QuadSphereBiomeLayer(314187);
    /// <summary>Width of the transition between competing biome layers.</summary>
    API_FIELD(Attributes="EditorOrder(510), EditorDisplay(\"Biomes\"), Limit(0, 1, 0.01f)") float BiomeBlendWidth = 0.5f;
    /// <summary>Water surface radius measured from the planet center in local units.</summary>
    API_FIELD(Attributes="EditorOrder(600), EditorDisplay(\"Water and Beach\")") double WaterHeight = 100000.0;
    /// <summary>Radial half-width of the beach band around the water surface.</summary>
    API_FIELD(Attributes="EditorOrder(610), EditorDisplay(\"Water and Beach\")") double BeachWidth = 0.0;
    /// <summary>Strength of terrain flattening toward the water surface within the beach band.</summary>
    API_FIELD(Attributes="EditorOrder(620), EditorDisplay(\"Water and Beach\"), Limit(0, 1, 0.01f)") float BeachSmoothing = 0.5f;
    /// <summary>Reusable prefab entry JSON assets, sampled on base terrain. Rebuild after editing assets or this list.</summary>
    API_FIELD(Attributes="EditorOrder(900), EditorDisplay(\"Prefabs\"), AssetReference(\"FlaxEngine.QuadSpherePrefabAsset\")") Array<AssetReference<JsonAsset>> PrefabAssets;
    /// <summary>Maximum prefab placement attempts processed per update.</summary>
    API_FIELD(Attributes="EditorOrder(910), EditorDisplay(\"Prefabs\"), Limit(1, 100000)") int32 PrefabAttemptsPerUpdate = 128;
    /// <summary>Whether to report prefab placement and cleanup diagnostics.</summary>
    API_FIELD(Attributes="EditorOrder(920), EditorDisplay(\"Prefabs\"), NoSerialize") bool PrefabDiagnostics = true;
    /// <summary>Reusable foliage entry JSON assets placed on committed terrain. Rebuild after editing assets or this list.</summary>
    API_FIELD(Attributes="EditorOrder(1000), EditorDisplay(\"Foliage\"), AssetReference(\"FlaxEngine.QuadSphereFoliageAsset\")") Array<AssetReference<JsonAsset>> FoliageAssets;
    /// <summary>Show streamed foliage around the scene viewport outside play mode.</summary>
    API_FIELD(Attributes="EditorOrder(1010), EditorDisplay(\"Foliage\")") bool PreviewFoliageInEditor = true;
    /// <summary>Editor preview distance for entries using the legacy distance when that distance is zero. Zero disables this fallback.</summary>
    API_FIELD(Attributes="EditorOrder(1020), EditorDisplay(\"Foliage\"), Limit(0)") double EditorFoliagePreviewDistance = 10000.0;
    /// <summary>Legacy default foliage streaming distance.</summary>
    API_FIELD(Attributes="HideInEditor") double FoliageSpawnDistance = 0.0;
    /// <summary>Number of foliage cells per cube-face side.</summary>
    API_FIELD(Attributes="EditorOrder(1030), EditorDisplay(\"Foliage\"), Limit(1, 1024)") int32 FoliageCellsPerFace = 1024;
    /// <summary>Maximum simultaneously streamed foliage entry/cell pairs.</summary>
    API_FIELD(Attributes="EditorOrder(1040), EditorDisplay(\"Foliage\"), Limit(1, 4096)") int32 MaxFoliageCells = 512;
    /// <summary>Maximum deterministic foliage placement attempts per update.</summary>
    API_FIELD(Attributes="EditorOrder(1050), EditorDisplay(\"Foliage\"), Limit(1, 100000)") int32 FoliageAttemptsPerUpdate = 1024;
    /// <summary>Minimum seconds between intermediate cluster rebuilds for a dense cell. Zero publishes every installation batch; the first and final batches are always published immediately.</summary>
    API_FIELD(Attributes="EditorOrder(1060), EditorDisplay(\"Foliage\"), Limit(0)") float FoliageClusterRebuildInterval = 0.1f;
    /// <summary>Legacy default foliage shadow setting.</summary>
    API_FIELD(Attributes="HideInEditor") bool FoliageCastShadows = true;
    /// <summary>Legacy default foliage collision distance.</summary>
    API_FIELD(Attributes="HideInEditor") double FoliageCollisionDistance = 0.0;
    /// <summary>Legacy default maximum active foliage collision boxes per entry.</summary>
    API_FIELD(Attributes="HideInEditor") int32 MaxFoliageColliders = 64;

    /// <summary>Gets latitude, longitude and altitude from a world-space position.</summary>
    /// <returns>True on success; false for invalid input or transform.</returns>
    API_FUNCTION() bool GetCoordinates(API_PARAM(Ref) const Vector3& worldPosition, API_PARAM(Out) float& latitude,
                                       API_PARAM(Out) float& longitude, API_PARAM(Out) float& altitude) const;

    /// <summary>Rebuilds terrain after settings or brushes have changed.</summary>
    API_FUNCTION(Attributes="Button(\"Rebuild Terrain\", \"Update the planet preview after changing terrain settings.\")") void RebuildTerrain();

    /// <summary>Whether all committed terrain patches have active collision in play mode.</summary>
    API_PROPERTY(Attributes="HideInEditor, NoSerialize") bool GetIsTerrainReady() const;

    /// <summary>Whether committed terrain collision is ready in the direction of a world-space position.</summary>
    API_FUNCTION() bool IsGroundReady(API_PARAM(Ref) const Vector3& worldPosition) const;

    ~QuadSphereActor() override;
    void OnBeginPlay() override;
    void OnEndPlay() override;

private:
    QuadSphereRuntime* _runtime = nullptr;
    void UpdateTerrain();
};

/// <summary>
/// A non-destructive brush for quad-sphere terrain. Add each native quad sphere to Targets and place the influence volume
/// across its unbrushed surface. Simple Sphere disables brushes. Changes are applied asynchronously to affected patches;
/// brushes smaller than the terrain vertex spacing may not visibly deform the mesh.
/// </summary>
API_CLASS(Attributes="ActorContextMenu(\"New/Terrain/Quad Sphere Brush (Native)\"), ActorToolbox(\"Terrain\", \"Quad Sphere Brush (Native)\")") class FLAXENGINE_API QuadSphereBrush : public Actor
{
    DECLARE_SCENE_OBJECT(QuadSphereBrush);

public:
    /// <summary>Explicitly targeted native quad spheres. Brushes in prefabs spawned by a quad sphere also target their owner.</summary>
    API_FIELD() Array<ScriptingObjectReference<QuadSphereActor>> Targets;

    /// <summary>Brush operation.</summary>
    API_FIELD() QuadSphereBrushMode Mode = QuadSphereBrushMode::Carve;

    /// <summary>Optional material and foliage biome override.</summary>
    API_FIELD() QuadSphereBrushBiome BiomeOverride = QuadSphereBrushBiome::None;

    /// <summary>Shape of the brush influence volume.</summary>
    API_FIELD() QuadSphereBrushShape Shape = QuadSphereBrushShape::Circle;

    /// <summary>World-space radius or cube half-size. Actor scale does not affect this size.</summary>
    API_FIELD() float Radius = 1000.0f;

    /// <summary>Fraction of the radius used for a smooth edge.</summary>
    API_FIELD(Attributes="Limit(0, 1, 0.01f)") float Falloff = 0.25f;

    /// <summary>Amount by which the carve operation lowers terrain, in planet-local units.</summary>
    API_FIELD() float Depth = 100.0f;

    /// <summary>Order in which overlapping brushes are applied; ties are ordered by actor ID.</summary>
    API_FIELD() int32 Order = 0;

public:
    bool TargetsActor(const QuadSphereActor* actor) const;
    static void GetSquareAxes(const Vector3& position, const Vector3& planetCenter, const Quaternion& rotation,
                              Vector3& normal, Vector3& axisX, Vector3& axisY);

    void Serialize(SerializeStream& stream, const void* otherObj) override;
    void Deserialize(DeserializeStream& stream, ISerializeModifier* modifier) override;
#if USE_EDITOR
    void OnDebugDrawSelected() override;
#endif
};
