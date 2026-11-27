using System;
using FlaxEngine;

namespace Game;

/// <summary>Operation performed by a quad-sphere terrain brush.</summary>
public enum QuadSphereBrushMode
{
    /// <summary>Lower the terrain by the brush depth.</summary>
    Carve,
    /// <summary>Blend the terrain toward the brush center's radial height.</summary>
    Flatten,
    /// <summary>Leave terrain height unchanged.</summary>
    None,
    /// <summary>Remove generated foliage without changing terrain height.</summary>
    RemoveFoliage,
}

/// <summary>Biome layer applied by a quad-sphere brush.</summary>
public enum QuadSphereBrushBiome
{
    /// <summary>Keep the existing biome weights.</summary>
    None,
    /// <summary>Override with material layer 0.</summary>
    Layer0,
    /// <summary>Override with material layer 1.</summary>
    Layer1,
    /// <summary>Override with material layer 2.</summary>
    Layer2,
    /// <summary>Override with material layer 3.</summary>
    Layer3,
    /// <summary>Override with the beach layer.</summary>
    Beach,
}

/// <summary>Shape of a quad-sphere terrain brush.</summary>
public enum QuadSphereBrushShape
{
    /// <summary>A spherical influence with a circular footprint.</summary>
    Circle,
    /// <summary>A cubic influence with a square footprint.</summary>
    Square,
}

/// <summary>A non-destructive brush affecting the unbrushed surface of selected quad spheres it intersects.</summary>
[ActorToolbox("Terrain", "Quad Sphere Brush")]
public class QuadSphereBrush : Actor
{
    /// <summary>Explicitly targeted quad spheres. A brush inside a spawned quad-sphere prefab also affects its spawning planet.</summary>
    [Tooltip("Explicitly targeted quad spheres. A brush inside a spawned quad-sphere prefab also affects its spawning planet, even when this list is empty.")]
    public QuadSphereActor[] Targets = Array.Empty<QuadSphereActor>();

    /// <summary>Brush operation.</summary>
    [Tooltip("Flatten blends toward the brush center's radial height. None leaves height unchanged. Remove Foliage excludes generated foliage using the unbrushed terrain footprint, without changing height or removing prefabs.")]
    public QuadSphereBrushMode Mode = QuadSphereBrushMode.Carve;

    /// <summary>Optional material and foliage biome override.</summary>
    [Tooltip("Override the material and foliage biome within the brush. The selected layer blends using the brush falloff; None leaves biomes unchanged. Overlapping overrides follow brush order.")]
    public QuadSphereBrushBiome BiomeOverride = QuadSphereBrushBiome.None;

    /// <summary>Shape of the brush influence volume.</summary>
    [Tooltip("Circle uses a spherical volume. Square uses a cubic volume aligned to the planet and rotated by the brush.")]
    public QuadSphereBrushShape Shape = QuadSphereBrushShape.Circle;

    /// <summary>World-space radius or cube half-size of the influence volume.</summary>
    [Tooltip("World-space radius for Circle or half-size for Square. Place the center near the unbrushed surface. Actor scale does not change this size. Brush changes regenerate intersecting terrain patches and foliage cells.")]
    public float Radius = 1000.0f;

    /// <summary>Fraction of the radius used for a smooth edge.</summary>
    [Limit(0, 1, 0.01f), Tooltip("Fraction of the brush radius used for a smooth edge. Zero gives a hard edge. Remove Foliage uses this to smoothly reduce foliage density.")]
    public float Falloff = 0.25f;

    /// <summary>Amount by which the carve operation lowers terrain, in planet-local units.</summary>
    [Tooltip("Amount to lower terrain in planet-local units. Used in Carve mode.")]
    public float Depth = 100.0f;

    /// <summary>Order in which overlapping brushes are applied; ties are ordered by actor ID.</summary>
    [Tooltip("Overlapping brushes are applied in ascending order, with actor ID breaking ties.")]
    public int Order = 0;

    internal bool TargetsActor(QuadSphereActor actor) => actor != null && Targets != null && Array.IndexOf(Targets, actor) >= 0;

    internal static void GetSquareAxes(Vector3 position, Vector3 planetCenter, Quaternion rotation,
        out Vector3 normal, out Vector3 axisX, out Vector3 axisY)
    {
        normal = position - planetCenter;
        normal = normal.LengthSquared > 0.000000000001 ? Vector3.Normalize(normal) : Vector3.UnitY;
        axisX = Vector3.Transform(Vector3.UnitX, rotation);
        axisX -= normal * Vector3.Dot(axisX, normal);
        if (axisX.LengthSquared < 0.000000000001)
        {
            axisX = Vector3.Transform(Vector3.UnitZ, rotation);
            axisX -= normal * Vector3.Dot(axisX, normal);
        }
        axisX = Vector3.Normalize(axisX);
        axisY = Vector3.Cross(normal, axisX);
    }

#if FLAX_EDITOR
    /// <inheritdoc />
    public override void OnDebugDrawSelected()
    {
        base.OnDebugDrawSelected();
        if (!float.IsFinite(Radius) || Radius <= 0)
            return;
        if (Shape == QuadSphereBrushShape.Circle)
        {
            DebugDraw.DrawWireSphere(new BoundingSphere(Position, Radius), Color.Yellow);
        }
        else if (Shape == QuadSphereBrushShape.Square && Targets != null)
        {
            foreach (var target in Targets)
            {
                if (target == null)
                    continue;
                GetSquareAxes(Position, target.Position, Orientation, out var normal, out var x, out var y);
                x *= Radius;
                y *= Radius;
                normal *= Radius;
                for (int side = -1; side <= 1; side += 2)
                {
                    var center = Position + normal * side;
                    DebugDraw.DrawLine(center - x - y, center + x - y, Color.Yellow);
                    DebugDraw.DrawLine(center + x - y, center + x + y, Color.Yellow);
                    DebugDraw.DrawLine(center + x + y, center - x + y, Color.Yellow);
                    DebugDraw.DrawLine(center - x + y, center - x - y, Color.Yellow);
                }
                DebugDraw.DrawLine(Position - x - y - normal, Position - x - y + normal, Color.Yellow);
                DebugDraw.DrawLine(Position + x - y - normal, Position + x - y + normal, Color.Yellow);
                DebugDraw.DrawLine(Position + x + y - normal, Position + x + y + normal, Color.Yellow);
                DebugDraw.DrawLine(Position - x + y - normal, Position - x + y + normal, Color.Yellow);
            }
        }
    }
#endif
}
