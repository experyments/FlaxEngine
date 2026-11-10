// Copyright (c) Wojciech Figat. All rights reserved.

using FlaxEditor.Gizmo;
using FlaxEngine;
using FlaxEngine.GUI;

namespace FlaxEditor.Viewport.Cameras
{
    /// <summary>
    /// Free-flight camera for the main scene viewport.
    /// </summary>
    [HideInEditor]
    public class SixDofCamera : FPSCamera
    {
        internal bool IsRolling { get; private set; }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            IsRolling = false;
            base.Update(deltaTime);
        }

        /// <inheritdoc />
        public override void UpdateView(float dt, ref Vector3 moveDelta, ref Float2 mouseDelta, out bool centerMouse)
        {
            var viewport = (MainEditorGizmoViewport)Viewport;
            if (!viewport.UseSixDofCamera)
            {
                base.UpdateView(dt, ref moveDelta, ref mouseDelta, out centerMouse);
                return;
            }

            centerMouse = true;
            if (IsAnimatingMove)
                return;

            Viewport.GetInput(out var input);
            Viewport.GetPrevInput(out var previousInput);
            var gizmo = viewport.Gizmos.Active as TransformGizmoBase;
            var usingGizmo = gizmo != null && gizmo.ActiveAxis != TransformGizmoBase.Axis.None;
            var position = viewport.ViewPosition;
            var rotation = viewport.ViewOrientation;
            var forward = Vector3.Forward * rotation;
            var up = Vector3.Up * rotation;
            var right = Vector3.Right * rotation;

            if (input.IsPanning || input.IsMoving || input.IsRotating)
                position += moveDelta * rotation;

            if (input.IsPanning)
            {
                var speed = viewport.RelativePanning ? Mathf.Abs((position - TargetPoint).Length) * 0.005f : viewport.PanningSpeed;
                var sign = viewport.InvertPanning ? 1 : -1;
                position += (right * mouseDelta.X + up * mouseDelta.Y) * (speed * sign);
            }

            if (input.IsMoving)
                position -= forward * mouseDelta.Y;

            if (input.IsRotating || input.IsMoving || (input.IsOrbiting && !usingGizmo && previousInput.IsOrbiting))
            {
                var pitch = input.IsMoving ? 0 : mouseDelta.Y;
                var localRotation = Quaternion.RotationYawPitchRoll(mouseDelta.X * Mathf.DegreesToRadians, pitch * Mathf.DegreesToRadians, 0);
                rotation *= localRotation;
            }

            if (input.IsRotating && viewport.Root is WindowRootControl root && root.Window != null)
            {
                var options = Editor.Instance.Options.Options.Input;
                var roll = (root.Window.GetKey(options.SixDofCameraRollLeft.Key) ? 1 : 0) - (root.Window.GetKey(options.SixDofCameraRollRight.Key) ? 1 : 0);
                IsRolling = roll != 0;
                if (roll != 0)
                    rotation *= Quaternion.RotationAxis(Float3.Forward, roll * dt * Mathf.PiOverTwo);
            }
            rotation.Normalize();

            if (input.IsZooming && !input.IsRotating)
            {
                position += forward * (viewport.MouseWheelZoomSpeedFactor * input.MouseWheelDelta * 25.0f);
                if (input.IsAltDown)
                    position += forward * (viewport.MouseSpeed * 40 * viewport.MousePositionDelta.ValuesSum);
            }

            if (input.IsRotating && (input.ZoomInDown || input.ZoomOutDown))
                AdditionalZoomFOV += input.ZoomInDown ? -0.8f : 0.8f;
            else if (!input.IsRotating)
                AdditionalZoomFOV = 0;

            if (input.IsOrbiting && usingGizmo)
            {
                centerMouse = false;
                viewport.ViewPosition += gizmo.LastDelta.Translation;
                return;
            }

            viewport.ViewOrientation = rotation;
            if (input.IsOrbiting)
            {
                var radius = Mathf.Max((float)Vector3.Distance(ref position, ref TargetPoint), 0.0001f);
                viewport.ViewPosition = TargetPoint - viewport.ViewDirection * radius;
            }
            else
            {
                TargetPoint += position - viewport.ViewPosition;
                viewport.ViewPosition = position;
            }
        }

        internal static Quaternion AlignToGravity(Quaternion orientation, Vector3 position, Vector3 center)
        {
            var radial = position - center;
            if (radial.LengthSquared < 0.000001f)
                return orientation;
            radial.Normalize();
            Float3 gravityUp = (Float3)radial;
            var forward = Float3.Forward * orientation;
            var cameraUp = Float3.Up * orientation;
            var projectedUp = gravityUp - forward * Float3.Dot(gravityUp, forward);
            if (projectedUp.LengthSquared < 0.000001f)
                return orientation;
            projectedUp.Normalize();
            var correction = Quaternion.GetRotationFromTo(cameraUp, projectedUp, forward);
            var result = correction * orientation;
            result.Normalize();
            return result;
        }
    }
}
