using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;
using FlaxEngine;

namespace Game;

/// <summary>Noise settings for one quad-sphere biome layer.</summary>
[Serializable]
public struct QuadSphereBiomeLayer
{
    /// <summary>Noise frequency on the unit sphere.</summary>
    [Limit(0, 4096), Tooltip("Noise frequency on the unit sphere. Higher values produce smaller regions.")]
    public float NoiseFrequency;
    /// <summary>Number of noise octaves.</summary>
    [Limit(1, 8)]
    public int NoiseOctaves;
    /// <summary>Offset added to the terrain noise seed.</summary>
    public int SeedOffset;
    /// <summary>Scales this layer's noise score relative to the other layers.</summary>
    [Limit(0, 16), Tooltip("Scales this layer's noise score relative to other layers.")]
    public float NoiseContrast;

    /// <summary>Creates default settings with the specified seed offset.</summary>
    public QuadSphereBiomeLayer(int seedOffset)
    {
        NoiseFrequency = 4.0f;
        NoiseOctaves = 3;
        SeedOffset = seedOffset;
        NoiseContrast = 2.0f;
    }
}

/// <summary>Shadow casting selection for a quad-sphere foliage entry.</summary>
public enum QuadSphereFoliageShadows
{
    /// <summary>Use the legacy actor-wide shadow setting.</summary>
    Legacy,
    /// <summary>Cast shadows.</summary>
    Enabled,
    /// <summary>Do not cast shadows.</summary>
    Disabled,
}

/// <summary>Hemispheres eligible for placement. Poles lie on the actor's local Y axis.</summary>
public enum QuadSphereFoliageHemisphere
{
    /// <summary>Both hemispheres.</summary>
    Both,
    /// <summary>Only the northern (positive Y) hemisphere.</summary>
    North,
    /// <summary>Only the southern (negative Y) hemisphere.</summary>
    South,
}

/// <summary>Noise used to modulate foliage density and scale.</summary>
public enum QuadSphereFoliageNoiseType
{
    /// <summary>Smooth value noise.</summary>
    Value,
    /// <summary>Three-dimensional gradient Perlin noise.</summary>
    Perlin,
    /// <summary>Perlin noise folded into sharp ridges.</summary>
    RidgedPerlin,
}

/// <summary>Deterministic instanced foliage placement settings.</summary>
[Serializable]
public struct QuadSphereFoliageConfig
{
    /// <summary>Model rendered by the foliage instances.</summary>
    public Model Model;
    /// <summary>Whether this foliage entry casts shadows.</summary>
    [DefaultValue(QuadSphereFoliageShadows.Legacy), Tooltip("Shadow casting for this foliage entry. Legacy uses the old actor-wide setting; new planets default to casting shadows.")]
    public QuadSphereFoliageShadows Shadows;
    /// <summary>Align instances to the terrain normal instead of the planet's radial direction.</summary>
    [DefaultValue(false), Tooltip("Align foliage to the ground normal. When disabled, foliage points away from the planet center. Rebuild terrain after changing.")]
    public bool AlignToGroundNormal;
    /// <summary>Eligible material and beach layers.</summary>
    [DefaultValue(QuadSpherePrefabBiomes.All)]
    public QuadSpherePrefabBiomes Biomes;
    /// <summary>Placement attempts per million square local units.</summary>
    [DefaultValue(1.0f), Limit(0, 1000000)]
    public float Density;
    /// <summary>Probability of accepting an eligible placement.</summary>
    [DefaultValue(1.0f), Limit(0, 1, 0.01f)]
    public float Probability;
    /// <summary>Noise type used for both density and scale.</summary>
    [DefaultValue(QuadSphereFoliageNoiseType.Value), Tooltip("Noise type for density and scale. Set a noise strength above zero to see its effect. Rebuild terrain after changing.")]
    public QuadSphereFoliageNoiseType NoiseType;
    /// <summary>Frequency of the foliage noise on the unit sphere.</summary>
    [DefaultValue(4.0f), Limit(0, 4096), Tooltip("Noise frequency on the unit sphere. Higher values produce smaller patches. Rebuild terrain after changing.")]
    public float NoiseFrequency;
    /// <summary>Number of foliage noise octaves.</summary>
    [DefaultValue(3), Limit(1, 8)]
    public int NoiseOctaves;
    /// <summary>Offset added to the planet seed for foliage noise.</summary>
    public int NoiseSeedOffset;
    /// <summary>Strength of noise modulation of placement probability.</summary>
    [DefaultValue(0.0f), Limit(0, 1, 0.01f), Tooltip("Noise strength for foliage density. Zero disables density noise; one uses the full noise value to reduce placement probability.")]
    public float NoiseDensityStrength;
    /// <summary>Strength of noise modulation of instance scale.</summary>
    [DefaultValue(0.0f), Limit(0, 1, 0.01f), Tooltip("Blend random scale toward the noise value within Min Scale and Max Scale. Zero keeps random scale; one uses noise alone.")]
    public float NoiseScaleStrength;
    /// <summary>Minimum radial altitude; zero disables the limit.</summary>
    public double MinAltitude;
    /// <summary>Maximum radial altitude; zero disables the limit.</summary>
    [DefaultValue(double.MaxValue)]
    public double MaxAltitude;
    /// <summary>Minimum allowed slope in degrees.</summary>
    [Limit(0, 90)]
    public float MinSlope;
    /// <summary>Maximum allowed slope in degrees.</summary>
    [DefaultValue(90.0f), Limit(0, 90)]
    public float MaxSlope;
    /// <summary>Fade density from full at the minimum slope to zero at the maximum slope.</summary>
    [DefaultValue(false), Tooltip("Fade foliage density linearly from full at Min Slope to zero at Max Slope. Slopes outside the range are excluded; equal limits retain the hard cutoff. Rebuild terrain after changing.")]
    public bool SmoothSlope;
    /// <summary>Minimum temperature; 1 at the equator and 0 at the poles.</summary>
    [DefaultValue(0.0f), Limit(0, 1, 0.01f), Tooltip("Minimum temperature. Temperature is 1 at the equator (local Y = 0) and falls linearly with latitude to 0 at either pole. Rebuild terrain after changing.")]
    public float MinTemperature;
    /// <summary>Maximum temperature; 1 at the equator and 0 at the poles.</summary>
    [DefaultValue(1.0f), Limit(0, 1, 0.01f), Tooltip("Maximum temperature. Temperature is 1 at the equator (local Y = 0) and falls linearly with latitude to 0 at either pole. Rebuild terrain after changing.")]
    public float MaxTemperature;
    /// <summary>Hemisphere restriction.</summary>
    [DefaultValue(QuadSphereFoliageHemisphere.Both), Tooltip("Restrict foliage to the northern (+Y) or southern (-Y) hemisphere. Rebuild terrain after changing.")]
    public QuadSphereFoliageHemisphere Hemisphere;
    /// <summary>Minimum uniform instance scale.</summary>
    [DefaultValue(1.0f), Limit(0.001f)]
    public float MinScale;
    /// <summary>Maximum uniform instance scale.</summary>
    [DefaultValue(1.0f), Limit(0.001f)]
    public float MaxScale;
    /// <summary>World-space streaming and render distance; -1 uses the legacy actor setting.</summary>
    [DefaultValue(-1.0), Limit(-1), Tooltip("World-space streaming and render distance from the main camera. Zero disables this entry; -1 uses the legacy actor setting.")]
    public double SpawnDistance;
    /// <summary>Maximum streamed cells for this entry; -1 uses only the actor-wide limit.</summary>
    [DefaultValue(-1), Limit(-1, 4096), Tooltip("Maximum streamed cells for this entry, nearest first. Zero disables the entry; -1 uses only the actor-wide Max Foliage Cells limit. Rebuild terrain after changing.")]
    public int MaxCells;
    /// <summary>World-space collision distance; -1 uses the legacy actor setting.</summary>
    [DefaultValue(-1.0), Limit(-1), Tooltip("World-space distance for nearby box collision. Zero disables collision; -1 uses the legacy actor setting.")]
    public double CollisionDistance;
    /// <summary>Maximum active collision boxes for this entry; -1 uses the legacy actor setting.</summary>
    [DefaultValue(-1), Limit(-1, 1024), Tooltip("Maximum nearby collision boxes for this entry. Zero disables collision; -1 uses the legacy actor setting.")]
    public int MaxColliders;
    /// <summary>Local size of the optional collision box. A zero component disables collision.</summary>
    [Tooltip("Optional model-local box dimensions. Set all components positive to enable nearby collision; zero disables it.")]
    public Float3 CollisionSize;
    /// <summary>Model-local offset of the collision box center.</summary>
    public Float3 CollisionOffset;

    /// <summary>Creates default placement settings.</summary>
    public QuadSphereFoliageConfig()
    {
        Model = null;
        Shadows = QuadSphereFoliageShadows.Legacy;
        AlignToGroundNormal = false;
        Biomes = QuadSpherePrefabBiomes.All;
        Density = 1.0f;
        Probability = 1.0f;
        NoiseType = QuadSphereFoliageNoiseType.Value;
        NoiseFrequency = 4.0f;
        NoiseOctaves = 3;
        NoiseSeedOffset = 0;
        NoiseDensityStrength = 0;
        NoiseScaleStrength = 0;
        MinAltitude = 0;
        MaxAltitude = double.MaxValue;
        MinSlope = 0;
        MaxSlope = 90;
        SmoothSlope = false;
        MinTemperature = 0;
        MaxTemperature = 1;
        Hemisphere = QuadSphereFoliageHemisphere.Both;
        MinScale = 1;
        MaxScale = 1;
        SpawnDistance = -1;
        MaxCells = -1;
        CollisionDistance = -1;
        MaxColliders = -1;
        CollisionSize = Float3.Zero;
        CollisionOffset = Float3.Zero;
    }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        Shadows = QuadSphereFoliageShadows.Legacy;
        AlignToGroundNormal = false;
        SmoothSlope = false;
        MinTemperature = 0;
        MaxTemperature = 1;
        Hemisphere = QuadSphereFoliageHemisphere.Both;
        NoiseType = QuadSphereFoliageNoiseType.Value;
        NoiseFrequency = 4.0f;
        NoiseOctaves = 3;
        NoiseSeedOffset = 0;
        NoiseDensityStrength = 0;
        NoiseScaleStrength = 0;
        SpawnDistance = -1;
        MaxCells = -1;
        CollisionDistance = -1;
        MaxColliders = -1;
    }
}

/// <summary>Biome layers eligible for prefab placement.</summary>
[Flags]
public enum QuadSpherePrefabBiomes
{
    /// <summary>
    /// 
    /// </summary>
    None = 0,
    /// <summary>
    /// 
    /// </summary>
    Layer0 = 1,
    /// <summary>
    /// 
    /// </summary>
    Layer1 = 2,
    /// <summary>
    /// 
    /// </summary>
    Layer2 = 4,
    /// <summary>
    /// 
    /// </summary>
    Layer3 = 8,
    /// <summary>
    /// 
    /// </summary>
    Beach = 16,
    All = Layer0 | Layer1 | Layer2 | Layer3 | Beach,
}

/// <summary>Deterministic placement settings for one prefab.</summary>
[Serializable]
public struct QuadSpherePrefabConfig
{
    /// <summary>Prefab to instantiate.</summary>
    public Prefab Prefab;
    /// <summary>Eligible material and beach layers.</summary>
    [DefaultValue(QuadSpherePrefabBiomes.All)]
    public QuadSpherePrefabBiomes Biomes;
    /// <summary>Placement attempts per million square local units of the base sphere.</summary>
    [DefaultValue(1.0f), Limit(0, 1000000), Tooltip("Placement attempts per 1,000,000 square local units of the base sphere, before probability and terrain filters.")]
    public float Density;
    /// <summary>Probability of accepting an eligible placement.</summary>
    [DefaultValue(1.0f), Limit(0, 1, 0.01f)]
    public float Probability;
    /// <summary>Frequency of the optional placement noise on the unit sphere.</summary>
    [DefaultValue(4.0f), Limit(0, 4096)]
    public float NoiseFrequency;
    /// <summary>Number of placement noise octaves.</summary>
    [DefaultValue(3), Limit(1, 8)]
    public int NoiseOctaves;
    /// <summary>Offset added to the planet seed for placement noise.</summary>
    public int SeedOffset;
    /// <summary>Strength of noise modulation; zero disables it.</summary>
    [Limit(0, 1, 0.01f)]
    public float NoiseStrength;
    /// <summary>Minimum distance from the planet center, in local units; zero disables this limit.</summary>
    [Tooltip("Minimum distance from the planet center in local units. Zero disables this limit.")]
    public double MinAltitude;
    /// <summary>Maximum distance from the planet center, in local units; zero disables this limit.</summary>
    [DefaultValue(double.MaxValue), Tooltip("Maximum distance from the planet center in local units. Zero disables this limit.")]
    public double MaxAltitude;
    /// <summary>Distance inside either altitude boundary over which probability fades in.</summary>
    [Limit(0, float.MaxValue)]
    public float AltitudeFalloff;
    /// <summary>Minimum allowed terrain slope in degrees from radial up; zero is flat on a sphere.</summary>
    [Limit(0, 90)]
    public float MinSlope;
    /// <summary>Maximum allowed terrain slope in degrees from radial up.</summary>
    [DefaultValue(90.0f), Limit(0, 90)]
    public float MaxSlope;
    /// <summary>Minimum temperature; 1 at the equator and 0 at the poles.</summary>
    [DefaultValue(0.0f), Limit(0, 1, 0.01f), Tooltip("Minimum temperature. Temperature is 1 at the equator (local Y = 0) and falls linearly with latitude to 0 at either pole. Rebuild terrain after changing.")]
    public float MinTemperature;
    /// <summary>Maximum temperature; 1 at the equator and 0 at the poles.</summary>
    [DefaultValue(1.0f), Limit(0, 1, 0.01f), Tooltip("Maximum temperature. Temperature is 1 at the equator (local Y = 0) and falls linearly with latitude to 0 at either pole. Rebuild terrain after changing.")]
    public float MaxTemperature;
    /// <summary>Hemisphere restriction.</summary>
    [DefaultValue(QuadSphereFoliageHemisphere.Both), Tooltip("Restrict prefabs to the northern (+Y) or southern (-Y) hemisphere. Rebuild terrain after changing.")]
    public QuadSphereFoliageHemisphere Hemisphere;

    /// <summary>Creates default placement settings.</summary>
    public QuadSpherePrefabConfig()
    {
        Prefab = null;
        Biomes = QuadSpherePrefabBiomes.All;
        Density = 1.0f;
        Probability = 1.0f;
        NoiseFrequency = 4.0f;
        NoiseOctaves = 3;
        SeedOffset = 0;
        NoiseStrength = 0.0f;
        MinAltitude = 0.0;
        MaxAltitude = double.MaxValue;
        AltitudeFalloff = 0.0f;
        MinSlope = 0.0f;
        MaxSlope = 90.0f;
        MinTemperature = 0;
        MaxTemperature = 1;
        Hemisphere = QuadSphereFoliageHemisphere.Both;
    }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        MinTemperature = 0;
        MaxTemperature = 1;
        Hemisphere = QuadSphereFoliageHemisphere.Both;
    }
}

/// <summary>A quad-sphere planet with distance-based terrain detail and an editor preview.</summary>
public partial class QuadSphereActor : Actor
{
    /// <summary>Actor used for LOD in play mode; defaults to the main camera when unset.</summary>
    [Tooltip("Actor used for LOD in play mode. If unset, the main camera is used. In editor view mode, LOD follows the scene viewport camera.")]
    public Actor LodTarget;
    /// <summary>Material applied to terrain patches.</summary>
    public MaterialBase Material;
    /// <summary>Repeats per base UV unit used to rebase patch UVs; zero disables rebasing.</summary>
    [Tooltip("Optional UV precision improvement for wrapping textures. Set to the material's UV tiling multiplier (for example 128 for Texcoords * 128), or zero to disable. All uses of patch UVs must repeat at this period; clamp samplers and nonperiodic UV effects are not compatible. Rebuild terrain after changing.")]
    public float UvRebaseRepeat = 0.0f;
    /// <summary>Base planet radius in local units.</summary>
    public double Radius = 100000.0;
    /// <summary>Generate a sphere without terrain displacement or biome noise.</summary>
    [Tooltip("Generate a sphere without terrain noise, heightmap, brushes, or biome generation. Layer 0 is used as a constant material fallback; LOD and collision still apply.")]
    public bool SimpleSphere;

    /// <summary>Whether to generate terrain collision in play mode.</summary>
    [Tooltip("Generate collision for terrain patches in play mode. Changing this rebuilds the terrain.")]
    public bool GenerateCollision = true;
    /// <summary>Whether terrain patches cast shadows.</summary>
    [Tooltip("Whether terrain patches cast shadows.")]
    public bool CastShadows = true;
    /// <summary>Number of mesh subdivisions per side of a terrain patch.</summary>
    public int PatchResolution = 16;
    /// <summary>Number of base chunks per side of each cube face.</summary>
    [Tooltip("Number of base chunks per side of each cube face. Rebuild terrain after changing. Each chunk has its own LOD tree and mesh resolution.")]
    public int ChunksPerPatch = 1;
    /// <summary>Maximum quadtree refinement level for terrain LOD.</summary>
    [Limit(0, 10), Tooltip("Maximum quadtree refinement level (0-10). More levels add mesh detail near the LOD target, not new noise features.")]
    public int MaxLod = 5;
    /// <summary>Distance threshold for refinement, expressed as a multiple of chunk size.</summary>
    [Tooltip("Refine a chunk when the target is within this multiple of the chunk's approximate size from its surface center.")]
    public float LodDistance = 2.0f;
    /// <summary>Interval in seconds between terrain LOD updates.</summary>
    public float LodUpdateInterval = 0.5f;
    /// <summary>Maximum concurrent geometry jobs; zero selects a worker count automatically.</summary>
    [Limit(0, 32), Tooltip("Maximum concurrent terrain geometry jobs. Zero automatically chooses up to 8 workers.")]
    public int GenerationWorkers = 0;
    /// <summary>Approximate main-thread patch installation budget in milliseconds per update.</summary>
    [Tooltip("Approximate main-thread time budget in milliseconds for installing completed terrain patches per update. At least one patch can be installed.")]
    public float PatchInstallBudgetMs = 3.0f;
    /// <summary>Maximum world-space distance for rendering base chunks; zero disables culling.</summary>
    [Tooltip("Maximum world-space distance from the LOD target to a base chunk's conservative bounds. All its LOD patches share visibility. Zero disables render culling; collision remains active.")]
    public double ChunkCullDistance = 0.0;
    /// <summary>Fraction of the culling distance added to the hide threshold to reduce flicker.</summary>
    [Limit(0, 1, 0.01f), Tooltip("Fraction of Chunk Cull Distance added to the hide threshold to reduce flicker near the boundary. Zero disables hysteresis.")]
    public double ChunkCullHysteresis = 0.05;
    /// <summary>Depth of patch edge skirts in local units.</summary>
    [Tooltip("Depth of patch edge skirts in local units. Increase this to cover larger LOD height differences.")]
    public float SkirtDepth = 100.0f;

