using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.UI.Tests;
public sealed partial class GridViewPerformanceMeasurementTests
{
    private static readonly Lazy<Action<GridView, DrawingContext>> RenderQuickAnalysisPreviewOnly = new(() =>
    {
        var method = typeof(GridView).GetMethod(
            "RenderQuickAnalysisPreview",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(GridView), "RenderQuickAnalysisPreview");
        return method.CreateDelegate<Action<GridView, DrawingContext>>();
    });

    private static GridView CreateTextHeavyGrid(double width, double height)
        => CreateTextHeavyGrid(width, height, null);

    private static GridView CreateSelectionOnlyGrid(double width, double height, out GridRange[] selectionSteps)
    {
        const int rowCount = 240;
        const int columnCount = 120;
        const double rowHeight = 18;
        const double columnWidth = 48;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        selectionSteps = Enumerable
            .Range(0, 80)
            .Select(index =>
            {
                var row = (uint)((index * 7 % rowCount) + 1);
                var column = (uint)((index * 11 % columnCount) + 1);
                return new GridRange(
                    new CellAddress(sheetId, row, column),
                    new CellAddress(sheetId, row, column));
            })
            .ToArray();

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel([], rows, columns),
            SelectedRange = selectionSteps[0]
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateTextHeavyGrid(double width, double height, CellStyle? style)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var text = $"R{row.Row}C{column.Col}";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new TextValue(text),
                    text,
                    null,
                    StyleId.Default,
                    null,
                    style));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateConditionalIconHeavyGrid(double width, double height)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var iconStyles = new (string Style, int Count)[]
        {
            ("3Arrows", 3),
            ("3Flags", 3),
            ("4Rating", 4),
            ("5Quarters", 5),
            ("3Signs", 3),
            ("3Symbols", 3)
        };
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var iconStyle = iconStyles[(int)((row.Row + column.Col) % iconStyles.Length)];
                var iconIndex = (int)((row.Row * 3 + column.Col) % (uint)iconStyle.Count);
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    BlankValue.Instance,
                    "",
                    null,
                    StyleId.Default,
                    null,
                    Style: null,
                    ConditionalIcon: new ConditionalFormatIcon(
                        iconStyle.Style,
                        iconIndex,
                        iconStyle.Count,
                        ShowValue: false)));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateWrappedTextHeavyGrid(double width, double height)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 42;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        var style = new CellStyle { WrapText = true };
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var text = $"Wrapped value R{row.Row:D2} C{column.Col:D2} forecast pipeline";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new TextValue(text),
                    text,
                    null,
                    StyleId.Default,
                    null,
                    style));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateSparseSurfaceGrid(double width, double height)
    {
        const int rowCount = 480;
        const int columnCount = 240;
        const double rowHeight = 18;
        const double columnWidth = 48;
        const int styledCellCount = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var surfaceStyle = new CellStyle
        {
            FillColor = new CellColor(226, 239, 218)
        };
        var cells = new List<DisplayCell>(styledCellCount);
        for (var index = 0; index < styledCellCount; index++)
        {
            var row = (uint)(1 + index * 7 % rowCount);
            var column = (uint)(1 + index * 11 % columnCount);
            cells.Add(new DisplayCell(
                row,
                column,
                BlankValue.Instance,
                "",
                null,
                StyleId.Default,
                null,
                surfaceStyle));
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateCommentIndicatorGrid(double width, double height)
    {
        const int rowCount = 120;
        const int columnCount = 80;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    BlankValue.Instance,
                    "",
                    null,
                    StyleId.Default,
                    null,
                    HasComment: true));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateSparklineGrid(double width, double height)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var value = row.Row * column.Col;
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new NumberValue(value),
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    StyleId.Default,
                    null,
                    null));
            }
        }

        var sparklines = new List<SparklineModel>(rowCount);
        var sparklineValues = new Dictionary<Guid, IReadOnlyList<double>>(rowCount);
        foreach (var row in rows)
        {
            var id = Guid.NewGuid();
            var kind = (row.Row % 3u) switch
            {
                0 => SparklineKind.WinLoss,
                1 => SparklineKind.Line,
                _ => SparklineKind.Column
            };
            sparklines.Add(new SparklineModel
            {
                Id = id,
                DataRange = new GridRange(
                    new CellAddress(sheetId, row.Row, 1),
                    new CellAddress(sheetId, row.Row, 16)),
                Location = new CellAddress(sheetId, row.Row, 26),
                Kind = kind
            });
            sparklineValues[id] = Enumerable
                .Range(0, 16)
                .Select(index => (double)(((index + 1) * ((int)row.Row % 11 + 1)) - (row.Row % 5 == 0 ? 35 : 0)))
                .ToArray();
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            Sparklines = sparklines,
            SparklineValues = sparklineValues,
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateChartGrid(double width, double height)
    {
        const int rowCount = 40;
        const int columnCount = 12;
        const double rowHeight = 20;
        const double columnWidth = 72;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        for (uint row = 1; row <= rowCount; row++)
        {
            for (uint col = 1; col <= columnCount; col++)
            {
                ScalarValue rawValue;
                string displayText;
                if (row == 1)
                {
                    rawValue = new TextValue(col == 1 ? "Month" : $"Series {col - 1}");
                    displayText = rawValue.ToString() ?? "";
                }
                else if (col == 1)
                {
                    rawValue = new TextValue($"M{row - 1}");
                    displayText = rawValue.ToString() ?? "";
                }
                else
                {
                    var value = (row - 1) * (col + 2);
                    rawValue = new NumberValue(value);
                    displayText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                cells.Add(new DisplayCell(
                    row,
                    col,
                    rawValue,
                    displayText,
                    null,
                    StyleId.Default,
                    null,
                    null));
            }
        }

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Render Benchmark",
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 30, 8)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            ShowLegend = true,
            Left = 96,
            Top = 72,
            Width = 560,
            Height = 340
        };

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            Charts = [chart],
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateFormulaTraceGrid(double width, double height, int arrowCount)
    {
        const int rowCount = 40;
        const int columnCount = 40;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var arrows = new List<FormulaTraceArrow>(arrowCount);
        for (var i = 0; i < arrowCount; i++)
        {
            var row = (uint)(1 + i / columnCount % (rowCount - 1));
            var column = (uint)(1 + i % (columnCount - 1));
            arrows.Add(new FormulaTraceArrow(
                new CellAddress(sheetId, row, column),
                new CellAddress(sheetId, row + 1, column + 1)));
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel([], rows, columns),
            FormulaTraceSheetId = sheetId,
            FormulaTraceArrows = arrows,
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateDrawingObjectHeavyGrid(double width, double height)
    {
        const int rowCount = 40;
        const int columnCount = 20;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();

        var fills = new[]
        {
            new CellColor(91, 155, 213),
            new CellColor(112, 173, 71),
            new CellColor(237, 125, 49),
            new CellColor(165, 165, 165)
        };
        var outlines = new[]
        {
            new CellColor(68, 114, 196),
            new CellColor(84, 130, 53),
            new CellColor(191, 95, 32),
            new CellColor(89, 89, 89)
        };

        var textBoxes = new List<TextBoxModel>(120);
        for (var index = 0; index < 120; index++)
        {
            var row = (uint)(1 + index % 36);
            var col = (uint)(1 + index * 3 % 17);
            textBoxes.Add(new TextBoxModel
            {
                Name = $"TextBox{index}",
                Anchor = new CellAddress(sheetId, row, col),
                Text = $"Benchmark text box {index % 24}",
                Width = 108 + index % 4 * 16,
                Height = 38 + index % 3 * 8,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                RotationDegrees = index % 18 == 0 ? 3 : 0
            });
        }

        var shapes = new List<DrawingShapeModel>(150);
        for (var index = 0; index < 150; index++)
        {
            var row = (uint)(1 + index * 2 % 37);
            var col = (uint)(1 + index * 5 % 18);
            shapes.Add(new DrawingShapeModel
            {
                Name = $"Shape{index}",
                Anchor = new CellAddress(sheetId, row, col),
                Kind = index % 5 == 0
                    ? DrawingShapeKind.Line
                    : index % 2 == 0
                        ? DrawingShapeKind.Ellipse
                        : DrawingShapeKind.Rectangle,
                Width = 72 + index % 5 * 12,
                Height = 28 + index % 4 * 10,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                GradientFillEndColor = index % 7 == 0 ? fills[(index + 1) % fills.Length] : null,
                EffectPreset = index % 11 == 0
                    ? DrawingShapeEffectPreset.Glow
                    : index % 13 == 0
                        ? DrawingShapeEffectPreset.SoftEdges
                        : index % 4 == 0
                            ? DrawingShapeEffectPreset.Shadow
                            : DrawingShapeEffectPreset.None,
                RotationDegrees = index % 23 == 0 ? -4 : 0
            });
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel([], rows, columns),
            DrawingShapes = shapes,
            TextBoxes = textBoxes,
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateOffscreenDrawingObjectHeavyGrid(double width, double height)
    {
        const int rowCount = 96;
        const int columnCount = 160;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();

        var fills = new[]
        {
            new CellColor(91, 155, 213),
            new CellColor(112, 173, 71),
            new CellColor(237, 125, 49),
            new CellColor(165, 165, 165)
        };
        var outlines = new[]
        {
            new CellColor(68, 114, 196),
            new CellColor(84, 130, 53),
            new CellColor(191, 95, 32),
            new CellColor(89, 89, 89)
        };

        var textBoxes = new List<TextBoxModel>(900);
        for (var index = 0; index < 900; index++)
        {
            var anchor = index % 3 == 0
                ? new CellAddress(sheetId, (uint)(62 + index % 28), (uint)(2 + index % 18))
                : new CellAddress(sheetId, (uint)(1 + index % 28), (uint)(74 + index % 70));
            textBoxes.Add(new TextBoxModel
            {
                Name = $"OffscreenTextBox{index}",
                Anchor = anchor,
                Text = $"Offscreen benchmark text box {index:D4}",
                Width = 128 + index % 5 * 18,
                Height = 34 + index % 4 * 8,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                RotationDegrees = index % 17 == 0 ? 7 : 0
            });
        }

        var shapes = new List<DrawingShapeModel>(900);
        for (var index = 0; index < 900; index++)
        {
            var anchor = index % 4 == 0
                ? new CellAddress(sheetId, (uint)(64 + index % 24), (uint)(3 + index % 16))
                : new CellAddress(sheetId, (uint)(2 + index % 30), (uint)(82 + index % 60));
            shapes.Add(new DrawingShapeModel
            {
                Name = $"OffscreenShape{index}",
                Anchor = anchor,
                Kind = index % 5 == 0
                    ? DrawingShapeKind.Line
                    : index % 2 == 0
                        ? DrawingShapeKind.Ellipse
                        : DrawingShapeKind.Rectangle,
                Width = 80 + index % 6 * 14,
                Height = 30 + index % 5 * 8,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                GradientFillEndColor = index % 9 == 0 ? fills[(index + 1) % fills.Length] : null,
                EffectPreset = index % 10 == 0
                    ? DrawingShapeEffectPreset.Glow
                    : index % 12 == 0
                        ? DrawingShapeEffectPreset.SoftEdges
                        : index % 3 == 0
                            ? DrawingShapeEffectPreset.Shadow
                            : DrawingShapeEffectPreset.None,
                RotationDegrees = index % 19 == 0 ? -6 : 0
            });
        }

        var charts = new List<ChartModel>(20);
        for (var index = 0; index < 20; index++)
        {
            charts.Add(new ChartModel
            {
                Type = ChartType.Column,
                Title = $"Offscreen Chart {index}",
                DataRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 8, 4)),
                Left = 2600 + index * 48,
                Top = 80 + index % 8 * 72,
                Width = 360,
                Height = 220
            });
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel([], rows, columns),
            Charts = charts,
            DrawingShapes = shapes,
            TextBoxes = textBoxes,
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateQuickAnalysisGrid(
        double width,
        double height,
        QuickAnalysisPreviewVisualKind visual,
        Func<RowMetric, ColMetric, double>? valueFactory = null,
        bool includeDisplayText = true)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var value = valueFactory?.Invoke(row, column) ?? ((row.Row * 7) + (column.Col * 3));
                var displayText = includeDisplayText
                    ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new NumberValue(value),
                    displayText,
                    null,
                    StyleId.Default,
                    null,
                    null));
            }
        }

        var previewRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, rowCount, columnCount));
        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = previewRange,
            QuickAnalysisPreviewRange = previewRange,
            QuickAnalysisPreviewVisual = visual
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static void MeasureQuickAnalysisPreviewVariant(
        QuickAnalysisPreviewVisualKind visual,
        string label)
    {
        const int iterations = 96;
        const int width = 1440;
        const int height = 900;
        var grid = CreateQuickAnalysisGrid(width, height, visual, includeDisplayText: false);

        DrawQuickAnalysisPreviewOnly(grid);
        DrawQuickAnalysisPreviewOnly(grid);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var step = Stopwatch.StartNew();
            DrawQuickAnalysisPreviewOnly(grid);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            $"PERF GRID_RENDER_QUICK_ANALYSIS_{label} " +
            $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F4} p95_ms={p95:F4} max_ms={ordered[^1]:F4} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    private static void DrawQuickAnalysisPreviewOnly(GridView grid)
    {
        var drawing = new DrawingGroup();
        using var dc = drawing.Open();
        RenderQuickAnalysisPreviewOnly.Value(grid, dc);
    }

    private static GridView CreateShrinkToFitGrid(double width, double height)
    {
        const int rowCount = 40;
        const int columnCount = 12;
        const double rowHeight = 20;
        const double columnWidth = 56;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        var style = new CellStyle { ShrinkToFit = true };
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var text = $"Shrink text R{row.Row:D2} C{column.Col:D2} 1234567890";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new TextValue(text),
                    text,
                    null,
                    StyleId.Default,
                    null,
                    style));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static void RenderOnce(GridView grid, int width, int height)
    {
        grid.InvalidateVisual();
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
    }

    private static int CountAnchorRectsWithScans(
        ViewportModel viewport,
        IReadOnlyList<(CellAddress Anchor, double Width, double Height, double MinimumWidth, double MinimumHeight)> objects)
    {
        var count = 0;
        foreach (var item in objects)
        {
            if (GridDrawingObjectPlanner.TryCreateAnchoredObjectRect(
                    viewport,
                    item.Anchor,
                    GridView.RowHeaderWidth,
                    GridView.ColHeaderHeight,
                    item.Width,
                    item.Height,
                    item.MinimumWidth,
                    item.MinimumHeight,
                    out _))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountAnchorRectsWithLookups(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        IReadOnlyList<(CellAddress Anchor, double Width, double Height, double MinimumWidth, double MinimumHeight)> objects)
    {
        var count = 0;
        foreach (var item in objects)
        {
            if (GridDrawingObjectPlanner.TryCreateAnchoredObjectRect(
                    rows,
                    columns,
                    item.Anchor,
                    GridView.RowHeaderWidth,
                    GridView.ColHeaderHeight,
                    item.Width,
                    item.Height,
                    item.MinimumWidth,
                    item.MinimumHeight,
                    out _))
            {
                count++;
            }
        }

        return count;
    }

    private static void DrawFormulaTraceArrowsOnce(
        Action<DrawingContext, Point, Point, FormulaTraceArrowLayoutKind> drawArrow,
        IReadOnlyList<Point> starts,
        IReadOnlyList<Point> ends)
    {
        var group = new DrawingGroup();
        using var dc = group.Open();
        for (var i = 0; i < starts.Count; i++)
            drawArrow(dc, starts[i], ends[i], FormulaTraceArrowLayoutKind.VisibleArrow);
    }

    private static void SetResizeTarget(GridView grid, string target)
    {
        var field = typeof(GridView).GetField("_resizeTarget", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(GridView), "_resizeTarget");
        field.SetValue(grid, Enum.Parse(field.FieldType, target));
    }

    private struct CountingFormulaTraceArrowLayoutConsumer : IFormulaTraceArrowLayoutConsumer
    {
        public int Count { get; private set; }

        public void AcceptLayout(
            LayoutPoint start,
            LayoutPoint end,
            FormulaTraceArrowLayoutKind kind,
            CellAddress? navigationTarget,
            FormulaTraceArrowKind arrowKind) =>
            Count++;
    }

    private static class StaTestRunner
    {
        private static readonly Lazy<System.Windows.Threading.Dispatcher> StaDispatcher = new(CreateDispatcher);

        public static void Run(Action action)
        {
            Exception? exception = null;
            StaDispatcher.Value.Invoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            if (exception is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }

        private static System.Windows.Threading.Dispatcher CreateDispatcher()
        {
            System.Windows.Threading.Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ready.Wait();

            return dispatcher ?? throw new InvalidOperationException("STA dispatcher was not created.");
        }
    }
}
