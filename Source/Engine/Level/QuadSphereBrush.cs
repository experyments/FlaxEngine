using System;
using FlaxEngine;

namespace Game;

/// <summary>Operation performed by a quad-sphere terrain brush.</summary>
public enum QuadSphereBrushMode
{
    /// <summary>Lower the terrain by the brush depth.</summary>
    Carve,
    /// <summary>Blend the terrain toward a constant height above the planet radius.</summary>
    Flatten,
}

/// <summary>A spherical, non-destructive brush affecting the unbrushed surface of any quad sphere it intersects.</summary>
[ActorToolbox("Terrain", "Quad Sphere Brush")]
public class QuadSphereBrush : Actor
{
    /// <summary>Brush operation.</summary>
    public QuadSphereBrushMode Mode = QuadSphereBrushMode.Carve;

    /// <summary>World-space radius of the spherical influence volume.</summary>
    [Tooltip("World-space radius of the brush. Place its center near the noise/heightmap surface before brushes. Actor scale does not change this radius.")]
    public float Radius = 1000.0f;

    /// <summary>Fraction of the radius used for a smooth edge.</summary>
    [Limit(0, 1, 0.01f), Tooltip("Fraction of the brush radius used for a smooth edge. Zero gives a hard edge.")]
    public float Falloff = 0.25f;

    /// <summary>Amount by which the carve operation lowers terrain, in planet-local units.</summary>
    [Tooltip("Amount to lower terrain in planet-local units. Used in Carve mode.")]
    public float Depth = 100.0f;

    /// <summary>Target height above each planet's base radius, in planet-local units.</summary>
    [Tooltip("Target height above the planet's base radius in planet-local units. Used in Flatten mode.")]
    public float FlattenHeight = 0.0f;

    /// <summary>Order in which overlapping brushes are applied; ties are ordered by actor ID.</summary>
    [Tooltip("Overlapping brushes are applied in ascending order, with actor ID breaking ties.")]
    public int Order = 0;

#if FLAX_EDITOR
    /// <inheritdoc />
    public override void OnDebugDrawSelected()
    {
        base.OnDebugDrawSelected();
        if (float.IsFinite(Radius) && Radius > 0)
            DebugDraw.DrawWireSphere(new BoundingSphere(Position, Radius), Color.Yellow);
    }
#endif
}
