using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

// ══════════════════════════════════════════════════════════════════════════════
//  SNAP ENGINE  — framework-free snap-to-grid / smart-guide helper (Wave 12B)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A thin value type that describes an axis-aligned guide line produced by
/// <see cref="SnapEngine"/> when a snap is active.
/// All coordinates are in slide DIP space (same units as SlideTransform).
/// </summary>
public readonly struct SnapGuideLine
{
    /// <summary>True = horizontal guide (spans the slide width); False = vertical.</summary>
    public bool IsHorizontal { get; init; }

    /// <summary>
    /// Position of the guide along its perpendicular axis.
    /// For horizontal guides: Y value (DIP). For vertical guides: X value (DIP).
    /// </summary>
    public double Position { get; init; }

    /// <summary>Human-readable label for the snap source (e.g. "grid", "shape edge").</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Result of a single <see cref="SnapEngine.Snap"/> call.
/// </summary>
public readonly struct SnapResult
{
    /// <summary>
    /// The snapped offset to apply to the moving rect (in DIP).
    /// (0, 0) means no snap was applied in that axis.
    /// </summary>
    public double SnapDx { get; init; }

    /// <summary>Y component of the snapped offset.</summary>
    public double SnapDy { get; init; }

    /// <summary>
    /// Active guide lines to render as transient alignment indicators.
    /// Empty when no snap occurred.
    /// </summary>
    public IReadOnlyList<SnapGuideLine> Guides { get; init; }

    /// <summary>Sentinel: no snap.</summary>
    public static readonly SnapResult None = new()
    {
        SnapDx = 0,
        SnapDy = 0,
        Guides = Array.Empty<SnapGuideLine>()
    };
}

/// <summary>
/// A candidate snap edge / center that <see cref="SnapEngine"/> considers during a drag.
/// </summary>
public readonly struct SnapCandidate
{
    /// <summary>True = horizontal snap target (Y position); False = vertical (X position).</summary>
    public bool IsHorizontal { get; init; }

    /// <summary>Position along the axis (DIP).</summary>
    public double Position { get; init; }

    /// <summary>Label shown in the guide line (e.g. "left edge", "center").</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Axis-independent snap calculation:
/// Given a moving rect, a set of candidate snap targets, a grid pitch, and a threshold,
/// returns the snapped offset and the set of active guide lines.
///
/// Design decisions:
/// <list type="bullet">
///   <item>Grid and shape-edge snapping are unified — all candidates go into the same pool.</item>
///   <item>For each axis the closest candidate within threshold wins; ties favour grid.</item>
///   <item>When <paramref name="snapEnabled"/> is false (Alt held) the engine returns <see cref="SnapResult.None"/>.</item>
///   <item>All coordinates in DIP so the caller uses <see cref="FreeP.App.Rendering.Wpf.SlideTransform"/> to convert.</item>
/// </list>
/// </summary>
public static class SnapEngine
{
    // Default PowerPoint grid pitch: one-twelfth inch, exactly 8 DIP.
    public const double DefaultGridPitchDip = 8.0;

    // Default snap threshold in DIP (~6 screen px at 1× zoom; ~0.5pt)
    public const double DefaultThresholdDip = 6.0;

    /// <summary>
    /// Computes the snap result for a move drag.
    /// </summary>
    /// <param name="movingRect">
    /// The proposed new position of the moving shape rect, expressed as
    /// (left, top, right, bottom) in slide DIP.
    /// </param>
    /// <param name="candidates">
    /// External snap candidates (shape edges / centers / slide edges) supplied by the caller.
    /// Grid candidates are added internally from <paramref name="gridPitchDip"/>.
    /// </param>
    /// <param name="slideWidthDip">Slide width in DIP (used for grid and slide-center candidates).</param>
    /// <param name="slideHeightDip">Slide height in DIP.</param>
    /// <param name="snapEnabled">When false (Alt held) returns <see cref="SnapResult.None"/>.</param>
    /// <param name="gridPitchDip">Grid pitch in DIP. Pass 0 to disable grid snap.</param>
    /// <param name="thresholdDip">Maximum DIP distance for a snap to trigger.</param>
    public static SnapResult Snap(
        (double left, double top, double right, double bottom) movingRect,
        IEnumerable<SnapCandidate>? candidates,
        double slideWidthDip,
        double slideHeightDip,
        bool   snapEnabled      = true,
        double gridPitchDip     = DefaultGridPitchDip,
        double thresholdDip     = DefaultThresholdDip)
    {
        if (!snapEnabled)
            return SnapResult.None;

        double cx = (movingRect.left + movingRect.right)  / 2.0;
        double cy = (movingRect.top  + movingRect.bottom) / 2.0;

        // Gather all horizontal (Y) and vertical (X) probe values from the moving rect.
        var probeX = new[] { movingRect.left, cx, movingRect.right };
        var probeY = new[] { movingRect.top,  cy, movingRect.bottom };

        // Build the full candidate pool.
        var poolH = new List<SnapCandidate>(); // horizontal (Y-axis)
        var poolV = new List<SnapCandidate>(); // vertical   (X-axis)

        // 1. External candidates.
        if (candidates is not null)
        {
            foreach (var c in candidates)
            {
                if (c.IsHorizontal) poolH.Add(c);
                else                poolV.Add(c);
            }
        }

        // 2. Slide edges + center.
        poolV.Add(new SnapCandidate { IsHorizontal = false, Position = 0,              Label = "slide left" });
        poolV.Add(new SnapCandidate { IsHorizontal = false, Position = slideWidthDip,  Label = "slide right" });
        poolV.Add(new SnapCandidate { IsHorizontal = false, Position = slideWidthDip / 2, Label = "slide center" });
        poolH.Add(new SnapCandidate { IsHorizontal = true,  Position = 0,              Label = "slide top" });
        poolH.Add(new SnapCandidate { IsHorizontal = true,  Position = slideHeightDip, Label = "slide bottom" });
        poolH.Add(new SnapCandidate { IsHorizontal = true,  Position = slideHeightDip / 2, Label = "slide center" });

        // 3. Grid lines.
        if (gridPitchDip > 0)
            AddGridCandidates(poolV, poolH, probeX, probeY, slideWidthDip, slideHeightDip, gridPitchDip, thresholdDip);

        // Find best snap on each axis.
        double bestDx = 0, bestDy = 0;
        SnapGuideLine? guideX = null, guideY = null;

        FindBestSnap(probeX, poolV, thresholdDip, isHorizontal: false, out bestDx, out guideX);
        FindBestSnap(probeY, poolH, thresholdDip, isHorizontal: true,  out bestDy, out guideY);

        if (bestDx == 0 && bestDy == 0)
            return SnapResult.None;

        var guides = new List<SnapGuideLine>();
        if (guideX.HasValue) guides.Add(guideX.Value);
        if (guideY.HasValue) guides.Add(guideY.Value);

        return new SnapResult { SnapDx = bestDx, SnapDy = bestDy, Guides = guides };
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void AddGridCandidates(
        List<SnapCandidate> poolV,
        List<SnapCandidate> poolH,
        double[] probeX, double[] probeY,
        double slideW, double slideH,
        double pitch, double threshold)
    {
        // For each probe value, find the nearest grid lines within threshold.
        foreach (double px in probeX)
        {
            double nearest = Math.Round(px / pitch) * pitch;
            if (Math.Abs(px - nearest) <= threshold)
                poolV.Add(new SnapCandidate { IsHorizontal = false, Position = nearest, Label = "grid" });
        }
        foreach (double py in probeY)
        {
            double nearest = Math.Round(py / pitch) * pitch;
            if (Math.Abs(py - nearest) <= threshold)
                poolH.Add(new SnapCandidate { IsHorizontal = true, Position = nearest, Label = "grid" });
        }
    }

    private static void FindBestSnap(
        double[] probes,
        List<SnapCandidate> pool,
        double threshold,
        bool isHorizontal,
        out double bestDelta,
        out SnapGuideLine? guide)
    {
        bestDelta = 0;
        guide     = null;
        double bestDist = double.MaxValue;

        foreach (var cand in pool)
        {
            foreach (double probe in probes)
            {
                double dist = Math.Abs(probe - cand.Position);
                if (dist <= threshold && dist < bestDist)
                {
                    bestDist  = dist;
                    bestDelta = cand.Position - probe;
                    guide = new SnapGuideLine
                    {
                        IsHorizontal = isHorizontal,
                        Position     = cand.Position,
                        Label        = cand.Label
                    };
                }
            }
        }
    }

    // ── Public helpers: build shape-edge candidates from a slide ────────────

    private const double EmuPerDip = DrawingMlCoordinateUnits.EmuPerPixel;
    private static double EmuToDip(long emu) => emu / EmuPerDip;

    /// <summary>
    /// Builds snap candidates from all shapes on the slide <em>except</em> the ones being
    /// dragged.  Includes left/right/top/bottom edges and center X/Y of each shape.
    /// </summary>
    public static List<SnapCandidate> BuildShapeCandidates(
        FreeP.Core.Model.Slide slide,
        IEnumerable<uint>  excludeIds)
    {
        var excluded = new HashSet<uint>(excludeIds);
        var result   = new List<SnapCandidate>();

        foreach (var shape in slide.Shapes)
        {
            if (excluded.Contains(shape.Id)) continue;

            double left  = EmuToDip(shape.OffsetXEmu);
            double top   = EmuToDip(shape.OffsetYEmu);
            double right = EmuToDip(shape.OffsetXEmu + shape.ExtentCxEmu);
            double bot   = EmuToDip(shape.OffsetYEmu + shape.ExtentCyEmu);
            double cx    = (left + right) / 2;
            double cy    = (top  + bot)   / 2;

            result.Add(new SnapCandidate { IsHorizontal = false, Position = left,  Label = "left edge"   });
            result.Add(new SnapCandidate { IsHorizontal = false, Position = right, Label = "right edge"  });
            result.Add(new SnapCandidate { IsHorizontal = false, Position = cx,    Label = "center"      });
            result.Add(new SnapCandidate { IsHorizontal = true,  Position = top,   Label = "top edge"    });
            result.Add(new SnapCandidate { IsHorizontal = true,  Position = bot,   Label = "bottom edge" });
            result.Add(new SnapCandidate { IsHorizontal = true,  Position = cy,    Label = "center"      });
        }

        return result;
    }
}
