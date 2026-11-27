#if FLAX_TESTS
using System;
using System.Threading;
using FlaxEngine;
using NUnit.Framework;

namespace Game;

public partial class QuadSphereActor
{
    [TestFixture]
    public class BiomeTests
    {
        private static TerrainSnapshot CreateSettings() => new TerrainSnapshot
        {
            Radius = 100000,
            Resolution = 8,
            ChunksPerPatch = 2,
            SkirtDepth = 100,
            Brushes = Array.Empty<BrushData>(),
            PlanetTransform = Transform.Identity,
            NoiseSeed = 12345,
            BiomeLayerCount = 4,
            BiomeLayers = new[] { new QuadSphereBiomeLayer(0), new QuadSphereBiomeLayer(104729),
                new QuadSphereBiomeLayer(209458), new QuadSphereBiomeLayer(314187) },
            BiomeBlendWidth = 0.5f,
        };

#if FLAX_EDITOR
        [Test]
        public void InspectorPrefabEntryMatchesConstructorDefaults()
        {
            // ArrayEditor creates a new entry using TypeUtils.GetDefaultValue, not new T().
            var config = (QuadSpherePrefabConfig)FlaxEngine.Utilities.TypeUtils.GetDefaultValue(
                new FlaxEditor.Scripting.ScriptType(typeof(QuadSpherePrefabConfig)));
            var expected = new QuadSpherePrefabConfig();
            Assert.That(config, Is.EqualTo(expected));
            Assert.That(ValidPrefabConfig(config), Is.True);
            Assert.That(config.Biomes, Is.EqualTo(QuadSpherePrefabBiomes.All));
            Assert.That(PrefabAltitudeWeight(100000, config), Is.EqualTo(1));
        }
#endif

        [Test]
        public void FoliageFaceRangesIncludeEveryNearbyCellAcrossSeams()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._radius = 1000;
                planet._foliageDivisions = 16;
                var random = new Random(12345);
                for (int sample = 0; sample < 100; sample++)
                {
                    var camera = new Double3(random.NextDouble() * 3000 - 1500, random.NextDouble() * 3000 - 1500, random.NextDouble() * 3000 - 1500);
                    double distance = random.NextDouble() * 2000;
                    for (int face = 0; face < 6; face++)
                    {
                        bool intersects = planet.FoliageFaceRange(face, camera, distance, out int minX, out int maxX, out int minY, out int maxY);
                        for (int y = 0; y < planet._foliageDivisions; y++)
                            for (int x = 0; x < planet._foliageDivisions; x++)
                            {
                                var center = (Double3)planet.FoliageCellCenter(face, x, y);
                                if ((center - camera).Length <= distance)
                                {
                                    Assert.That(intersects, Is.True);
                                    Assert.That(x, Is.InRange(minX, maxX));
                                    Assert.That(y, Is.InRange(minY, maxY));
                                }
                            }
                    }
                }
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

#if FLAX_EDITOR
        [Test]
        public void InspectorFoliageEntryMatchesConstructorDefaults()
        {
            var config = (QuadSphereFoliageConfig)FlaxEngine.Utilities.TypeUtils.GetDefaultValue(
                new FlaxEditor.Scripting.ScriptType(typeof(QuadSphereFoliageConfig)));
            Assert.That(config, Is.EqualTo(new QuadSphereFoliageConfig()));
            Assert.That(ValidPrefabConfig(FoliagePlacementSettings(config)), Is.True);
        }
#endif

        [Test]
        public void PrefabRandomIsStableAndDependsOnPlacementIdentity()
        {
            double value = PrefabRandom(12345, 7, 2, 42, 1);
            Assert.That(value, Is.InRange(0.0, 1.0));
            Assert.That(PrefabRandom(12345, 7, 2, 42, 1), Is.EqualTo(value));
            Assert.That(PrefabRandom(12346, 7, 2, 42, 1), Is.Not.EqualTo(value));
            Assert.That(PrefabRandom(12345, 8, 2, 42, 1), Is.Not.EqualTo(value));
            Assert.That(PrefabRandom(12345, 7, 3, 42, 1), Is.Not.EqualTo(value));
            Assert.That(PrefabRandom(12345, 7, 2, 43, 1), Is.Not.EqualTo(value));
            Assert.That(PrefabRandom(12345, 7, 2, 42, 2), Is.Not.EqualTo(value));
        }

