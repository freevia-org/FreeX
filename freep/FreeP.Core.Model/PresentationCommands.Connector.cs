using Free.Shared.Drawing;

namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// CONNECTOR ATTACHMENT / ROUTING  (Wave 23)
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Sets the absolute bounds of a connector shape so that its endpoints line up with
/// the resolved connection-site points of its attached shapes.
///
/// This command is NOT issued directly by user actions; it is embedded inline inside
/// <see cref="MoveShapeCommand"/>, <see cref="ResizeShapeCommand"/>, and
/// <see cref="RotateShapeCommand"/>'s Apply/Revert so the entire operation (shape
/// move + connector follow) is a single undoable step.
/// </summary>
public sealed class UpdateConnectorBoundsCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _connectorId;
    private readonly long _newX;
    private readonly long _newY;
    private readonly long _newCx;
    private readonly long _newCy;

    // Captured on first Apply for Revert.
    private long _oldX;
    private long _oldY;
    private long _oldCx;
    private long _oldCy;

    // Internal read-only accessors used by the parent command's capture logic.
    internal uint ConnectorId => _connectorId;
    internal long NewX  => _newX;
    internal long NewY  => _newY;
    internal long NewCx => _newCx;
    internal long NewCy => _newCy;

    public UpdateConnectorBoundsCommand(
        int slideIndex, uint connectorId,
        long newX, long newY, long newCx, long newCy)
    {
        _slideIndex  = slideIndex;
        _connectorId = connectorId;
        _newX        = newX;
        _newY        = newY;
        _newCx       = newCx;
        _newCy       = newCy;
    }

    public string Label => "Reroute Connector";

    public void Apply(Presentation p)
    {
        var c = FindConnector(p);
        if (c is null) return;
        _oldX  = c.OffsetXEmu;
        _oldY  = c.OffsetYEmu;
        _oldCx = c.ExtentCxEmu;
        _oldCy = c.ExtentCyEmu;
        ApplyBounds(c, _newX, _newY, _newCx, _newCy);
    }

    public void Revert(Presentation p)
    {
        var c = FindConnector(p);
        if (c is null) return;
        ApplyBounds(c, _oldX, _oldY, _oldCx, _oldCy);
    }

    private SlideShape? FindConnector(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return null;
        return p.Slides[_slideIndex].Shapes.FirstOrDefault(s => s.Id == _connectorId);
    }

    private static void ApplyBounds(SlideShape c, long x, long y, long cx, long cy)
    {
        c.OffsetXEmu  = x;
        c.OffsetYEmu  = y;
        c.ExtentCxEmu = cx;
        c.ExtentCyEmu = cy;
    }
}

/// <summary>
/// Helpers for building connector-reroute commands.
/// Called from shape-mutation commands after the moved shape's new position is known.
/// </summary>
internal static class ConnectorRouter
{
    /// <summary>
    /// Finds all connectors on slide <paramref name="slideIndex"/> whose start or end is
    /// attached to <paramref name="movedShapeId"/>, resolves both endpoints from the
    /// slide's current shape positions, and returns one <see cref="UpdateConnectorBoundsCommand"/>
    /// per affected connector.
    ///
    /// Call this AFTER the moved shape's position has been updated in the model so
    /// <see cref="ConnectionSiteHelper.Resolve"/> sees the new coordinates.
    /// </summary>
    internal static IEnumerable<UpdateConnectorBoundsCommand> BuildRerouteCommands(
        Presentation p, int slideIndex, uint movedShapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count)
            yield break;

        var slide = p.Slides[slideIndex];

        foreach (var shape in slide.Shapes)
        {
            if (shape.Kind != SlideShapeKind.Connector) continue;
            if (shape.ConnectionStart is null && shape.ConnectionEnd is null) continue;

            bool startAttached = shape.ConnectionStart?.ShapeId == movedShapeId;
            bool endAttached   = shape.ConnectionEnd  ?.ShapeId == movedShapeId;
            if (!startAttached && !endAttached) continue;

            // Resolve both endpoints (whichever is attached uses the live slide shape).
            (long sx, long sy) = shape.ConnectionStart is not null
                ? ConnectionSiteHelper.Resolve(shape.ConnectionStart, slide)
                : (shape.OffsetXEmu, shape.OffsetYEmu);

            (long ex, long ey) = shape.ConnectionEnd is not null
                ? ConnectionSiteHelper.Resolve(shape.ConnectionEnd, slide)
                : (shape.OffsetXEmu + shape.ExtentCxEmu, shape.OffsetYEmu + shape.ExtentCyEmu);

            // Connector bounding box = axis-aligned rect covering both endpoints.
            long newX  = Math.Min(sx, ex);
            long newY  = Math.Min(sy, ey);
            long newCx = Math.Max(Math.Abs(ex - sx), 1L); // minimum 1 EMU to keep valid
            long newCy = Math.Max(Math.Abs(ey - sy), 1L);

            yield return new UpdateConnectorBoundsCommand(slideIndex, shape.Id, newX, newY, newCx, newCy);
        }
    }
}
