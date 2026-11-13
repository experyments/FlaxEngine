using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;
using FlaxEngine;

namespace Game;

/// <summary>A quad-sphere planet with distance-based terrain detail and an editor preview.</summary>
public partial class QuadSphereActor : Actor
{
    /// <summary>Actor used for LOD in play mode; defaults to the main camera when unset.</summary>
    [Tooltip("Actor used for LOD in play mode. If unset, the main camera is used. In editor view mode, LOD follows the scene viewport camera.")]
    public Actor LodTarget;
    /// <summary>Material applied to terrain patches.</summary>
    public MaterialBase Material;
    /// <summary>Base planet radius in local units.</summary>
    public double Radius = 100000.0;

    /// <summary>Whether to generate terrain collision in play mode.</summary>
    [Tooltip("Generate collision for terrain patches in play mode. Changing this rebuilds the terrain.")]
    public bool GenerateCollision = true;
    /// <summary>Whether terrain patches cast shadows.</summary>
    [Tooltip("Whether terrain patches cast shadows.")]
    public bool CastShadows = true;
    /// <summary>Number of mesh subdivisions per side of a terrain patch.</summary>
    public int PatchResolution = 16;
    /// <summary>Number of base chunks per side of each cube face.</summary>
    [Tooltip("Number of base chunks per side of each cube face (1-16). Rebuild terrain after changing. Each chunk has its own LOD tree and mesh resolution.")]
    public int ChunksPerPatch = 1;
    /// <summary>Maximum quadtree refinement level for terrain LOD.</summary>
    [Limit(0, 7), Tooltip("Maximum quadtree refinement level (0-7). More levels add mesh detail near the LOD target, not new noise features.")]
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

    /// <summary>Uncompressed equirectangular texture used for global terrain height.</summary>
    [Tooltip("Uncompressed equirectangular heightmap; the red channel is multiplied by Heightmap Height.")]
    public Texture Heightmap;
    /// <summary>Multiplier applied to the global heightmap's red channel.</summary>
    public float HeightmapHeight = 1000.0f;
    private readonly Dictionary<(int Face, int Level, int X, int Y), Patch> _patches = new();
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
    private HashSet<(int Face, int Level, int X, int Y)> _desired;
    private List<(int Face, int Level, int X, int Y)> _pendingKeys;
    private int _pendingIndex;
    private TerrainSnapshot _snapshot;
    private CancellationTokenSource _generationCancellation;
    private readonly List<Task<PatchGeometry>> _generation = new();

    private sealed class TerrainSnapshot
    {
        public double Radius;
        public float SkirtDepth, NoiseHeight, NoiseFrequency, HeightmapHeight;
        public int Resolution, ChunksPerPatch, NoiseOctaves, NoiseSeed;
        public HeightImage Heightmap;
        public BrushData[] Brushes;
        public Transform PlanetTransform;
    }

    private sealed class PatchGeometry
    {
        public (int Face, int Level, int X, int Y) Key;
        public Double3 Origin;
        public Float3[] Vertices, Normals, Tangents;
        public Float2[] UVs;
        public int[] Indices;
    }

    private sealed class Patch
    {
        public StaticModel Renderer;
        public MeshCollider Collider;
        public Model Model;
        public CollisionData Collision;
        public Task<bool> Cooking;
        public bool CollisionAssigned;
        public bool Committed;
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

    private sealed class BrushData
    {
        public Guid Id;
        public int Order;
        public QuadSphereBrushMode Mode;
        public Vector3 Position;
        public float Radius, Falloff, Depth, FlattenHeight;
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

        for (int level = 0; level <= 7; level++)
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
        ClearPatches();
        _brushes = Array.Empty<BrushData>();
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
        if (!float.IsFinite(SkirtDepth) || !float.IsFinite(NoiseHeight) || !float.IsFinite(NoiseFrequency) || !float.IsFinite(HeightmapHeight))
        {
            Debug.LogError("Quad sphere terrain settings must be finite.");
            return;
        }
        _radius = Radius;
        _resolution = Math.Clamp(PatchResolution, 2, 128);
        _chunksPerPatch = Math.Clamp(ChunksPerPatch, 1, 16);
        _skirtDepth = Math.Max(0, SkirtDepth);
        _noiseHeight = NoiseHeight;
        _noiseFrequency = Math.Max(0, NoiseFrequency);
        _noiseOctaves = Math.Clamp(NoiseOctaves, 0, 12);
        _noiseSeed = NoiseSeed;
        _heightmapHeight = HeightmapHeight;
        if (Heightmap != null && !TryReadImage(Heightmap, out _heightmap))
            return;

