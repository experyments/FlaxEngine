#if FLAX_TESTS
using System;
using FlaxEngine;
using NUnit.Framework;

namespace Game;

public partial class QuadSphereActor
{
    [TestFixture]
    public class BrushTests
    {
        private static TerrainSnapshot CreateSettings(QuadSphereBrushMode mode)
        {
            return new TerrainSnapshot
            {
                Radius = 100.0,
                PlanetTransform = Transform.Identity,
                Brushes = new[]
                {
                    new BrushData
                    {
                        Mode = mode,
                        Position = new Vector3(0, 100, 0),
                        Radius = 20,
                        Falloff = 0.5f,
                        Depth = 10,
                        FlattenHeight = 30,
                    },
                },
            };
        }

        [Test]
        public void CarveAndFlattenUseConstantPlanetHeight()
        {
            var settings = CreateSettings(QuadSphereBrushMode.Carve);
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(-10.0).Within(1e-6));
            Assert.That(Elevation(Double3.UnitX, settings), Is.EqualTo(0.0).Within(1e-6));

            settings.Brushes[0].Mode = QuadSphereBrushMode.Flatten;
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(30.0).Within(1e-6));
        }

        [Test]
        public void BrushFalloffIsSmoothAndUsesWorldSpace()
        {
            var settings = CreateSettings(QuadSphereBrushMode.Carve);
            // A point on the sphere 15 world units from the brush center is
            // halfway through the 10-unit falloff region.
            double cosine = 1.0 - 15.0 * 15.0 / (2.0 * 100.0 * 100.0);
            var direction = new Double3(Math.Sqrt(1.0 - cosine * cosine), cosine, 0);
            Assert.That(Elevation(direction, settings), Is.EqualTo(-5.0).Within(1e-4));

            settings.PlanetTransform = new Transform(new Vector3(1000, 0, 0), Quaternion.Identity, Float3.One);
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(0.0).Within(1e-6));
            settings.Brushes[0].Position = new Vector3(1000, 100, 0);
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(-10.0).Within(1e-6));

            settings.PlanetTransform = new Transform(Vector3.Zero, Quaternion.Identity, new Float3(2, 2, 2));
            settings.Brushes[0].Position = new Vector3(0, 200, 0);
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(-10.0).Within(1e-6));
        }

        [Test]
        public void OverlappingBrushesUseUnmodifiedSurfaceAndSpecifiedOrder()
        {
            var settings = CreateSettings(QuadSphereBrushMode.Carve);
            settings.Brushes[0].Depth = 50;
            var flatten = new BrushData
            {
                Mode = QuadSphereBrushMode.Flatten,
                Position = new Vector3(0, 100, 0),
                Radius = 20,
                Falloff = 0.5f,
                FlattenHeight = 30,
            };
            var carve = settings.Brushes[0];
            settings.Brushes = new[] { carve, flatten };
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(30.0).Within(1e-6));
            settings.Brushes = new[] { flatten, carve };
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(-20.0).Within(1e-6));
        }
    }
}
#endif
