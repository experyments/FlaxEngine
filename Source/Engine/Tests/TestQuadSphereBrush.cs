#if FLAX_TESTS
using System;
using System.Threading;
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

        [Test]
        public void SpawnedPrefabBrushTargetsOnlyItsOwnerAutomatically()
        {
            var owner = new QuadSphereActor();
            var other = new QuadSphereActor();
            var root = new QuadSphereBrush();
            var child = new QuadSphereBrush();
            var brush = new QuadSphereBrush();
            try
            {
                root.SetParent(owner, false, false);
                child.SetParent(root, false, false);
                brush.SetParent(child, false, false);
                Assert.That(owner.IsSpawnedPrefabBrush(brush), Is.False);
                owner._spawnedPrefabRoots.Add(root.ID);
                Assert.That(owner.IsSpawnedPrefabBrush(brush), Is.True);
                Assert.That(other.IsSpawnedPrefabBrush(brush), Is.False);
                Assert.That(brush.TargetsActor(other), Is.False);
                brush.Targets = new[] { other };
                Assert.That(brush.TargetsActor(other), Is.True);
                other.SetParent(child, false, false);
                brush.SetParent(other, false, false);
                Assert.That(owner.IsSpawnedPrefabBrush(brush), Is.False);
                brush.SetParent(child, false, false);
                other.SetParent(null, false, false);
                owner._spawnedPrefabRoots.Remove(root.ID);
                Assert.That(owner.IsSpawnedPrefabBrush(brush), Is.False);
            }
            finally
            {
                FlaxEngine.Object.Destroy(owner);
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
        public void BrushesApplyAfterBeachSmoothing()
        {
            var settings = CreateSettings(QuadSphereBrushMode.Flatten);
            settings.WaterHeight = 110;
            settings.BeachWidth = 20;
            settings.BeachSmoothing = 1;
            var brush = settings.Brushes[0];
            brush.Position = new Vector3(0, 105, 0);
            brush.Radius = 1;
            brush.FlattenHeight = 20;
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(20).Within(1e-6));
            settings.Brushes = Array.Empty<BrushData>();
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(5).Within(1e-6));
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
        public void BiomeOverrideBlendsAndChangesFoliageFiltersWithoutChangingHeight()
        {
            var settings = CreateSettings(QuadSphereBrushMode.None);
            var brush = settings.Brushes[0];
            brush.BiomeOverride = QuadSphereBrushBiome.Layer2;
            Assert.That(Elevation(Double3.UnitY, settings), Is.EqualTo(0));
            var center = ApplyBrushBiomes(Double3.UnitY, 0, new Color32(255, 0, 0, 0), settings, out bool applied);
            Assert.That(applied, Is.True);
            Assert.That(center, Is.EqualTo(new Color32(0, 0, 255, 0)));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Layer2, settings), Is.EqualTo(1));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Layer0, settings), Is.EqualTo(0));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Beach, settings), Is.EqualTo(0));

            double cosine = 1.0 - 15.0 * 15.0 / (2.0 * 100.0 * 100.0);
            var edge = new Double3(Math.Sqrt(1.0 - cosine * cosine), cosine, 0);
            var blended = ApplyBrushBiomes(edge, 0, new Color32(255, 0, 0, 0), settings, out applied);
            Assert.That(applied, Is.True);
            Assert.That(blended.R + blended.B, Is.EqualTo(255));
            Assert.That(blended.B, Is.InRange(127, 128));
            Assert.That(PrefabBiomeWeight(edge, QuadSpherePrefabBiomes.Layer2, settings), Is.EqualTo(blended.B / 255.0).Within(1e-8));
            Assert.That(PrefabBiomeWeight(edge, QuadSpherePrefabBiomes.Beach, settings), Is.EqualTo(0));
            var outside = ApplyBrushBiomes(Double3.UnitX, 0, new Color32(255, 0, 0, 0), settings, out applied);
            Assert.That(applied, Is.False);
            Assert.That(outside, Is.EqualTo(new Color32(255, 0, 0, 0)));
        }

        [Test]
        public void BiomeOverrideIsWrittenToTerrainMesh()
        {
            var settings = CreateSettings(QuadSphereBrushMode.None);
            settings.Resolution = 2;
            settings.ChunksPerPatch = 1;
            settings.BiomeLayerCount = 1;
            settings.Brushes[0].Position = Vector3.Zero;
            settings.Brushes[0].Radius = 200;
            settings.Brushes[0].Falloff = 0;
            settings.Brushes[0].BiomeOverride = QuadSphereBrushBiome.Layer3;
            var patch = BuildPatch((0, 0, 0, 0), settings, CancellationToken.None);
            foreach (var color in patch.BiomeWeights)
                Assert.That(color, Is.EqualTo(new Color32(0, 0, 0, 255)));
        }

        [Test]
        public void SquareBiomeOverrideUsesTheSameFootprintAsHeight()
        {
            var settings = CreateSettings(QuadSphereBrushMode.None);
            var brush = settings.Brushes[0];
            brush.Shape = QuadSphereBrushShape.Square;
            brush.BiomeOverride = QuadSphereBrushBiome.Layer1;
            QuadSphereBrush.GetSquareAxes(brush.Position, Vector3.Zero, Quaternion.Identity,
                out brush.AxisNormal, out brush.AxisX, out brush.AxisY);
            var direction = new Double3(0.15, Math.Sqrt(1.0 - 2 * 0.15 * 0.15), 0.15);
            var biome = ApplyBrushBiomes(direction, 0, new Color32(255, 0, 0, 0), settings, out bool applied);
            Assert.That(applied, Is.True);
            Assert.That(biome.G, Is.InRange(127, 128));
            brush.Shape = QuadSphereBrushShape.Circle;
            biome = ApplyBrushBiomes(direction, 0, new Color32(255, 0, 0, 0), settings, out applied);
            Assert.That(applied, Is.False);
            Assert.That(biome, Is.EqualTo(new Color32(255, 0, 0, 0)));
        }

        [Test]
        public void BiomeOverrideFollowsBrushOrderAndCanPaintBeach()
        {
            var settings = CreateSettings(QuadSphereBrushMode.None);
            var first = settings.Brushes[0];
            first.BiomeOverride = QuadSphereBrushBiome.Layer1;
            var second = new BrushData { Mode = QuadSphereBrushMode.None, Position = first.Position, Radius = 20, BiomeOverride = QuadSphereBrushBiome.Beach };
            settings.Brushes = new[] { first, second };
            Assert.That(ApplyBrushBiomes(Double3.UnitY, 0, new Color32(255, 0, 0, 0), settings, out _), Is.EqualTo(new Color32(0, 0, 0, 0)));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Beach, settings), Is.EqualTo(1));
            settings.Brushes = new[] { second, first };
            Assert.That(ApplyBrushBiomes(Double3.UnitY, 0, new Color32(255, 0, 0, 0), settings, out _), Is.EqualTo(new Color32(0, 255, 0, 0)));
            Assert.That(PrefabBiomeWeight(Double3.UnitY, QuadSpherePrefabBiomes.Layer1, settings), Is.EqualTo(1));
        }

        [Test]
        public void BiomeOverrideChangesAreDetected()
        {
            var first = CreateSettings(QuadSphereBrushMode.None).Brushes;
            var second = CreateSettings(QuadSphereBrushMode.None).Brushes;
            Assert.That(SameBrushes(first, second), Is.True);
            second[0].BiomeOverride = QuadSphereBrushBiome.Layer3;
            Assert.That(SameBrushes(first, second), Is.False);
            Assert.That(ChangedBrushInfluences(first, Transform.Identity, second, Transform.Identity).Count, Is.EqualTo(2));
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
        public void ChangedBrushInfluencesIncludeOldAndNewFootprints()
        {
            var id = Guid.NewGuid();
            var original = new BrushData { Id = id, Position = new Vector3(1000, 0, 0), Radius = 10, Depth = 10 };
            var moved = new BrushData { Id = id, Position = new Vector3(-1000, 0, 0), Radius = 10, Depth = 10 };
            var unchanged = new BrushData { Id = Guid.NewGuid(), Position = new Vector3(0, 1000, 0), Radius = 10 };
            var oldBrushes = new[] { original, unchanged };
            var newBrushes = new[] { moved, unchanged };
            var changes = ChangedBrushInfluences(oldBrushes, Transform.Identity, newBrushes, Transform.Identity);
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[0].Brush, Is.SameAs(original));
            Assert.That(changes[1].Brush, Is.SameAs(moved));
            Assert.That(ChangedBrushInfluences(oldBrushes, Transform.Identity, oldBrushes, Transform.Identity), Is.Empty);
            Assert.That(ChangedBrushInfluences(oldBrushes, Transform.Identity, new[] { unchanged }, Transform.Identity).Count, Is.EqualTo(1));
            var reordered = new BrushData { Id = id, Position = original.Position, Radius = 10, Depth = 10, Order = 1 };
            Assert.That(ChangedBrushInfluences(new[] { original }, Transform.Identity, new[] { reordered }, Transform.Identity).Count, Is.EqualTo(2));
            var translated = new Transform(new Vector3(1, 0, 0), Quaternion.Identity, Float3.One);
            Assert.That(ChangedBrushInfluences(oldBrushes, Transform.Identity, oldBrushes, translated).Count, Is.EqualTo(4));
        }

        [Test]
        public void BrushRegionsCoverSeamsAndIgnoreDistantPatches()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._radius = 1000;
                var transform = new Transform(new Vector3(10000, 2000, -3000), Quaternion.Identity, new Float3(2, 3, 4));
                var position = transform.LocalToWorld((Vector3)(Direction(0, 1, 0) * planet._radius));
                var brush = new BrushData { Position = position, Radius = 2, Shape = QuadSphereBrushShape.Square };
                var influences = ChangedBrushInfluences(Array.Empty<BrushData>(), transform, new[] { brush }, transform);
                Assert.That(planet.BrushInfluencesRegion(0, 7, 4, 8, influences), Is.True);
                Assert.That(planet.BrushInfluencesRegion(5, 0, 4, 8, influences), Is.True);
                Assert.That(planet.BrushInfluencesRegion(1, 4, 4, 8, influences), Is.False);
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [TestCase(QuadSphereBrushShape.Circle)]
        [TestCase(QuadSphereBrushShape.Square)]
        public void RegionBoundsCoverChangedSurfaceAndNormalSamples(QuadSphereBrushShape shape)
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._radius = 1000;
                var transform = new Transform(new Vector3(30000, -20000, 10000), Quaternion.Identity, new Float3(2, 3, 4));
                var position = transform.LocalToWorld((Vector3)(Direction(0, 1, 0.1) * 1000));
                var brush = new BrushData { Position = position, Radius = 120, Depth = 20, Shape = shape };
                if (shape == QuadSphereBrushShape.Square)
                    QuadSphereBrush.GetSquareAxes(position, transform.Translation, Quaternion.Identity, out brush.AxisNormal, out brush.AxisX, out brush.AxisY);
                var influences = ChangedBrushInfluences(Array.Empty<BrushData>(), transform, new[] { brush }, transform);
                var settings = new TerrainSnapshot { Radius = 1000, PlanetTransform = transform, Brushes = new[] { brush } };
                var baseline = new TerrainSnapshot { Radius = 1000, PlanetTransform = transform, Brushes = Array.Empty<BrushData>() };
                int affected = 0;
                for (int face = 0; face < 6; face++)
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            bool selected = planet.BrushInfluencesRegion(face, x, y, 8, influences);
                            if (selected)
                                affected++;
                            for (int sy = 0; sy <= 4; sy++)
                                for (int sx = 0; sx <= 4; sx++)
                                {
                                    var direction = Direction(face, -1.0 + 2.0 * (x + sx / 4.0) / 8, -1.0 + 2.0 * (y + sy / 4.0) / 8);
                                    if (Elevation(direction, settings) != Elevation(direction, baseline) ||
                                        (SurfaceNormal(direction, settings) - SurfaceNormal(direction, baseline)).Length > 1e-8)
                                        Assert.That(selected, Is.True, $"Missed face {face}, cell {x},{y}");
                                }
                        }
                Assert.That(affected, Is.GreaterThan(0).And.LessThan(6 * 8 * 8));
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void BrushChangesResetOnlyAffectedFoliageCells()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._radius = 1000;
                planet._chunksPerPatch = 8;
                planet._foliageDivisions = 8;
                var near = (Cell: (0 * 8 + 4) * 8 + 4, Entry: 0);
                var far = (Cell: (1 * 8 + 4) * 8 + 4, Entry: 0);
                var nearCell = new FoliageCell { Entry = 0, Complete = true };
                var farCell = new FoliageCell { Entry = 0, Complete = true };
                planet._foliageCells.Add(near, nearCell);
                planet._foliageCells.Add(far, farCell);
                planet.InvalidateFoliageForPatches(new[] { (Face: 0, Level: 0, X: 4, Y: 4) });
                Assert.That(planet._foliageCells[near], Is.Not.SameAs(nearCell));
                Assert.That(planet._foliageCells[near].Complete, Is.False);
                Assert.That(planet._foliageCells[far], Is.SameAs(farCell));
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
        }

        [Test]
        public void LodPatchChangesInvalidateOverlappingFoliageCells()
        {
            var planet = new QuadSphereActor();
            try
            {
                planet._chunksPerPatch = 2;
                planet._foliageDivisions = 8;
                var inside = (Cell: (0 * 8 + 1) * 8 + 1, Entry: 0);
                var outside = (Cell: (0 * 8 + 5) * 8 + 5, Entry: 0);
                var original = new FoliageCell { Complete = true };
                var unaffected = new FoliageCell { Complete = true };
                planet._foliageCells.Add(inside, original);
                planet._foliageCells.Add(outside, unaffected);
                planet.InvalidateFoliageForPatches(new[] { (Face: 0, Level: 1, X: 0, Y: 0) });
                Assert.That(planet._foliageCells[inside], Is.Not.SameAs(original));
                Assert.That(planet._foliageCells[outside], Is.SameAs(unaffected));
            }
            finally
            {
                FlaxEngine.Object.Destroy(planet);
            }
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