    /// <summary>Initial amplitude of the terrain noise in local units.</summary>
    public float NoiseHeight = 1000.0f;
    /// <summary>Initial frequency of the terrain noise.</summary>
    public float NoiseFrequency = 4.0f;
    /// <summary>Number of terrain noise octaves.</summary>
    public int NoiseOctaves = 4;
    /// <summary>Seed used to generate terrain noise.</summary>
    public int NoiseSeed = 12345;

    /// <summary>Number of generated biome layers, stored in the patch vertex colors.</summary>
    [Limit(1, 4), Tooltip("Number of biome layers (1-4). Use Quad Sphere Layer Weight nodes in a surface material; layers use vertex colors and follow patch mesh resolution. Rebuild terrain after changing.")]
    public int BiomeLayerCount = 4;
    /// <summary>Noise settings for the first material layer.</summary>
    [Tooltip("Layer 0 noise settings. Rebuild terrain after changing.")]
    public QuadSphereBiomeLayer BiomeLayer0 = new QuadSphereBiomeLayer(0);
    /// <summary>Noise settings for the second material layer.</summary>
    [Tooltip("Layer 1 noise settings. Rebuild terrain after changing.")]
    public QuadSphereBiomeLayer BiomeLayer1 = new QuadSphereBiomeLayer(104729);
    /// <summary>Noise settings for the third material layer.</summary>
    [Tooltip("Layer 2 noise settings. Rebuild terrain after changing.")]
    public QuadSphereBiomeLayer BiomeLayer2 = new QuadSphereBiomeLayer(209458);
    /// <summary>Noise settings for the fourth material layer.</summary>
    [Tooltip("Layer 3 noise settings. Rebuild terrain after changing.")]
    public QuadSphereBiomeLayer BiomeLayer3 = new QuadSphereBiomeLayer(314187);
    /// <summary>Width of the transition between competing biome layers.</summary>
    [Limit(0, 1, 0.01f), Tooltip("Score range blended with the strongest biome layer: zero selects one layer. Rebuild terrain after changing.")]
    public float BiomeBlendWidth = 0.5f;

    /// <summary>Water surface radius measured from the planet center in local units.</summary>
    [Tooltip("Distance from the planet center to the water surface in local units, not an offset from Radius. Rebuild terrain after changing.")]
    public double WaterHeight = 100000.0;
    /// <summary>Radial half-width of the beach band around the water surface.</summary>
    [Tooltip("Radial height range on each side of the water surface used for the beach layer. Zero disables beaches. Rebuild terrain after changing.")]
    public double BeachWidth = 0.0;
    /// <summary>Strength of terrain flattening toward the water surface within the beach band.</summary>
    [Limit(0, 1, 0.01f), Tooltip("How strongly the beach band flattens toward water height. Zero keeps terrain unchanged. Rebuild terrain after changing.")]
    public float BeachSmoothing = 0.5f;

    /// <summary>Uncompressed equirectangular texture used for global terrain height.</summary>
    [Tooltip("Uncompressed equirectangular heightmap; the red channel is multiplied by Heightmap Height.")]
    public Texture Heightmap;
    /// <summary>Multiplier applied to the global heightmap's red channel.</summary>
    public float HeightmapHeight = 1000.0f;
    /// <summary>Prefab placement entries. Prefabs use base terrain and their brushes affect this planet. Rebuild after changing these settings.</summary>
    [Tooltip("Prefabs spawn in play mode on the base terrain, before brushes and foliage. Brushes inside spawned prefabs automatically affect this planet. Rebuild terrain after changing these settings.")]
    public QuadSpherePrefabConfig[] PrefabConfigs = Array.Empty<QuadSpherePrefabConfig>();
    /// <summary>Maximum world-space distance from the main camera to spawned prefabs; zero disables spawning.</summary>
    [Tooltip("Maximum world-space distance from the main camera to spawned prefabs. Zero disables spawning; no prefabs spawn without a main camera.")]
    public double PrefabSpawnDistance = 0.0;
    /// <summary>Maximum prefab placement attempts processed per update.</summary>
    [Limit(1, 100000)]
    public int PrefabAttemptsPerUpdate = 128;
    /// <summary>Temporarily report prefab placement and cleanup diagnostics in play mode.</summary>
    [NoSerialize, Tooltip("Log prefab placement and cleanup diagnostics every five seconds in play mode. Turn off to stop logging.")]
    public bool PrefabDiagnostics = true;
    /// <summary>Instanced foliage entries placed on committed terrain patches. Rebuild terrain after changing these settings.</summary>
    [Tooltip("Instanced foliage entries placed on the committed terrain mesh, including brushes. Placements refresh when their terrain LOD changes. Rebuild terrain after changing. Optional box collision is enabled per entry.")]
    public QuadSphereFoliageConfig[] FoliageConfigs = Array.Empty<QuadSphereFoliageConfig>();
    /// <summary>Legacy default foliage streaming distance.</summary>
    [HideInEditor]
    public double FoliageSpawnDistance = 0.0;
    /// <summary>Number of foliage cells per cube-face side.</summary>
    [Limit(1, 1024), Tooltip("Number of foliage cells per cube-face side (1-1024). Rebuild terrain after changing. More cells reduce streaming granularity but increase actor count.")]
    public int FoliageCellsPerFace = 1024;
    /// <summary>Maximum simultaneously streamed foliage entry/cell pairs across all entries.</summary>
    [Limit(1, 4096), Tooltip("Total maximum streamed foliage entry/cell pairs across all entries, in addition to each entry's Max Cells limit. The closest are prioritized.")]
    public int MaxFoliageCells = 512;
    /// <summary>Maximum deterministic foliage placement attempts per update.</summary>
    [Limit(1, 100000), Tooltip("Maximum foliage placement attempts per update. Lower values reduce per-frame work but slow streaming.")]
    public int FoliageAttemptsPerUpdate = 1024;
    /// <summary>Legacy default foliage shadow setting.</summary>
    [HideInEditor]
    public bool FoliageCastShadows = true;
    /// <summary>Legacy default foliage collision distance.</summary>
    [HideInEditor]
    public double FoliageCollisionDistance = 0.0;
    /// <summary>Legacy default maximum active foliage collision boxes per entry.</summary>
    [HideInEditor]
    public int MaxFoliageColliders = 64;
    private readonly Dictionary<(int Face, int Level, int X, int Y), Patch> _patches = new();
    private readonly Dictionary<int, PrefabChunk> _prefabChunks = new();
    private readonly HashSet<Guid> _spawnedPrefabRoots = new();
    private readonly HashSet<Guid> _invalidBrushBiomes = new();
    private readonly Dictionary<(int Cell, int Entry), FoliageCell> _foliageCells = new();
    private readonly List<(int Cell, int Entry)> _foliageCellOrder = new();
    private readonly List<BoxCollider> _foliageColliderPool = new();
    private readonly Dictionary<(int Cell, int Entry, int Index), BoxCollider> _foliageActiveColliders = new();
    private QuadSphereFoliageConfig[] _foliageConfigs = Array.Empty<QuadSphereFoliageConfig>();
    private int _foliageDivisions;
    private float _foliageSelectionElapsed;
    private float _foliageCollisionElapsed;
    private int _foliageCellLimit;
    private bool _foliageCellsLimited;
    private bool _foliageCastShadows;
    private QuadSpherePrefabConfig[] _prefabConfigs = Array.Empty<QuadSpherePrefabConfig>();
    private TerrainSnapshot _prefabSnapshot;
    private TerrainSnapshot _foliageSnapshot;
    private bool _prefabBrushesDirty;
    private bool _foliageSurfaceErrorLogged;
    private float _prefabDiagnosticElapsed = 5.0f;
    private PrefabDiagnosticCounts _prefabDiagnosticCounts;
    private readonly List<Patch> _retired = new();
    private readonly Dictionary<(int Face, int Level, int X, int Y), Patch> _previousBrushPatches = new();
    private Vector3[] _cullCenters;
    private bool[] _cullVisible;
    private double _cullRadius;
    private double _previousCullRadius;
    private HeightImage _heightmap;
    private BrushData[] _brushes = Array.Empty<BrushData>();
    private Transform _brushTransform;
    private BrushData[] _previousBrushes;
    private Transform _previousBrushTransform;
    private BrushData[] _failedBrushes;
    private Transform _failedBrushTransform;
    private double _baseElevationBound;
    private float _brushCheckElapsed;
    private bool _initialRebuildPending;
    private bool _running;
    private bool _ready;
    private bool _hasCompletedLod;
    private bool _generateCollision;
    private bool _castShadows;
    private float _elapsed;
    private double _radius;
    private int _resolution;
    private int _chunksPerPatch;
    private float _skirtDepth;
    private float _noiseHeight;
    private float _noiseFrequency;
    private int _noiseOctaves;
    private int _noiseSeed;
    private float _heightmapHeight;
    private float _uvRebaseRepeat;
    private bool _simpleSphere;
    private int _biomeLayerCount;
    private QuadSphereBiomeLayer[] _biomeLayers;
    private float _biomeBlendWidth;
    private double _waterHeight, _beachWidth;
    private float _beachSmoothing;
    private HashSet<(int Face, int Level, int X, int Y)> _desired;
    private List<(int Face, int Level, int X, int Y)> _pendingKeys;
    private int _pendingIndex;
    private TerrainSnapshot _snapshot;
    private CancellationTokenSource _generationCancellation;
    private readonly List<Task<PatchGeometry>> _generation = new();

    private sealed class TerrainSnapshot
    {
        public double Radius;
        public float SkirtDepth, NoiseHeight, NoiseFrequency, HeightmapHeight, UvRebaseRepeat;
        public int Resolution, ChunksPerPatch, NoiseOctaves, NoiseSeed;
        public bool SimpleSphere;
        public int BiomeLayerCount;
        public QuadSphereBiomeLayer[] BiomeLayers;
        public float BiomeBlendWidth;
        public double WaterHeight, BeachWidth;
        public float BeachSmoothing;
        public HeightImage Heightmap;
        public BrushData[] Brushes;
        public Transform PlanetTransform;
    }

    private sealed class FoliageCell
    {
        public readonly List<FoliagePlacement> Placements = new();
        public readonly Dictionary<Model, int> Types = new();
        public Foliage Renderer;
        public int Entry, Attempt, AttemptCount = -1, Installed;
        public bool GenerationComplete, Complete;
    }

    private struct FoliagePlacement
    {
        public Transform Transform;
        public int Entry;
    }

    private struct FoliageCollisionCandidate
    {
        public (int Cell, int Entry, int Index) Key;
        public FoliagePlacement Placement;
        public double Distance;
    }

    private struct FoliageSearchRegion
    {
        public int Face, Entry, MinX, MaxX, MinY, MaxY;
        public double MaxDistance;
    }

    private sealed class PatchGeometry
    {
        public (int Face, int Level, int X, int Y) Key;
        public Double3 Origin;
        public Float3[] Vertices, Normals, Tangents;
        public Float2[] UVs;
        public Color32[] BiomeWeights;
        public int[] Indices;
    }

    private sealed class Patch
    {
        public StaticModel Renderer;
        public MeshCollider Collider;
        public Double3 Origin;
        public Float3[] Vertices;
        public Model Model;
        public CollisionData Collision;
        public Task<bool> Cooking;
        public bool CollisionAssigned;
        public bool Committed;
    }

    private sealed class PrefabChunk
    {
        public readonly List<PrefabCandidate> Candidates = new();
        public int Entry, Attempt, AttemptCount = -1;
    }

    private struct PrefabCandidate
    {
        public Vector3 Surface;
        public int Entry;
        public float Yaw;
        public Actor Actor;
        public Guid ActorId;
        // Avoid retrying a failed spawn or an externally destroyed actor until re-entry.
        public bool SpawnAttempted;
    }

    private struct PrefabDiagnosticCounts
    {
        public long Attempts, Area, Hemisphere, Temperature, Altitude, Slope, Biome, Probability, Radial, SpawnFailed, Spawned, Destroyed;
    }

    private sealed class HeightImage
    {
        public Color[] Pixels;
        public int Width;
        public int Height;

        public float Sample(float u, float v, bool wrap)
        {
            u = wrap ? u - (float)Math.Floor(u) : Math.Clamp(u, 0.0f, 1.0f);
            v = Math.Clamp(v, 0.0f, 1.0f);
            float x = u * (wrap ? Width : Width - 1);
            float y = v * (Height - 1);
            int x0 = (int)x;
            int y0 = (int)y;
            int x1 = wrap ? (x0 + 1) % Width : Math.Min(x0 + 1, Width - 1);
            int y1 = Math.Min(y0 + 1, Height - 1);
            float tx = x - x0;
            float ty = y - y0;
            float a = Pixels[y0 * Width + x0].R * (1 - tx) + Pixels[y0 * Width + x1].R * tx;
            float b = Pixels[y1 * Width + x0].R * (1 - tx) + Pixels[y1 * Width + x1].R * tx;
            return a * (1 - ty) + b * ty;
        }
    }

    private struct BrushInfluence
    {
        public BrushData Brush;
        public Transform PlanetTransform;
    }

    private sealed class BrushData
    {
        public Guid Id;
        public int Order;
        public QuadSphereBrushMode Mode;
        public QuadSphereBrushShape Shape;
        public QuadSphereBrushBiome BiomeOverride;
        public Vector3 Position, AxisX, AxisY, AxisNormal;
        public float Radius, Falloff, Depth;
        public double FlattenHeight;
    }

    /// <summary>Whether the first complete terrain has usable collision on every committed patch. False when collision is disabled.</summary>
    [HideInEditor, NoSerialize]
    public bool IsTerrainReady
    {
        get
        {
            if (!GenerateCollision || !_generateCollision || !_running || !_ready || !_hasCompletedLod || !Engine.IsPlayMode || !IsActiveInHierarchy)
                return false;
            foreach (var patch in _patches.Values)
                if (patch.Committed && !HasCollision(patch))
                    return false;
            return true;
        }
    }

    /// <summary>Converts a world position to planet-local latitude, longitude, and altitude.</summary>
    /// <param name="worldPosition">Position in world space.</param>
    /// <param name="latitude">Latitude in degrees, from -90 at local -Y to 90 at local +Y.</param>
    /// <param name="longitude">Longitude in degrees, from -180 to 180; zero is local +X and positive 90 is local +Z. At a pole, longitude is zero.</param>
    /// <param name="altitude">Signed distance in planet-local units above the configured base <see cref="Radius"/>, not the terrain surface.</param>
    /// <exception cref="ArgumentException">The position is non-finite, at the planet center, or produces an altitude outside the float range.</exception>
    /// <exception cref="InvalidOperationException">The planet radius or transform is invalid.</exception>
    public void GetCoordinates(Vector3 worldPosition, out float latitude, out float longitude, out float altitude)
    {
        var transform = Transform;
        if (!double.IsFinite(Radius) || Radius <= 0 ||
            !double.IsFinite(transform.Translation.X) || !double.IsFinite(transform.Translation.Y) || !double.IsFinite(transform.Translation.Z) ||
            !float.IsFinite(transform.Orientation.X) || !float.IsFinite(transform.Orientation.Y) || !float.IsFinite(transform.Orientation.Z) || !float.IsFinite(transform.Orientation.W) ||
            !float.IsFinite(transform.Scale.X) || !float.IsFinite(transform.Scale.Y) || !float.IsFinite(transform.Scale.Z) ||
            transform.Scale.X == 0 || transform.Scale.Y == 0 || transform.Scale.Z == 0)
            throw new InvalidOperationException("Quad sphere coordinates require a positive finite radius and a finite transform with nonzero scale.");
        if (!double.IsFinite(worldPosition.X) || !double.IsFinite(worldPosition.Y) || !double.IsFinite(worldPosition.Z))
            throw new ArgumentException("World position must be finite.", nameof(worldPosition));

        var local = (Double3)transform.WorldToLocal(worldPosition);
        double distance = local.Length;
        if (!double.IsFinite(distance) || distance == 0)
            throw new ArgumentException("World position must have a finite, nonzero distance from the planet center in local space.", nameof(worldPosition));
        double height = distance - Radius;
        if (!double.IsFinite(height) || height > float.MaxValue || height < -float.MaxValue)
            throw new ArgumentException("Altitude is outside the float range.", nameof(worldPosition));

        double horizontal = Math.Sqrt(local.X * local.X + local.Z * local.Z);
        latitude = (float)(Math.Atan2(local.Y, horizontal) * (180.0 / Math.PI));
        longitude = horizontal == 0 ? 0 : (float)(Math.Atan2(local.Z, local.X) * (180.0 / Math.PI));
        altitude = (float)height;
    }

