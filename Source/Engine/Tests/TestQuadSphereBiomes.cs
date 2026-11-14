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