        [Test]
        public void PrefabsUseBaseTerrainWhileFoliageUsesBrushedTerrain()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._prefabSnapshot = CreateSettings();
                var brushed = CreateSettings();
                brushed.Brushes = new[]
                {
                    new BrushData { Mode = QuadSphereBrushMode.Carve, Position = Vector3.Zero, Radius = 200000, Depth = 500 },
                };
                planet._foliageSnapshot = brushed;
                planet._noiseSeed = 12345;
                var config = FoliagePlacementSettings(new QuadSphereFoliageConfig());
                int accepted = 0;
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    bool prefab = planet.TryPlacement(config, 8, 0, 0, 0, 0, 0, attempt, false, out var baseSurface, out _);
                    bool foliage = planet.TryPlacement(config, 8, 0, 0, 0, 0, 0, attempt, false, out var brushedSurface, out _, QuadSphereFoliageNoiseType.Value, planet._foliageSnapshot);
                    Assert.That(foliage, Is.EqualTo(prefab));
                    if (prefab)
                    {
                        Assert.That(baseSurface.Length, Is.EqualTo(100000).Within(1e-5));
                        Assert.That(brushedSurface.Length, Is.EqualTo(99500).Within(1e-5));
                        accepted++;
                    }
                }
                Assert.That(accepted, Is.GreaterThan(0));
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [TestCase(0.4, 0.4)]
        [TestCase(0.46, 0.46)]
        public void FoliageSurfaceFollowsBrushedPatchTriangles(double u, double v)
        {
            var settings = CreateSettings();
            settings.Brushes = new[]
            {
                new BrushData { Mode = QuadSphereBrushMode.Carve, Position = (Vector3)(Direction(0, -0.6, -0.6) * settings.Radius), Radius = 50000, Falloff = 0.5f, Depth = 1000 },
            };
            var geometry = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            var patch = new Patch { Origin = geometry.Origin, Vertices = geometry.Vertices, Committed = true };
            var direction = Direction(0, -1.0 + u, -1.0 + v);
            Assert.That(TrySamplePatchSurface(patch, settings.Resolution, u, v, direction, out var surface), Is.True);

            double gridX = u * settings.Resolution;
            double gridY = v * settings.Resolution;
            int x = (int)gridX, y = (int)gridY;
            int a = y * (settings.Resolution + 1) + x;
            int b = a + 1, c = a + settings.Resolution + 1;
            if (gridX - x + gridY - y > 1.0)
                a = c + 1;
            var p0 = geometry.Origin + (Double3)geometry.Vertices[a];
            var p1 = geometry.Origin + (Double3)geometry.Vertices[b];
            var p2 = geometry.Origin + (Double3)geometry.Vertices[c];
            var planeNormal = Double3.Normalize(Double3.Cross(p1 - p0, p2 - p0));
            Assert.That(Math.Abs(Double3.Dot(surface - p0, planeNormal)), Is.LessThan(0.001));

            settings.Brushes = Array.Empty<BrushData>();
            var baseGeometry = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            var basePatch = new Patch { Origin = baseGeometry.Origin, Vertices = baseGeometry.Vertices };
            Assert.That(TrySamplePatchSurface(basePatch, settings.Resolution, u, v, direction, out var baseSurface), Is.True);
            Assert.That(baseSurface.Length - surface.Length, Is.GreaterThan(100));
        }

        [Test]
        public void FoliagePlacementUsesCommittedBrushMesh()
        {
            var planet = new QuadSphereActor();
            try
            {
                var settings = CreateSettings();
                settings.Brushes = new[] { new BrushData { Mode = QuadSphereBrushMode.Carve, Position = Vector3.Zero, Radius = 200000, Depth = 500 } };
                var geometry = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
                planet._patches.Add((0, 0, 0, 0), new Patch { Origin = geometry.Origin, Vertices = geometry.Vertices, Committed = true });
                planet._chunksPerPatch = settings.ChunksPerPatch;
                planet._resolution = settings.Resolution;
                planet._noiseSeed = 12345;
                var config = FoliagePlacementSettings(new QuadSphereFoliageConfig());
                int accepted = 0;
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    if (!planet.TryPlacement(config, 2, 0, 0, 0, 0, 0, attempt, false, out var surface, out _, QuadSphereFoliageNoiseType.Value, settings, true))
                        continue;
                    double u = -1.0 + PrefabRandom(12345, 0, 0, attempt, 1);
                    double v = -1.0 + PrefabRandom(12345, 0, 0, attempt, 2);
                    Assert.That(TrySamplePatchSurface(new Patch { Origin = geometry.Origin, Vertices = geometry.Vertices }, settings.Resolution, u + 1, v + 1, Direction(0, u, v), out var expected), Is.True);
                    Assert.That((surface - expected).Length, Is.LessThan(0.0001));
                    accepted++;
                }
                Assert.That(accepted, Is.GreaterThan(0));
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void FoliagePlacementUsesDeterministicTerrainAndBiomeFilters()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._prefabSnapshot = CreateSettings();
                planet._prefabSnapshot.SimpleSphere = true;
                planet._noiseSeed = 12345;
                var config = FoliagePlacementSettings(new QuadSphereFoliageConfig());
                int accepted = 0;
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    bool first = planet.TryPlacement(config, 8, 0, 0, 0, 0, 0, attempt, false, out var surface, out var normal);
                    bool again = planet.TryPlacement(config, 8, 0, 0, 0, 0, 0, attempt, false, out var reentered, out var repeatedNormal);
                    Assert.That(again, Is.EqualTo(first));
                    Assert.That(reentered, Is.EqualTo(surface));
                    Assert.That(repeatedNormal, Is.EqualTo(normal));
                    if (first)
                    {
                        Assert.That((normal - Double3.Normalize(surface)).Length, Is.LessThan(0.00001));
                        accepted++;
                    }
                }
                Assert.That(accepted, Is.GreaterThan(0));
                config.Biomes = QuadSpherePrefabBiomes.Layer1;
                for (int attempt = 0; attempt < 64; attempt++)
                    Assert.That(planet.TryPlacement(config, 8, 0, 0, 0, 0, 0, attempt, false, out _, out _), Is.False);
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [TestCase(QuadSphereFoliageNoiseType.Value)]
        [TestCase(QuadSphereFoliageNoiseType.Perlin)]
        [TestCase(QuadSphereFoliageNoiseType.RidgedPerlin)]
        public void FoliageNoiseModulatesDensityAndScaleDeterministically(QuadSphereFoliageNoiseType noiseType)
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._prefabSnapshot = CreateSettings();
                planet._prefabSnapshot.SimpleSphere = true;
                planet._noiseSeed = 12345;
                var config = new QuadSphereFoliageConfig { MinScale = 1, MaxScale = 3, NoiseType = noiseType, NoiseFrequency = 7, NoiseOctaves = 2, NoiseSeedOffset = 19 };
                var baseline = FoliagePlacementSettings(config);
                config.NoiseDensityStrength = 1;
                var noisy = FoliagePlacementSettings(config);
                int baselineCount = 0, noisyCount = 0;
                for (int attempt = 0; attempt < 512; attempt++)
                {
                    bool withoutNoise = planet.TryPlacement(baseline, 8, 0, 0, 0, 0, 0, attempt, false, out var surface, out _);
                    bool withNoise = planet.TryPlacement(noisy, 8, 0, 0, 0, 0, 0, attempt, false, out var noisySurface, out _, noiseType);
                    bool repeated = planet.TryPlacement(noisy, 8, 0, 0, 0, 0, 0, attempt, false, out _, out _, noiseType);
                    Assert.That(repeated, Is.EqualTo(withNoise));
                    if (withoutNoise)
                        baselineCount++;
                    if (withNoise)
                    {
                        noisyCount++;
                        Assert.That(withoutNoise, Is.True);
                        Assert.That(noisySurface, Is.EqualTo(surface));
                    }
                }
                Assert.That(noisyCount, Is.GreaterThan(0));
                Assert.That(noisyCount, Is.LessThan(baselineCount));

