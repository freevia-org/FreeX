namespace FreeW.Core.Model;

/// <summary>
/// The preset outlines themselves. Formulas follow the DrawingML presetShapeDefinitions (adjust
/// defaults, pins against the shorter side <c>ss</c>) where those are simple; a few organic shapes
/// (sun, moon, cloud) are approximations, and identity is preserved separately via
/// <see cref="Shape.PresetName"/>.
/// </summary>
public static partial class PresetShapeGeometry
{
    private static readonly Dictionary<string, Action<PathBuilder>> Builders = CreateBuilders();

    /// <summary>Every preset token this engine draws with a dedicated builder.</summary>
    public static IReadOnlyCollection<string> DedicatedPresets => Builders.Keys;

    private static Dictionary<string, Action<PathBuilder>> CreateBuilders()
    {
        var map = new Dictionary<string, Action<PathBuilder>>(StringComparer.Ordinal);

        // ── Lines and connectors ────────────────────────────────────────────────────────────────
        map["line"] = p => p.Stroke(() => { p.M(0, 0); p.L(p.W, p.H); });
        map["straightConnector1"] = map["line"];
        map["bentConnector2"] = p => p.Stroke(() => { p.M(0, 0); p.L(p.W, 0); p.L(p.W, p.H); });
        map["bentConnector3"] = p => p.Stroke(() =>
        {
            var x1 = p.W * p.Adj("adj1", 50000, -1000000, 1000000);
            p.M(0, 0); p.L(x1, 0); p.L(x1, p.H); p.L(p.W, p.H);
        });
        map["bentConnector4"] = p => p.Stroke(() =>
        {
            var x1 = p.W * p.Adj("adj1", 50000, -1000000, 1000000);
            var y2 = p.H * p.Adj("adj2", 50000, -1000000, 1000000);
            p.M(0, 0); p.L(x1, 0); p.L(x1, y2); p.L(p.W, y2); p.L(p.W, p.H);
        });
        map["bentConnector5"] = p => p.Stroke(() =>
        {
            var x1 = p.W * p.Adj("adj1", 50000, -1000000, 1000000);
            var y2 = p.H * p.Adj("adj2", 50000, -1000000, 1000000);
            var x3 = p.W * p.Adj("adj3", 50000, -1000000, 1000000);
            p.M(0, 0); p.L(x1, 0); p.L(x1, y2); p.L(x3, y2); p.L(x3, p.H); p.L(p.W, p.H);
        });
        map["curvedConnector2"] = p => p.Stroke(() => { p.M(0, 0); p.C(p.Hc, 0, p.W, p.Vc, p.W, p.H); });
        map["curvedConnector3"] = p => p.Stroke(() =>
        {
            var x2 = p.W * p.Adj("adj1", 50000, -1000000, 1000000);
            var x1 = x2 / 2;
            var x3 = (p.W + x2) / 2;
            p.M(0, 0);
            p.C(x1, 0, x2, p.H / 4, x2, p.Vc);
            p.C(x2, p.H * 3 / 4, x3, p.H, p.W, p.H);
        });
        map["curvedConnector4"] = p => p.Stroke(() =>
        {
            var x2 = p.W * p.Adj("adj1", 50000, -1000000, 1000000);
            var x1 = x2 / 2;
            var x3 = (p.W + x2) / 2;
            var x4 = (x2 + x3) / 2;
            var x5 = (x3 + p.W) / 2;
            var y4 = p.H * p.Adj("adj2", 50000, -1000000, 1000000);
            var y1 = y4 / 2;
            var y2 = y1 / 2;
            var y3 = (y1 + y4) / 2;
            var y5 = (p.H + y4) / 2;
            p.M(0, 0);
            p.C(x1, 0, x2, y2, x2, y1);
            p.C(x2, y3, x4, y4, x3, y4);
            p.C(x5, y4, p.W, y5, p.W, p.H);
        });
        map["curvedConnector5"] = map["curvedConnector3"];

        // ── Rectangles ──────────────────────────────────────────────────────────────────────────
        map["snip1Rect"] = p =>
        {
            var dx = p.Ss * p.Adj("adj", 16667, 0, 50000);
            p.Poly((0, 0), (p.W - dx, 0), (p.W, dx), (p.W, p.H), (0, p.H));
        };
        map["snip2SameRect"] = p =>
        {
            var tx = p.Ss * p.Adj("adj1", 16667, 0, 50000);
            var bx = p.Ss * p.Adj("adj2", 0, 0, 50000);
            p.Poly((tx, 0), (p.W - tx, 0), (p.W, tx), (p.W, p.H - bx), (p.W - bx, p.H), (bx, p.H), (0, p.H - bx), (0, tx));
        };
        map["snip2DiagRect"] = p =>
        {
            var a = p.Ss * p.Adj("adj1", 0, 0, 50000);
            var b = p.Ss * p.Adj("adj2", 16667, 0, 50000);
            p.Poly((a, 0), (p.W - b, 0), (p.W, b), (p.W, p.H - a), (p.W - a, p.H), (b, p.H), (0, p.H - b), (0, a));
        };
        map["snipRoundRect"] = p =>
        {
            var r = p.Ss * p.Adj("adj1", 16667, 0, 50000);
            var dx = p.Ss * p.Adj("adj2", 16667, 0, 50000);
            p.M(r, 0); p.L(p.W - dx, 0); p.L(p.W, dx); p.L(p.W, p.H); p.L(0, p.H); p.L(0, r);
            p.Arc(r, r, 180, 90); p.Z();
        };
        map["round1Rect"] = p =>
        {
            var r = p.Ss * p.Adj("adj", 16667, 0, 50000);
            p.M(0, 0); p.L(p.W - r, 0); p.Arc(r, r, 270, 90); p.L(p.W, p.H); p.L(0, p.H); p.Z();
        };
        map["round2SameRect"] = p => RoundedCorners(p,
            p.Ss * p.Adj("adj1", 16667, 0, 50000), p.Ss * p.Adj("adj1", 16667, 0, 50000),
            p.Ss * p.Adj("adj2", 0, 0, 50000), p.Ss * p.Adj("adj2", 0, 0, 50000));
        map["round2DiagRect"] = p => RoundedCorners(p,
            p.Ss * p.Adj("adj1", 16667, 0, 50000), p.Ss * p.Adj("adj2", 0, 0, 50000),
            p.Ss * p.Adj("adj1", 16667, 0, 50000), p.Ss * p.Adj("adj2", 0, 0, 50000));

        // ── Basic shapes ────────────────────────────────────────────────────────────────────────
        map["triangle"] = p => p.Poly((0, p.H), (p.W * p.Adj("adj", 50000), 0), (p.W, p.H));
        map["rtTriangle"] = p => p.Poly((0, p.H), (0, 0), (p.W, p.H));
        map["parallelogram"] = p =>
        {
            var x2 = p.Ss * p.Adj("adj", 25000, 0, 100000 * p.W / p.Ss);
            p.Poly((0, p.H), (x2, 0), (p.W, 0), (p.W - x2, p.H));
        };
        map["trapezoid"] = p =>
        {
            var x2 = p.Ss * p.Adj("adj", 25000, 0, 50000 * p.W / p.Ss);
            p.Poly((0, p.H), (x2, 0), (p.W - x2, 0), (p.W, p.H));
        };
        map["diamond"] = p => p.Poly((0, p.Vc), (p.Hc, 0), (p.W, p.Vc), (p.Hc, p.H));
        map["pentagon"] = p => RegularPolygon(p, 5, 1.05146, 1.10557, centreScaled: true);
        map["hexagon"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 25000, 0, 50000 * p.W / p.Ss);
            var dy = p.Vc * 1.15470 * Math.Sin(Math.PI / 3);
            p.Poly((0, p.Vc), (x1, p.Vc - dy), (p.W - x1, p.Vc - dy), (p.W, p.Vc), (p.W - x1, p.Vc + dy), (x1, p.Vc + dy));
        };
        map["heptagon"] = p => RegularPolygon(p, 7, 1.02572, 1.05210, centreScaled: true);
        map["octagon"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 29289, 0, 50000);
            p.Poly((0, x1), (x1, 0), (p.W - x1, 0), (p.W, x1), (p.W, p.H - x1), (p.W - x1, p.H), (x1, p.H), (0, p.H - x1));
        };
        map["decagon"] = p => RegularPolygon(p, 10, 1.0, 1.05146, centreScaled: false, startDeg: 180);
        map["dodecagon"] = p => p.GridPoly(21600,
            (0, 7906), (2894, 2894), (7906, 0), (13694, 0), (18706, 2894), (21600, 7906),
            (21600, 13694), (18706, 18706), (13694, 21600), (7906, 21600), (2894, 18706), (0, 13694));
        map["plus"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 25000, 0, 50000);
            p.Poly((0, x1), (x1, x1), (x1, 0), (p.W - x1, 0), (p.W - x1, x1), (p.W, x1), (p.W, p.H - x1),
                (p.W - x1, p.H - x1), (p.W - x1, p.H), (x1, p.H), (x1, p.H - x1), (0, p.H - x1));
        };
        map["donut"] = p =>
        {
            var dr = p.Ss * p.Adj("adj", 25000, 0, 50000);
            p.Ellipse(p.Hc, p.Vc, p.Hc, p.Vc);
            p.Ellipse(p.Hc, p.Vc, p.Hc - dr, p.Vc - dr);
        };
        map["arc"] = p => p.Stroke(() =>
        {
            var (start, sweep) = Angles(p, 16200000, 0);
            p.M(p.Hc + p.Hc * Cos(start), p.Vc + p.Vc * Sin(start));
            p.Arc(p.Hc, p.Vc, start, sweep);
        });
        map["chord"] = p =>
        {
            var (start, sweep) = Angles(p, 2700000, 16200000);
            p.M(p.Hc + p.Hc * Cos(start), p.Vc + p.Vc * Sin(start));
            p.Arc(p.Hc, p.Vc, start, sweep);
            p.Z();
        };
        map["pie"] = p =>
        {
            var (start, sweep) = Angles(p, 0, 16200000);
            p.M(p.Hc + p.Hc * Cos(start), p.Vc + p.Vc * Sin(start));
            p.Arc(p.Hc, p.Vc, start, sweep);
            p.L(p.Hc, p.Vc);
            p.Z();
        };
        map["blockArc"] = p =>
        {
            var (start, sweep) = Angles(p, 10800000, 0);
            var dr = p.Ss * p.Adj("adj3", 25000, 0, 50000);
            var (irx, iry) = (Math.Max(p.Hc - dr, 0.001), Math.Max(p.Vc - dr, 0.001));
            var end = start + sweep;
            p.M(p.Hc + p.Hc * Cos(start), p.Vc + p.Vc * Sin(start));
            p.Arc(p.Hc, p.Vc, start, sweep);
            p.L(p.Hc + irx * Cos(end), p.Vc + iry * Sin(end));
            p.Arc(irx, iry, end, -sweep);
            p.Z();
        };
        map["teardrop"] = p =>
        {
            var a = p.Adj("adj", 100000, 0, 200000);
            var (x1, y1) = (p.Hc + p.Hc * a, p.Vc - p.Vc * a);
            p.M(0, p.Vc);
            p.Arc(p.Hc, p.Vc, 180, 90);
            p.Q((p.Hc + x1) / 2, 0, x1, y1);
            p.Q(p.W, (p.Vc + y1) / 2, p.W, p.Vc);
            p.Arc(p.Hc, p.Vc, 0, 180);
            p.Z();
        };
        map["frame"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj1", 12500, 0, 50000);
            p.Rect(0, 0, p.W, p.H);
            p.Rect(x1, x1, p.W - x1, p.H - x1);
        };
        map["halfFrame"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj2", 33333, 0, 100000 * p.W / p.Ss);
            var y1 = p.Ss * p.Adj("adj1", 33333, 0, 100000 * p.H / p.Ss);
            var y2 = p.H - p.H * x1 / p.W;
            var x2 = p.W - y1 * p.W / p.H;
            p.Poly((0, 0), (p.W, 0), (x2, y1), (x1, y1), (x1, y2), (0, p.H));
        };
        map["corner"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj2", 50000, 0, 100000 * p.W / p.Ss);
            var y1 = p.H - p.Ss * p.Adj("adj1", 50000, 0, 100000 * p.H / p.Ss);
            p.Poly((0, 0), (x1, 0), (x1, y1), (p.W, y1), (p.W, p.H), (0, p.H));
        };
        map["diagStripe"] = p =>
        {
            var a = p.Adj("adj", 50000);
            p.Poly((0, p.H * a), (p.W * a, 0), (p.W, 0), (0, p.H));
        };
        map["can"] = p =>
        {
            var ry = p.Ss * p.Adj("adj", 25000, 0, 50000 * p.H / p.Ss) / 2;
            if (!(ry > 0)) { p.Rect(0, 0, p.W, p.H); return; }
            p.M(0, ry); p.Arc(p.Hc, ry, 180, 180); p.L(p.W, p.H - ry); p.Arc(p.Hc, ry, 0, 180); p.Z();
            p.Stroke(() => { p.M(0, ry); p.Arc(p.Hc, ry, 180, -180); });
        };
        map["cube"] = p =>
        {
            var y1 = p.Ss * p.Adj("adj", 25000);
            var x4 = p.W - y1;
            p.Poly((0, y1), (y1, 0), (p.W, 0), (p.W, p.H - y1), (x4, p.H), (0, p.H));
            p.Stroke(() => { p.M(0, y1); p.L(x4, y1); p.L(p.W, 0); });
            p.StrokeLine(x4, y1, x4, p.H);
        };
        map["bevel"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 12500, 0, 50000);
            p.Rect(0, 0, p.W, p.H);
            p.Stroke(() => { p.M(x1, x1); p.L(p.W - x1, x1); p.L(p.W - x1, p.H - x1); p.L(x1, p.H - x1); p.L(x1, x1); });
            p.StrokeLine(0, 0, x1, x1);
            p.StrokeLine(p.W, 0, p.W - x1, x1);
            p.StrokeLine(p.W, p.H, p.W - x1, p.H - x1);
            p.StrokeLine(0, p.H, x1, p.H - x1);
        };
        map["foldedCorner"] = p =>
        {
            var dy2 = p.Ss * p.Adj("adj", 16667, 0, 50000);
            var (y2, x1) = (p.H - dy2, p.W - dy2);
            var dy1 = dy2 / 5;
            p.Poly((0, 0), (p.W, 0), (p.W, y2), (x1, p.H), (0, p.H));
            p.Stroke(() => { p.M(x1, p.H); p.L(x1 + dy1, y2 + dy1); p.L(p.W, y2); });
        };
        map["smileyFace"] = p =>
        {
            var a = p.Adj("adj", 4653, -4653, 4653);
            p.Ellipse(p.Hc, p.Vc, p.Hc, p.Vc);
            var (rx, ry) = (p.W * 1125 / 21600, p.H * 1125 / 21600);
            p.Ellipse(p.W * 6215 / 21600 + rx, p.H * 7570 / 21600, rx, ry);
            p.Ellipse(p.W * 13135 / 21600 + rx, p.H * 7570 / 21600, rx, ry);
            var y3 = p.H * 16515 / 21600;
            var dy2 = p.H * a;
            var y5 = y3 + dy2 + p.H * a * 2;
            p.Stroke(() => { p.M(p.W * 4969 / 21699, y3 - dy2); p.Q(p.Hc, y5, p.W * 16640 / 21600, y3 - dy2); });
        };
        map["heart"] = p =>
        {
            var (dx1, dx2) = (p.W * 49 / 48, p.W * 10 / 48);
            var y1 = -p.H / 3;
            p.M(p.Hc, p.H / 4);
            p.C(p.Hc + dx2, y1, p.Hc + dx1, p.H / 4, p.Hc, p.H);
            p.C(p.Hc - dx1, p.H / 4, p.Hc - dx2, y1, p.Hc, p.H / 4);
            p.Z();
        };
        map["lightningBolt"] = p => p.GridPoly(21600,
            (8472, 0), (12860, 6080), (11050, 6797), (16577, 12007), (14767, 12877), (21600, 21600),
            (10012, 14915), (12222, 13987), (5022, 9705), (7602, 8382), (0, 3890));
        map["sun"] = p =>
        {
            var a = p.Adj("adj", 25000, 12500, 46875);
            var inner = 0.5 - a;
            p.Ellipse(p.Hc, p.Vc, p.W * inner, p.H * inner);
            var baseRadius = Math.Min(0.46, inner + 0.07);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * 45.0;
                var (l, r) = (angle - 9.5, angle + 9.5);
                p.Poly(
                    (p.Hc + p.W * 0.5 * Cos(angle), p.Vc + p.H * 0.5 * Sin(angle)),
                    (p.Hc + p.W * baseRadius * Cos(r), p.Vc + p.H * baseRadius * Sin(r)),
                    (p.Hc + p.W * baseRadius * Cos(l), p.Vc + p.H * baseRadius * Sin(l)));
            }
        };
        map["moon"] = p =>
        {
            var inner = p.W * (1 - p.Adj("adj", 50000, 0, 87500));
            p.M(p.W, p.H);
            p.Arc(p.W, p.Vc, 90, 180);
            p.Arc(Math.Max(inner, 0.001), p.Vc, 270, -180);
            p.Z();
        };
        map["cloud"] = p => Cloud(p, 0, 0, p.W, p.H);
        map["leftBracket"] = p => p.Stroke(() =>
        {
            var y1 = Math.Min(p.Ss * p.Adj("adj", 8333, 0, 50000 * p.H / p.Ss), p.Vc);
            p.M(p.W, p.H); p.Arc(p.W, y1, 90, 90); p.L(0, y1); p.Arc(p.W, y1, 180, 90);
        });
        map["rightBracket"] = p => p.InFrame(false, true, false, () => map["leftBracket"](p));
        map["leftBrace"] = p => p.Stroke(() =>
        {
            var y4 = p.H * p.Adj("adj2", 50000);
            var y1 = Math.Min(p.Ss * p.Adj("adj1", 8333, 0, 100000), Math.Min(y4, p.H - y4) / 2);
            p.M(p.W, p.H);
            p.Arc(p.Hc, y1, 90, 90); p.L(p.Hc, y4 + y1);
            p.Arc(p.Hc, y1, 0, -90); p.Arc(p.Hc, y1, 90, -90);
            p.L(p.Hc, y1); p.Arc(p.Hc, y1, 180, 90);
        });
        map["rightBrace"] = p => p.InFrame(false, true, false, () => map["leftBrace"](p));
        map["bracketPair"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 16667, 0, 50000);
            p.Stroke(() => { p.M(x1, p.H); p.Arc(x1, x1, 90, 90); p.L(0, x1); p.Arc(x1, x1, 180, 90); });
            p.Stroke(() => { p.M(p.W - x1, 0); p.Arc(x1, x1, 270, 90); p.L(p.W, p.H - x1); p.Arc(x1, x1, 0, 90); });
        };
        map["bracePair"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 8333, 0, 25000);
            var x2 = x1 * 2;
            p.Stroke(() =>
            {
                p.M(x2, p.H); p.Arc(x1, x1, 90, 90); p.L(x1, p.Vc + x1);
                p.Arc(x1, x1, 0, -90); p.Arc(x1, x1, 90, -90);
                p.L(x1, x1); p.Arc(x1, x1, 180, 90);
            });
            p.Stroke(() =>
            {
                p.M(p.W - x2, 0); p.Arc(x1, x1, 270, 90); p.L(p.W - x1, p.Vc - x1);
                p.Arc(x1, x1, 180, -90); p.Arc(x1, x1, 270, -90);
                p.L(p.W - x1, p.H - x1); p.Arc(x1, x1, 0, 90);
            });
        };
        map["plaque"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 16667, 0, 50000);
            if (!(x1 > 0)) { p.Rect(0, 0, p.W, p.H); return; }
            p.M(0, x1); p.Arc(x1, x1, 90, -90); p.L(p.W - x1, 0); p.Arc(x1, x1, 180, -90);
            p.L(p.W, p.H - x1); p.Arc(x1, x1, 270, -90); p.L(x1, p.H); p.Arc(x1, x1, 0, -90); p.Z();
        };

        // ── Block arrows ────────────────────────────────────────────────────────────────────────
        map["rightArrow"] = RightArrow;
        map["leftArrow"] = p => p.InFrame(false, true, false, () => RightArrow(p));
        map["downArrow"] = p => p.InFrame(true, false, false, () => RightArrow(p));
        map["upArrow"] = p => p.InFrame(true, true, false, () => RightArrow(p));
        map["leftRightArrow"] = LeftRightArrow;
        map["upDownArrow"] = p => p.InFrame(true, false, false, () => LeftRightArrow(p));
        map["quadArrow"] = p =>
        {
            var a2 = p.Adj("adj2", 22500, 0, 50000);
            var a1 = Math.Min(p.Adj("adj1", 22500), 2 * a2);
            var a3 = Math.Min(p.Adj("adj3", 22500), (1 - 2 * a2) / 2);
            var x1 = p.Ss * a3;
            var dx2 = p.Ss * a2;
            var dx3 = p.Ss * a1 / 2;
            var (x2, x5, x3, x4, x6) = (p.Hc - dx2, p.Hc + dx2, p.Hc - dx3, p.Hc + dx3, p.W - x1);
            var (y2, y5, y3, y4, y6) = (p.Vc - dx2, p.Vc + dx2, p.Vc - dx3, p.Vc + dx3, p.H - x1);
            p.Poly((0, p.Vc), (x1, y2), (x1, y3), (x3, y3), (x3, x1), (x2, x1), (p.Hc, 0), (x5, x1), (x4, x1),
                (x4, y3), (x6, y3), (x6, y2), (p.W, p.Vc), (x6, y5), (x6, y4), (x4, y4), (x4, y6), (x5, y6),
                (p.Hc, p.H), (x2, y6), (x3, y6), (x3, y4), (x1, y4), (x1, y5));
        };
        map["leftRightUpArrow"] = p =>
        {
            var a2 = p.Adj("adj2", 22500, 0, 50000);
            var a1 = Math.Min(p.Adj("adj1", 22500), 2 * a2);
            var a3 = Math.Min(p.Adj("adj3", 22500), (1 - 2 * a2) / 2);
            var x1 = p.Ss * a3;
            var dx2 = p.Ss * a2;
            var dx3 = p.Ss * a1 / 2;
            var (x2, x5, x3, x4, x6) = (p.Hc - dx2, p.Hc + dx2, p.Hc - dx3, p.Hc + dx3, p.W - x1);
            var y4 = p.H - dx2;
            var (y2, y3, y5) = (p.H - 2 * dx2, y4 - dx3, y4 + dx3);
            p.Poly((0, y4), (x1, y2), (x1, y3), (x3, y3), (x3, x1), (x2, x1), (p.Hc, 0), (x5, x1), (x4, x1),
                (x4, y3), (x6, y3), (x6, y2), (p.W, y4), (x6, p.H), (x6, y5), (x1, y5), (x1, p.H));
        };
        map["leftUpArrow"] = p =>
        {
            var a2 = p.Adj("adj2", 25000, 0, 50000);
            var a1 = Math.Min(p.Adj("adj1", 25000), 2 * a2);
            var x1 = p.Ss * Math.Min(p.Adj("adj3", 25000), 1 - 2 * a2);
            var dx2 = p.Ss * a2;
            var dx3 = p.Ss * a1 / 2;
            var (x5, y5) = (p.W - dx2, p.H - dx2);
            p.Poly((0, y5), (x1, p.H - 2 * dx2), (x1, y5 - dx3), (x5 - dx3, y5 - dx3), (x5 - dx3, x1),
                (p.W - 2 * dx2, x1), (x5, 0), (p.W, x1), (x5 + dx3, x1), (x5 + dx3, y5 + dx3), (x1, y5 + dx3), (x1, p.H));
        };
        map["bentUpArrow"] = p =>
        {
            var a2 = p.Adj("adj2", 25000, 0, 50000);
            var a1 = Math.Min(p.Adj("adj1", 25000), 2 * a2);
            var x1 = p.Ss * p.Adj("adj3", 25000, 0, 50000);
            var dx2 = p.Ss * a2;
            var dx3 = p.Ss * a1 / 2;
            var x5 = p.W - dx2;
            var y2 = p.H - p.Ss * a1;
            p.Poly((0, y2), (x5 - dx3, y2), (x5 - dx3, x1), (p.W - 2 * dx2, x1), (x5, 0), (p.W, x1),
                (x5 + dx3, x1), (x5 + dx3, p.H), (0, p.H));
        };
        map["bentArrow"] = p =>
        {
            var a2 = p.Adj("adj2", 25000, 0, 50000);
            var th = p.Ss * Math.Min(p.Adj("adj1", 25000), 2 * a2);
            var aw2 = p.Ss * a2;
            var ah = p.Ss * p.Adj("adj3", 25000, 0, 50000);
            var dh2 = aw2 - th / 2;
            var x3 = p.W - ah;
            var bd = Math.Min(p.Ss * p.Adj("adj4", 43750), Math.Min(x3, p.H - dh2));
            var bd2 = Math.Max(bd - th, 0);
            p.M(0, p.H); p.L(0, dh2 + bd); p.Arc(bd, bd, 180, 90);
            p.L(x3, dh2); p.L(x3, 0); p.L(p.W, aw2); p.L(x3, 2 * aw2); p.L(x3, dh2 + th);
            p.L(th + bd2, dh2 + th); p.Arc(bd2, bd2, 270, -90); p.L(th, p.H); p.Z();
        };
        map["uturnArrow"] = p =>
        {
            var a2 = p.Adj("adj2", 25000, 0, 25000);
            var th = p.Ss * Math.Min(p.Adj("adj1", 25000), 2 * a2);
            var aw2 = p.Ss * a2;
            var ah = p.Ss * p.Adj("adj3", 25000, 0, 50000);
            var y5 = Math.Max(p.H * p.Adj("adj5", 75000), ah + th);
            var xOut = p.W - aw2 + th / 2;
            var xIn = p.W - aw2 - th / 2;
            var bd = Math.Min(p.Ss * p.Adj("adj4", 43750), Math.Min(xOut / 2, y5 - ah));
            var bd2 = Math.Max(bd - th, 0);
            p.M(0, p.H); p.L(0, bd); p.Arc(bd, bd, 180, 90);
            p.L(xOut - bd, 0); p.Arc(bd, bd, 270, 90);
            p.L(xOut, y5 - ah); p.L(p.W, y5 - ah); p.L(p.W - aw2, y5); p.L(p.W - 2 * aw2, y5 - ah); p.L(xIn, y5 - ah);
            p.L(xIn, th + bd2); p.Arc(bd2, bd2, 0, -90);
            p.L(th + bd2, th); p.Arc(bd2, bd2, 270, -90);
            p.L(th, p.H); p.Z();
        };
        map["chevron"] = p =>
        {
            var x1 = p.Ss * p.Adj("adj", 50000, 0, 100000 * p.W / p.Ss);
            p.Poly((0, 0), (p.W - x1, 0), (p.W, p.Vc), (p.W - x1, p.H), (0, p.H), (x1, p.Vc));
        };
        map["homePlate"] = p =>
        {
            var x1 = p.W - p.Ss * p.Adj("adj", 50000, 0, 100000 * p.W / p.Ss);
            p.Poly((0, 0), (x1, 0), (p.W, p.Vc), (x1, p.H), (0, p.H));
        };
        map["notchedRightArrow"] = p =>
        {
            var dx2 = p.Ss * p.Adj("adj2", 50000, 0, 100000 * p.W / p.Ss);
            var dy1 = p.H * p.Adj("adj1", 50000) / 2;
            var x2 = p.W - dx2;
            p.Poly((0, p.Vc - dy1), (x2, p.Vc - dy1), (x2, 0), (p.W, p.Vc), (x2, p.H), (x2, p.Vc + dy1),
                (0, p.Vc + dy1), (dy1 * dx2 / p.Vc, p.Vc));
        };
        map["stripedRightArrow"] = p =>
        {
            var x4 = p.Ss * 5 / 32;
            var dx5 = p.Ss * p.Adj("adj2", 50000, 0, 84375 * p.W / p.Ss);
            var dy1 = p.H * p.Adj("adj1", 50000) / 2;
            var (y1, y2, x5) = (p.Vc - dy1, p.Vc + dy1, p.W - dx5);
            p.Rect(0, y1, p.Ss / 32, y2);
            p.Rect(p.Ss / 16, y1, p.Ss / 8, y2);
            p.Poly((x4, y1), (x5, y1), (x5, 0), (p.W, p.Vc), (x5, p.H), (x5, y2), (x4, y2));
        };
        map["rightArrowCallout"] = RightArrowCallout;
        map["leftArrowCallout"] = p => p.InFrame(false, true, false, () => RightArrowCallout(p));
        map["downArrowCallout"] = p => p.InFrame(true, false, false, () => RightArrowCallout(p));
        map["upArrowCallout"] = p => p.InFrame(true, true, false, () => RightArrowCallout(p));

        // ── Equation shapes ─────────────────────────────────────────────────────────────────────
        map["mathPlus"] = p =>
        {
            var (dx1, dy1) = (p.W * 0.36745, p.H * 0.36745);
            var dx2 = p.Ss * p.Adj("adj1", 23520, 0, 73490) / 2;
            var (x1, x2, x3, x4) = (p.Hc - dx1, p.Hc - dx2, p.Hc + dx2, p.Hc + dx1);
            var (y1, y2, y3, y4) = (p.Vc - dy1, p.Vc - dx2, p.Vc + dx2, p.Vc + dy1);
            p.Poly((x1, y2), (x2, y2), (x2, y1), (x3, y1), (x3, y2), (x4, y2), (x4, y3), (x3, y3), (x3, y4),
                (x2, y4), (x2, y3), (x1, y3));
        };
        map["mathMinus"] = p =>
        {
            var dy1 = p.H * p.Adj("adj1", 23520) / 2;
            var dx1 = p.W * 0.36745;
            p.Rect(p.Hc - dx1, p.Vc - dy1, p.Hc + dx1, p.Vc + dy1);
        };
        map["mathMultiply"] = p =>
        {
            var t = p.Ss * p.Adj("adj1", 23520, 0, 51965) / 2;
            var angle = Math.Atan2(p.H, p.W);
            var (ca, sa) = (Math.Cos(angle), Math.Sin(angle));
            var length = Math.Sqrt(p.W * p.W + p.H * p.H) * 0.51965 * 0.72;
            (double X, double Y) At(double along, double dirY, double nx, double ny) =>
                (p.Hc + along * ca + t * nx, p.Vc + along * sa * dirY + t * ny);
            p.Poly(
                (p.Hc, p.Vc - t / ca),
                At(length, -1, -sa, -ca), At(length, -1, sa, ca),
                (p.Hc + t / sa, p.Vc),
                At(length, 1, sa, -ca), At(length, 1, -sa, ca),
                (p.Hc, p.Vc + t / ca),
                At(-length, -1, sa, ca), At(-length, -1, -sa, -ca),
                (p.Hc - t / sa, p.Vc),
                At(-length, 1, -sa, ca), At(-length, 1, sa, -ca));
        };
        map["mathDivide"] = p =>
        {
            var dy1 = p.H * p.Adj("adj1", 23520, 1000, 36745) / 2;
            var gap = p.H * p.Adj("adj2", 5880, 0, 36745);
            var radius = p.Ss * p.Adj("adj3", 11760, 1000, 36745);
            var dx1 = p.W * 0.36745;
            p.Rect(p.Hc - dx1, p.Vc - dy1, p.Hc + dx1, p.Vc + dy1);
            p.Ellipse(p.Hc, p.Vc - dy1 - gap - radius, radius, radius);
            p.Ellipse(p.Hc, p.Vc + dy1 + gap + radius, radius, radius);
        };
        map["mathEqual"] = p =>
        {
            var dy1 = p.H * p.Adj("adj1", 23520, 0, 36745);
            var dy2 = p.H * p.Adj("adj2", 11760, 0, 100000) / 2;
            var dx1 = p.W * 0.36745;
            p.Rect(p.Hc - dx1, p.Vc - dy2 - dy1, p.Hc + dx1, p.Vc - dy2);
            p.Rect(p.Hc - dx1, p.Vc + dy2, p.Hc + dx1, p.Vc + dy2 + dy1);
        };

        // ── Flowchart ───────────────────────────────────────────────────────────────────────────
        map["flowChartAlternateProcess"] = p =>
        {
            var r = p.Ss / 6;
            RoundedCorners(p, r, r, r, r);
        };
        map["flowChartDecision"] = map["diamond"];
        map["flowChartInputOutput"] = p => p.GridPoly(5, (0, 5), (1, 0), (5, 0), (4, 5));
        map["flowChartPredefinedProcess"] = p =>
        {
            p.Rect(0, 0, p.W, p.H);
            p.StrokeLine(p.W / 8, 0, p.W / 8, p.H);
            p.StrokeLine(p.W * 7 / 8, 0, p.W * 7 / 8, p.H);
        };
        map["flowChartInternalStorage"] = p =>
        {
            p.Rect(0, 0, p.W, p.H);
            p.StrokeLine(p.W / 8, 0, p.W / 8, p.H);
            p.StrokeLine(0, p.H / 8, p.W, p.H / 8);
        };
        map["flowChartDocument"] = p =>
        {
            double X(double v) => v * p.W / 21600;
            double Y(double v) => v * p.H / 21600;
            p.M(0, 0); p.L(p.W, 0); p.L(p.W, Y(17322));
            p.C(X(10800), Y(17322), X(10800), Y(23922), 0, Y(20172));
            p.Z();
        };
        map["flowChartMultidocument"] = p =>
        {
            double X(double v) => v * p.W / 21600;
            double Y(double v) => v * p.H / 21600;
            p.M(0, Y(3675)); p.L(X(18595), Y(3675)); p.L(X(18595), Y(18022));
            p.C(X(9298), Y(18022), X(9298), Y(23542), 0, Y(20782));
            p.Z();
            p.Stroke(() => { p.M(X(1532), Y(3675)); p.L(X(1532), Y(1815)); p.L(X(20000), Y(1815)); p.L(X(20000), Y(16252)); p.L(X(18595), Y(16352)); });
            p.Stroke(() => { p.M(X(2972), Y(1815)); p.L(X(2972), 0); p.L(p.W, 0); p.L(p.W, Y(14392)); p.L(X(20000), Y(14467)); });
        };
        map["flowChartTerminator"] = p =>
        {
            var rx = p.W * 3475 / 21600;
            p.M(rx, 0); p.L(p.W - rx, 0); p.Arc(rx, p.Vc, 270, 180); p.L(rx, p.H); p.Arc(rx, p.Vc, 90, 180); p.Z();
        };
        map["flowChartPreparation"] = p => p.GridPoly(10, (0, 5), (2, 0), (8, 0), (10, 5), (8, 10), (2, 10));
        map["flowChartManualInput"] = p => p.GridPoly(5, (0, 1), (5, 0), (5, 5), (0, 5));
        map["flowChartManualOperation"] = p => p.GridPoly(5, (0, 0), (5, 0), (4, 5), (1, 5));
        map["flowChartConnector"] = p => p.Ellipse(p.Hc, p.Vc, p.Hc, p.Vc);
        map["flowChartOffpageConnector"] = p => p.GridPoly(10, (0, 0), (10, 0), (10, 8), (5, 10), (0, 8));
        map["flowChartPunchedCard"] = p => p.GridPoly(5, (0, 1), (1, 0), (5, 0), (5, 5), (0, 5));
        map["flowChartPunchedTape"] = p =>
        {
            var (rx, ry) = (p.W / 4, p.H / 10);
            p.M(0, ry); p.Arc(rx, ry, 180, -180); p.Arc(rx, ry, 180, 180);
            p.L(p.W, p.H - ry); p.Arc(rx, ry, 0, -180); p.Arc(rx, ry, 0, 180); p.Z();
        };
        map["flowChartSummingJunction"] = p =>
        {
            p.Ellipse(p.Hc, p.Vc, p.Hc, p.Vc);
            var (dx, dy) = (p.Hc * Cos(45), p.Vc * Sin(45));
            p.StrokeLine(p.Hc - dx, p.Vc - dy, p.Hc + dx, p.Vc + dy);
            p.StrokeLine(p.Hc + dx, p.Vc - dy, p.Hc - dx, p.Vc + dy);
        };
        map["flowChartOr"] = p =>
        {
            p.Ellipse(p.Hc, p.Vc, p.Hc, p.Vc);
            p.StrokeLine(p.Hc, 0, p.Hc, p.H);
            p.StrokeLine(0, p.Vc, p.W, p.Vc);
        };
        map["flowChartCollate"] = p =>
        {
            p.Poly((0, 0), (p.W, 0), (p.Hc, p.Vc));
            p.Poly((p.Hc, p.Vc), (p.W, p.H), (0, p.H));
        };
        map["flowChartSort"] = p =>
        {
            p.Poly((0, p.Vc), (p.Hc, 0), (p.W, p.Vc), (p.Hc, p.H));
            p.StrokeLine(0, p.Vc, p.W, p.Vc);
        };
        map["flowChartExtract"] = p => p.Poly((0, p.H), (p.Hc, 0), (p.W, p.H));
        map["flowChartMerge"] = p => p.Poly((0, 0), (p.W, 0), (p.Hc, p.H));
        map["flowChartOfflineStorage"] = p =>
        {
            p.Poly((0, 0), (p.W, 0), (p.Hc, p.H));
            p.StrokeLine(p.W * 2 / 5, p.H * 4 / 5, p.W * 3 / 5, p.H * 4 / 5);
        };
        map["flowChartOnlineStorage"] = p =>
        {
            var rx = p.W / 6;
            p.M(rx, 0); p.L(p.W, 0); p.Arc(rx, p.Vc, 270, -180); p.L(rx, p.H); p.Arc(rx, p.Vc, 90, 180); p.Z();
        };
        map["flowChartDelay"] = p =>
        {
            p.M(0, 0); p.L(p.Hc, 0); p.Arc(p.Hc, p.Vc, 270, 180); p.L(0, p.H); p.Z();
        };
        map["flowChartMagneticTape"] = p =>
        {
            p.M(p.Hc, p.H); p.Arc(p.Hc, p.Vc, 90, 315);
            var y = p.Vc + p.Vc * Sin(45);
            p.L(p.W, y); p.L(p.W, p.H); p.Z();
        };
        map["flowChartMagneticDisk"] = p =>
        {
            var ry = p.H / 6;
            p.M(0, ry); p.Arc(p.Hc, ry, 180, 180); p.L(p.W, p.H - ry); p.Arc(p.Hc, ry, 0, 180); p.Z();
            p.Stroke(() => { p.M(p.W, ry); p.Arc(p.Hc, ry, 0, 180); });
        };
        map["flowChartMagneticDrum"] = p =>
        {
            var rx = p.W / 6;
            p.M(rx, 0); p.L(p.W - rx, 0); p.Arc(rx, p.Vc, 270, 180); p.L(rx, p.H); p.Arc(rx, p.Vc, 90, 180); p.Z();
            p.Stroke(() => { p.M(p.W - rx, p.H); p.Arc(rx, p.Vc, 90, 180); });
        };
        map["flowChartDisplay"] = p =>
        {
            var rx = p.W / 6;
            p.M(0, p.Vc); p.L(rx, 0); p.L(p.W - rx, 0); p.Arc(rx, p.Vc, 270, 180); p.L(rx, p.H); p.Z();
        };

        // ── Stars and banners ───────────────────────────────────────────────────────────────────
        map["star4"] = p => Star(p, 4, 12500, 1, 1, false);
        map["star5"] = p => Star(p, 5, 19098, 1.05146, 1.10557, true);
        map["star6"] = p => Star(p, 6, 28868, 1.15470, 1, false);
        map["star7"] = p => Star(p, 7, 34601, 1.02572, 1.05210, true);
        map["star8"] = p => Star(p, 8, 37500, 1, 1, false);
        map["star10"] = p => Star(p, 10, 42533, 1.05146, 1, false);
        map["star12"] = p => Star(p, 12, 37500, 1, 1, false);
        map["star16"] = p => Star(p, 16, 37500, 1, 1, false);
        map["star24"] = p => Star(p, 24, 37500, 1, 1, false);
        map["star32"] = p => Star(p, 32, 37500, 1, 1, false);
        map["irregularSeal1"] = p => p.GridPoly(21600,
            (10800, 5800), (14522, 0), (14155, 5325), (18380, 4457), (16702, 7315), (21097, 8137),
            (17607, 10475), (21600, 13290), (16837, 12942), (18145, 18095), (14020, 14457), (13247, 19737),
            (10532, 14935), (8485, 21600), (7715, 15627), (4762, 17617), (5667, 13937), (135, 14587),
            (3722, 11775), (0, 8615), (4627, 7617), (370, 2295), (7312, 6320), (8352, 2295));
        map["irregularSeal2"] = p => p.GridPoly(21600,
            (11462, 4342), (14790, 0), (14525, 5777), (18007, 3172), (16380, 6532), (21600, 6645),
            (16985, 9402), (18270, 11290), (16380, 12310), (18877, 15632), (14640, 14350), (14942, 17370),
            (12180, 15935), (11612, 18842), (9872, 17370), (8700, 19712), (7527, 18125), (4917, 21600),
            (4805, 18240), (1285, 17825), (3330, 15370), (0, 12877), (3935, 11592), (1172, 8270),
            (5372, 7817), (4502, 3625), (8550, 6382), (9722, 1887));
        map["wave"] = p =>
        {
            var y1 = p.H * p.Adj("adj1", 12500, 0, 20000);
            var dy2 = y1 * 10 / 3;
            var (y2, y3, y4) = (y1 - dy2, y1 + dy2, p.H - y1);
            var (y5, y6) = (y4 - dy2, y4 + dy2);
            var of2 = p.W * p.Adj("adj2", 0, -10000, 10000) * 2;
            var dx2 = of2 > 0 ? 0 : of2;
            var x2 = -dx2;
            var dx5 = of2 > 0 ? of2 : 0;
            var x5 = p.W - dx5;
            var dx3 = (dx2 + x5) / 3;
            var x3 = x2 + dx3;
            var x4 = (x3 + x5) / 2;
            var x6 = dx5;
            var x10 = p.W + dx2;
            var x7 = x6 + dx3;
            var x8 = (x7 + x10) / 2;
            p.M(x2, y1); p.C(x3, y2, x4, y3, x5, y1); p.L(x10, y4); p.C(x8, y6, x7, y5, x6, y4); p.Z();
        };

        // ── Callouts ────────────────────────────────────────────────────────────────────────────
        map["wedgeRectCallout"] = p => WedgeRectCallout(p, 0);
        map["wedgeRoundRectCallout"] = p => WedgeRectCallout(p, p.Ss * p.Adj("adj3", 16667, 0, 50000));
        map["wedgeEllipseCallout"] = p =>
        {
            var (xPos, yPos) = CalloutTip(p, -20833, 62500);
            var angle = Math.Atan2((yPos - p.Vc) * p.W, (xPos - p.Hc) * p.H) * 180 / Math.PI;
            var start = angle + 11;
            p.M(xPos, yPos);
            p.L(p.Hc + p.Hc * Cos(start), p.Vc + p.Vc * Sin(start));
            p.Arc(p.Hc, p.Vc, start, 338);
            p.Z();
        };
        map["cloudCallout"] = p =>
        {
            var (xPos, yPos) = CalloutTip(p, -20833, 62500);
            Cloud(p, 0, 0, p.W, p.H);
            var angle = Math.Atan2((yPos - p.Vc) * p.W, (xPos - p.Hc) * p.H);
            var (ex, ey) = (p.Hc + p.Hc * Math.Cos(angle), p.Vc + p.Vc * Math.Sin(angle));
            for (var i = 0; i < 3; i++)
            {
                var t = (i + 1) / 3.0;
                var radius = p.Ss * (0.06 - 0.015 * i);
                var (cx, cy) = (ex + (xPos - ex) * t, ey + (yPos - ey) * t);
                if (Math.Abs(xPos - ex) + Math.Abs(yPos - ey) > radius)
                    p.Ellipse(cx, cy, radius, radius);
            }
        };
        foreach (var (name, points, accent) in new[]
                 {
                     ("callout1", 2, false), ("callout2", 3, false), ("callout3", 4, false),
                     ("borderCallout1", 2, false), ("borderCallout2", 3, false), ("borderCallout3", 4, false),
                     ("accentCallout1", 2, true), ("accentCallout2", 3, true), ("accentCallout3", 4, true),
                     ("accentBorderCallout1", 2, true), ("accentBorderCallout2", 3, true), ("accentBorderCallout3", 4, true),
                 })
        {
            map[name] = p => LineCallout(p, points, accent);
        }

        return map;
    }

    private static double Cos(double degrees) => Math.Cos(degrees * Math.PI / 180);

    private static double Sin(double degrees) => Math.Sin(degrees * Math.PI / 180);

    /// <summary>Start angle and clockwise sweep (degrees) from the adj1/adj2 angle guides.</summary>
    private static (double Start, double Sweep) Angles(PathBuilder p, double defaultStart, double defaultEnd)
    {
        var start = p.Raw("adj1", defaultStart) / 60000.0;
        var end = p.Raw("adj2", defaultEnd) / 60000.0;
        var sweep = (end - start) % 360;
        if (sweep <= 0)
            sweep += 360;
        return (start % 360, sweep);
    }

    private static void RoundedCorners(PathBuilder p, double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        p.M(topLeft, 0);
        p.L(p.W - topRight, 0);
        p.Arc(topRight, topRight, 270, 90);
        p.L(p.W, p.H - bottomRight);
        p.Arc(bottomRight, bottomRight, 0, 90);
        p.L(bottomLeft, p.H);
        p.Arc(bottomLeft, bottomLeft, 90, 90);
        p.L(0, topLeft);
        p.Arc(topLeft, topLeft, 180, 90);
        p.Z();
    }

    private static void RegularPolygon(PathBuilder p, int sides, double hf, double vf, bool centreScaled, double startDeg = -90)
    {
        var (rx, ry) = (p.Hc * hf, p.Vc * vf);
        var cy = centreScaled ? p.Vc * vf : p.Vc;
        var points = new (double X, double Y)[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = startDeg + i * 360.0 / sides;
            points[i] = (p.Hc + rx * Cos(angle), cy + ry * Sin(angle));
        }

        p.Poly(points);
    }

    private static void Star(PathBuilder p, int points, double defaultAdj, double hf, double vf, bool centreScaled)
    {
        var a = p.Adj("adj", defaultAdj, 0, 50000) * 2;
        var (rx, ry) = (p.Hc * hf, p.Vc * vf);
        var cy = centreScaled ? p.Vc * vf : p.Vc;
        var vertices = new (double X, double Y)[points * 2];
        for (var i = 0; i < vertices.Length; i++)
        {
            var scale = i % 2 == 0 ? 1 : a;
            var angle = -90 + i * 180.0 / points;
            vertices[i] = (p.Hc + rx * scale * Cos(angle), cy + ry * scale * Sin(angle));
        }

        p.Poly(vertices);
    }

    private static void RightArrow(PathBuilder p)
    {
        var dx1 = p.Ss * p.Adj("adj2", 50000, 0, 100000 * p.W / p.Ss);
        var dy1 = p.H * p.Adj("adj1", 50000) / 2;
        var x1 = p.W - dx1;
        p.Poly((0, p.Vc - dy1), (x1, p.Vc - dy1), (x1, 0), (p.W, p.Vc), (x1, p.H), (x1, p.Vc + dy1), (0, p.Vc + dy1));
    }

    private static void LeftRightArrow(PathBuilder p)
    {
        var x2 = p.Ss * p.Adj("adj2", 50000, 0, 50000 * p.W / p.Ss);
        var dy = p.H * p.Adj("adj1", 50000) / 2;
        var x3 = p.W - x2;
        p.Poly((0, p.Vc), (x2, 0), (x2, p.Vc - dy), (x3, p.Vc - dy), (x3, 0), (p.W, p.Vc), (x3, p.H),
            (x3, p.Vc + dy), (x2, p.Vc + dy), (x2, p.H));
    }

    private static void RightArrowCallout(PathBuilder p)
    {
        var a2 = p.Adj("adj2", 25000, 0, 50000 * p.H / p.Ss);
        var dy1 = p.Ss * a2;
        var dy2 = p.Ss * Math.Min(p.Adj("adj1", 25000), 2 * a2) / 2;
        var dx3 = p.Ss * p.Adj("adj3", 25000, 0, 100000 * p.W / p.Ss);
        var x3 = p.W - dx3;
        var x2 = Math.Min(p.W * p.Adj("adj4", 64977), x3);
        p.Poly((0, 0), (x2, 0), (x2, p.Vc - dy2), (x3, p.Vc - dy2), (x3, p.Vc - dy1), (p.W, p.Vc),
            (x3, p.Vc + dy1), (x3, p.Vc + dy2), (x2, p.Vc + dy2), (x2, p.H), (0, p.H));
    }

    private static (double X, double Y) CalloutTip(PathBuilder p, double defaultX, double defaultY) =>
        (p.Hc + p.W * p.Adj("adj1", defaultX, -1000000, 1000000),
         p.Vc + p.H * p.Adj("adj2", defaultY, -1000000, 1000000));

    private static void WedgeRectCallout(PathBuilder p, double radius)
    {
        var (xPos, yPos) = CalloutTip(p, -20833, 62500);
        var (dxPos, dyPos) = (xPos - p.Hc, yPos - p.Vc);
        var vertical = Math.Abs(dyPos) - Math.Abs(dxPos * p.H / p.W) > 0;
        var x1 = p.W * (dxPos > 0 ? 7 : 2) / 12;
        var x2 = p.W * (dxPos > 0 ? 10 : 5) / 12;
        var y1 = p.H * (dyPos > 0 ? 7 : 2) / 12;
        var y2 = p.H * (dyPos > 0 ? 10 : 5) / 12;
        var r = Math.Min(radius, Math.Min(p.W, p.H) / 12);

        p.M(r, 0);
        if (vertical && dyPos < 0) { p.L(x1, 0); p.L(xPos, yPos); p.L(x2, 0); }
        p.L(p.W - r, 0);
        p.Arc(r, r, 270, 90);
        if (!vertical && dxPos > 0) { p.L(p.W, y1); p.L(xPos, yPos); p.L(p.W, y2); }
        p.L(p.W, p.H - r);
        p.Arc(r, r, 0, 90);
        if (vertical && dyPos >= 0) { p.L(x2, p.H); p.L(xPos, yPos); p.L(x1, p.H); }
        p.L(r, p.H);
        p.Arc(r, r, 90, 90);
        if (!vertical && dxPos <= 0) { p.L(0, y2); p.L(xPos, yPos); p.L(0, y1); }
        p.L(0, r);
        p.Arc(r, r, 180, 90);
        p.Z();
    }

    private static void LineCallout(PathBuilder p, int points, bool accent)
    {
        (double Y, double X)[] defaults = points switch
        {
            2 => [(18750, -8333), (112500, -38333)],
            3 => [(18750, -8333), (18750, -16667), (112500, -46667)],
            _ => [(18750, -8333), (18750, -16667), (100000, -16667), (112963, -8333)],
        };

        p.Rect(0, 0, p.W, p.H);
        var leader = new (double X, double Y)[points];
        for (var i = 0; i < points; i++)
        {
            var y = p.H * p.Adj($"adj{i * 2 + 1}", defaults[i].Y, -1000000, 1000000);
            var x = p.W * p.Adj($"adj{i * 2 + 2}", defaults[i].X, -1000000, 1000000);
            leader[i] = (x, y);
        }

        p.Stroke(() =>
        {
            p.M(leader[0].X, leader[0].Y);
            for (var i = 1; i < points; i++)
                p.L(leader[i].X, leader[i].Y);
        });
        if (accent)
            p.StrokeLine(leader[0].X, 0, leader[0].X, p.H);
    }

    /// <summary>An approximate cloud: scallops bulging out of an ellipse inscribed in the box.</summary>
    private static void Cloud(PathBuilder p, double left, double top, double width, double height)
    {
        const int Bumps = 9;
        var (cx, cy) = (left + width / 2, top + height / 2);
        var (rx, ry) = (width * 0.43, height * 0.40);
        (double X, double Y) On(double degrees, double scale) =>
            (cx + rx * scale * Cos(degrees), cy + ry * scale * Sin(degrees));

        var first = On(-100, 1);
        p.M(first.X, first.Y);
        for (var i = 0; i < Bumps; i++)
        {
            var a0 = -100 + i * 360.0 / Bumps;
            var a1 = a0 + 360.0 / Bumps;
            var c1 = On(a0 + 4, 1.34);
            var c2 = On(a1 - 4, 1.34);
            var end = On(a1, 1);
            p.C(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
        }

        p.Z();
    }
}