    /// <summary>Checks whether the committed terrain patch in the direction of a world-space position has collision ready. False when collision is disabled.</summary>
    public bool IsGroundReady(Vector3 worldPosition)
    {
        if (!GenerateCollision || !_generateCollision || !_running || !_ready || !_hasCompletedLod || !Engine.IsPlayMode || !IsActiveInHierarchy)
            return false;
        Vector3 local = Transform.WorldToLocal(worldPosition);
        double ax = Math.Abs(local.X), ay = Math.Abs(local.Y), az = Math.Abs(local.Z);
        if (!double.IsFinite(ax) || !double.IsFinite(ay) || !double.IsFinite(az) || Math.Max(ax, Math.Max(ay, az)) < 0.000001)
            return false;

        int face;
        double u, v;
        if (ax >= ay && ax >= az)
        {
            face = local.X >= 0 ? 0 : 1;
            u = (face == 0 ? -local.Z : local.Z) / ax;
            v = local.Y / ax;
        }
        else if (ay >= az)
        {
            face = local.Y >= 0 ? 2 : 3;
            u = local.X / ay;
            v = (face == 2 ? -local.Z : local.Z) / ay;
        }
        else
        {
            face = local.Z >= 0 ? 4 : 5;
            u = (face == 4 ? local.X : -local.X) / az;
            v = local.Y / az;
        }

        for (int level = 0; level <= 10; level++)
        {
            int divisions = _chunksPerPatch << level;
            int x = Math.Clamp((int)((u + 1.0) * 0.5 * divisions), 0, divisions - 1);
            int y = Math.Clamp((int)((v + 1.0) * 0.5 * divisions), 0, divisions - 1);
            if (_patches.TryGetValue((face, level, x, y), out var patch) && patch.Committed && HasCollision(patch))
                return true;
        }
        return false;
    }

    private static bool HasCollision(Patch patch) => patch.CollisionAssigned && patch.Collider != null &&
        patch.Collider.IsActiveInHierarchy && patch.Collision != null && patch.Collision.IsLoaded &&
        patch.Collider.CollisionData == patch.Collision;

    /// <summary>Starts terrain updates and rebuilds the terrain.</summary>
    public override void OnBeginPlay()
    {
        base.OnBeginPlay();
        _running = true;
        Scripting.Update += UpdateTerrain;
        // Defer scene enumeration until the scene has finished starting.
        _initialRebuildPending = true;
    }

    /// <summary>Stops terrain updates and clears terrain patches.</summary>
    public override void OnEndPlay()
    {
        Scripting.Update -= UpdateTerrain;
        _running = false;
        _initialRebuildPending = false;
        long destroyedBefore = _prefabDiagnosticCounts.Destroyed;
        ClearPatches();
        if (PrefabDiagnostics)
            Debug.Log($"Quad sphere prefab diagnostics [{Name}]: end-play cleanup scheduled destruction of {_prefabDiagnosticCounts.Destroyed - destroyedBefore} prefab actors.");
        _brushes = Array.Empty<BrushData>();
        _invalidBrushBiomes.Clear();
        base.OnEndPlay();
    }

    /// <summary>Reloads the elevation texture and rebuilds the terrain after settings change.</summary>
    [Button("Rebuild Terrain", "Update the planet preview after changing terrain settings.")]
    public void RebuildTerrain()
    {
        if (!_running)
        {
            Debug.LogWarning("Add the quad sphere actor to an active scene before rebuilding the terrain.");
            return;
        }

        _initialRebuildPending = false;
        _ready = false;
        ClearPatches();
        _generateCollision = GenerateCollision;
        _castShadows = CastShadows;
        _heightmap = null;
        if (Radius <= 0 || !double.IsFinite(Radius) || Radius > float.MaxValue / 4.0)
        {
            Debug.LogError("Quad sphere radius must be positive, finite, and representable by patch meshes.");
            return;
        }
        if (!float.IsFinite(SkirtDepth) || !float.IsFinite(UvRebaseRepeat) || UvRebaseRepeat < 0 ||
            (!SimpleSphere && (!float.IsFinite(NoiseHeight) || !float.IsFinite(NoiseFrequency) || !float.IsFinite(HeightmapHeight) || !float.IsFinite(BiomeBlendWidth) ||
                !double.IsFinite(BeachWidth) || BeachWidth < 0.0 || BeachWidth > float.MaxValue / 4.0 || !float.IsFinite(BeachSmoothing) ||
                (BeachWidth > 0.0 && (!double.IsFinite(WaterHeight) || WaterHeight <= 0.0 || WaterHeight > float.MaxValue / 4.0)))))
        {
            Debug.LogError("Quad sphere settings must be finite, UV repeat and beach width must be nonnegative, and an enabled beach requires a positive water height.");
            return;
        }
        _radius = Radius;
        _simpleSphere = SimpleSphere;
        _resolution = Math.Clamp(PatchResolution, 2, 128);
        _chunksPerPatch = Math.Clamp(ChunksPerPatch, 1, 128);
        _skirtDepth = Math.Max(0, SkirtDepth);
        _noiseHeight = _simpleSphere ? 0.0f : NoiseHeight;
        _noiseFrequency = _simpleSphere ? 0.0f : Math.Max(0, NoiseFrequency);
        _noiseOctaves = _simpleSphere ? 0 : Math.Clamp(NoiseOctaves, 0, 12);
        _noiseSeed = NoiseSeed;
        _heightmapHeight = _simpleSphere ? 0.0f : HeightmapHeight;
        _uvRebaseRepeat = UvRebaseRepeat;
        _biomeLayerCount = Math.Clamp(BiomeLayerCount, 1, 4);
        _biomeLayers = new[] { BiomeLayer0, BiomeLayer1, BiomeLayer2, BiomeLayer3 };
        if (!_simpleSphere)
        {
            for (int i = 0; i < _biomeLayerCount; i++)
            {
                var layer = _biomeLayers[i];
                if (!float.IsFinite(layer.NoiseFrequency) || !float.IsFinite(layer.NoiseContrast))
                {
                    Debug.LogError($"Quad sphere biome layer {i} noise settings must be finite.");
                    return;
                }
                layer.NoiseFrequency = Math.Clamp(layer.NoiseFrequency, 0.0f, 4096.0f);
                layer.NoiseOctaves = Math.Clamp(layer.NoiseOctaves, 1, 8);
                layer.NoiseContrast = Math.Clamp(layer.NoiseContrast, 0.0f, 16.0f);
                _biomeLayers[i] = layer;
            }
        }
        _biomeBlendWidth = _simpleSphere ? 0.0f : Math.Clamp(BiomeBlendWidth, 0.0f, 1.0f);
        _waterHeight = _simpleSphere ? _radius : WaterHeight;
        _beachWidth = _simpleSphere ? 0.0 : BeachWidth;
        _beachSmoothing = _simpleSphere ? 0.0f : Math.Clamp(BeachSmoothing, 0.0f, 1.0f);
        if (!_simpleSphere && Heightmap != null && !TryReadImage(Heightmap, out _heightmap))
            return;

        _baseElevationBound = 2.0 * Math.Abs(_noiseHeight) + Math.Abs(_heightmapHeight) * MaxAbsRed(_heightmap) + _beachWidth * _beachSmoothing;
        _brushTransform = Transform;
        _brushes = _simpleSphere || Engine.IsPlayMode ? Array.Empty<BrushData>() : CollectBrushes(_brushTransform);
        _failedBrushes = null;
        _brushCheckElapsed = 0;
        _prefabBrushesDirty = true;
        _foliageSurfaceErrorLogged = false;
        _prefabConfigs = PrefabConfigs != null ? (QuadSpherePrefabConfig[])PrefabConfigs.Clone() : Array.Empty<QuadSpherePrefabConfig>();
        _foliageConfigs = FoliageConfigs != null ? (QuadSphereFoliageConfig[])FoliageConfigs.Clone() : Array.Empty<QuadSphereFoliageConfig>();
        for (int i = 0; i < _foliageConfigs.Length; i++)
        {
            if (!ValidFoliageNoise(_foliageConfigs[i]))
                Debug.LogWarning($"Quad sphere foliage entry {i} has invalid noise settings; the noise type must be supported, frequency finite and nonnegative, and strengths finite. The entry will not be streamed.");
        }
        _foliageDivisions = Math.Clamp(FoliageCellsPerFace, 1, 1024);
        _prefabSnapshot = CreateTerrainSnapshot();
        _prefabSnapshot.Brushes = Array.Empty<BrushData>();
        _foliageSnapshot = CreateTerrainSnapshot();
        InitializeCullBounds();
        _ready = true;
        RefreshLod();
    }

    private static bool TryReadImage(Texture texture, out HeightImage image)
    {
        image = null;
        if (texture.WaitForLoaded() || texture.Width <= 0 || texture.Height <= 0 || texture.GetPixels(out Color[] pixels))
        {
            Debug.LogError("Cannot read planet height texture. Use a loaded, uncompressed texture format: " + texture.ID);
            return false;
        }
        image = new HeightImage { Pixels = pixels, Width = texture.Width, Height = texture.Height };
        return true;
    }

    private BrushData[] CollectBrushes(Transform planetTransform)
    {
        var result = new List<BrushData>();
        double scale = Math.Max(Math.Abs(planetTransform.Scale.X), Math.Max(Math.Abs(planetTransform.Scale.Y), Math.Abs(planetTransform.Scale.Z)));
        double minScale = Math.Min(Math.Abs(planetTransform.Scale.X), Math.Min(Math.Abs(planetTransform.Scale.Y), Math.Abs(planetTransform.Scale.Z)));
        // The brush mask is evaluated on the unbrushed surface. This bounding
        // shell is conservative even for non-uniformly scaled planets.
        double extent = Math.Max(0.01, _radius + _baseElevationBound) * scale;
        double innerExtent = Math.Max(0.01, _radius - _baseElevationBound) * minScale;
        foreach (var brush in Level.GetActors<QuadSphereBrush>(true))
        {
            if (brush == null || (!brush.TargetsActor(this) && !IsSpawnedPrefabBrush(brush)) || !float.IsFinite(brush.Radius) || brush.Radius <= 0 ||
                !float.IsFinite(brush.Falloff) || !float.IsFinite(brush.Depth) ||
                (brush.Mode != QuadSphereBrushMode.Carve && brush.Mode != QuadSphereBrushMode.Flatten && brush.Mode != QuadSphereBrushMode.None && brush.Mode != QuadSphereBrushMode.RemoveFoliage) ||
                (brush.Shape != QuadSphereBrushShape.Circle && brush.Shape != QuadSphereBrushShape.Square))
                continue;
            var position = brush.Position;
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
                continue;
            double distance = (position - planetTransform.Translation).Length;
            if (!double.IsFinite(distance))
                continue;
            double brushExtent = brush.Radius * (brush.Shape == QuadSphereBrushShape.Square ? Math.Sqrt(3.0) : 1.0);
            if (double.IsFinite(extent) && distance > extent + brushExtent)
                continue;
            if (double.IsFinite(innerExtent) && distance + brushExtent < innerExtent)
                continue;
            double flattenHeight = brush.Mode == QuadSphereBrushMode.Flatten ? BrushFlattenHeight(position, planetTransform, _radius) : 0.0;
            if (!double.IsFinite(flattenHeight))
                continue;
            var biomeOverride = brush.BiomeOverride;
            if ((int)biomeOverride < (int)QuadSphereBrushBiome.None || (int)biomeOverride > (int)QuadSphereBrushBiome.Beach)
            {
                if (_invalidBrushBiomes.Add(brush.ID))
                    Debug.LogWarning($"Quad sphere brush {brush.Name} has an invalid biome override; its biome override is ignored.");
                biomeOverride = QuadSphereBrushBiome.None;
            }
            else
                _invalidBrushBiomes.Remove(brush.ID);
            var data = new BrushData { Id = brush.ID, Order = brush.Order, Mode = brush.Mode, Shape = brush.Shape, Position = position,
                BiomeOverride = biomeOverride, Radius = brush.Radius, Falloff = Math.Clamp(brush.Falloff, 0.0f, 1.0f),
                Depth = Math.Max(0.0f, brush.Depth), FlattenHeight = flattenHeight };
            if (brush.Shape == QuadSphereBrushShape.Square)
            {
                QuadSphereBrush.GetSquareAxes(position, planetTransform.Translation, brush.Orientation,
                    out data.AxisNormal, out data.AxisX, out data.AxisY);
                if (!double.IsFinite(data.AxisX.X) || !double.IsFinite(data.AxisX.Y) || !double.IsFinite(data.AxisX.Z) ||
                    !double.IsFinite(data.AxisY.X) || !double.IsFinite(data.AxisY.Y) || !double.IsFinite(data.AxisY.Z))
                    continue;
            }
            result.Add(data);
        }
        result.Sort((a, b) =>
        {
            int order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : a.Id.CompareTo(b.Id);
        });
        return result.ToArray();
    }

    private bool IsSpawnedPrefabBrush(QuadSphereBrush brush)
    {
        for (Actor actor = brush; actor != null && actor != this; actor = actor.Parent)
        {
            if (actor is QuadSphereActor)
                break;
            if (_spawnedPrefabRoots.Contains(actor.ID))
                return true;
        }
        return false;
    }

    private static double BrushFlattenHeight(Vector3 position, Transform planetTransform, double radius) =>
        planetTransform.WorldToLocal(position).Length - radius;

