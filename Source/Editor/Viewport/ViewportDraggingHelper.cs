// Copyright (c) Wojciech Figat. All rights reserved.

#if USE_LARGE_WORLDS
using Real = System.Double;
#else
using Real = System.Single;
#endif

using System;
using System.Linq;
using FlaxEditor.Content;
using FlaxEditor.Gizmo;
using FlaxEditor.GUI.Drag;
using FlaxEditor.SceneGraph;
using FlaxEditor.SceneGraph.Actors;
using FlaxEditor.Scripting;
using FlaxEngine;
using FlaxEngine.GUI;

namespace FlaxEditor.Viewport
{
    /// <summary>
    /// Utility to help managing dragging assets, actors and other objects into the editor viewport.
    /// </summary>
    public class ViewportDragHandlers : DragHandlers
    {
        /// <summary>
        /// The custom drag drop event arguments.
        /// </summary>
        /// <seealso cref="FlaxEditor.GUI.Drag.DragEventArgs" />
        public class DragDropEventArgs : DragEventArgs
        {
            /// <summary>
            /// The hit.
            /// </summary>
            public SceneGraphNode Hit;

            /// <summary>
            /// The hit location.
            /// </summary>
            public Vector3 HitLocation;
        }

        private readonly IGizmoOwner _owner;
        private readonly EditorViewport _viewport;
        private readonly DragAssets<DragDropEventArgs> _dragAssets;
        private readonly DragActorType<DragDropEventArgs> _dragActorType;
        private readonly DragScriptItems<DragDropEventArgs> _dragScriptItem;

        private ModelInstanceActor _previewModel;
        private int _previewModelEntryIndex;
        private BrushSurface _previewBrushSurface;

        internal ViewportDragHandlers(IGizmoOwner owner, EditorViewport viewport, Func<AssetItem, bool> validateAsset, Func<ScriptType, bool> validateDragActorType, Func<ScriptItem, bool> validateDragScriptItem)
        {
            _owner = owner;
            _viewport = viewport;
            Add(_dragAssets = new DragAssets<DragDropEventArgs>(validateAsset));
            Add(_dragActorType = new DragActorType<DragDropEventArgs>(validateDragActorType));
            Add(_dragScriptItem = new DragScriptItems<DragDropEventArgs>(validateDragScriptItem));
        }

        internal void ClearDragEffects()
        {
            _previewModel = null;
            _previewModelEntryIndex = -1;
            _previewBrushSurface = new BrushSurface();
        }

        internal void CollectDrawCalls(ViewportDebugDrawData debugDrawData, ref RenderContext renderContext)
        {
            if (_previewModel)
                debugDrawData.HighlightModel(_previewModel, _previewModelEntryIndex);
            if (_previewBrushSurface.Brush)
                debugDrawData.HighlightBrushSurface(_previewBrushSurface);
        }

        internal DragDropEffect DragEnter(ref Float2 location, DragData data)
        {
            var result = OnDragEnter(data);
            SetDragEffects(ref location);
            return result;
        }

        internal DragDropEffect DragMove(ref Float2 location, DragData data)
        {
            SetDragEffects(ref location);
            return Effect;
        }

        internal DragDropEffect DragDrop(ref Float2 location, DragData data)
        {
            Vector3 hitLocation = _viewport.ViewPosition, hitNormal = -_viewport.ViewDirection;
            SceneGraphNode hit = null;
            if (HasValidDrag)
            {
                GetHitLocation(ref location, out hit, out hitLocation, out hitNormal);
            }

            var result = DragDropEffect.None;
            if (_dragAssets.HasValidDrag)
            {
                result = _dragAssets.Effect;
                foreach (var asset in _dragAssets.Objects)
                    Spawn(asset, hit, ref location, ref hitLocation, ref hitNormal);
            }
            else if (_dragActorType.HasValidDrag)
            {
                result = _dragActorType.Effect;
                foreach (var actorType in _dragActorType.Objects)
                    Spawn(actorType, hit, ref location, ref hitLocation, ref hitNormal);
            }
            else if (_dragScriptItem.HasValidDrag)
            {
                result = _dragScriptItem.Effect;
                foreach (var scripItem in _dragScriptItem.Objects)
                    Spawn(scripItem, hit, ref location, ref hitLocation, ref hitNormal);
            }
            OnDragDrop(new DragDropEventArgs { Hit = hit, HitLocation = hitLocation });

            return result;
        }