                var point = Direction(0, 0.2, -0.3) * 100000;
                float randomScale = planet.FoliageInstanceScale(config, point, 0, 0, 42);
                Assert.That(randomScale, Is.EqualTo(1 + 2 * (float)PrefabRandom(planet._noiseSeed, 0, 0, 42, 6)));
                config.NoiseScaleStrength = 1;
                float scale = planet.FoliageInstanceScale(config, point, 0, 0, 42);
                float expected = 1 + 2 * (float)planet.PlacementNoise(Double3.Normalize(point), config.NoiseFrequency, config.NoiseOctaves, config.NoiseSeedOffset, noiseType);
                Assert.That(scale, Is.EqualTo(expected).Within(0.000001f));
                Assert.That(scale, Is.InRange(1.0f, 3.0f));
                Assert.That(planet.FoliageInstanceScale(config, point, 0, 0, 43), Is.EqualTo(scale).Within(0.000001f));
                config.NoiseScaleStrength = 0.5f;
                Assert.That(planet.FoliageInstanceScale(config, point, 0, 0, 42), Is.EqualTo((randomScale + scale) * 0.5f).Within(0.000001f));
                config.NoiseScaleStrength = 1;
                config.NoiseSeedOffset++;
                Assert.That(planet.FoliageInstanceScale(config, point, 0, 0, 42), Is.Not.EqualTo(scale));
                config.NoiseScaleStrength = float.NaN;
                Assert.That(ValidFoliageNoise(config), Is.False);
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void FoliageNoiseTypesAreBoundedSeededAndProduceRidges()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._noiseSeed = 12345;
                var direction = Direction(0, 0.21, -0.37);
                double perlin = planet.PlacementNoise(direction, 7, 1, 0, QuadSphereFoliageNoiseType.Perlin);
                double ridge = planet.PlacementNoise(direction, 7, 1, 0, QuadSphereFoliageNoiseType.RidgedPerlin);
                Assert.That(ridge, Is.EqualTo(1 - Math.Abs(GradientNoise(direction * 7, 12345))).Within(0.000001));
                Assert.That(planet.PlacementNoise(Double3.UnitX, 1, 1, 0, QuadSphereFoliageNoiseType.Perlin), Is.EqualTo(0.5).Within(0.000001));
                Assert.That(planet.PlacementNoise(Double3.UnitX, 1, 1, 0, QuadSphereFoliageNoiseType.RidgedPerlin), Is.EqualTo(1).Within(0.000001));
                Assert.That(planet.PlacementNoise(direction, 7, 1, 1, QuadSphereFoliageNoiseType.Perlin), Is.Not.EqualTo(perlin));
                foreach (QuadSphereFoliageNoiseType type in Enum.GetValues(typeof(QuadSphereFoliageNoiseType)))
                {
                    double min = 1, max = 0;
                    for (int i = 0; i < 100; i++)
                    {
                        var sample = Direction(0, -0.9 + i * 0.018, 0.31);
                        double value = planet.PlacementNoise(sample, 9, 3, 5, type);
                        Assert.That(value, Is.InRange(0.0, 1.0));
                        Assert.That(planet.PlacementNoise(sample, 9, 3, 5, type), Is.EqualTo(value));
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                    Assert.That(max - min, Is.GreaterThan(0.01));
                }
                var invalid = new QuadSphereFoliageConfig { NoiseType = (QuadSphereFoliageNoiseType)99 };
                Assert.That(ValidFoliageNoise(invalid), Is.False);
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void FoliageOrientationUsesSelectedUpAxisAndPreservesYaw()
        {
            var surface = new Double3(0, 1000, 0);
            var normal = Double3.Normalize(new Double3(0.6, 0.8, 0));
            var radial = FoliageOrientation(surface, normal, false, 1.2f);
            var aligned = FoliageOrientation(surface, normal, true, 1.2f);
            var alignedWithoutYaw = FoliageOrientation(surface, normal, true, 0);

            Assert.That(Float3.NearEqual(Float3.Transform(Float3.UnitY, radial), Float3.UnitY, 0.0001f), Is.True);
            Assert.That(Float3.NearEqual(Float3.Transform(Float3.UnitY, aligned), (Float3)normal, 0.0001f), Is.True);
            Assert.That(Float3.NearEqual(Float3.Transform(Float3.UnitY, alignedWithoutYaw), (Float3)normal, 0.0001f), Is.True);
            Assert.That(Float3.NearEqual(Float3.Transform(Float3.UnitZ, aligned), Float3.Transform(Float3.UnitZ, alignedWithoutYaw), 0.0001f), Is.False);
        }

        [Test]
        public void FoliagePlacementReturnsSlopedTerrainNormalForAlignment()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._prefabSnapshot = CreateSettings();
                planet._prefabSnapshot.NoiseHeight = 10000;
                planet._prefabSnapshot.NoiseFrequency = 4;
                planet._prefabSnapshot.NoiseOctaves = 3;
                planet._noiseSeed = 12345;
                var config = FoliagePlacementSettings(new QuadSphereFoliageConfig());
                bool foundSlope = false;
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    if (!planet.TryPlacement(config, 8, 0, 0, 0, 0, 0, attempt, false, out var surface, out var normal))
                        continue;
                    var radial = Double3.Normalize(surface);
                    Assert.That((normal - SurfaceNormal(radial, planet._prefabSnapshot)).Length, Is.LessThan(0.00001));
                    if ((normal - radial).Length < 0.01)
                        continue;
                    var orientation = FoliageOrientation(surface, normal, true, 0.5f);
                    Assert.That(Float3.NearEqual(Float3.Transform(Float3.UnitY, orientation), (Float3)normal, 0.0001f), Is.True);
                    foundSlope = true;
                    break;
                }
                Assert.That(foundSlope, Is.True);
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void PartiallyInstalledFoliageOnlyCreatesCollidersForVisibleInstances()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._foliageConfigs = new[] { new QuadSphereFoliageConfig { SpawnDistance = 100, CollisionDistance = 100, MaxColliders = 2, CollisionSize = new Float3(2, 2, 2) } };
                var cell = new FoliageCell { Installed = 1, Renderer = new Foliage { IsActive = true } };
                cell.Placements.Add(new FoliagePlacement { Entry = 0, Transform = new Transform(new Vector3(1, 0, 0)) });
                cell.Placements.Add(new FoliagePlacement { Entry = 0, Transform = new Transform(new Vector3(2, 0, 0)) });
                planet._foliageCells.Add((7, 0), cell);
                planet.UpdateFoliageColliders(Vector3.Zero, Transform.Identity);
                Assert.That(planet._foliageActiveColliders.Count, Is.EqualTo(1));
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 0, 0)), Is.True);
                cell.Installed = 2;
                planet.UpdateFoliageColliders(Vector3.Zero, Transform.Identity);
                Assert.That(planet._foliageActiveColliders.Count, Is.EqualTo(2));
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 0, 1)), Is.True);
            }
            finally
            {
                planet.ClearFoliage();
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void FoliageCollisionPoolTracksClosestEligiblePlacements()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet.FoliageSpawnDistance = 100;
                planet.FoliageCollisionDistance = 20;
                planet.MaxFoliageColliders = 1;
                planet._foliageConfigs = new[] { new QuadSphereFoliageConfig { CollisionSize = new Float3(2, 4, 2), CollisionOffset = new Float3(0, 2, 0) } };
                var cell = new FoliageCell { Complete = true, Installed = 2, Renderer = new Foliage { IsActive = true } };
                cell.Placements.Add(new FoliagePlacement { Entry = 0, Transform = new Transform(new Vector3(1, 0, 0)) });
                cell.Placements.Add(new FoliagePlacement { Entry = 0, Transform = new Transform(new Vector3(9, 0, 0)) });
                planet._foliageCells.Add((7, 0), cell);

                planet.UpdateFoliageColliders(Vector3.Zero, Transform.Identity);
                Assert.That(planet._foliageActiveColliders.Count, Is.EqualTo(1));
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 0, 0)), Is.True);
                var collider = planet._foliageActiveColliders[(7, 0, 0)];
                Assert.That(collider.LocalPosition, Is.EqualTo(new Vector3(1, 2, 0)));

                planet.UpdateFoliageColliders(new Vector3(10, 0, 0), Transform.Identity);
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 0, 1)), Is.True);
                Assert.That(planet._foliageActiveColliders[(7, 0, 1)], Is.EqualTo(collider));
                planet.MaxFoliageColliders = 0;
                planet.UpdateFoliageColliders(Vector3.Zero, Transform.Identity);
                Assert.That(planet._foliageActiveColliders.Count, Is.Zero);
                Assert.That(planet._foliageColliderPool.Count, Is.EqualTo(1));
            }
            finally
            {
                planet.ClearFoliage();
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void FoliageDeserializationPreservesLegacyDefaultsAndExplicitZero()
        {
            var legacy = FlaxEngine.Json.JsonSerializer.Deserialize<QuadSphereFoliageConfig>("{\"Density\":2}");
            Assert.That(legacy.SpawnDistance, Is.EqualTo(-1));
            Assert.That(legacy.CollisionDistance, Is.EqualTo(-1));
            Assert.That(legacy.MaxColliders, Is.EqualTo(-1));
            Assert.That(legacy.Shadows, Is.EqualTo(QuadSphereFoliageShadows.Legacy));
            Assert.That(legacy.AlignToGroundNormal, Is.False);
            Assert.That(legacy.NoiseType, Is.EqualTo(QuadSphereFoliageNoiseType.Value));
            Assert.That(legacy.NoiseFrequency, Is.EqualTo(4));
            Assert.That(legacy.NoiseOctaves, Is.EqualTo(3));
            Assert.That(legacy.NoiseDensityStrength, Is.Zero);
            Assert.That(legacy.NoiseScaleStrength, Is.Zero);
            var aligned = FlaxEngine.Json.JsonSerializer.Deserialize<QuadSphereFoliageConfig>(
                FlaxEngine.Json.JsonSerializer.Serialize(new QuadSphereFoliageConfig { AlignToGroundNormal = true, NoiseType = QuadSphereFoliageNoiseType.RidgedPerlin, NoiseFrequency = 8, NoiseOctaves = 4, NoiseSeedOffset = 17, NoiseDensityStrength = 0.6f, NoiseScaleStrength = 0.8f }));
            Assert.That(aligned.AlignToGroundNormal, Is.True);
            Assert.That(aligned.NoiseType, Is.EqualTo(QuadSphereFoliageNoiseType.RidgedPerlin));
            Assert.That(aligned.NoiseFrequency, Is.EqualTo(8));
            Assert.That(aligned.NoiseOctaves, Is.EqualTo(4));
            Assert.That(aligned.NoiseSeedOffset, Is.EqualTo(17));
            Assert.That(aligned.NoiseDensityStrength, Is.EqualTo(0.6f));
            Assert.That(aligned.NoiseScaleStrength, Is.EqualTo(0.8f));
            var disabled = FlaxEngine.Json.JsonSerializer.Deserialize<QuadSphereFoliageConfig>(
                "{\"SpawnDistance\":0,\"CollisionDistance\":0,\"MaxColliders\":0}");
            Assert.That(disabled.SpawnDistance, Is.Zero);
            Assert.That(disabled.CollisionDistance, Is.Zero);
            Assert.That(disabled.MaxColliders, Is.Zero);
        }

        [Test]
        public void FoliageEntriesUseIndependentShadowSettings()
        {
            var planet = new QuadSphereActor();
            try
            {
                var legacy = FlaxEngine.Json.JsonSerializer.Deserialize<QuadSphereFoliageConfig>("{\"Density\":2}");
                var enabled = new QuadSphereFoliageConfig { Shadows = QuadSphereFoliageShadows.Enabled };
                var disabled = new QuadSphereFoliageConfig { Shadows = QuadSphereFoliageShadows.Disabled };
                enabled = FlaxEngine.Json.JsonSerializer.Deserialize<QuadSphereFoliageConfig>(FlaxEngine.Json.JsonSerializer.Serialize(enabled));
                disabled = FlaxEngine.Json.JsonSerializer.Deserialize<QuadSphereFoliageConfig>(FlaxEngine.Json.JsonSerializer.Serialize(disabled));

                planet.FoliageCastShadows = false;
                Assert.That(planet.FoliageEntryCastsShadows(legacy), Is.False);
                Assert.That(planet.FoliageEntryCastsShadows(enabled), Is.True);
                Assert.That(planet.FoliageEntryCastsShadows(disabled), Is.False);
                planet.FoliageCastShadows = true;
                Assert.That(planet.FoliageEntryCastsShadows(legacy), Is.True);
                Assert.That(planet.FoliageEntryCastsShadows(enabled), Is.True);
                Assert.That(planet.FoliageEntryCastsShadows(disabled), Is.False);
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void FoliageEntriesUseIndependentDistancesAndColliderLimits()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet.FoliageSpawnDistance = 150;
                planet.FoliageCollisionDistance = 30;
                planet.MaxFoliageColliders = 3;
                var legacy = new QuadSphereFoliageConfig();
                Assert.That(planet.FoliageEntrySpawnDistance(legacy), Is.EqualTo(150));
                Assert.That(planet.FoliageEntryCollisionDistance(legacy), Is.EqualTo(30));
                Assert.That(planet.FoliageEntryColliderLimit(legacy), Is.EqualTo(3));

                planet._foliageConfigs = new[]
                {
                    new QuadSphereFoliageConfig { SpawnDistance = 100, CollisionDistance = 50, MaxColliders = 1, CollisionSize = new Float3(2, 4, 2) },
                    new QuadSphereFoliageConfig { SpawnDistance = 5, CollisionDistance = 20, MaxColliders = 2, CollisionSize = new Float3(1, 1, 1) },
                };
                var trees = new FoliageCell { Complete = true, Installed = 2, Renderer = new Foliage { IsActive = true } };
                trees.Placements.Add(new FoliagePlacement { Entry = 0, Transform = new Transform(new Vector3(1, 0, 0)) });
                trees.Placements.Add(new FoliagePlacement { Entry = 0, Transform = new Transform(new Vector3(2, 0, 0)) });
                var grass = new FoliageCell { Complete = true, Installed = 2, Renderer = new Foliage { IsActive = true } };
                grass.Placements.Add(new FoliagePlacement { Entry = 1, Transform = new Transform(new Vector3(3, 0, 0)) });
                grass.Placements.Add(new FoliagePlacement { Entry = 1, Transform = new Transform(new Vector3(6, 0, 0)) });
                planet._foliageCells.Add((7, 0), trees);
                planet._foliageCells.Add((7, 1), grass);

                planet.UpdateFoliageColliders(Vector3.Zero, Transform.Identity);
                Assert.That(planet._foliageActiveColliders.Count, Is.EqualTo(2));
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 0, 0)), Is.True);
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 1, 0)), Is.True);
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 1, 1)), Is.False);

                planet._foliageConfigs[1].CollisionDistance = 0;
                planet.UpdateFoliageColliders(Vector3.Zero, Transform.Identity);
                Assert.That(planet._foliageActiveColliders.Count, Is.EqualTo(1));
                Assert.That(planet._foliageActiveColliders.ContainsKey((7, 0, 0)), Is.True);
            }
            finally
            {
                planet.ClearFoliage();
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void PrefabRangeUsesWorldSpaceAndIncludesBoundary()
        {
            var rotation = Quaternion.RotationAxis(Float3.UnitZ, Mathf.PiOverTwo);
            var transform = new Transform(new Vector3(60000000, -900000, 120000), rotation, new Float3(2, 3, 4));
            var position = transform.LocalToWorld(new Vector3(0, 100, 0));
            Assert.That(PrefabInRange(position, position + new Vector3(0, 3, 4), 5), Is.True);
            Assert.That(PrefabInRange(position, position + new Vector3(0, 3, 4), 4.99), Is.False);
            Assert.That(PrefabInRange(position, new Vector3(float.NaN, 0, 0), 5), Is.False);
        }

        [Test]
        public void PrefabCleanupChecksActorWorldPositionAndAllowsLaterReentry()
        {
            var planet = new QuadSphereActor();
            var instance = new QuadSphereActor();
            try
            {
                planet.PrefabSpawnDistance = 10;
                var surface = new Vector3(100, 0, 0);
                var chunk = new PrefabChunk();
                chunk.Candidates.Add(new PrefabCandidate { Surface = surface, Actor = instance, SpawnAttempted = true });
                planet._prefabChunks.Add(0, chunk);
                instance.Position = surface;
                planet.CleanupPrefabDistances(surface);
                Assert.That(chunk.Candidates[0].Actor, Is.EqualTo(instance));

                // The actor moved independently while its original placement is still in range.
                instance.Position = surface + new Vector3(20, 0, 0);
                planet.CleanupPrefabDistances(surface);
                instance = null; // Cleanup scheduled its destruction.
                Assert.That(chunk.Candidates[0].Actor, Is.Null);
                Assert.That(chunk.Candidates[0].SpawnAttempted, Is.True);
                planet.CleanupPrefabDistances(surface + new Vector3(100, 0, 0));
                Assert.That(chunk.Candidates[0].SpawnAttempted, Is.False);
                Assert.That(PrefabInRange(chunk.Candidates[0].Surface, surface, planet.PrefabSpawnDistance), Is.True);
            }
            finally
            {
                if (instance != null)
                    FlaxEngine.Object.Destroy(instance);
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void PrefabCandidatesRemainDeterministicAcrossMovementAndChunkReentry()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._prefabSnapshot = CreateSettings();
                planet._prefabSnapshot.SimpleSphere = true;
                planet._chunksPerPatch = 2;
                planet._noiseSeed = 12345;
                planet.PrefabSpawnDistance = 10;
                var config = new QuadSpherePrefabConfig();
                var first = new PrefabChunk();
                var reentered = new PrefabChunk();
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    planet.AddPrefabCandidate(config, first, 0, 0, 0, 0, attempt);
                    planet.AddPrefabCandidate(config, reentered, 0, 0, 0, 0, attempt);
                }
                Assert.That(first.Candidates.Count, Is.GreaterThan(1));
                Assert.That(reentered.Candidates.Count, Is.EqualTo(first.Candidates.Count));
                bool distinctYaw = false;
                for (int i = 0; i < first.Candidates.Count; i++)
                {
                    Assert.That(reentered.Candidates[i].Surface, Is.EqualTo(first.Candidates[i].Surface));
                    Assert.That(reentered.Candidates[i].Yaw, Is.EqualTo(first.Candidates[i].Yaw));
                    Assert.That(first.Candidates[i].Yaw, Is.InRange(0.0f, Mathf.Pi * 2.0f));
                    if (first.Candidates[i].Yaw != first.Candidates[0].Yaw)
                        distinctYaw = true;
                }
                Assert.That(distinctYaw, Is.True);

                var candidate = first.Candidates[0];
                var worldPosition = planet.Transform.LocalToWorld(candidate.Surface);
                var radial = Vector3.Normalize(worldPosition - planet.Transform.LocalToWorld(Vector3.Zero));
                var orientation = PlacementOrientation(radial, candidate.Yaw);
                var reenteredOrientation = PlacementOrientation(radial, reentered.Candidates[0].Yaw);
                Assert.That(orientation, Is.EqualTo(reenteredOrientation));
                Assert.That(Float3.NearEqual(Float3.Transform(Float3.UnitY, orientation), (Float3)radial, 0.0001f), Is.True);
                var farCamera = worldPosition + new Vector3(100, 0, 0);
                Assert.That(PrefabInRange(worldPosition, farCamera, planet.PrefabSpawnDistance), Is.False);
                Assert.That(PrefabInRange(worldPosition, worldPosition, planet.PrefabSpawnDistance), Is.True);

                candidate.SpawnAttempted = true;
                first.Candidates[0] = candidate;
                planet._prefabChunks.Add(0, first);
                planet.CleanupPrefabDistances(worldPosition);
                Assert.That(first.Candidates[0].SpawnAttempted, Is.True);
                planet.CleanupPrefabDistances(farCamera);
                Assert.That(first.Candidates[0].SpawnAttempted, Is.False);
                Assert.That(first.Candidates[0].Surface, Is.EqualTo(candidate.Surface));
                Assert.That(first.Candidates[0].Yaw, Is.EqualTo(candidate.Yaw));
                Assert.That(PrefabInRange(planet.Transform.LocalToWorld(first.Candidates[0].Surface), worldPosition, planet.PrefabSpawnDistance), Is.True);
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void PrefabAltitudeFalloffIsInsideRadialRange()
        {
            var config = new QuadSpherePrefabConfig { MinAltitude = 100, MaxAltitude = 200, AltitudeFalloff = 20 };
            Assert.That(PrefabAltitudeWeight(99, config), Is.EqualTo(0));
            Assert.That(PrefabAltitudeWeight(100, config), Is.EqualTo(0));
            Assert.That(PrefabAltitudeWeight(110, config), Is.EqualTo(0.5));
            Assert.That(PrefabAltitudeWeight(150, config), Is.EqualTo(1));
            Assert.That(PrefabAltitudeWeight(190, config), Is.EqualTo(0.5));
            Assert.That(PrefabAltitudeWeight(201, config), Is.EqualTo(0));
            config.AltitudeFalloff = 0;
            Assert.That(PrefabAltitudeWeight(100, config), Is.EqualTo(1));
        }

        [Test]
        public void ZeroAltitudeLimitsAreIgnoredIncludingFalloff()
        {
            var config = new QuadSpherePrefabConfig { MinAltitude = 0, MaxAltitude = 0, AltitudeFalloff = 20 };
            Assert.That(ValidPrefabConfig(config), Is.True);
            Assert.That(PrefabAltitudeWeight(0, config), Is.EqualTo(1));
            Assert.That(PrefabAltitudeWeight(100000, config), Is.EqualTo(1));

            config.MinAltitude = 100;
            Assert.That(ValidPrefabConfig(config), Is.True);
            Assert.That(PrefabAltitudeWeight(99, config), Is.EqualTo(0));
            Assert.That(PrefabAltitudeWeight(110, config), Is.EqualTo(0.5));
            Assert.That(PrefabAltitudeWeight(100000, config), Is.EqualTo(1));

            config.MinAltitude = 0;
            config.MaxAltitude = 200;
            Assert.That(PrefabAltitudeWeight(0, config), Is.EqualTo(1));
            Assert.That(PrefabAltitudeWeight(190, config), Is.EqualTo(0.5));
            Assert.That(PrefabAltitudeWeight(201, config), Is.EqualTo(0));

            config.MinAltitude = 201;
            Assert.That(ValidPrefabConfig(config), Is.False);
        }

        [Test]
        public void PrefabBiomesIncludeBeachAndSimpleSphereFallback()
        {
            var settings = CreateSettings();
            settings.SimpleSphere = true;
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Layer0, settings), Is.EqualTo(1));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Layer1 | QuadSpherePrefabBiomes.Beach, settings), Is.EqualTo(0));
            settings.SimpleSphere = false;
            settings.BiomeLayerCount = 1;
            settings.WaterHeight = settings.Radius;
            settings.BeachWidth = 100;
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Layer0, settings), Is.EqualTo(0));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Beach, settings), Is.EqualTo(1));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.All, settings), Is.EqualTo(1).Within(1e-8));
        }

        [Test]
        public void WeightsAreNormalizedDeterministicAndSeeded()
        {
            var settings = CreateSettings();
            int changed = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y <= 8; y++)
                {
                    for (int x = 0; x <= 8; x++)
                    {
                        var direction = Direction(face, x / 4.0 - 1.0, y / 4.0 - 1.0);
                        var weights = GetBiomeWeights(direction, settings);
                        Assert.That(weights.R + weights.G + weights.B + weights.A, Is.EqualTo(255));
                        Assert.That(GetBiomeWeights(direction, settings), Is.EqualTo(weights));
                        settings.BiomeLayers[2].SeedOffset++;
                        if (!GetBiomeWeights(direction, settings).Equals(weights))
                            changed++;
                        settings.BiomeLayers[2].SeedOffset--;
                    }
                }
            }
            Assert.That(changed, Is.GreaterThan(0));
            settings.BiomeLayerCount = 1;
            Assert.That(GetBiomeWeights(Double3.UnitY, settings), Is.EqualTo(new Color32(255, 0, 0, 0)));
            for (int count = 2; count <= 4; count++)
            {
                settings.BiomeLayerCount = count;
                var weights = GetBiomeWeights(Double3.UnitX, settings);
                Assert.That(weights.R + weights.G + weights.B + weights.A, Is.EqualTo(255));
                for (int layer = count; layer < 4; layer++)
                    Assert.That(weights[layer], Is.EqualTo(0));
            }
        }

        [Test]
        public void PerLayerNoiseSettingsAffectGenerationIndependently()
        {
            var settings = CreateSettings();
            var reference = new Color32[6 * 9 * 9];
            int index = 0;
            for (int face = 0; face < 6; face++)
                for (int y = 0; y <= 8; y++)
                    for (int x = 0; x <= 8; x++)
                        reference[index++] = GetBiomeWeights(Direction(face, x / 4.0 - 1.0, y / 4.0 - 1.0), settings);

            for (int layer = 0; layer < 4; layer++)
            {
                var previous = settings.BiomeLayers[layer];
                for (int setting = 0; setting < 4; setting++)
                {
                    settings.BiomeLayers[layer] = previous;
                    switch (setting)
                    {
                        case 0: settings.BiomeLayers[layer].NoiseFrequency += 1.25f; break;
                        case 1: settings.BiomeLayers[layer].NoiseOctaves = 1; break;
                        case 2: settings.BiomeLayers[layer].SeedOffset += 17; break;
                        case 3: settings.BiomeLayers[layer].NoiseContrast = 1.0f; break;
                    }
                    int changed = 0;
                    index = 0;
                    for (int face = 0; face < 6; face++)
                        for (int y = 0; y <= 8; y++)
                            for (int x = 0; x <= 8; x++)
                                if (!GetBiomeWeights(Direction(face, x / 4.0 - 1.0, y / 4.0 - 1.0), settings).Equals(reference[index++]))
                                    changed++;
                    Assert.That(changed, Is.GreaterThan(0), $"Layer {layer} setting {setting} must affect biome weights.");
                }
                settings.BiomeLayers[layer] = previous;
            }
        }

        [Test]
        public void SimpleSphereIgnoresTerrainAndBiomeData()
        {
            var settings = CreateSettings();
            settings.SimpleSphere = true;
            settings.NoiseHeight = float.NaN;
            settings.NoiseFrequency = float.NaN;
            settings.NoiseOctaves = 4;
            settings.Heightmap = new HeightImage { Width = 1, Height = 1, Pixels = new[] { Color.White } };
            settings.HeightmapHeight = 100;
            settings.Brushes = new[] { new BrushData { Mode = QuadSphereBrushMode.Carve, Position = new Vector3(0, 100000, 0), Radius = 100, Depth = 50 } };
            settings.BiomeLayers[0].NoiseFrequency = float.NaN;
            settings.WaterHeight = double.NaN;
            settings.BeachWidth = 100;
            settings.BeachSmoothing = 1;
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(0));
            Assert.That(SurfaceNormal(Double3.UnitY, settings), Is.EqualTo(Double3.UnitY));
            Assert.That(GetBiomeWeights(Double3.UnitY, settings), Is.EqualTo(new Color32(255, 0, 0, 0)));
            var patch = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            for (int i = 0; i < (settings.Resolution + 1) * (settings.Resolution + 1); i++)
            {
                var point = patch.Origin + (Double3)patch.Vertices[i];
                Assert.That(point.Length, Is.EqualTo(settings.Radius).Within(0.01));
                Assert.That(patch.BiomeWeights[i], Is.EqualTo(new Color32(255, 0, 0, 0)));
            }
        }

        [Test]
        public void BeachBandUsesRadialWaterHeightAndSmoothFlattening()
        {
            var settings = CreateSettings();
            settings.Radius = 6000000000.0;
            settings.WaterHeight = settings.Radius + 100;
            settings.BeachWidth = 200;
            settings.BeachSmoothing = 0.5f;
            Assert.That(GetBeachWeight(100, settings), Is.EqualTo(1).Within(1e-8));
            Assert.That(GetBeachWeight(0, settings), Is.EqualTo(0.5).Within(1e-8));
            Assert.That(GetBeachWeight(200, settings), Is.EqualTo(0.5).Within(1e-8));
            Assert.That(GetBeachWeight(-100, settings), Is.EqualTo(0));
            Assert.That(GetBeachWeight(300, settings), Is.EqualTo(0));
            Assert.That(FlattenBeach(0, GetBeachWeight(0, settings), settings), Is.EqualTo(25).Within(1e-8));
            Assert.That(FlattenBeach(200, GetBeachWeight(200, settings), settings), Is.EqualTo(175).Within(1e-8));
            settings.BeachSmoothing = 0;
            Assert.That(FlattenBeach(0, GetBeachWeight(0, settings), settings), Is.EqualTo(0));
            settings.BeachWidth = 0;
            Assert.That(GetBeachWeight(100, settings), Is.EqualTo(0));
        }

        [Test]
        public void BeachWeightReservesTheMissingRgbaWeight()
        {
            var biome = new Color32(40, 60, 75, 80);
            Assert.That(ApplyBeachWeight(biome, 0), Is.EqualTo(biome));
            Assert.That(ApplyBeachWeight(biome, 1), Is.EqualTo(new Color32(0, 0, 0, 0)));
            for (int i = 0; i <= 255; i++)
            {
                var weights = ApplyBeachWeight(biome, i / 255.0);
                Assert.That(weights.R + weights.G + weights.B + weights.A, Is.EqualTo(255 - i));
            }
        }

        [Test]
        public void BeachPatchUsesBaseHeightForColorAndFlattening()
        {
            var settings = CreateSettings();
            settings.WaterHeight = double.NaN;
            var original = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            Assert.That(Elevation(Direction(0, -1, -1), settings), Is.EqualTo(0));
            Assert.That(original.BiomeWeights[0], Is.EqualTo(GetBiomeWeights(Direction(0, -1, -1), settings)));
            settings.WaterHeight = settings.Radius + 50;
            settings.BeachWidth = 100;
            settings.BeachSmoothing = 1;
            var patch = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            var point = patch.Origin + (Double3)patch.Vertices[0];
            Assert.That(point.Length, Is.EqualTo(settings.Radius + 25).Within(0.01));
            var weights = patch.BiomeWeights[0];
            Assert.That(weights.R + weights.G + weights.B + weights.A, Is.EqualTo(127));
            settings.WaterHeight = settings.Radius;
            patch = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            Assert.That(patch.BiomeWeights[0], Is.EqualTo(new Color32(0, 0, 0, 0)));
        }

        [Test]
        public void BeachFollowsBrushedTerrain()
        {
            var settings = CreateSettings();
            settings.WaterHeight = settings.Radius;
            settings.BeachWidth = 100;
            settings.BeachSmoothing = 1;
            settings.Brushes = new[] { new BrushData { Mode = QuadSphereBrushMode.Carve,
                Position = new Vector3(0, settings.Radius, 0), Radius = 1000, Depth = 50 } };
            Assert.That(BaseElevation(Double3.UnitY, settings), Is.EqualTo(-50).Within(1e-6));
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(-25).Within(1e-6));
            var patch = BuildPatch((2, 0, 0, 0), settings, CancellationToken.None);
            var weights = patch.BiomeWeights[8 * 9 + 8];
            Assert.That(weights.R + weights.G + weights.B + weights.A, Is.EqualTo(127));
        }

        [Test]
        public void SharedVerticesAndSkirtsKeepTheSameWeights()
        {
            var settings = CreateSettings();
            settings.NoiseHeight = 200;
            settings.NoiseFrequency = 4;
            settings.NoiseOctaves = 2;
            settings.WaterHeight = settings.Radius + 10;
            settings.BeachWidth = 150;
            settings.BeachSmoothing = 0.5f;
            var left = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            var right = BuildPatch((0, 0, 1, 0), settings, CancellationToken.None);
            var adjacentFace = BuildPatch((4, 0, 1, 0), settings, CancellationToken.None);
            var child = BuildPatch((0, 1, 0, 0), settings, CancellationToken.None);
            const int n = 8, stride = n + 1;
            for (int y = 0; y <= n; y++)
            {
                Assert.That(left.BiomeWeights[y * stride + n], Is.EqualTo(right.BiomeWeights[y * stride]));
                Assert.That(left.BiomeWeights[y * stride], Is.EqualTo(adjacentFace.BiomeWeights[y * stride + n]));
                Assert.That(left.BiomeWeights[stride * stride + stride + y], Is.EqualTo(left.BiomeWeights[y * stride + n]));
                for (int x = 0; x <= n; x += 2)
                {
                    if (y % 2 == 0)
                        Assert.That(child.BiomeWeights[y * stride + x], Is.EqualTo(left.BiomeWeights[(y / 2) * stride + x / 2]));
                }
            }
        }

        [Test]
        public void AdjacentCubeFacesSampleTheSameBiome()
        {
            var settings = CreateSettings();
            for (int i = 0; i <= 16; i++)
            {
                double v = i / 8.0 - 1.0;
                Assert.That(GetBiomeWeights(Direction(0, -1, v), settings),
                    Is.EqualTo(GetBiomeWeights(Direction(4, 1, v), settings)));
            }
        }
    }
}
#endif