        _baseElevationBound = 2.0 * Math.Abs(_noiseHeight) + Math.Abs(_heightmapHeight) * MaxAbsRed(_heightmap);
        _brushTransform = Transform;
        _brushes = CollectBrushes(_brushTransform);
        _failedBrushes = null;
        _brushCheckElapsed = 0;
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
            if (brush == null || !float.IsFinite(brush.Radius) || brush.Radius <= 0 ||
                !float.IsFinite(brush.Falloff) || !float.IsFinite(brush.Depth) || !float.IsFinite(brush.FlattenHeight) ||
                (brush.Mode != QuadSphereBrushMode.Carve && brush.Mode != QuadSphereBrushMode.Flatten))
                continue;
            var position = brush.Position;
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
                continue;
            double distance = (position - planetTransform.Translation).Length;
            if (double.IsFinite(extent) && double.IsFinite(distance) && distance > extent + brush.Radius)
                continue;
            if (double.IsFinite(innerExtent) && double.IsFinite(distance) && distance + brush.Radius < innerExtent)
                continue;
            result.Add(new BrushData { Id = brush.ID, Order = brush.Order, Mode = brush.Mode, Position = position,
                Radius = brush.Radius, Falloff = Math.Clamp(brush.Falloff, 0.0f, 1.0f),
                Depth = Math.Max(0.0f, brush.Depth), FlattenHeight = brush.FlattenHeight });
        }
        result.Sort((a, b) =>
        {
            int order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : a.Id.CompareTo(b.Id);
        });
        return result.ToArray();
    }