    private static bool SameBrushes(BrushData[] a, BrushData[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!SameBrush(a[i], b[i]))
                return false;
        }
        return true;
    }

    private static bool SameBrush(BrushData x, BrushData y) =>
        x.Id == y.Id && x.Order == y.Order && x.Mode == y.Mode && x.Shape == y.Shape && x.BiomeOverride == y.BiomeOverride && x.Position.Equals(y.Position) &&
        x.AxisX.Equals(y.AxisX) && x.AxisY.Equals(y.AxisY) && x.AxisNormal.Equals(y.AxisNormal) &&
        x.Radius == y.Radius && x.Falloff == y.Falloff && x.Depth == y.Depth && x.FlattenHeight == y.FlattenHeight;

    private static List<BrushInfluence> ChangedBrushInfluences(BrushData[] oldBrushes, Transform oldTransform, BrushData[] newBrushes, Transform newTransform)
    {
        var result = new List<BrushInfluence>();
        var oldById = new Dictionary<Guid, BrushData>();
        var newById = new Dictionary<Guid, BrushData>();
        foreach (var brush in oldBrushes)
            oldById.Add(brush.Id, brush);
        foreach (var brush in newBrushes)
            newById.Add(brush.Id, brush);
        bool movedPlanet = !oldTransform.Equals(newTransform);
        foreach (var brush in oldBrushes)
            if (movedPlanet || !newById.TryGetValue(brush.Id, out var updated) || !SameBrush(brush, updated))
                result.Add(new BrushInfluence { Brush = brush, PlanetTransform = oldTransform });
        foreach (var brush in newBrushes)
            if (movedPlanet || !oldById.TryGetValue(brush.Id, out var previous) || !SameBrush(brush, previous))
                result.Add(new BrushInfluence { Brush = brush, PlanetTransform = newTransform });
        return result;
    }

    private bool BrushInfluencesRegion(int face, int x, int y, int divisions, List<BrushInfluence> influences)
    {
        double u = -1.0 + 2.0 * (x + 0.5) / divisions;
        double v = -1.0 + 2.0 * (y + 0.5) / divisions;
        var center = (Vector3)(Direction(face, u, v) * _radius);
        // Include normal samples just outside the patch/cell and float transform rounding.
        double localRadius = _baseElevationBound + 0.01 + (_radius + _baseElevationBound + 0.01) * (Math.Sqrt(2.0) / divisions + 0.00021);
        foreach (var influence in influences)
        {
            var transform = influence.PlanetTransform;
            double scale = Math.Max(Math.Abs(transform.Scale.X), Math.Max(Math.Abs(transform.Scale.Y), Math.Abs(transform.Scale.Z)));
            var worldCenter = transform.LocalToWorld(center);
            double distance = (worldCenter - influence.Brush.Position).Length;
            double brushRadius = influence.Brush.Radius * (influence.Brush.Shape == QuadSphereBrushShape.Square ? Math.Sqrt(3.0) : 1.0);
            double padding = (worldCenter.Length + influence.Brush.Position.Length + localRadius * scale + 1.0) * 0.00001;
            double bound = localRadius * scale + brushRadius + padding;
            if (!double.IsFinite(distance) || !double.IsFinite(bound) || distance <= bound)
                return true;
        }
        return false;
    }

    private void RebuildForBrushes(BrushData[] brushes, Transform planetTransform)
    {
        var changedBrushInfluences = ChangedBrushInfluences(_brushes, _brushTransform, brushes, planetTransform);
        var affected = new List<(int Face, int Level, int X, int Y)>();
        foreach (var pair in _patches)
        {
            var key = pair.Key;
            if (BrushInfluencesRegion(key.Face, key.X, key.Y, _chunksPerPatch << key.Level, changedBrushInfluences))
                affected.Add(key);
        }
        // Keep old affected patches visible until all replacements are ready.
        foreach (var key in affected)
        {
            _previousBrushPatches.Add(key, _patches[key]);
            _patches.Remove(key);
        }
        _previousBrushes = _brushes;
        _previousBrushTransform = _brushTransform;
        _hasCompletedLod = false;
        _brushes = brushes;
        _brushTransform = planetTransform;
        _failedBrushes = null;
        _previousCullRadius = _cullRadius;
        InitializeCullBounds();
        _desired = new HashSet<(int Face, int Level, int X, int Y)>(_patches.Keys);
        _desired.UnionWith(affected);
        _pendingKeys = affected;
        _pendingIndex = 0;
        _snapshot = CreateTerrainSnapshot();
        _generationCancellation = new CancellationTokenSource();
        AdvanceGeneration();
    }

    private void RestoreBrushPatches()
    {
        // A failed replacement must not remove the last usable terrain/collision.
        foreach (var pair in _previousBrushPatches)
        {
            if (_patches.TryGetValue(pair.Key, out var replacement))
            {
                DestroyPatch(replacement);
                _patches.Remove(pair.Key);
            }
            _patches.Add(pair.Key, pair.Value);
        }
        _previousBrushPatches.Clear();
        _failedBrushes = _brushes;
        _failedBrushTransform = _brushTransform;
        _brushes = _previousBrushes;
        _brushTransform = _previousBrushTransform;
        _previousBrushes = null;
        _desired = null;
        _pendingKeys = null;
        _snapshot = null;
        _generationCancellation?.Dispose();
        _generationCancellation = null;
        _hasCompletedLod = true;
        InitializeCullBounds();
        UpdatePatchVisibility();
    }

    private void UpdateTerrain()
    {
        UpdatePrefabDiagnostics();
        if (_initialRebuildPending)
        {
            RebuildTerrain();
            return;
        }
        if (IsActiveInHierarchy && (GenerateCollision != _generateCollision || SimpleSphere != _simpleSphere))
        {
            RebuildTerrain();
            return;
        }
        if (!_ready || !IsActiveInHierarchy)
            return;
        if (_castShadows != CastShadows)
        {
            _castShadows = CastShadows;
            foreach (var patch in _patches.Values)
                SetPatchShadows(patch.Renderer, _castShadows);
            foreach (var patch in _previousBrushPatches.Values)
                SetPatchShadows(patch.Renderer, _castShadows);
        }
        UpdatePatchVisibility();
        bool committedLod = false;
        if (_hasCompletedLod && _previousBrushes == null)
        {
            bool prefabsPending = UpdatePrefabs();
            if (!prefabsPending && _desired == null)
            {
                if (UpdateBrushLayer())
                    return;
                UpdateFoliage();
            }
        }
        else if (_prefabChunks.Count != 0)
            CleanupPrefabDistances();
        if (_desired != null && !AdvanceGeneration())
            return;
        bool cooking = false;
        foreach (var patch in _patches.Values)
        {
            if (patch.CollisionAssigned)
                continue;
            if (!patch.Cooking.IsCompleted)
            {
                cooking = true;
                continue;
            }
            patch.CollisionAssigned = true;
            if (patch.Cooking.IsFaulted || patch.Cooking.IsCanceled || patch.Cooking.Result)
                Debug.LogError("Failed to cook quad sphere collision: " + patch.Cooking.Exception);
            else
                patch.Collider.CollisionData = patch.Collision;
        }
        if (cooking)
            return;
        if (_previousBrushes != null)
        {
            foreach (var key in _previousBrushPatches.Keys)
            {
                var patch = _patches[key];
                if (patch.Cooking != null && (patch.Cooking.IsFaulted || patch.Cooking.IsCanceled || patch.Cooking.Result))
                {
                    RestoreBrushPatches();
                    return;
                }
            }
        }
        if (_desired != null)
        {
            if (_foliageCells.Count != 0)
            {
                var changedPatches = new HashSet<(int Face, int Level, int X, int Y)>(_previousBrushPatches.Keys);
                foreach (var pair in _patches)
                    if (!pair.Value.Committed || !_desired.Contains(pair.Key))
                        changedPatches.Add(pair.Key);
                InvalidateFoliageForPatches(changedPatches);
            }
            if (_previousBrushes != null)
                _foliageSnapshot = CreateTerrainSnapshot();
            foreach (var patch in _previousBrushPatches.Values)
            {
                patch.Renderer.IsActive = false;
                if (patch.Collider != null)
                    patch.Collider.IsActive = false;
                _retired.Add(patch);
            }
            _previousBrushPatches.Clear();
            _previousBrushes = null;
            var remove = new List<(int Face, int Level, int X, int Y)>();
            foreach (var pair in _patches)
            {
                if (!_desired.Contains(pair.Key))
                {
                    pair.Value.Renderer.IsActive = false;
                    if (pair.Value.Collider != null)
                        pair.Value.Collider.IsActive = false;
                    _retired.Add(pair.Value);
                    remove.Add(pair.Key);
                }
                else
                {
                    pair.Value.Committed = true;
                    if (pair.Value.Collider != null)
                        pair.Value.Collider.IsActive = true;
                }
            }
            foreach (var key in remove)
                _patches.Remove(key);
            _hasCompletedLod = true;
            committedLod = true;
            _desired = null;
            _pendingKeys = null;
            _snapshot = null;
            _generationCancellation?.Dispose();
            _generationCancellation = null;
            UpdatePatchVisibility();
        }
        foreach (var patch in _retired)
            DestroyPatch(patch);
        _retired.Clear();
        _elapsed += Engine.IsPlayMode ? Time.DeltaTime : Time.UnscaledDeltaTime;
        if (!committedLod && _elapsed >= Math.Max(0.05f, LodUpdateInterval))
        {
            _elapsed = 0;
            RefreshLod();
        }
    }

    private bool UpdateBrushLayer()
    {
        if (_simpleSphere)
            return false;
        _brushCheckElapsed += Time.UnscaledDeltaTime;
        if (!_prefabBrushesDirty && _brushCheckElapsed < 0.25f)
            return false;
        _prefabBrushesDirty = false;
        _brushCheckElapsed = 0;
        var planetTransform = Transform;
        var brushes = CollectBrushes(planetTransform);
        bool failedAttempt = _failedBrushes != null && SameBrushes(_failedBrushes, brushes) &&
            _failedBrushTransform.Equals(planetTransform);
        if (!failedAttempt && (!SameBrushes(_brushes, brushes) ||
            ((brushes.Length != 0 || _brushes.Length != 0) && !_brushTransform.Equals(planetTransform))))
        {
            RebuildForBrushes(brushes, planetTransform);
            return true;
        }
        return false;
    }

    private void UpdatePatchVisibility()
    {
        // A missing cache (for example during a script reload) must not hide terrain.
        if (_cullCenters == null || _cullVisible == null)
        {
            foreach (var patch in _patches.Values)
                if (patch.Renderer.IsActive != patch.Committed)
                    patch.Renderer.IsActive = patch.Committed;
            foreach (var patch in _previousBrushPatches.Values)
                if (patch.Renderer.IsActive != patch.Committed)
                    patch.Renderer.IsActive = patch.Committed;
            return;
        }
        Vector3 target = Vector3.Zero;
        bool culling = double.IsFinite(ChunkCullDistance) && ChunkCullDistance > 0 && TryGetLodTargetPosition(out target);
        double hysteresis = double.IsFinite(ChunkCullHysteresis) ? Math.Clamp(ChunkCullHysteresis, 0.0, 1.0) : 0.0;
        var transform = Transform;
        double scale = Math.Max(Math.Abs(transform.Scale.X), Math.Max(Math.Abs(transform.Scale.Y), Math.Abs(transform.Scale.Z)));
        double cullRadius = _previousBrushPatches.Count != 0 ? Math.Max(_cullRadius, _previousCullRadius) : _cullRadius;
        double worldRadius = cullRadius * scale;
        for (int i = 0; i < _cullCenters.Length; i++)
        {
            bool visible = true;
            if (culling && double.IsFinite(worldRadius))
            {
                double distance = (target - transform.LocalToWorld(_cullCenters[i])).Length;
                double margin = _cullVisible[i] ? ChunkCullDistance * hysteresis : 0.0;
                // Invalid transforms or positions fail open, rather than hiding terrain.
                visible = !double.IsFinite(distance) || distance <= ChunkCullDistance + worldRadius + margin;
            }
            _cullVisible[i] = visible;
        }
        foreach (var pair in _patches)
        {
            var key = pair.Key;
            var patch = pair.Value;
            int root = (key.Face * _chunksPerPatch + (key.Y >> key.Level)) * _chunksPerPatch + (key.X >> key.Level);
            bool visible = patch.Committed && _cullVisible[root];
            if (patch.Renderer.IsActive != visible)
                patch.Renderer.IsActive = visible;
        }
        foreach (var pair in _previousBrushPatches)
        {
            var key = pair.Key;
            var patch = pair.Value;
            int root = (key.Face * _chunksPerPatch + (key.Y >> key.Level)) * _chunksPerPatch + (key.X >> key.Level);
            bool visible = patch.Committed && _cullVisible[root];
            if (patch.Renderer.IsActive != visible)
                patch.Renderer.IsActive = visible;
        }
    }

    private void InitializeCullBounds()
    {
        int count = 6 * _chunksPerPatch * _chunksPerPatch;
        _cullCenters = new Vector3[count];
        _cullVisible = new bool[count];
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < _chunksPerPatch; y++)
                for (int x = 0; x < _chunksPerPatch; x++)
                {
                    double u = -1.0 + 2.0 * (x + 0.5) / _chunksPerPatch;
                    double v = -1.0 + 2.0 * (y + 0.5) / _chunksPerPatch;
                    int root = (face * _chunksPerPatch + y) * _chunksPerPatch + x;
                    _cullCenters[root] = (Vector3)(Direction(face, u, v) * _radius);
                }

        // A carve adds at most its depth; flattening cannot exceed the larger
        // of the current bound and the target height's magnitude.
        double displacement = _baseElevationBound;
        foreach (var brush in _brushes)
        {
            if (brush.Mode == QuadSphereBrushMode.Carve)
                displacement += brush.Depth;
            else if (brush.Mode == QuadSphereBrushMode.Flatten)
                displacement = Math.Max(displacement, Math.Abs(brush.FlattenHeight));
        }
        displacement += _skirtDepth + 0.01;
        // Normalizing (face, u, v) is at most 1-Lipschitz because its length is
        // at least one. The furthest point in the root square is sqrt(2)/N away.
        // The small padding covers float mesh vertices and direction rounding.
        double extent = _radius + displacement;
        _cullRadius = displacement + extent * (Math.Sqrt(2.0) / _chunksPerPatch + 0.00001);
    }

    private static double MaxAbsRed(HeightImage image)
    {
        if (image == null)
            return 0;
        double maximum = 0;
        foreach (var pixel in image.Pixels)
        {
            if (!float.IsFinite(pixel.R))
                return double.PositiveInfinity;
            maximum = Math.Max(maximum, Math.Abs(pixel.R));
        }
        return maximum;
    }

    private TerrainSnapshot CreateTerrainSnapshot() => new TerrainSnapshot { Radius = _radius, Resolution = _resolution, ChunksPerPatch = _chunksPerPatch, SkirtDepth = _skirtDepth,
        NoiseHeight = _noiseHeight, NoiseFrequency = _noiseFrequency, NoiseOctaves = _noiseOctaves,
        NoiseSeed = _noiseSeed, HeightmapHeight = _heightmapHeight, Heightmap = _heightmap, UvRebaseRepeat = _uvRebaseRepeat,
        SimpleSphere = _simpleSphere, BiomeLayerCount = _biomeLayerCount, BiomeLayers = _biomeLayers, BiomeBlendWidth = _biomeBlendWidth,
        WaterHeight = _waterHeight, BeachWidth = _beachWidth, BeachSmoothing = _beachSmoothing,
        Brushes = _brushes, PlanetTransform = _brushTransform };

    private void RefreshLod()
    {
        if (!_ready || _desired != null)
            return;
        var desired = new HashSet<(int Face, int Level, int X, int Y)>();
        var snapshot = CreateTerrainSnapshot();
        bool hasTarget = TryGetLodTargetPosition(out var targetPosition);
        Vector3 target = hasTarget ? Transform.WorldToLocal(targetPosition) : Vector3.Zero;
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < _chunksPerPatch; y++)
                for (int x = 0; x < _chunksPerPatch; x++)
                    SelectPatches(desired, face, 0, x, y, target, hasTarget, snapshot);

        _desired = desired;
        _pendingKeys = new List<(int Face, int Level, int X, int Y)>();
        foreach (var key in desired)
            if (!_patches.ContainsKey(key))
                _pendingKeys.Add(key);
        _pendingIndex = 0;
        _snapshot = snapshot;
        _generationCancellation = new CancellationTokenSource();
        AdvanceGeneration();
    }

    private bool TryGetLodTargetPosition(out Vector3 position)
    {
#if FLAX_EDITOR
        if (!Engine.IsPlayMode)
        {
            var viewport = FlaxEditor.Editor.Instance?.Windows?.EditWin?.Viewport;
            if (viewport != null)
            {
                position = viewport.ViewPosition;
                return true;
            }
        }
#endif
        var target = LodTarget != null ? LodTarget : Camera.MainCamera;
        if (target != null)
        {
            position = target.Position;
            return true;
        }
        position = Vector3.Zero;
        return false;
    }

    private bool AdvanceGeneration()
    {
        double budgetMs = float.IsFinite(PatchInstallBudgetMs) ? Math.Clamp(PatchInstallBudgetMs, 0.1f, 100.0f) : 3.0;
        long start = Stopwatch.GetTimestamp();
        int installed = 0;
        for (int i = 0; i < _generation.Count;)
        {
            var job = _generation[i];
            if (!job.IsCompleted)
            {
                i++;
                continue;
            }
            if (job.IsFaulted || job.IsCanceled)
            {
                if (job.IsFaulted)
                    Debug.LogException(job.Exception);
                CancelGeneration();
                _desired = null;
                _pendingKeys = null;
                _snapshot = null;
                if (_previousBrushes != null)
                    RestoreBrushPatches();
                return false;
            }
            // Model, renderer and collider creation must stay on the main thread.
            var geometry = job.Result;
            _generation.RemoveAt(i);
            var patch = CreatePatch(geometry);
            patch.Renderer.IsActive = false;
            if (patch.Collider != null)
                patch.Collider.IsActive = false;
            _patches.Add(geometry.Key, patch);
            installed++;
            if (installed > 0 && (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency >= budgetMs)
                break;
        }

        int workerLimit = GenerationWorkers <= 0 ? Math.Clamp(Environment.ProcessorCount - 1, 1, 8) : Math.Clamp(GenerationWorkers, 1, 32);
        while (_generation.Count < workerLimit && _pendingIndex < _pendingKeys.Count)
        {
            var key = _pendingKeys[_pendingIndex++];
            var snapshot = _snapshot;
            var token = _generationCancellation.Token;
            _generation.Add(Task.Run(() => BuildPatch(key, snapshot, token), token));
        }
        return _pendingIndex == _pendingKeys.Count && _generation.Count == 0;
    }

    private void CancelGeneration()
    {
        _generationCancellation?.Cancel();
        foreach (var job in _generation)
        {
            // A canceled generation can still fault after its jobs are detached.
            _ = job.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        _generation.Clear();
        _generationCancellation?.Dispose();
        _generationCancellation = null;
    }

    private void SelectPatches(HashSet<(int Face, int Level, int X, int Y)> result, int face, int level, int x, int y, Vector3 target, bool hasTarget, TerrainSnapshot settings)
    {
        if (hasTarget && level < Math.Clamp(MaxLod, 0, 10))
        {
            int divisions = settings.ChunksPerPatch << level;
            double u = -1.0 + 2.0 * (x + 0.5) / divisions;
            double v = -1.0 + 2.0 * (y + 0.5) / divisions;
            Double3 center = Surface(Direction(face, u, v), settings);
            double distance = (target - (Vector3)center).Length;
            double patchSize = settings.Radius * 2.0 / divisions;
            if (distance < patchSize * Math.Max(0.1f, LodDistance))
            {
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        SelectPatches(result, face, level + 1, x * 2 + dx, y * 2 + dy, target, true, settings);
                return;
            }
        }
        result.Add((face, level, x, y));
    }

    private static PatchGeometry BuildPatch((int Face, int Level, int X, int Y) key, TerrainSnapshot settings, CancellationToken token)
    {
        int n = settings.Resolution;
        int patchesPerChunk = 1 << key.Level;
        int divisions = settings.ChunksPerPatch * patchesPerChunk;
        double step = 2.0 / divisions;
        double u0 = -1.0 + key.X * step;
        double v0 = -1.0 + key.Y * step;
        double uvStartX = (key.X % patchesPerChunk) * n / (double)patchesPerChunk;
        double uvStartY = (key.Y % patchesPerChunk) * n / (double)patchesPerChunk;
        // Subtract only whole texture periods, once per patch, before conversion
        // to float. This keeps interpolation and its derivatives continuous.
        double uvOffsetX = settings.UvRebaseRepeat > 0 ? Math.Floor(uvStartX * settings.UvRebaseRepeat) / settings.UvRebaseRepeat : 0.0;
        double uvOffsetY = settings.UvRebaseRepeat > 0 ? Math.Floor(uvStartY * settings.UvRebaseRepeat) / settings.UvRebaseRepeat : 0.0;
        Double3 origin = Direction(key.Face, u0 + step * 0.5, v0 + step * 0.5) * settings.Radius;
        Double3 uAxis = key.Face switch
        {
            0 => new Double3(0, 0, -1),
            1 => Double3.UnitZ,
            5 => new Double3(-1, 0, 0),
            _ => Double3.UnitX,
        };
        var vertices = new List<Float3>((n + 1) * (n + 1) + 4 * (n + 1));
        var normals = new List<Float3>(vertices.Capacity);
        var tangents = new List<Float3>(vertices.Capacity);
        var uvs = new List<Float2>(vertices.Capacity);
        var biomeWeights = new List<Color32>(vertices.Capacity);
        var indices = new List<int>(n * n * 6 + n * 24);

        for (int y = 0; y <= n; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x <= n; x++)
            {
                // Use the same face coordinates for shared vertices at every LOD.
                double u = -1.0 + 2.0 * ((double)key.X * n + x) / ((double)divisions * n);
                double v = -1.0 + 2.0 * ((double)key.Y * n + y) / ((double)divisions * n);
                Double3 direction = Direction(key.Face, u, v);
                Double3 normal = SurfaceNormal(direction, settings);
                // Sample the beach before flattening so the mask doesn't expand with smoothing.
                double height = BaseElevation(direction, settings);
                double beach = GetBeachWeight(height, settings);
                height = FlattenBeach(height, beach, settings);
                var biome = ApplyBrushBiomes(direction, height, ApplyBeachWeight(GetBiomeWeights(direction, settings), beach), settings, out _);
                height = BrushElevation(direction, height, settings);
                vertices.Add(direction * Math.Max(0.01, settings.Radius + height) - origin);
                normals.Add(normal);
                tangents.Add(SurfaceTangent(direction, normal, uAxis));
                biomeWeights.Add(biome);
                // Every LOD shares the base chunk grid modulo the selected repeat period.
                uvs.Add(new Float2((float)(uvStartX + x / (double)patchesPerChunk - uvOffsetX),
                    (float)(uvStartY + y / (double)patchesPerChunk - uvOffsetY)));
            }
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int a = y * (n + 1) + x;
                int b = a + 1;
                int c = a + n + 1;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(c + 1); indices.Add(c);
            }

        for (int edge = 0; edge < 4 && settings.SkirtDepth > 0; edge++)
        {
            int start = vertices.Count;
            for (int i = 0; i <= n; i++)
            {
                int source = edge switch { 0 => i, 1 => i * (n + 1) + n, 2 => n * (n + 1) + n - i, _ => (n - i) * (n + 1) };
                int sourceX = source % (n + 1);
                int sourceY = source / (n + 1);
                double u = -1.0 + 2.0 * ((double)key.X * n + sourceX) / ((double)divisions * n);
                double v = -1.0 + 2.0 * ((double)key.Y * n + sourceY) / ((double)divisions * n);
                Double3 direction = Direction(key.Face, u, v);
                vertices.Add(vertices[source] - (Float3)(direction * settings.SkirtDepth));
                normals.Add(normals[source]);
                tangents.Add(tangents[source]);
                uvs.Add(uvs[source]);
                biomeWeights.Add(biomeWeights[source]);
                if (i == 0) continue;
                int previous = edge switch { 0 => i - 1, 1 => (i - 1) * (n + 1) + n, 2 => n * (n + 1) + n - i + 1, _ => (n - i + 1) * (n + 1) };
                indices.Add(previous); indices.Add(start + i - 1); indices.Add(source);
                indices.Add(source); indices.Add(start + i - 1); indices.Add(start + i);
            }
        }

        return new PatchGeometry { Key = key, Origin = origin, Vertices = vertices.ToArray(),
            Normals = normals.ToArray(), Tangents = tangents.ToArray(), UVs = uvs.ToArray(), BiomeWeights = biomeWeights.ToArray(), Indices = indices.ToArray() };
    }

    private Patch CreatePatch(PatchGeometry geometry)
    {
        var model = Content.CreateVirtualAsset<Model>();
        if (model == null || model.SetupLODs(new[] { 1 }))
            throw new InvalidOperationException("Failed to create a planet patch model.");
        var vertexArray = geometry.Vertices;
        var indexArray = geometry.Indices;
        // Keep the usual packed tangent frame, but avoid the half-float UV stream
        // used by Mesh.UpdateMesh for the patch UVs.
        var accessor = new MeshAccessor();
        var positionLayout = GPUVertexLayout.Get(new[] {
            new VertexElement(VertexElement.Types.Position, 0, 0, false, PixelFormat.R32G32B32_Float) });
        var attributeLayout = GPUVertexLayout.Get(new[] {
            new VertexElement(VertexElement.Types.Normal, 1, 0, false, PixelFormat.R10G10B10A2_UNorm),
            new VertexElement(VertexElement.Types.Tangent, 1, 0, false, PixelFormat.R10G10B10A2_UNorm),
            new VertexElement(VertexElement.Types.TexCoord, 1, 0, false, PixelFormat.R32G32_Float) });
        var biomeLayout = GPUVertexLayout.Get(new[] {
            new VertexElement(VertexElement.Types.Color, 2, 0, false, PixelFormat.R8G8B8A8_UNorm) });
        if (accessor.AllocateBuffer(MeshBufferType.Index, indexArray.Length, PixelFormat.R32_UInt) ||
            accessor.AllocateBuffer(MeshBufferType.Vertex0, vertexArray.Length, positionLayout) ||
            accessor.AllocateBuffer(MeshBufferType.Vertex1, vertexArray.Length, attributeLayout) ||
            accessor.AllocateBuffer(MeshBufferType.Vertex2, vertexArray.Length, biomeLayout))
        {
            Destroy(model);
            throw new InvalidOperationException("Failed to allocate planet patch mesh buffers.");
        }
        indexArray.AsSpan().CopyTo(MemoryMarshal.Cast<byte, int>(accessor.Index().Data));
        accessor.Position().Set(vertexArray);
        geometry.BiomeWeights.AsSpan().CopyTo(MemoryMarshal.Cast<byte, Color32>(accessor.Color().Data));
        var normalStream = accessor.Normal();
        var tangentStream = accessor.Tangent();
        var uvStream = accessor.TexCoord();
        for (int i = 0; i < vertexArray.Length; i++)
        {
            // RenderTools.CalculateTangentFrame uses sign 0 for this orthogonal frame.
            normalStream.SetFloat3(i, geometry.Normals[i] * 0.5f + 0.5f);
            tangentStream.SetFloat4(i, new Float4(geometry.Tangents[i] * 0.5f + 0.5f, 0.0f));
            uvStream.SetFloat2(i, geometry.UVs[i]);
        }
        if (accessor.UpdateMesh(model.LODs[0].Meshes[0]))
        {
            Destroy(model);
            throw new InvalidOperationException("Failed to upload planet patch mesh.");
        }
        var renderer = new StaticModel { HideFlags = HideFlags.FullyHidden };
        renderer.SetParent(this, false, false);
        renderer.LocalPosition = (Vector3)geometry.Origin;
        renderer.Model = model;
        if (Material != null)
            renderer.SetMaterial(0, Material);
        if (!_castShadows)
            SetPatchShadows(renderer, false);

        if (!Engine.IsPlayMode || !_generateCollision)
            return new Patch { Renderer = renderer, Model = model, Origin = (Double3)renderer.LocalPosition, Vertices = _foliageConfigs.Length != 0 ? vertexArray : null, CollisionAssigned = true };

        var collider = new MeshCollider { HideFlags = HideFlags.FullyHidden };
        collider.SetParent(this, false, false);
        collider.LocalPosition = (Vector3)geometry.Origin;
        var collision = Content.CreateVirtualAsset<CollisionData>();
        if (collision == null)
            throw new InvalidOperationException("Failed to create planet collision data.");
        var cooking = Task.Run(() => collision.CookCollision(CollisionDataType.TriangleMesh, vertexArray, indexArray));
        return new Patch { Renderer = renderer, Collider = collider, Model = model, Origin = (Double3)renderer.LocalPosition, Vertices = _foliageConfigs.Length != 0 ? vertexArray : null, Collision = collision, Cooking = cooking };
    }

    private static void SetPatchShadows(StaticModel renderer, bool castShadows)
    {
        var entries = renderer.Entries;
        if (entries == null || entries.Length == 0)
            entries = new[] { ModelInstanceEntry.Default };
        for (int i = 0; i < entries.Length; i++)
            entries[i].ShadowsMode = castShadows ? ShadowsCastingMode.All : ShadowsCastingMode.None;
        renderer.Entries = entries;
    }

    private new static Double3 Direction(int face, double u, double v)
    {
        var cube = face switch
        {
            0 => new Double3(1, v, -u),
            1 => new Double3(-1, v, u),
            2 => new Double3(u, 1, -v),
            3 => new Double3(u, -1, v),
            4 => new Double3(u, v, 1),
            _ => new Double3(-u, v, -1),
        };
        return Double3.Normalize(cube);
    }

    private static Double3 Surface(Double3 direction, TerrainSnapshot settings) => direction * Math.Max(0.01, settings.Radius + Elevation(direction, settings));

    private static Double3 SurfaceNormal(Double3 direction, TerrainSnapshot settings)
    {
        if (settings.SimpleSphere)
            return direction;
        var reference = Math.Abs(direction.Y) < 0.9 ? Double3.UnitY : Double3.UnitX;
        var right = Double3.Normalize(Double3.Cross(reference, direction));
        var up = Double3.Cross(direction, right);
        const double epsilon = 0.0001;
        var dx = Surface(Double3.Normalize(direction + right * epsilon), settings) - Surface(Double3.Normalize(direction - right * epsilon), settings);
        var dy = Surface(Double3.Normalize(direction + up * epsilon), settings) - Surface(Double3.Normalize(direction - up * epsilon), settings);
        var normal = Double3.Cross(dx, dy);
        return normal.LengthSquared > 0.000001 ? Double3.Normalize(normal) : direction;
    }

    private static Double3 SurfaceTangent(Double3 direction, Double3 normal, Double3 uAxis)
    {
        // Intersect the surface tangent plane with the plane of increasing cube-face U.
        var tangent = uAxis * Double3.Dot(normal, direction) - direction * Double3.Dot(normal, uAxis);
        if (tangent.LengthSquared < 0.000000000001)
        {
            tangent = uAxis - normal * Double3.Dot(normal, uAxis);
            if (tangent.LengthSquared < 0.000000000001)
                tangent = Double3.Cross(normal, Math.Abs(normal.Y) < 0.9 ? Double3.UnitY : Double3.UnitX);
        }
        return Double3.Normalize(tangent);
    }

    private static double Elevation(Double3 direction, TerrainSnapshot settings)
    {
        double height = BaseElevation(direction, settings);
        height = FlattenBeach(height, GetBeachWeight(height, settings), settings);
        return BrushElevation(direction, height, settings);
    }

    private static double BaseElevation(Double3 direction, TerrainSnapshot settings)
    {
        if (settings.SimpleSphere)
            return 0.0;
        double height = 0;
        double frequency = settings.NoiseFrequency;
        double amplitude = settings.NoiseHeight;
        for (int i = 0; i < settings.NoiseOctaves; i++)
        {
            height += ValueNoise(direction * frequency, settings.NoiseSeed + i) * amplitude;
            frequency *= 2;
            amplitude *= 0.5;
        }
        if (settings.Heightmap != null)
        {
            float u = (float)(Math.Atan2(direction.Z, direction.X) / (Math.PI * 2.0) + 0.5);
            float v = (float)(0.5 - Math.Asin(Math.Clamp(direction.Y, -1.0f, 1.0f)) / Math.PI);
            height += settings.Heightmap.Sample(u, v, true) * settings.HeightmapHeight;
        }
        return height;
    }

    private static double BrushElevation(Double3 direction, double height, TerrainSnapshot settings)
    {
        if (settings.SimpleSphere || settings.Brushes.Length == 0)
            return height;

        // Use the unbrushed surface for every mask so overlapping brushes have
        // stable footprints even as earlier brushes change the resulting height.
        Vector3 localPosition = (Vector3)(direction * Math.Max(0.01, settings.Radius + height));
        Vector3 worldPosition = settings.PlanetTransform.LocalToWorld(localPosition);
        foreach (var brush in settings.Brushes)
        {
            double weight = BrushWeight(worldPosition, brush);
            if (brush.Mode == QuadSphereBrushMode.Carve)
                height -= brush.Depth * weight;
            else if (brush.Mode == QuadSphereBrushMode.Flatten)
                height += (brush.FlattenHeight - height) * weight;
        }
        return height;
    }

    private static double BrushWeight(Vector3 worldPosition, BrushData brush)
    {
        var offset = worldPosition - brush.Position;
        double distance = brush.Shape == QuadSphereBrushShape.Square
            ? Math.Max(Math.Abs(Vector3.Dot(offset, brush.AxisNormal)),
                Math.Max(Math.Abs(Vector3.Dot(offset, brush.AxisX)), Math.Abs(Vector3.Dot(offset, brush.AxisY))))
            : offset.Length;
        if (distance >= brush.Radius)
            return 0;
        double weight = brush.Falloff <= 0 ? 1.0 : Math.Clamp((brush.Radius - distance) / (brush.Radius * brush.Falloff), 0.0, 1.0);
        return weight * weight * (3.0 - 2.0 * weight);
    }

    private static Color32 ApplyBrushBiomes(Double3 direction, double baseHeight, Color32 biome, TerrainSnapshot settings, out bool applied)
    {
        applied = false;
        if (settings.SimpleSphere || settings.Brushes.Length == 0)
            return biome;
        var worldPosition = settings.PlanetTransform.LocalToWorld((Vector3)(direction * Math.Max(0.01, settings.Radius + baseHeight)));
        Span<double> weights = stackalloc double[5] { biome.R, biome.G, biome.B, biome.A, 255 - biome.R - biome.G - biome.B - biome.A };
        foreach (var brush in settings.Brushes)
        {
            int target = (int)brush.BiomeOverride - 1;
            if (target < 0 || target > 4)
                continue;
            double weight = BrushWeight(worldPosition, brush);
            if (weight <= 0)
                continue;
            applied = true;
            for (int i = 0; i < 5; i++)
                weights[i] = weights[i] * (1.0 - weight) + (i == target ? 255.0 * weight : 0.0);
        }
        if (!applied)
            return biome;
        Span<byte> quantized = stackalloc byte[5];
        int remaining = 255;
        for (int i = 0; i < 5; i++)
        {
            quantized[i] = (byte)Math.Clamp((int)Math.Floor(weights[i]), 0, 255);
            remaining -= quantized[i];
            weights[i] -= quantized[i];
        }
        while (remaining-- > 0)
        {
            int best = 0;
            for (int i = 1; i < 5; i++)
                if (weights[i] > weights[best])
                    best = i;
            quantized[best]++;
            weights[best] = -1;
        }
        return new Color32(quantized[0], quantized[1], quantized[2], quantized[3]);
    }

    private static double GetBeachWeight(double height, TerrainSnapshot settings)
    {
        if (settings.SimpleSphere || settings.BeachWidth <= 0.0)
            return 0.0;
        double distance = Math.Abs(height - (settings.WaterHeight - settings.Radius));
        double weight = Math.Clamp(1.0 - distance / settings.BeachWidth, 0.0, 1.0);
        return weight * weight * (3.0 - 2.0 * weight);
    }

    private static double FlattenBeach(double height, double weight, TerrainSnapshot settings)
    {
        if (weight <= 0.0 || settings.BeachSmoothing <= 0.0f)
            return height;
        return height + ((settings.WaterHeight - settings.Radius) - height) * (weight * settings.BeachSmoothing);
    }

    private static Color32 ApplyBeachWeight(Color32 biome, double beach)
    {
        if (beach <= 0.0)
            return biome;
        if (beach >= 1.0)
            return new Color32(0, 0, 0, 0);
        // The remaining RGBA sum encodes the biome contribution; its complement is beach.
        int biomeTotal = 255 - (int)Math.Round(Math.Clamp(beach, 0.0, 1.0) * 255.0);
        Span<byte> weights = stackalloc byte[4];
        Span<int> remainders = stackalloc int[4];
        int remaining = biomeTotal;
        for (int i = 0; i < 4; i++)
        {
            int scaled = biome[i] * biomeTotal;
            weights[i] = (byte)(scaled / 255);
            remainders[i] = scaled % 255;
            remaining -= weights[i];
        }
        while (remaining-- > 0)
        {
            int best = 0;
            for (int i = 1; i < 4; i++)
                if (remainders[i] > remainders[best])
                    best = i;
            weights[best]++;
            remainders[best] = -1;
        }
        return new Color32(weights[0], weights[1], weights[2], weights[3]);
    }

    // Classification depends only on the planet-local direction, not patch or LOD.
    private static Color32 GetBiomeWeights(Double3 direction, TerrainSnapshot settings)
    {
        int count = Math.Clamp(settings.BiomeLayerCount, 1, 4);
        if (settings.SimpleSphere || count == 1 || settings.BiomeLayers == null || settings.BiomeLayers.Length < count)
            return new Color32(255, 0, 0, 0);

        Span<double> scores = stackalloc double[4];
        double maximum = double.NegativeInfinity;
        int winner = 0;
        for (int layerIndex = 0; layerIndex < count; layerIndex++)
        {
            var layer = settings.BiomeLayers[layerIndex];
            double noise = 0.0, amplitude = 1.0, amplitudeSum = 0.0;
            double frequency = layer.NoiseFrequency;
            for (int octave = 0; octave < layer.NoiseOctaves; octave++)
            {
                int seed = unchecked(settings.NoiseSeed + layer.SeedOffset + octave * 1013);
                noise += ValueNoise(direction * frequency, seed) * amplitude;
                amplitudeSum += amplitude;
                frequency *= 2.0;
                amplitude *= 0.5;
            }
            scores[layerIndex] = amplitudeSum > 0.0 ? noise / amplitudeSum * layer.NoiseContrast : 0.0;
            if (scores[layerIndex] > maximum)
            {
                maximum = scores[layerIndex];
                winner = layerIndex;
            }
        }

        Span<byte> weights = stackalloc byte[4];
        weights.Clear();
        if (settings.BiomeBlendWidth <= 0.0f)
        {
            weights[winner] = 255;
            return new Color32(weights[0], weights[1], weights[2], weights[3]);
        }

        double total = 0.0;
        for (int i = 0; i < count; i++)
        {
            double blend = Math.Clamp(1.0 - (maximum - scores[i]) / settings.BiomeBlendWidth, 0.0, 1.0);
            scores[i] = blend * blend * (3.0 - 2.0 * blend);
            total += scores[i];
        }
        // Largest-remainder quantization keeps the four UNorm channels normalized.
        int remaining = 255;
        for (int i = 0; i < count; i++)
        {
            double scaled = scores[i] * 255.0 / total;
            weights[i] = (byte)scaled;
            remaining -= weights[i];
            scores[i] = scaled - weights[i];
        }
        while (remaining-- > 0)
        {
            int best = 0;
            for (int i = 1; i < count; i++)
                if (scores[i] > scores[best])
                    best = i;
            weights[best]++;
            scores[best] = -1.0;
        }
        return new Color32(weights[0], weights[1], weights[2], weights[3]);
    }

    private static double ValueNoise(Double3 p, int seed)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y), z = (int)Math.Floor(p.Z);
        double tx = p.X - x, ty = p.Y - y, tz = p.Z - z;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty); tz = tz * tz * (3 - 2 * tz);
        double result = 0;
        for (int dz = 0; dz <= 1; dz++)
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    uint hash = unchecked((uint)((x + dx) * 73856093 ^ (y + dy) * 19349663 ^ (z + dz) * 83492791 ^ seed));
                    hash ^= hash >> 16; hash *= 0x7feb352d; hash ^= hash >> 15; hash *= 0x846ca68b; hash ^= hash >> 16;
                    double value = (hash / (double)uint.MaxValue) * 2 - 1;
                    result += value * (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty) * (dz == 0 ? 1 - tz : tz);
                }
        return result;
    }

    private void UpdatePrefabDiagnostics()
    {
        if (!Engine.IsPlayMode || !PrefabDiagnostics)
        {
            _prefabDiagnosticElapsed = 5.0f;
            _prefabDiagnosticCounts = default;
            return;
        }
        _prefabDiagnosticElapsed += Time.UnscaledDeltaTime;
        if (_prefabDiagnosticElapsed < 5.0f)
            return;
        _prefabDiagnosticElapsed = 0;

        bool hasTarget = TryGetPrefabCameraPosition(out var target);
        int pendingChunks = 0, candidates = 0, inRange = 0, waitingToSpawn = 0, alive = 0, inScene = 0, active = 0;
        int foliagePending = 0, foliageInstances = 0;
        foreach (var cell in _foliageCells.Values)
        {
            if (!cell.Complete)
                foliagePending++;
            foliageInstances += cell.Placements.Count;
        }
        var transform = Transform;
        foreach (var chunk in _prefabChunks.Values)
        {
            if (chunk.Entry < _prefabConfigs.Length)
                pendingChunks++;
            foreach (var candidate in chunk.Candidates)
            {
                candidates++;
                if (hasTarget && PrefabInRange(transform.LocalToWorld(candidate.Surface), target, PrefabSpawnDistance))
                {
                    inRange++;
                    if (candidate.Actor == null && !candidate.SpawnAttempted)
                        waitingToSpawn++;
                }
                var actor = candidate.Actor;
                if (actor == null)
                    continue;
                alive++;
                if (actor.Scene != null)
                    inScene++;
                if (actor.IsActiveInHierarchy)
                    active++;
            }
        }
        var counts = _prefabDiagnosticCounts;
        _prefabDiagnosticCounts = default;
        var text = new StringBuilder();
        text.Append($"Quad sphere prefab diagnostics [{Name}]: ready={_ready}, active={IsActiveInHierarchy}, initialRebuildPending={_initialRebuildPending}, completedLod={_hasCompletedLod}, brushRebuild={_previousBrushes != null}, generationJobs={_generation.Count}, pendingPatches={(_pendingKeys == null ? 0 : _pendingKeys.Count - _pendingIndex)}.");
        text.Append($" MainCamera={(hasTarget ? target.ToString() : "missing")}, spawnDistance={PrefabSpawnDistance}, radius={_radius}, chunksPerFaceSide={_chunksPerPatch}, chunkBounds={_cullRadius}, attemptsPerUpdate={PrefabAttemptsPerUpdate}.");
        text.Append($" Configs: live={PrefabConfigs?.Length ?? 0}, rebuildSnapshot={_prefabConfigs.Length}; chunks={_prefabChunks.Count}/{_cullCenters?.Length ?? 0}, pendingChunks={pendingChunks}, candidates={candidates}, candidatesInRange={inRange}, waitingToSpawn={waitingToSpawn}, trackedActors={alive}, inScene={inScene}, activeActors={active}.");
        text.Append($" Foliage: configs={_foliageConfigs.Length}, cells={_foliageCells.Count}, cellLimitReached={_foliageCellsLimited}, pendingCells={foliagePending}, placements={foliageInstances}, activeColliders={_foliageActiveColliders.Count}, pooledColliders={_foliageColliderPool.Count}.");
        text.Append($" Since last report: attempts={counts.Attempts}, rejectedArea={counts.Area}, rejectedHemisphere={counts.Hemisphere}, rejectedTemperature={counts.Temperature}, rejectedAltitude={counts.Altitude}, rejectedSlope={counts.Slope}, rejectedBiome={counts.Biome}, rejectedProbability={counts.Probability}, rejectedRadial={counts.Radial}, spawnFailed={counts.SpawnFailed}, spawned={counts.Spawned}, destructionScheduled={counts.Destroyed}.");
        for (int i = 0; i < _prefabConfigs.Length; i++)
        {
            var config = _prefabConfigs[i];
            string asset = config.Prefab == null ? "missing" : config.Prefab.LastLoadFailed ? "load failed" : config.Prefab.IsLoaded ? "loaded" : "loading";
            text.Append($" Entry {i}: asset={asset}, valid={ValidPrefabConfig(config)}, density={config.Density}, probability={config.Probability}, biomes={config.Biomes}, altitude={config.MinAltitude}..{config.MaxAltitude}, altitudeFalloff={config.AltitudeFalloff}, slope={config.MinSlope}..{config.MaxSlope}, temperature={config.MinTemperature}..{config.MaxTemperature}, hemisphere={config.Hemisphere}, noiseStrength={config.NoiseStrength}.");
        }
        Debug.Log(text.ToString());
    }

    private bool UpdatePrefabs()
    {
        if (!Engine.IsPlayMode || _prefabSnapshot == null || _prefabConfigs.Length == 0 ||
            !double.IsFinite(PrefabSpawnDistance) || PrefabSpawnDistance <= 0 || !TryGetPrefabCameraPosition(out var target))
        {
            ClearPrefabs();
            return false;
        }

        // Cleanup is not budgeted: actors must not remain out of range while
        // placement attempts or spawns are waiting for a later update.
        CleanupPrefabDistances(target);

        var transform = Transform;
        double scale = Math.Max(Math.Abs(transform.Scale.X), Math.Max(Math.Abs(transform.Scale.Y), Math.Abs(transform.Scale.Z)));
        double bounds = _cullRadius * scale;
        if (!double.IsFinite(bounds))
        {
            ClearPrefabs();
            return false;
        }

        for (int root = 0; root < _cullCenters.Length; root++)
        {
            double distance = (target - transform.LocalToWorld(_cullCenters[root])).Length;
            if (double.IsFinite(distance) && distance <= PrefabSpawnDistance + bounds)
            {
                if (!_prefabChunks.ContainsKey(root))
                    _prefabChunks.Add(root, new PrefabChunk());
            }
            else if (_prefabChunks.TryGetValue(root, out var chunk))
            {
                DestroyPrefabs(chunk);
                _prefabChunks.Remove(root);
            }
        }

        int budget = Math.Clamp(PrefabAttemptsPerUpdate, 1, 100000);
        foreach (var pair in _prefabChunks)
        {
            var chunk = pair.Value;
            int root = pair.Key;
            int face = root / (_chunksPerPatch * _chunksPerPatch);
            int y = root / _chunksPerPatch % _chunksPerPatch;
            int x = root % _chunksPerPatch;
            while (chunk.Entry < _prefabConfigs.Length && budget > 0)
            {
                var config = _prefabConfigs[chunk.Entry];
                if (config.Prefab == null || config.Prefab.LastLoadFailed || !ValidPrefabConfig(config))
                {
                    chunk.Entry++;
                    chunk.AttemptCount = -1;
                    continue;
                }
                if (!config.Prefab.IsLoaded)
                    break;
                if (chunk.AttemptCount < 0)
                {
                    double side = 2.0 / _chunksPerPatch;
                    double expected = config.Density * (_radius / 2000000.0) * (_radius / 2000000.0) * side * side;
                    if (!double.IsFinite(expected) || expected > int.MaxValue - 1)
                    {
                        Debug.LogWarning("Quad sphere prefab density produces too many attempts for a base chunk; reduce density or increase Chunks Per Patch.");
                        chunk.Entry++;
                        continue;
                    }
                    chunk.AttemptCount = (int)expected;
                    if (PrefabRandom(_noiseSeed, root, chunk.Entry, -1, 0) < expected - chunk.AttemptCount)
                        chunk.AttemptCount++;
                    chunk.Attempt = 0;
                }
                if (chunk.Attempt >= chunk.AttemptCount)
                {
                    chunk.Entry++;
                    chunk.AttemptCount = -1;
                    continue;
                }
                AddPrefabCandidate(config, chunk, root, face, x, y, chunk.Attempt++);
                budget--;
            }
            if (budget == 0)
                break;
        }

        int spawnBudget = Math.Clamp(PrefabAttemptsPerUpdate, 1, 100000);
        foreach (var chunk in _prefabChunks.Values)
        {
            for (int i = 0; i < chunk.Candidates.Count && spawnBudget > 0; i++)
            {
                var candidate = chunk.Candidates[i];
                if (candidate.Actor != null || candidate.SpawnAttempted ||
                    !PrefabInRange(transform.LocalToWorld(candidate.Surface), target, PrefabSpawnDistance))
                    continue;
                var config = _prefabConfigs[candidate.Entry];
                if (config.Prefab == null || !config.Prefab.IsLoaded || config.Prefab.LastLoadFailed)
                    continue;
                candidate.SpawnAttempted = true;
                candidate.Actor = SpawnPrefabCandidate(config, transform, candidate.Surface, candidate.Yaw);
                if (candidate.Actor != null)
                {
                    candidate.ActorId = candidate.Actor.ID;
                    _spawnedPrefabRoots.Add(candidate.ActorId);
                    _prefabBrushesDirty = true;
                }
                chunk.Candidates[i] = candidate;
                spawnBudget--;
            }
            if (spawnBudget == 0)
                break;
        }

        foreach (var chunk in _prefabChunks.Values)
        {
            if (chunk.Entry < _prefabConfigs.Length)
                return true;
            foreach (var candidate in chunk.Candidates)
            {
                var prefab = _prefabConfigs[candidate.Entry].Prefab;
                if (candidate.Actor == null && !candidate.SpawnAttempted && prefab != null && !prefab.LastLoadFailed &&
                    PrefabInRange(transform.LocalToWorld(candidate.Surface), target, PrefabSpawnDistance))
                    return true;
            }
        }
        return false;
    }

    private static bool TryGetPrefabCameraPosition(out Vector3 position)
    {
        var camera = Camera.MainCamera;
        if (camera != null)
        {
            position = camera.Position;
            return true;
        }
        position = Vector3.Zero;
        return false;
    }

    private static bool PrefabInRange(Vector3 position, Vector3 camera, double distance)
    {
        double actualDistance = (position - camera).Length;
        return double.IsFinite(actualDistance) && actualDistance <= distance;
    }

    private void CleanupPrefabDistances()
    {
        if (!Engine.IsPlayMode || !double.IsFinite(PrefabSpawnDistance) || PrefabSpawnDistance <= 0 ||
            !TryGetPrefabCameraPosition(out var target))
        {
            ClearPrefabs();
            return;
        }
        CleanupPrefabDistances(target);
    }

    private void CleanupPrefabDistances(Vector3 target)
    {
        var transform = Transform;
        foreach (var chunk in _prefabChunks.Values)
        {
            for (int i = 0; i < chunk.Candidates.Count; i++)
            {
                var candidate = chunk.Candidates[i];
                if (candidate.Actor != null && !PrefabInRange(candidate.Actor.Position, target, PrefabSpawnDistance))
                {
                    DestroySpawnedPrefab(candidate.Actor);
                    candidate.Actor = null;
                }
                if (candidate.Actor == null)
                {
                    if (candidate.ActorId != Guid.Empty)
                    {
                        _spawnedPrefabRoots.Remove(candidate.ActorId);
                        candidate.ActorId = Guid.Empty;
                        _prefabBrushesDirty = true;
                    }
                    if (!PrefabInRange(transform.LocalToWorld(candidate.Surface), target, PrefabSpawnDistance))
                        candidate.SpawnAttempted = false;
                }
                chunk.Candidates[i] = candidate;
            }
        }
    }

    private static bool ValidPrefabConfig(QuadSpherePrefabConfig config) =>
        float.IsFinite(config.Density) && config.Density > 0 && float.IsFinite(config.Probability) && config.Probability > 0 &&
        float.IsFinite(config.NoiseFrequency) && config.NoiseFrequency >= 0 && float.IsFinite(config.NoiseStrength) &&
        double.IsFinite(config.MinAltitude) && double.IsFinite(config.MaxAltitude) &&
        (config.MinAltitude == 0 || config.MaxAltitude == 0 || config.MaxAltitude >= config.MinAltitude) &&
        float.IsFinite(config.AltitudeFalloff) && config.AltitudeFalloff >= 0 &&
        float.IsFinite(config.MinSlope) && float.IsFinite(config.MaxSlope) && config.MaxSlope >= config.MinSlope;

    private void AddPrefabCandidate(QuadSpherePrefabConfig config, PrefabChunk chunk, int root, int face, int x, int y, int attempt)
    {
        _prefabDiagnosticCounts.Attempts++;
        if (TryPlacement(config, _chunksPerPatch, root, chunk.Entry, face, x, y, attempt, true, out var surface, out _))
            chunk.Candidates.Add(new PrefabCandidate { Surface = (Vector3)surface, Entry = chunk.Entry, Yaw = PlacementYaw(_noiseSeed, root, chunk.Entry, attempt) });
    }

    private bool TryPlacement(QuadSpherePrefabConfig config, int divisions, int root, int entry, int face, int x, int y, int attempt, bool diagnostics, out Double3 surface, out Double3 normal, QuadSphereFoliageNoiseType noiseType = QuadSphereFoliageNoiseType.Value, TerrainSnapshot terrain = null, bool useTerrainMesh = false, bool smoothSlope = false)
    {
        terrain ??= _prefabSnapshot;
        surface = Double3.Zero;
        normal = Double3.Zero;
        double u = -1.0 + 2.0 * (x + PrefabRandom(_noiseSeed, root, entry, attempt, 1)) / divisions;
        double v = -1.0 + 2.0 * (y + PrefabRandom(_noiseSeed, root, entry, attempt, 2)) / divisions;
        // Rejection sampling corrects the cube projection's varying area.
        double areaWeight = Math.Pow(1.0 + u * u + v * v, -1.5);
        if (PrefabRandom(_noiseSeed, root, entry, attempt, 3) >= areaWeight)
        {
            if (diagnostics) _prefabDiagnosticCounts.Area++;
            return false;
        }

        var direction = Direction(face, u, v);
        if (config.Hemisphere == QuadSphereFoliageHemisphere.North && direction.Y < 0 ||
            config.Hemisphere == QuadSphereFoliageHemisphere.South && direction.Y > 0)
        {
            if (diagnostics) _prefabDiagnosticCounts.Hemisphere++;
            return false;
        }
        if (config.MinTemperature > 0 || config.MaxTemperature < 1)
        {
            // 1 at the equator, 0 at the poles, linear in latitude.
            double temperature = 1.0 - Math.Asin(Math.Clamp(Math.Abs(direction.Y), 0.0, 1.0)) / (Math.PI * 0.5);
            if (temperature < config.MinTemperature || temperature > config.MaxTemperature)
            {
                if (diagnostics) _prefabDiagnosticCounts.Temperature++;
                return false;
            }
        }
        if (useTerrainMesh)
        {
            if (FoliageRemovedByBrush(direction, terrain, root, entry, attempt))
                return false;
            if (!TrySampleCommittedSurface(face, u, v, direction, out surface))
            {
                if (!_foliageSurfaceErrorLogged)
                {
                    Debug.LogWarning("Quad sphere foliage could not sample a committed terrain triangle; affected placements will be skipped.");
                    _foliageSurfaceErrorLogged = true;
                }
                return false;
            }
        }
        else
            surface = Surface(direction, terrain);
        double altitude = surface.Length;
        double altitudeWeight = PrefabAltitudeWeight(altitude, config);
        if (altitudeWeight <= 0)
        {
            if (diagnostics) _prefabDiagnosticCounts.Altitude++;
            return false;
        }
        normal = SurfaceNormal(direction, terrain);
        double slope = terrain.SimpleSphere ? 0.0 : Math.Acos(Math.Clamp(Double3.Dot(normal, direction), -1.0, 1.0)) * (180.0 / Math.PI);
        double slopeWeight = FoliageSlopeWeight(slope, config.MinSlope, config.MaxSlope, smoothSlope);
        if (slopeWeight <= 0)
        {
            if (diagnostics) _prefabDiagnosticCounts.Slope++;
            return false;
        }

        double biomeWeight = PrefabBiomeWeight(direction, config.Biomes, terrain);
        if (biomeWeight <= 0)
        {
            if (diagnostics) _prefabDiagnosticCounts.Biome++;
            return false;
        }

        double noiseWeight = 1.0;
        if (config.NoiseStrength > 0)
            noiseWeight -= Math.Clamp(config.NoiseStrength, 0, 1) * (1.0 - PlacementNoise(direction, config.NoiseFrequency, config.NoiseOctaves, config.SeedOffset, noiseType));
        double probability = Math.Clamp(config.Probability, 0, 1) * biomeWeight * altitudeWeight * noiseWeight * slopeWeight;
        if (PrefabRandom(_noiseSeed, root, entry, attempt, 4) >= probability)
        {
            if (diagnostics) _prefabDiagnosticCounts.Probability++;
            return false;
        }

        return true;
    }

    private static double FoliageSlopeWeight(double slope, float minSlope, float maxSlope, bool smoothSlope)
    {
        if (slope < minSlope || slope > maxSlope)
            return 0;
        return smoothSlope && maxSlope > minSlope ? (maxSlope - slope) / ((double)maxSlope - minSlope) : 1.0;
    }

    private bool FoliageRemovedByBrush(Double3 direction, TerrainSnapshot terrain, int cell, int entry, int attempt)
    {
        if (terrain.SimpleSphere)
            return false;
        double removal = 0;
        Vector3 worldPosition = Vector3.Zero;
        bool sampledPosition = false;
        foreach (var brush in terrain.Brushes)
        {
            if (brush.Mode != QuadSphereBrushMode.RemoveFoliage)
                continue;
            if (!sampledPosition)
            {
                double height = BaseElevation(direction, terrain);
                height = FlattenBeach(height, GetBeachWeight(height, terrain), terrain);
                worldPosition = terrain.PlanetTransform.LocalToWorld((Vector3)(direction * Math.Max(0.01, terrain.Radius + height)));
                sampledPosition = true;
            }
            removal = Math.Max(removal, BrushWeight(worldPosition, brush));
            if (removal >= 1.0)
                return true;
        }
        return removal > 0 && PrefabRandom(_noiseSeed, cell, entry, attempt, 7) < removal;
    }

    private bool TrySampleCommittedSurface(int face, double u, double v, Double3 direction, out Double3 surface)
    {
        surface = Double3.Zero;
        for (int level = 0; level <= 10; level++)
        {
            int divisions = _chunksPerPatch << level;
            double patchX = (u + 1.0) * 0.5 * divisions;
            double patchY = (v + 1.0) * 0.5 * divisions;
            int x = Math.Clamp((int)patchX, 0, divisions - 1);
            int y = Math.Clamp((int)patchY, 0, divisions - 1);
            if (!_patches.TryGetValue((face, level, x, y), out var patch) || !patch.Committed)
                continue;
            return TrySamplePatchSurface(patch, _resolution, patchX - x, patchY - y, direction, out surface);
        }
        return false;
    }

    private static bool TrySamplePatchSurface(Patch patch, int resolution, double u, double v, Double3 direction, out Double3 surface)
    {
        surface = Double3.Zero;
        if (patch.Vertices == null || patch.Vertices.Length < (resolution + 1) * (resolution + 1))
            return false;
        double gridX = Math.Clamp(u * resolution, 0.0, resolution);
        double gridY = Math.Clamp(v * resolution, 0.0, resolution);
        int x = Math.Min((int)gridX, resolution - 1);
        int y = Math.Min((int)gridY, resolution - 1);
        int a = y * (resolution + 1) + x;
        int b = a + 1;
        int c = a + resolution + 1;
        int d = c + 1;
        // Radial projection onto the cube face preserves the mesh's diagonal.
        if (gridX - x + gridY - y > 1.0)
            a = d;
        var p0 = patch.Origin + (Double3)patch.Vertices[a];
        var p1 = patch.Origin + (Double3)patch.Vertices[b];
        var p2 = patch.Origin + (Double3)patch.Vertices[c];
        var planeNormal = Double3.Cross(p1 - p0, p2 - p0);
        double denominator = Double3.Dot(planeNormal, direction);
        if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-20)
            return false;
        double distance = Double3.Dot(planeNormal, p0) / denominator;
        if (!double.IsFinite(distance) || distance <= 0)
            return false;
        surface = direction * distance;
        return true;
    }

    private double PlacementNoise(Double3 direction, float noiseFrequency, int noiseOctaves, int seedOffset, QuadSphereFoliageNoiseType noiseType = QuadSphereFoliageNoiseType.Value)
    {
        double noise = 0, amplitude = 1, total = 0, frequency = Math.Clamp(noiseFrequency, 0, 4096);
        for (int octave = 0; octave < Math.Clamp(noiseOctaves, 1, 8); octave++)
        {
            int seed = unchecked(_noiseSeed + seedOffset + octave * 1013);
            double sample;
            switch (noiseType)
            {
            case QuadSphereFoliageNoiseType.Value:
                sample = ValueNoise(direction * frequency, seed);
                break;
            case QuadSphereFoliageNoiseType.Perlin:
                sample = GradientNoise(direction * frequency, seed);
                break;
            case QuadSphereFoliageNoiseType.RidgedPerlin:
                sample = 2.0 * (1.0 - Math.Clamp(Math.Abs(GradientNoise(direction * frequency, seed)), 0.0, 1.0)) - 1.0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(noiseType), noiseType, "Unsupported foliage noise type.");
            }
            noise += sample * amplitude;
            total += amplitude;
            amplitude *= 0.5;
            frequency *= 2;
        }
        return Math.Clamp((noise / total + 1.0) * 0.5, 0.0, 1.0);
    }

    private static double GradientNoise(Double3 p, int seed)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y), z = (int)Math.Floor(p.Z);
        double fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
        double tx = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
        double ty = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
        double tz = fz * fz * fz * (fz * (fz * 6 - 15) + 10);
        double result = 0;
        for (int dz = 0; dz <= 1; dz++)
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    uint hash = unchecked((uint)((x + dx) * 73856093 ^ (y + dy) * 19349663 ^ (z + dz) * 83492791 ^ seed));
                    hash ^= hash >> 16; hash = unchecked(hash * 0x7feb352du); hash ^= hash >> 15;
                    hash = unchecked(hash * 0x846ca68bu); hash ^= hash >> 16;
                    int gradient = (int)(hash % 12);
                    double gx = fx - dx, gy = fy - dy, gz = fz - dz;
                    double a = gradient < 8 ? gx : gy;
                    double b = gradient < 4 ? gy : gz;
                    double value = ((gradient & 1) == 0 ? a : -a) + ((gradient & 2) == 0 ? b : -b);
                    result += value * (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty) * (dz == 0 ? 1 - tz : tz);
                }
        return result;
    }

    private Actor SpawnPrefabCandidate(QuadSpherePrefabConfig config, Transform transform, Vector3 surface, float yaw)
    {
        Vector3 position = transform.LocalToWorld(surface);
        Vector3 radial = position - transform.LocalToWorld(Vector3.Zero);
        if (radial.LengthSquared < 0.000000000001)
        {
            _prefabDiagnosticCounts.Radial++;
            return null;
        }
        radial = Vector3.Normalize(radial);
        var instance = PrefabManager.SpawnPrefab(config.Prefab, null);
        if (instance == null)
        {
            _prefabDiagnosticCounts.SpawnFailed++;
            return null;
        }
        instance.Position = position;
        instance.Orientation = PlacementOrientation(radial, yaw);
        instance.SetParent(this, true, false);
        _prefabDiagnosticCounts.Spawned++;
        return instance;
    }

    private static QuadSpherePrefabConfig FoliagePlacementSettings(QuadSphereFoliageConfig config) => new QuadSpherePrefabConfig
    {
        Biomes = config.Biomes,
        Density = config.Density,
        Probability = config.Probability,
        NoiseFrequency = config.NoiseFrequency,
        NoiseOctaves = config.NoiseOctaves,
        SeedOffset = config.NoiseSeedOffset,
        NoiseStrength = config.NoiseDensityStrength,
        MinAltitude = config.MinAltitude,
        MaxAltitude = config.MaxAltitude,
        MinSlope = config.MinSlope,
        MaxSlope = config.MaxSlope,
        MinTemperature = config.MinTemperature,
        MaxTemperature = config.MaxTemperature,
        Hemisphere = config.Hemisphere,
    };

    private static bool ValidFoliageNoise(QuadSphereFoliageConfig config) =>
        (config.NoiseType == QuadSphereFoliageNoiseType.Value || config.NoiseType == QuadSphereFoliageNoiseType.Perlin || config.NoiseType == QuadSphereFoliageNoiseType.RidgedPerlin) &&
        float.IsFinite(config.NoiseFrequency) && config.NoiseFrequency >= 0 &&
        float.IsFinite(config.NoiseDensityStrength) && float.IsFinite(config.NoiseScaleStrength);

    private void UpdateFoliage()
    {//!Engine.IsPlayMode || 
        if (_foliageSnapshot == null || _foliageConfigs.Length == 0 ||
            !TryGetPrefabCameraPosition(out var camera) ||
            !double.IsFinite(camera.X) || !double.IsFinite(camera.Y) || !double.IsFinite(camera.Z))
        {
            ClearFoliage();
            return;
        }

        var transform = Transform;
        double scale = Math.Max(Math.Abs(transform.Scale.X), Math.Max(Math.Abs(transform.Scale.Y), Math.Abs(transform.Scale.Z)));
        double minScale = Math.Min(Math.Abs(transform.Scale.X), Math.Min(Math.Abs(transform.Scale.Y), Math.Abs(transform.Scale.Z)));
        double factor = Math.Sqrt(2.0) / _chunksPerPatch + 0.00001;
        double displacement = (_cullRadius - _radius * factor) / (1.0 + factor);
        double cellBounds = (displacement + (_radius + displacement) * (Math.Sqrt(2.0) / _foliageDivisions + 0.00001)) * scale;
        if (!double.IsFinite(cellBounds) || !double.IsFinite(minScale) || minScale <= 0)
        {
            ClearFoliage();
            return;
        }

        _foliageSelectionElapsed += Time.UnscaledDeltaTime;
        int cellLimit = Math.Clamp(MaxFoliageCells, 1, 4096);
        if (_foliageSelectionElapsed >= 0.25f || _foliageCellLimit != cellLimit)
        {
            _foliageSelectionElapsed = 0;
            _foliageCellLimit = cellLimit;
            var ordered = SelectFoliageCells(camera, transform, scale, minScale, cellBounds, cellLimit, out _foliageCellsLimited);
            var desired = new HashSet<(int Cell, int Entry)>();
            foreach (var item in ordered)
            {
                desired.Add(item.Key);
                if (!_foliageCells.ContainsKey(item.Key))
                    _foliageCells.Add(item.Key, new FoliageCell { Entry = item.Key.Entry });
            }
            var remove = new List<(int Cell, int Entry)>();
            foreach (var pair in _foliageCells)
                if (!desired.Contains(pair.Key))
                    remove.Add(pair.Key);
            foreach (var key in remove)
            {
                var cell = _foliageCells[key];
                if (cell.Renderer != null)
                    Destroy(cell.Renderer);
                _foliageCells.Remove(key);
            }
            if (remove.Count != 0)
                _foliageCollisionElapsed = 0.1f;
            _foliageCellOrder.Clear();
            foreach (var item in ordered)
                _foliageCellOrder.Add(item.Key);
        }

        if (_foliageCastShadows != FoliageCastShadows)
        {
            _foliageCastShadows = FoliageCastShadows;
            foreach (var pair in _foliageCells)
                if (pair.Value.Renderer != null && _foliageConfigs[pair.Key.Entry].Shadows == QuadSphereFoliageShadows.Legacy)
                    ConfigureFoliageDistance(pair.Value.Renderer, pair.Key.Entry);
        }

        int budget = Math.Clamp(FoliageAttemptsPerUpdate, 1, 100000);
        int installBudget = budget;
        foreach (var cellKey in _foliageCellOrder)
        {
            var cell = _foliageCells[cellKey];
            if (cell.Complete)
                continue;
            int key = cellKey.Cell;
            int face = key / (_foliageDivisions * _foliageDivisions);
            int y = key / _foliageDivisions % _foliageDivisions;
            int x = key % _foliageDivisions;
            while (!cell.GenerationComplete && cell.Entry == cellKey.Entry && budget > 0)
            {
                var config = _foliageConfigs[cell.Entry];
                var placement = FoliagePlacementSettings(config);
                if (config.Model == null || config.Model.LastLoadFailed || !ValidPrefabConfig(placement) ||
                    !float.IsFinite(config.MinScale) || !float.IsFinite(config.MaxScale) || config.MinScale <= 0 || config.MaxScale < config.MinScale)
                {
                    cell.Entry++;
                    cell.AttemptCount = -1;
                    continue;
                }
                if (!config.Model.IsLoaded)
                    break;
                if (cell.AttemptCount < 0)
                {
                    double expected = FoliageExpectedAttempts(config);
                    if (!double.IsFinite(expected) || expected > int.MaxValue - 1)
                    {
                        Debug.LogWarning("Quad sphere foliage density produces too many attempts per cell; reduce density or increase Foliage Cells Per Face.");
                        cell.Entry++;
                        continue;
                    }
                    cell.AttemptCount = FoliageAttemptCount(expected, key, cell.Entry);
                    cell.Attempt = 0;
                }
                if (cell.Attempt >= cell.AttemptCount)
                {
                    cell.Entry++;
                    cell.AttemptCount = -1;
                    continue;
                }
                int attempt = cell.Attempt++;
                if (TryPlacement(placement, _foliageDivisions, key, cell.Entry, face, x, y, attempt, false, out var surface, out var normal, config.NoiseType, _foliageSnapshot, true, config.SmoothSlope))
                    AddFoliagePlacement(cell, config, surface, normal, key, attempt);
                budget--;
            }
            if (cell.Entry != cellKey.Entry)
                cell.GenerationComplete = true;
            if (cell.GenerationComplete)
                InstallFoliageCell(cell, cellKey.Entry, ref installBudget);
            if (budget == 0 && installBudget == 0)
                break;
        }

        _foliageCollisionElapsed += Time.UnscaledDeltaTime;
        if (_foliageCollisionElapsed >= 0.1f)
        {
            _foliageCollisionElapsed = 0;
            UpdateFoliageColliders(camera, transform, cellBounds);
        }
    }

    private List<((int Cell, int Entry) Key, double Distance)> SelectFoliageCells(Vector3 camera, Transform transform, double scale, double minScale, double cellBounds, int cellLimit, out bool limited)
    {
        var nearest = new SortedSet<(double Distance, int Cell, int Entry)>();
        var perEntry = new SortedSet<(double Distance, int Cell, int Entry)>[_foliageConfigs.Length];
        var entryLimits = new int[_foliageConfigs.Length];
        var entryLimited = new bool[_foliageConfigs.Length];
        var regions = new PriorityQueue<FoliageSearchRegion, double>();
        var deferred = new Stack<(FoliageSearchRegion Region, double LowerBound)>();
        var localCamera = (Double3)transform.WorldToLocal(camera);
        double padding = (camera.Length + transform.Translation.Length + _radius * scale + 1.0) * 0.00001;
        for (int entry = 0; entry < _foliageConfigs.Length; entry++)
        {
            var config = _foliageConfigs[entry];
            int limit = FoliageEntryCellLimit(config, cellLimit);
            double spawnDistance = FoliageEntrySpawnDistance(config);
            if (limit == 0 || !double.IsFinite(spawnDistance) || spawnDistance <= 0 || config.Model == null || !ValidFoliageNoise(config))
                continue;
            entryLimits[entry] = limit;
            perEntry[entry] = new SortedSet<(double Distance, int Cell, int Entry)>();
            double localDistance = (spawnDistance + cellBounds) / minScale;
            for (int face = 0; face < 6; face++)
            {
                if (FoliageFaceRange(face, localCamera, localDistance, out int minX, out int maxX, out int minY, out int maxY))
                    EnqueueFoliageRegion(regions, deferred, new FoliageSearchRegion { Face = face, Entry = entry, MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, MaxDistance = spawnDistance + cellBounds }, camera, transform, scale, padding);
            }
        }

        limited = false;
        while (regions.Count != 0 || deferred.Count != 0)
        {
            bool fromStack = deferred.Count != 0;
            FoliageSearchRegion region;
            double lowerBound;
            if (fromStack)
            {
                var item = deferred.Pop();
                region = item.Region;
                lowerBound = item.LowerBound;
            }
            else
                regions.TryDequeue(out region, out lowerBound);
            if (limited && nearest.Count == cellLimit && lowerBound > nearest.Max.Distance)
            {
                if (!fromStack)
                    break;
                continue;
            }
            var entryCells = perEntry[region.Entry];
            if (entryLimited[region.Entry] && entryCells.Count == entryLimits[region.Entry] && lowerBound > entryCells.Max.Distance)
                continue;
            if (region.MinX != region.MaxX || region.MinY != region.MaxY)
            {
                var other = region;
                if (region.MaxX - region.MinX >= region.MaxY - region.MinY)
                {
                    region.MaxX = (region.MinX + region.MaxX) / 2;
                    other.MinX = region.MaxX + 1;
                }
                else
                {
                    region.MaxY = (region.MinY + region.MaxY) / 2;
                    other.MinY = region.MaxY + 1;
                }
                EnqueueFoliageRegion(regions, deferred, region, camera, transform, scale, padding);
                EnqueueFoliageRegion(regions, deferred, other, camera, transform, scale, padding);
                continue;
            }
            int cell = (region.Face * _foliageDivisions + region.MinY) * _foliageDivisions + region.MinX;
            double distance = (camera - transform.LocalToWorld(FoliageCellCenter(region.Face, region.MinX, region.MinY))).Length;
            if (!double.IsFinite(distance) || distance > region.MaxDistance)
                continue;
            var candidate = (Distance: distance, Cell: cell, Entry: region.Entry);
            if (entryCells.Count == entryLimits[region.Entry])
            {
                limited = entryLimited[region.Entry] = true;
                var farthest = entryCells.Max;
                if (candidate.CompareTo(farthest) >= 0)
                    continue;
                entryCells.Remove(farthest);
                nearest.Remove(farthest);
            }
            else if (nearest.Count == cellLimit)
            {
                limited = true;
                var farthest = nearest.Max;
                if (candidate.CompareTo(farthest) >= 0)
                    continue;
                nearest.Remove(farthest);
                perEntry[farthest.Entry].Remove(farthest);
            }
            nearest.Add(candidate);
            entryCells.Add(candidate);
        }

        var result = new List<((int Cell, int Entry) Key, double Distance)>(nearest.Count);
        foreach (var item in nearest)
            result.Add(((item.Cell, item.Entry), item.Distance));
        return result;
    }

    private double FoliageExpectedAttempts(QuadSphereFoliageConfig config)
    {
        double side = 2.0 / _foliageDivisions;
        return config.Density * (_radius / 1000.0) * (_radius / 1000.0) * side * side;
    }

    private int FoliageAttemptCount(double expected, int cell, int entry)
    {
        int count = (int)expected;
        if (PrefabRandom(_noiseSeed, cell, entry, -1, 0) < expected - count)
            count++;
        return count;
    }

    private void AddFoliagePlacement(FoliageCell cell, QuadSphereFoliageConfig config, Double3 surface, Double3 normal, int key, int attempt)
    {
        float yaw = PlacementYaw(_noiseSeed, key, cell.Entry, attempt);
        var orientation = FoliageOrientation(surface, normal, config.AlignToGroundNormal, yaw);
        float instanceScale = FoliageInstanceScale(config, surface, key, cell.Entry, attempt);
        cell.Placements.Add(new FoliagePlacement { Entry = cell.Entry, Transform = new Transform((Vector3)surface, orientation, new Float3(instanceScale)) });
    }

    private float FoliageInstanceScale(QuadSphereFoliageConfig config, Double3 surface, int key, int entry, int attempt)
    {
        float value = (float)PrefabRandom(_noiseSeed, key, entry, attempt, 6);
        if (config.NoiseScaleStrength > 0)
        {
            float noise = (float)PlacementNoise(Double3.Normalize(surface), config.NoiseFrequency, config.NoiseOctaves, config.NoiseSeedOffset, config.NoiseType);
            value += (noise - value) * Math.Clamp(config.NoiseScaleStrength, 0, 1);
        }
        return config.MinScale + (config.MaxScale - config.MinScale) * value;
    }

    private static Quaternion FoliageOrientation(Double3 surface, Double3 normal, bool alignToGroundNormal, float yaw)
    {
        Vector3 up = alignToGroundNormal ? (Vector3)normal : Vector3.Normalize((Vector3)surface);
        return PlacementOrientation(up, yaw);
    }

    private static Quaternion PlacementOrientation(Vector3 up, float yaw)
    {
        Vector3 reference = Math.Abs(up.Y) < 0.9 ? Vector3.UnitY : Vector3.UnitX;
        Float3 forward = (Float3)Vector3.Normalize(Vector3.Cross(reference, up));
        forward = Float3.Transform(forward, Quaternion.RotationAxis((Float3)up, yaw));
        return Quaternion.LookRotation(forward, (Float3)up);
    }

    private Vector3 FoliageCellCenter(int face, int x, int y)
    {
        double u = -1.0 + 2.0 * (x + 0.5) / _foliageDivisions;
        double v = -1.0 + 2.0 * (y + 0.5) / _foliageDivisions;
        return (Vector3)(Direction(face, u, v) * _radius);
    }

    private void EnqueueFoliageRegion(PriorityQueue<FoliageSearchRegion, double> regions, Stack<(FoliageSearchRegion Region, double LowerBound)> deferred, FoliageSearchRegion region, Vector3 camera, Transform transform, double scale, double padding)
    {
        double u = -1.0 + (region.MinX + region.MaxX + 1.0) / _foliageDivisions;
        double v = -1.0 + (region.MinY + region.MaxY + 1.0) / _foliageDivisions;
        var center = (Vector3)(Direction(region.Face, u, v) * _radius);
        double width = (double)(region.MaxX - region.MinX) / _foliageDivisions;
        double height = (double)(region.MaxY - region.MinY) / _foliageDivisions;
        // Cube-face normalization is 1-Lipschitz; max scale bounds the world-space radius.
        double radius = _radius * scale * Math.Sqrt(width * width + height * height) + padding;
        double lowerBound = (camera - transform.LocalToWorld(center)).Length - radius;
        if (!double.IsFinite(lowerBound))
            lowerBound = 0;
        if (lowerBound > region.MaxDistance)
            return;
        lowerBound = Math.Max(0, lowerBound);
        // Avoid an unbounded priority queue when many cells are equally distant.
        if (regions.Count >= 8192)
            deferred.Push((region, lowerBound));
        else
            regions.Enqueue(region, lowerBound);
    }

    private bool FoliageFaceRange(int face, Double3 camera, double localDistance, out int minX, out int maxX, out int minY, out int maxY)
    {
        minX = minY = 0;
        maxX = maxY = _foliageDivisions - 1;
        double length = camera.Length;
        if (double.IsFinite(length) && double.IsFinite(localDistance) && Math.Abs(length - _radius) > localDistance)
            return false;
        if (!double.IsFinite(length) || !double.IsFinite(localDistance) || length < 0.000001)
            return true;
        var direction = camera / length;
        // Bound each component of a nearby cell direction, then project its
        // interval onto the cube face using its minimum dominant component.
        double chord = Math.Min(2.0, (localDistance + Math.Abs(length - _radius)) / _radius + 0.000000001);
        double axis, u, v;
        switch (face)
        {
        case 0: axis = direction.X; u = -direction.Z; v = direction.Y; break;
        case 1: axis = -direction.X; u = direction.Z; v = direction.Y; break;
        case 2: axis = direction.Y; u = direction.X; v = -direction.Z; break;
        case 3: axis = -direction.Y; u = direction.X; v = direction.Z; break;
        case 4: axis = direction.Z; u = direction.X; v = direction.Y; break;
        default: axis = -direction.Z; u = -direction.X; v = direction.Y; break;
        }
        const double minimumAxis = 0.5773502691896257;
        double maximum = Math.Min(1.0, axis + chord);
        if (maximum < minimumAxis)
            return false;
        double minimum = Math.Max(minimumAxis, axis - chord);
        FoliageCoordinateRange(u, chord, minimum, maximum, out minX, out maxX);
        FoliageCoordinateRange(v, chord, minimum, maximum, out minY, out maxY);
        return minX <= maxX && minY <= maxY;
    }

    private void FoliageCoordinateRange(double coordinate, double chord, double minimumAxis, double maximumAxis, out int min, out int max)
    {
        double low = coordinate - chord, high = coordinate + chord;
        double minValue = Math.Max(-1.0, Math.Min(low / minimumAxis, low / maximumAxis));
        double maxValue = Math.Min(1.0, Math.Max(high / minimumAxis, high / maximumAxis));
        if (minValue > maxValue)
        {
            min = 1;
            max = 0;
            return;
        }
        min = Math.Clamp((int)Math.Floor((minValue + 1.0) * 0.5 * _foliageDivisions) - 1, 0, _foliageDivisions - 1);
        max = Math.Clamp((int)Math.Floor((maxValue + 1.0) * 0.5 * _foliageDivisions) + 1, 0, _foliageDivisions - 1);
    }

    private double FoliageEntrySpawnDistance(QuadSphereFoliageConfig config) =>
        config.SpawnDistance == -1 ? FoliageSpawnDistance : config.SpawnDistance;

    private static int FoliageEntryCellLimit(QuadSphereFoliageConfig config, int globalLimit) =>
        config.MaxCells == -1 ? globalLimit : Math.Clamp(config.MaxCells, 0, globalLimit);

    private double FoliageEntryCollisionDistance(QuadSphereFoliageConfig config) =>
        config.CollisionDistance == -1 ? FoliageCollisionDistance : config.CollisionDistance;

    private int FoliageEntryColliderLimit(QuadSphereFoliageConfig config) =>
        Math.Clamp(config.MaxColliders == -1 ? MaxFoliageColliders : config.MaxColliders, 0, 1024);

    private bool FoliageEntryCastsShadows(QuadSphereFoliageConfig config) =>
        config.Shadows == QuadSphereFoliageShadows.Legacy ? FoliageCastShadows : config.Shadows == QuadSphereFoliageShadows.Enabled;

    private void InstallFoliageCell(FoliageCell cell, int entry, ref int budget)
    {
        if (cell.Placements.Count == 0)
        {
            cell.Complete = cell.GenerationComplete;
            return;
        }
        if (budget == 0)
            return;
        if (cell.Renderer == null)
        {
            cell.Renderer = new Foliage { HideFlags = HideFlags.FullyHidden, IsActive = false };
            cell.Renderer.SetParent(this, false, false);
            cell.Renderer.LocalTransform = FlaxEngine.Transform.Identity;
        }
        var renderer = cell.Renderer;
        int previouslyInstalled = cell.Installed;
        while (cell.Installed < cell.Placements.Count && budget > 0)
        {
            var placement = cell.Placements[cell.Installed++];
            var model = _foliageConfigs[placement.Entry].Model;
            if (!cell.Types.TryGetValue(model, out int type))
            {
                type = cell.Types.Count;
                cell.Types.Add(model, type);
                renderer.AddFoliageType(model);
            }
            var instance = new FoliageInstance { Type = (ushort)type, Transform = placement.Transform };
            renderer.AddInstance(ref instance);
            budget--;
        }
        if (cell.Installed != previouslyInstalled)
        {
            ConfigureFoliageDistance(renderer, entry);
            renderer.RebuildClusters();
            renderer.IsActive = true;
        }
        cell.Complete = cell.GenerationComplete && cell.Installed == cell.Placements.Count;
    }

    private void ConfigureFoliageDistance(Foliage renderer, int entry)
    {
        for (int i = 0; i < renderer.FoliageTypesCount; i++)
        {
            renderer.FoliageTypes[i].CullDistance = (float)Math.Min(FoliageEntrySpawnDistance(_foliageConfigs[entry]), float.MaxValue);
            renderer.FoliageTypes[i].CullDistanceRandomRange = 0;
            renderer.FoliageTypes[i].ShadowsMode = FoliageEntryCastsShadows(_foliageConfigs[entry]) ? ShadowsCastingMode.All : ShadowsCastingMode.None;
        }
        renderer.UpdateCullDistance();
    }

    private void UpdateFoliageColliders(Vector3 camera, Transform planetTransform, double cellBounds = double.PositiveInfinity)
    {
        var nearest = new List<FoliageCollisionCandidate>[_foliageConfigs.Length];
        foreach (var pair in _foliageCells)
        {
            var cell = pair.Value;
            if (cell.Renderer == null || !cell.Renderer.IsActive)
                continue;
            var config = _foliageConfigs[pair.Key.Entry];
            int limit = FoliageEntryColliderLimit(config);
            double collisionDistance = Math.Min(FoliageEntryCollisionDistance(config), FoliageEntrySpawnDistance(config));
            if (limit == 0 || !double.IsFinite(collisionDistance) || collisionDistance <= 0)
                continue;
            if (double.IsFinite(cellBounds))
            {
                int face = pair.Key.Cell / (_foliageDivisions * _foliageDivisions);
                int y = pair.Key.Cell / _foliageDivisions % _foliageDivisions;
                int x = pair.Key.Cell % _foliageDivisions;
                double cellDistance = (camera - planetTransform.LocalToWorld(FoliageCellCenter(face, x, y))).Length;
                if (!double.IsFinite(cellDistance) || cellDistance > collisionDistance + cellBounds)
                    continue;
            }
            var candidates = nearest[pair.Key.Entry] ??= new List<FoliageCollisionCandidate>();
            for (int i = 0; i < cell.Installed; i++)
            {
                var placement = cell.Placements[i];
                var size = config.CollisionSize;
                var offset = config.CollisionOffset;
                if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || !float.IsFinite(size.Z) || size.X <= 0 || size.Y <= 0 || size.Z <= 0 ||
                    !float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z))
                    continue;
                double distance = (camera - planetTransform.LocalToWorld(placement.Transform.Translation)).Length;
                if (!double.IsFinite(distance) || distance > collisionDistance)
                    continue;
                int index = candidates.Count;
                while (index > 0 && candidates[index - 1].Distance > distance)
                    index--;
                if (index >= limit)
                    continue;
                candidates.Insert(index, new FoliageCollisionCandidate { Key = (pair.Key.Cell, pair.Key.Entry, i), Placement = placement, Distance = distance });
                if (candidates.Count > limit)
                    candidates.RemoveAt(limit);
            }
        }
        var desired = new HashSet<(int Cell, int Entry, int Index)>();
        foreach (var candidates in nearest)
            if (candidates != null)
                foreach (var candidate in candidates)
                    desired.Add(candidate.Key);
        var remove = new List<(int Cell, int Entry, int Index)>();
        foreach (var pair in _foliageActiveColliders)
            if (!desired.Contains(pair.Key))
                remove.Add(pair.Key);
        foreach (var key in remove)
        {
            var collider = _foliageActiveColliders[key];
            collider.IsActive = false;
            _foliageColliderPool.Add(collider);
            _foliageActiveColliders.Remove(key);
        }
        foreach (var candidates in nearest)
        {
            if (candidates == null)
                continue;
            foreach (var candidate in candidates)
            {
                if (_foliageActiveColliders.ContainsKey(candidate.Key))
                    continue;
                BoxCollider collider;
                if (_foliageColliderPool.Count != 0)
                {
                    int last = _foliageColliderPool.Count - 1;
                    collider = _foliageColliderPool[last];
                    _foliageColliderPool.RemoveAt(last);
                }
                else
                {
                    collider = new BoxCollider { HideFlags = HideFlags.FullyHidden, IsActive = false };
                    collider.SetParent(this, false, false);
                }
                var config = _foliageConfigs[candidate.Placement.Entry];
                var instance = candidate.Placement.Transform;
                collider.Size = config.CollisionSize;
                collider.LocalTransform = new Transform(instance.LocalToWorld((Vector3)config.CollisionOffset), instance.Orientation, instance.Scale);
                collider.IsActive = true;
                _foliageActiveColliders.Add(candidate.Key, collider);
            }
        }
    }

    private void InvalidateFoliageForPatches(IEnumerable<(int Face, int Level, int X, int Y)> patches)
    {
        var liveCells = new HashSet<int>();
        foreach (var key in _foliageCells.Keys)
            liveCells.Add(key.Cell);
        var affectedCells = new HashSet<int>();
        foreach (var patch in patches)
        {
            int divisions = _chunksPerPatch << patch.Level;
            int minX = (int)((long)patch.X * _foliageDivisions / divisions);
            int minY = (int)((long)patch.Y * _foliageDivisions / divisions);
            int maxX = (int)(((long)(patch.X + 1) * _foliageDivisions - 1) / divisions);
            int maxY = (int)(((long)(patch.Y + 1) * _foliageDivisions - 1) / divisions);
            long count = (long)(maxX - minX + 1) * (maxY - minY + 1);
            if (count <= liveCells.Count)
            {
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        int cell = (patch.Face * _foliageDivisions + y) * _foliageDivisions + x;
                        if (liveCells.Contains(cell))
                            affectedCells.Add(cell);
                    }
            }
            else
            {
                foreach (int cell in liveCells)
                {
                    int face = cell / (_foliageDivisions * _foliageDivisions);
                    int y = cell / _foliageDivisions % _foliageDivisions;
                    int x = cell % _foliageDivisions;
                    if (face == patch.Face && x >= minX && x <= maxX && y >= minY && y <= maxY)
                        affectedCells.Add(cell);
                }
            }
            if (affectedCells.Count == liveCells.Count)
                break;
        }
        if (affectedCells.Count == 0)
            return;
        var affected = new List<(int Cell, int Entry)>();
        foreach (var key in _foliageCells.Keys)
            if (affectedCells.Contains(key.Cell))
                affected.Add(key);

        var colliders = new List<(int Cell, int Entry, int Index)>();
        foreach (var pair in _foliageActiveColliders)
            if (affectedCells.Contains(pair.Key.Cell))
                colliders.Add(pair.Key);
        foreach (var key in colliders)
        {
            var collider = _foliageActiveColliders[key];
            if (collider != null)
            {
                collider.IsActive = false;
                _foliageColliderPool.Add(collider);
            }
            _foliageActiveColliders.Remove(key);
        }
        foreach (var key in affected)
        {
            var cell = _foliageCells[key];
            if (cell.Renderer != null)
            {
                cell.Renderer.IsActive = false;
                Destroy(cell.Renderer);
            }
            _foliageCells[key] = new FoliageCell { Entry = key.Entry };
        }
        _foliageCollisionElapsed = 0.1f;
    }

    private void DisableFoliageColliders()
    {
        foreach (var collider in _foliageActiveColliders.Values)
        {
            if (collider != null)
            {
                collider.IsActive = false;
                _foliageColliderPool.Add(collider);
            }
        }
        _foliageActiveColliders.Clear();
    }

    private void ClearFoliage()
    {
        DisableFoliageColliders();
        foreach (var collider in _foliageColliderPool)
            if (collider != null)
                Destroy(collider);
        _foliageColliderPool.Clear();
        foreach (var cell in _foliageCells.Values)
            if (cell.Renderer != null)
                Destroy(cell.Renderer);
        _foliageCells.Clear();
        _foliageCellOrder.Clear();
        _foliageSelectionElapsed = 0;
        _foliageCollisionElapsed = 0;
        _foliageCellLimit = 0;
        _foliageCellsLimited = false;
    }

    private static double PrefabAltitudeWeight(double altitude, QuadSpherePrefabConfig config)
    {
        bool hasMin = config.MinAltitude != 0;
        bool hasMax = config.MaxAltitude != 0;
        if ((hasMin && altitude < config.MinAltitude) || (hasMax && altitude > config.MaxAltitude))
            return 0;
        if (config.AltitudeFalloff <= 0 || (!hasMin && !hasMax))
            return 1;
        double distance = double.MaxValue;
        if (hasMin)
            distance = Math.Min(distance, altitude - config.MinAltitude);
        if (hasMax)
            distance = Math.Min(distance, config.MaxAltitude - altitude);
        return Math.Clamp(distance / config.AltitudeFalloff, 0.0, 1.0);
    }

    private static double PrefabBiomeWeight(Double3 direction, QuadSpherePrefabBiomes biomes, TerrainSnapshot settings)
    {
        double height = BaseElevation(direction, settings);
        double beach = GetBeachWeight(height, settings);
        var weights = GetBiomeWeights(direction, settings);
        var overridden = ApplyBrushBiomes(direction, FlattenBeach(height, beach, settings), ApplyBeachWeight(weights, beach), settings, out bool applied);
        double result = 0;
        if (applied)
        {
            int total = overridden.R + overridden.G + overridden.B + overridden.A;
            for (int i = 0; i < 4; i++)
                if (((int)biomes & (1 << i)) != 0)
                    result += overridden[i] / 255.0;
            if ((biomes & QuadSpherePrefabBiomes.Beach) != 0)
                result += (255 - total) / 255.0;
        }
        else
        {
            for (int i = 0; i < 4; i++)
                if (((int)biomes & (1 << i)) != 0)
                    result += weights[i] / 255.0 * (1.0 - beach);
            if ((biomes & QuadSpherePrefabBiomes.Beach) != 0)
                result += beach;
        }
        return result;
    }

    private static float PlacementYaw(int seed, int root, int entry, int attempt) =>
        (float)(PrefabRandom(seed, root, entry, attempt, 5) * Math.PI * 2.0);

    private static double PrefabRandom(int seed, int root, int entry, int attempt, int channel)
    {
        uint hash = unchecked((uint)seed ^ (uint)root * 0x9e3779b9u ^ (uint)entry * 0x85ebca6bu ^ (uint)attempt * 0xc2b2ae35u ^ (uint)channel * 0x27d4eb2du);
        hash ^= hash >> 16; hash = unchecked(hash * 0x7feb352du); hash ^= hash >> 15;
        hash = unchecked(hash * 0x846ca68bu); hash ^= hash >> 16;
        return hash / 4294967296.0;
    }

    private void DestroyPrefabs(PrefabChunk chunk)
    {
        foreach (var candidate in chunk.Candidates)
        {
            var actor = candidate.Actor;
            if (actor != null)
                DestroySpawnedPrefab(actor);
        }
        chunk.Candidates.Clear();
    }

    private void ClearPrefabs()
    {
        foreach (var chunk in _prefabChunks.Values)
            DestroyPrefabs(chunk);
        _prefabChunks.Clear();
        _spawnedPrefabRoots.Clear();
    }

    private void DestroySpawnedPrefab(Actor actor)
    {
        _spawnedPrefabRoots.Remove(actor.ID);
        _prefabBrushesDirty = true;
        actor.IsActive = false;
        Destroy(actor);
        _prefabDiagnosticCounts.Destroyed++;
    }

    private void ClearPatches()
    {
        ClearPrefabs();
        ClearFoliage();
        _foliageConfigs = Array.Empty<QuadSphereFoliageConfig>();
        _prefabSnapshot = null;
        _foliageSnapshot = null;
        _prefabBrushesDirty = false;
        _prefabConfigs = Array.Empty<QuadSpherePrefabConfig>();
        _hasCompletedLod = false;
        CancelGeneration();
        _desired = null;
        _pendingKeys = null;
        _snapshot = null;
        _cullCenters = null;
        _cullVisible = null;
        foreach (var patch in _patches.Values)
            DestroyPatch(patch);
        foreach (var patch in _retired)
            DestroyPatch(patch);
        foreach (var patch in _previousBrushPatches.Values)
            DestroyPatch(patch);
        _patches.Clear();
        _retired.Clear();
        _previousBrushPatches.Clear();
        _previousBrushes = null;
    }

    private static void DestroyPatch(Patch patch)
    {
        try { patch.Cooking?.Wait(); }
        catch (AggregateException e) { Debug.LogException(e); }
        Destroy(patch.Renderer);
        if (patch.Collider != null)
            Destroy(patch.Collider);
        Destroy(patch.Model);
        if (patch.Collision != null)
            Destroy(patch.Collision);
    }
}
