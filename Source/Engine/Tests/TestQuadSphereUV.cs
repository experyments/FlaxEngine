#if FLAX_TESTS
using System;
using System.Threading;
using FlaxEngine;
using NUnit.Framework;

namespace Game;

public partial class QuadSphereActor
{
    [TestFixture]
    public class UVTests
    {
        private static TerrainSnapshot CreateUVSettings(float repeat) => new TerrainSnapshot
        {
            Radius = 60000000.0,
            Resolution = 16,
            ChunksPerPatch = 8,
            UvRebaseRepeat = repeat,
            Brushes = Array.Empty<BrushData>(),
            PlanetTransform = Transform.Identity,
        };

        private static void AssertSameWrappedPhase(float a, float b, float repeat)
        {
            double cycles = ((double)a - b) * repeat;
            Assert.That(cycles, Is.EqualTo(Math.Round(cycles)).Within(1e-5));
        }

        [Test]
        public void RebasePreservesPhaseAndScaleAtEveryLod()
        {
            var original = CreateUVSettings(0);
            var rebased = CreateUVSettings(128);
            for (int level = 0; level <= 10; level++)
            {
                int count = 1 << level;
                var key = (Face: 0, Level: level, X: 2 * count + count - 1, Y: 3 * count + count / 2);
                var oldPatch = BuildPatch(key, original, CancellationToken.None);
                var newPatch = BuildPatch(key, rebased, CancellationToken.None);
                for (int y = 0; y <= 16; y++)
                {
                    for (int x = 0; x <= 16; x++)
                    {
                        int index = y * 17 + x;
                        AssertSameWrappedPhase(oldPatch.UVs[index].X, newPatch.UVs[index].X, 128);
                        AssertSameWrappedPhase(oldPatch.UVs[index].Y, newPatch.UVs[index].Y, 128);
                    }
                }
                Assert.That(newPatch.UVs[16].X - newPatch.UVs[0].X, Is.EqualTo(16.0 / count).Within(1e-6));
                Assert.That(newPatch.UVs[16 * 17].Y - newPatch.UVs[0].Y, Is.EqualTo(16.0 / count).Within(1e-6));
                Assert.That(newPatch.UVs[0].X, Is.InRange(0.0f, 1.0f / 128));
                Assert.That(newPatch.UVs[0].Y, Is.InRange(0.0f, 1.0f / 128));
            }
        }

        [Test]
        public void ParentAndChildMatchAtSharedVerticesModuloRepeat()
        {
            var settings = CreateUVSettings(128);
            for (int level = 0; level < 10; level++)
            {
                int count = 1 << level;
                int x = 2 * count + count - 1;
                int y = 3 * count + count / 2;
                var parent = BuildPatch((0, level, x, y), settings, CancellationToken.None);
                var child = BuildPatch((0, level + 1, 2 * x + 1, 2 * y + 1), settings, CancellationToken.None);
                for (int cy = 0; cy <= 16; cy += 2)
                {
                    for (int cx = 0; cx <= 16; cx += 2)
                    {
                        var parentUv = parent.UVs[(8 + cy / 2) * 17 + 8 + cx / 2];
                        var childUv = child.UVs[cy * 17 + cx];
                        AssertSameWrappedPhase(parentUv.X, childUv.X, 128);
                        AssertSameWrappedPhase(parentUv.Y, childUv.Y, 128);
                    }
                }
            }

        [Test]
        public void NeighborEdgesAndSkirtsKeepWrappedPhase()
        {
            var settings = CreateUVSettings(128);
            settings.SkirtDepth = 100;
            for (int level = 0; level <= 10; level++)
            {
                int count = 1 << level;
                // Cross a base chunk boundary as well as an LOD patch boundary.
                var left = BuildPatch((0, level, 3 * count - 1, 2 * count), settings, CancellationToken.None);
                var right = BuildPatch((0, level, 3 * count, 2 * count), settings, CancellationToken.None);
                for (int y = 0; y <= 16; y++)
                {
                    var a = left.UVs[y * 17 + 16];
                    var b = right.UVs[y * 17];
                    AssertSameWrappedPhase(a.X, b.X, 128);
                    AssertSameWrappedPhase(a.Y, b.Y, 128);
                    // The second skirt edge follows the right edge top to bottom.
                    Assert.That(left.UVs[17 * 17 + 17 + y], Is.EqualTo(a));
                }
            }
        }
        }
    }
}
#endif