    private static bool SameBrushes(BrushData[] a, BrushData[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Id != y.Id || x.Order != y.Order || x.Mode != y.Mode || !x.Position.Equals(y.Position) ||
                x.Radius != y.Radius || x.Falloff != y.Falloff || x.Depth != y.Depth || x.FlattenHeight != y.FlattenHeight)
                return false;
        }
        return true;
    }

    private void RebuildForBrushes(BrushData[] brushes, Transform planetTransform)
    {
        // Keep the committed terrain and collision until the replacement is ready.
        foreach (var pair in _patches)
            _previousBrushPatches.Add(pair.Key, pair.Value);
        _patches.Clear();
        _previousBrushes = _brushes;
        _previousBrushTransform = _brushTransform;
        _hasCompletedLod = false;
        _brushes = brushes;
        _brushTransform = planetTransform;
        _failedBrushes = null;
        _previousCullRadius = _cullRadius;
        InitializeCullBounds();
        RefreshLod();
    }

    private void RestoreBrushPatches()
    {
        // A failed replacement must not remove the last usable terrain/collision.
        foreach (var patch in _patches.Values)
            DestroyPatch(patch);
        _patches.Clear();
        foreach (var pair in _previousBrushPatches)
            _patches.Add(pair.Key, pair.Value);
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
        if (_initialRebuildPending)
        {
            RebuildTerrain();
            return;
        }
        if (IsActiveInHierarchy && GenerateCollision != _generateCollision)
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
            foreach (var patch in _patches.Values)
            {
                if (patch.Cooking != null && (patch.Cooking.IsFaulted || patch.Cooking.IsCanceled || patch.Cooking.Result))
                {
                    RestoreBrushPatches();
                    return;
                }
            }
        }
        if (_desired != null)
        {
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
        _brushCheckElapsed += Time.UnscaledDeltaTime;
        if (_hasCompletedLod && _brushCheckElapsed >= 0.25f)
        {
            _brushCheckElapsed = 0;
            var planetTransform = Transform;
            var brushes = CollectBrushes(planetTransform);
            bool failedAttempt = _failedBrushes != null && SameBrushes(_failedBrushes, brushes) &&
                _failedBrushTransform.Equals(planetTransform);
            if (!failedAttempt && (!SameBrushes(_brushes, brushes) ||
                ((brushes.Length != 0 || _brushes.Length != 0) && !_brushTransform.Equals(planetTransform))))
            {
                RebuildForBrushes(brushes, planetTransform);
                return;
            }
        }
        _elapsed += Engine.IsPlayMode ? Time.DeltaTime : Time.UnscaledDeltaTime;
        if (_elapsed >= Math.Max(0.05f, LodUpdateInterval))
        {
            _elapsed = 0;
            RefreshLod();
        }
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
            displacement = brush.Mode == QuadSphereBrushMode.Carve
                ? displacement + brush.Depth : Math.Max(displacement, Math.Abs(brush.FlattenHeight));
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

    private void RefreshLod()
    {
        if (!_ready || _desired != null)
            return;
        var desired = new HashSet<(int Face, int Level, int X, int Y)>();
        var snapshot = new TerrainSnapshot { Radius = _radius, Resolution = _resolution, ChunksPerPatch = _chunksPerPatch, SkirtDepth = _skirtDepth,
            NoiseHeight = _noiseHeight, NoiseFrequency = _noiseFrequency, NoiseOctaves = _noiseOctaves,
            NoiseSeed = _noiseSeed, HeightmapHeight = _heightmapHeight, Heightmap = _heightmap,
            Brushes = _brushes, PlanetTransform = _brushTransform };
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
        if (hasTarget && level < Math.Clamp(MaxLod, 0, 7))
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
        double uvDenominator = (double)patchesPerChunk * n;
        double step = 2.0 / divisions;
        double u0 = -1.0 + key.X * step;
        double v0 = -1.0 + key.Y * step;
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
                vertices.Add(Surface(direction, settings) - origin);
                normals.Add(normal);
                tangents.Add(SurfaceTangent(direction, normal, uAxis));
                // Every LOD shares its base chunk's UV square. Derive shared
                // coordinates from the same rational grid before rounding to float.
                uvs.Add(new Float2((float)(((key.X % patchesPerChunk) * n + x) / uvDenominator),
                    (float)(((key.Y % patchesPerChunk) * n + y) / uvDenominator)));
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
                if (i == 0) continue;
                int previous = edge switch { 0 => i - 1, 1 => (i - 1) * (n + 1) + n, 2 => n * (n + 1) + n - i + 1, _ => (n - i + 1) * (n + 1) };
                indices.Add(previous); indices.Add(start + i - 1); indices.Add(source);
                indices.Add(source); indices.Add(start + i - 1); indices.Add(start + i);
            }
        }

        return new PatchGeometry { Key = key, Origin = origin, Vertices = vertices.ToArray(),
            Normals = normals.ToArray(), Tangents = tangents.ToArray(), UVs = uvs.ToArray(), Indices = indices.ToArray() };
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
        if (accessor.AllocateBuffer(MeshBufferType.Index, indexArray.Length, PixelFormat.R32_UInt) ||
            accessor.AllocateBuffer(MeshBufferType.Vertex0, vertexArray.Length, positionLayout) ||
            accessor.AllocateBuffer(MeshBufferType.Vertex1, vertexArray.Length, attributeLayout))
        {
            Destroy(model);
            throw new InvalidOperationException("Failed to allocate planet patch mesh buffers.");
        }
        indexArray.AsSpan().CopyTo(MemoryMarshal.Cast<byte, int>(accessor.Index().Data));
        accessor.Position().Set(vertexArray);
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
            return new Patch { Renderer = renderer, Model = model, CollisionAssigned = true };

        var collider = new MeshCollider { HideFlags = HideFlags.FullyHidden };
        collider.SetParent(this, false, false);
        collider.LocalPosition = (Vector3)geometry.Origin;
        var collision = Content.CreateVirtualAsset<CollisionData>();
        if (collision == null)
            throw new InvalidOperationException("Failed to create planet collision data.");
        var cooking = Task.Run(() => collision.CookCollision(CollisionDataType.TriangleMesh, vertexArray, indexArray));
        return new Patch { Renderer = renderer, Collider = collider, Model = model, Collision = collision, Cooking = cooking };
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
        if (settings.Brushes.Length == 0)
            return height;

        // Use the unbrushed surface for every mask so overlapping brushes have
        // stable footprints even as earlier brushes change the resulting height.
        Vector3 localPosition = (Vector3)(direction * Math.Max(0.01, settings.Radius + height));
        Vector3 worldPosition = settings.PlanetTransform.LocalToWorld(localPosition);
        foreach (var brush in settings.Brushes)
        {
            double distance = (worldPosition - brush.Position).Length;
            if (distance >= brush.Radius)
                continue;
            double weight = brush.Falloff <= 0 ? 1.0 : Math.Clamp((brush.Radius - distance) / (brush.Radius * brush.Falloff), 0.0, 1.0);
            weight = weight * weight * (3.0 - 2.0 * weight);
            if (brush.Mode == QuadSphereBrushMode.Carve)
                height -= brush.Depth * weight;
            else
                height += (brush.FlattenHeight - height) * weight;
        }
        return height;
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

    private void ClearPatches()
    {
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
