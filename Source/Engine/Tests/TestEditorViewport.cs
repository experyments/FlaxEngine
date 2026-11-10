// Copyright (c) Wojciech Figat. All rights reserved.

#if FLAX_TESTS
using FlaxEditor.Viewport;
using FlaxEngine;
using NUnit.Framework;

namespace FlaxEditor.Tests
{
    [TestFixture]
    public class TestEditorViewport
    {
        [Test]
        public void TestMouseAnchorSnapsToDevicePixels()
        {
            const float dpiScale = 1.25f;
            var logicalPosition = new Float2(984.7999f, 580.7999f);

            var snapped = EditorViewport.SnapMousePositionToDevicePixels(logicalPosition, dpiScale);
            var physicalPosition = snapped * dpiScale;

            Assert.AreEqual(1231.0f, physicalPosition.X, 0.0001f);
            Assert.AreEqual(726.0f, physicalPosition.Y, 0.0001f);
            Assert.AreEqual(snapped, EditorViewport.SnapMousePositionToDevicePixels(snapped, dpiScale));
        }

        [Test]
        public void TestDroppedModelGravityAlignment()
        {
            var center = new Vector3(100, 200, 300);
            Assert.IsTrue(ViewportDragHandlers.TryGetGravityUp(center + new Vector3(10, 0, 0), center, out var up));
            var orientation = ViewportDragHandlers.AlignUpToGravity(Quaternion.Identity, up);
            Assert.IsTrue(Float3.NearEqual(Float3.Right, Float3.Up * orientation, 0.0001f));
            Assert.IsTrue(Float3.NearEqual(Float3.Forward, Float3.Forward * orientation, 0.0001f));

            Assert.IsTrue(ViewportDragHandlers.TryGetGravityUp(center + new Vector3(0, -10, 0), center, out up));
            orientation = ViewportDragHandlers.AlignUpToGravity(Quaternion.Identity, up);
            Assert.IsTrue(Float3.NearEqual(Float3.Down, Float3.Up * orientation, 0.0001f));
        }

        [Test]
        public void TestDroppedModelGravityBoundsAndCenter()
        {
            var bounds = new BoundingBox(new Vector3(-2, -3, -4), new Vector3(5, 6, 7));
            Assert.AreEqual(3.0, ViewportDragHandlers.GetBoundsBottomOffset(bounds, Float3.Up), 0.0001);
            Assert.AreEqual(5.0, ViewportDragHandlers.GetBoundsBottomOffset(bounds, Float3.Left), 0.0001);
            Assert.IsFalse(ViewportDragHandlers.TryGetGravityUp(Vector3.Zero, Vector3.Zero, out _));
        }
    }
}
#endif