        private void SetDragEffects(ref Float2 location)
        {
            if (_dragAssets.HasValidDrag && _dragAssets.Objects[0].IsOfType<MaterialBase>())
            {
                GetHitLocation(ref location, out var hit, out _, out _);
                var material = FlaxEngine.Content.LoadAsync<MaterialBase>(_dragAssets.Objects[0].ID);
                if (material && material.IsDecal)
                {
                    ClearDragEffects();
                    return;
                }

                // TODO: refactor this to be handled by ActorNode itself for generic actor highlights
                if (hit is StaticModelNode staticModelNode)
                {
                    _previewModel = (StaticModel)staticModelNode.Actor;
                    var ray = _viewport.ConvertMouseToRay(ref location);
                    _previewModel.IntersectsEntry(ref ray, out _, out _, out _previewModelEntryIndex);
                }
                else if (hit is AnimatedModelNode animatedModelNode)
                {
                    _previewModel = (AnimatedModel)animatedModelNode.Actor;
                    var ray = _viewport.ConvertMouseToRay(ref location);
                    _previewModel.IntersectsEntry(ref ray, out _, out _, out _previewModelEntryIndex);
                }
                else if (hit is BoxBrushNode.SideLinkNode brushSurfaceNode)
                {
                    _previewBrushSurface = brushSurfaceNode.Surface;
                }
                else
                {
                    ClearDragEffects();
                }
            }
            else
            {
                ClearDragEffects();
            }
        }

        private void GetHitLocation(ref Float2 location, out SceneGraphNode hit, out Vector3 hitLocation, out Vector3 hitNormal)
        {
            // Get mouse ray and try to hit any object
            var ray = _viewport.ConvertMouseToRay(ref location);
            var view = new Ray(_viewport.ViewPosition, _viewport.ViewDirection);
            var gridPlane = new Plane(Vector3.Zero, Vector3.Up);
            var flags = SceneGraphNode.RayCastData.FlagTypes.SkipColliders | SceneGraphNode.RayCastData.FlagTypes.SkipEditorPrimitives;
            hit = _owner.SceneGraphRoot.RayCast(ref ray, ref view, out var closest, out var normal, flags);
            var girdGizmo = _owner.Gizmos.Get<GridGizmo>();
            if (hit != null)
            {
                // Use hit location
                hitLocation = ray.Position + ray.Direction * closest;
                hitNormal = normal;
            }
            else if (girdGizmo != null && girdGizmo.Enabled && CollisionsHelper.RayIntersectsPlane(ref ray, ref gridPlane, out closest) && closest < 4000.0f)
            {
                // Use grid location
                hitLocation = ray.Position + ray.Direction * closest;
                hitNormal = Vector3.Up;
            }
            else
            {
                // Use area in front of the viewport
                hitLocation = view.GetPoint(100);
                hitNormal = Vector3.Up;
            }
        }

        private Vector3 PostProcessSpawnedActorLocation(Actor actor, ref Vector3 hitLocation)
        {
            // Refresh actor position to ensure that cached bounds are valid (in Actor::OnTransformChanged)
            actor.Position = Vector3.One;
            actor.Position = Vector3.Zero;

            // Place the object
            //var location = hitLocation - (box.Size.Length * 0.5f) * ViewDirection;
            var editorBounds = actor.EditorBoundingBoxWithChildren;
            var bottomToCenter = actor.Position.Y - editorBounds.Minimum.Y;
            var location = hitLocation + new Vector3(0, bottomToCenter, 0);
            var gravityCenter = Vector3.Zero;
            var gravityUp = Float3.Zero;
            var alignToGravity = actor is ModelInstanceActor &&
                                 _viewport is MainEditorGizmoViewport mainViewport &&
                                 mainViewport.TryGetGravityCenter(out gravityCenter) &&
                                 TryGetGravityUp(hitLocation, gravityCenter, out gravityUp);
            if (alignToGravity)
            {
                var actorUp = Float3.Up * actor.Orientation;
                bottomToCenter = GetBoundsBottomOffset(editorBounds, actorUp);
                location = hitLocation + new Vector3(gravityUp.X, gravityUp.Y, gravityUp.Z) * bottomToCenter;
            }

            // Apply grid snapping if enabled
            var transformGizmo = _owner.Gizmos.Get<TransformGizmo>();
            if (transformGizmo != null && (_owner.UseSnapping || transformGizmo.TranslationSnapEnable))
            {
                float snapValue = transformGizmo.TranslationSnapValue;
                location = new Vector3(
                                       (int)(location.X / snapValue) * snapValue,
                                       (int)(location.Y / snapValue) * snapValue,
                                       (int)(location.Z / snapValue) * snapValue);
            }

            if (alignToGravity && TryGetGravityUp(location, gravityCenter, out gravityUp))
                actor.Orientation = AlignUpToGravity(actor.Orientation, gravityUp);

            return location;
        }

        internal static bool TryGetGravityUp(Vector3 position, Vector3 center, out Float3 up)
        {
            var radial = position - center;
            if (radial.LengthSquared < 0.000001f)
            {
                up = Float3.Zero;
                return false;
            }
            radial.Normalize();
            up = (Float3)radial;
            return true;
        }

