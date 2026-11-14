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
        [Test]
        public void BrushAffectsOnlySelectedQuadSpheres()
        {
            var brush = new QuadSphereBrush();
            var first = new QuadSphereActor();
            var second = new QuadSphereActor();
            var other = new QuadSphereActor();
            try
            {
                Assert.That(brush.TargetsActor(first), Is.False);
                brush.Targets = null;
                Assert.That(brush.TargetsActor(first), Is.False);

                brush.Targets = new[] { first, null, second, first };
                Assert.That(brush.TargetsActor(first), Is.True);
                Assert.That(brush.TargetsActor(second), Is.True);
                Assert.That(brush.TargetsActor(other), Is.False);
                Assert.That(brush.TargetsActor(null), Is.False);

                brush.Targets[0] = other;
                brush.Targets[3] = null;
                Assert.That(brush.TargetsActor(first), Is.False);
                Assert.That(brush.TargetsActor(other), Is.True);
            }
            finally
            {
                FlaxEngine.Object.Destroy(brush);
                FlaxEngine.Object.Destroy(first);
                FlaxEngine.Object.Destroy(second);
                FlaxEngine.Object.Destroy(other);
            }
        }

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
                    },
                },
            };
        }

        [Test]
        public void CarveAndFlattenUseBrushCenterHeight()
        {
            var settings = CreateSettings(QuadSphereBrushMode.Carve);
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(-10.0).Within(1e-6));
            Assert.That(Elevation(Double3.UnitX, settings), Is.EqualTo(0.0).Within(1e-6));

            settings.Brushes[0].Mode = QuadSphereBrushMode.Flatten;
            settings.Brushes[0].Position = new Vector3(0, 110, 0);
            settings.Brushes[0].FlattenHeight = BrushFlattenHeight(settings.Brushes[0].Position, settings.PlanetTransform, settings.Radius);
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(10.0).Within(1e-6));
        }

        [Test]
        public void FlattenHeightUsesPlanetLocalCoordinates()
        {
            var rotation = Quaternion.RotationAxis(Float3.UnitZ, Mathf.PiOverTwo);
            var transform = new Transform(new Vector3(1000, 200, -300), rotation, new Float3(2, 3, 4));
            var position = transform.LocalToWorld(new Vector3(0, 110, 0));
            Assert.That(BrushFlattenHeight(position, transform, 100), Is.EqualTo(10.0).Within(1e-4));
            Assert.That(BrushFlattenHeight(position, transform, 90), Is.EqualTo(20.0).Within(1e-4));
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
        public void SquareFootprintUsesBrushRotationAndSmoothFalloff()
        {
            var settings = CreateSettings(QuadSphereBrushMode.Carve);
            var brush = settings.Brushes[0];
            var direction = new Double3(0.15, Math.Sqrt(1.0 - 2 * 0.15 * 0.15), 0.15);
            Assert.That(Elevation(direction, settings), Is.EqualTo(0.0).Within(1e-6));

            brush.Shape = QuadSphereBrushShape.Square;
            QuadSphereBrush.GetSquareAxes(brush.Position, Vector3.Zero, Quaternion.Identity,
                out brush.AxisNormal, out brush.AxisX, out brush.AxisY);
            Assert.That(Elevation(direction, settings), Is.EqualTo(-5.0).Within(1e-4));
            brush.Falloff = 0;
            Assert.That(Elevation(direction, settings), Is.EqualTo(-10.0).Within(1e-4));

            var rotation = Quaternion.RotationAxis(Float3.UnitY, Mathf.PiOverFour);
            QuadSphereBrush.GetSquareAxes(brush.Position, Vector3.Zero, rotation,
                out brush.AxisNormal, out brush.AxisX, out brush.AxisY);
            Assert.That(Elevation(direction, settings), Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void SquareAxesRemainValidWhenBrushRightIsRadial()
        {
            QuadSphereBrush.GetSquareAxes(new Vector3(100, 0, 0), Vector3.Zero, Quaternion.Identity,
                out var normal, out var x, out var y);
            Assert.That(normal.Length, Is.EqualTo(1).Within(1e-6));
            Assert.That(x.Length, Is.EqualTo(1).Within(1e-6));
            Assert.That(y.Length, Is.EqualTo(1).Within(1e-6));
            Assert.That(Vector3.Dot(normal, x), Is.EqualTo(0).Within(1e-6));
        }

        [Test]
        public void BrushChangesIncludeShapeAndOrientation()
        {
            var first = CreateSettings(QuadSphereBrushMode.Carve).Brushes;
            var second = CreateSettings(QuadSphereBrushMode.Carve).Brushes;
            Assert.That(SameBrushes(first, second), Is.True);
            second[0].Shape = QuadSphereBrushShape.Square;
            Assert.That(SameBrushes(first, second), Is.False);
            first[0].Shape = QuadSphereBrushShape.Square;
            second[0].AxisX = Vector3.UnitX;
            Assert.That(SameBrushes(first, second), Is.False);
        }

        [Test]
        public void OverlappingBrushesUseUnmodifiedSurfaceAndSpecifiedOrder()
        {
            var settings = CreateSettings(QuadSphereBrushMode.Carve);
            settings.Brushes[0].Depth = 50;
            var flatten = new BrushData
            {
                Mode = QuadSphereBrushMode.Flatten,
                Position = new Vector3(0, 110, 0),
                Radius = 20,
                Falloff = 0.5f,
            };
            flatten.FlattenHeight = BrushFlattenHeight(flatten.Position, settings.PlanetTransform, settings.Radius);
            var carve = settings.Brushes[0];
            settings.Brushes = new[] { carve, flatten };
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(10.0).Within(1e-6));
            settings.Brushes = new[] { flatten, carve };
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(-40.0).Within(1e-6));
        }
    }
}
#endif