        internal static Quaternion AlignUpToGravity(Quaternion orientation, Float3 gravityUp)
        {
            var up = Float3.Up * orientation;
            var right = Float3.Right * orientation;
            var result = Quaternion.GetRotationFromTo(up, gravityUp, right) * orientation;
            result.Normalize();
            return result;
        }

        internal static Real GetBoundsBottomOffset(BoundingBox bounds, Float3 up)
        {
            var bottom = new Vector3(
                up.X >= 0 ? bounds.Minimum.X : bounds.Maximum.X,
                up.Y >= 0 ? bounds.Minimum.Y : bounds.Maximum.Y,
                up.Z >= 0 ? bounds.Minimum.Z : bounds.Maximum.Z);
            return -Vector3.Dot(bottom, new Vector3(up.X, up.Y, up.Z));
        }

        private void Spawn(Actor actor, ref Vector3 hitLocation, ref Vector3 hitNormal)
        {
            actor.Position = PostProcessSpawnedActorLocation(actor, ref hitLocation);
            _owner.Spawn(actor);
            _viewport.Focus();

            // Scroll to the new actor in the hierarchy
            var actorNode = Editor.Instance.Scene.GetActorNode(actor);
            if (actorNode?.TreeNode.ParentTree.Parent is Panel treePanel)
                FlaxEngine.Scripting.InvokeOnUpdate(() => treePanel.ScrollViewTo(actorNode.TreeNode));
        }

        private void Spawn(ScriptItem item, SceneGraphNode hit, ref Float2 location, ref Vector3 hitLocation, ref Vector3 hitNormal)
        {
            var actorType = Editor.Instance.CodeEditing.Actors.Get(item);
            if (actorType != ScriptType.Null)
            {
                Spawn(actorType, hit, ref location, ref hitLocation, ref hitNormal);
            }
        }

        private void Spawn(ScriptType item, SceneGraphNode hit, ref Float2 location, ref Vector3 hitLocation, ref Vector3 hitNormal)
        {
            var actor = item.CreateInstance() as Actor;
            if (actor == null)
            {
                Editor.LogWarning("Failed to spawn actor of type " + item.TypeName);
                return;
            }
            actor.Name = item.Name;
            Spawn(actor, ref hitLocation, ref hitNormal);
        }

        private void Spawn(AssetItem item, SceneGraphNode hit, ref Float2 location, ref Vector3 hitLocation, ref Vector3 hitNormal)
        {
            if (item.IsOfType<MaterialBase>())
            {
                var material = FlaxEngine.Content.LoadAsync<MaterialBase>(item.ID);
                if (material && !material.WaitForLoaded(500) && material.IsDecal)
                {
                    var actor = new Decal
                    {
                        Material = material,
                        LocalOrientation = RootNode.RaycastNormalRotation(ref hitNormal),
                        Name = item.ShortName
                    };
                    Spawn(actor, ref hitLocation, ref hitNormal);
                }
                // TODO: refactor this to be handled by ActorNode itself for generic actor highlights
                else if (hit is StaticModelNode staticModelNode)
                {
                    var actor = (StaticModel)staticModelNode.Actor;
                    var ray = _viewport.ConvertMouseToRay(ref location);
                    if (actor.IntersectsEntry(ref ray, out _, out _, out var entryIndex))
                    {
                        using (new UndoBlock(_owner.Undo, actor, "Change material"))
                            actor.SetMaterial(entryIndex, material);
                    }
                }
                else if (hit is AnimatedModelNode animatedModelNode)
                {
                    var actor = (AnimatedModel)animatedModelNode.Actor;
                    var ray = _viewport.ConvertMouseToRay(ref location);
                    if (actor.IntersectsEntry(ref ray, out _, out _, out var entryIndex))
                    {
                        using (new UndoBlock(_owner.Undo, actor, "Change material"))
                            actor.SetMaterial(entryIndex, material);
                    }
                }
                else if (hit is BoxBrushNode.SideLinkNode brushSurfaceNode)
                {
                    using (new UndoBlock(_owner.Undo, brushSurfaceNode.Brush, "Change material"))
                    {
                        var surface = brushSurfaceNode.Surface;
                        surface.Material = material;
                        brushSurfaceNode.Surface = surface;
                    }
                }
                return;
            }
            if (item.IsOfType<SceneAsset>())
            {
                Editor.Instance.Scene.OpenScene(item.ID, true);
                return;
            }
            {
                var actor = item.OnEditorDrop(this);
                actor.Name = item.ShortName;
                Spawn(actor, ref hitLocation, ref hitNormal);
            }
        }
    }
}
