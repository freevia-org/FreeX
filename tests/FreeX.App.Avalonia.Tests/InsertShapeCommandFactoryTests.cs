using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="InsertShapeCommandFactory"/>: the default shape, the common-shapes
/// catalog, and that the built command adds a drawing shape to the sheet on apply. No running shell required.
/// </summary>
public sealed class InsertShapeCommandFactoryTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void DefaultShape_IsRectangle() =>
        InsertShapeCommandFactory.DefaultShape.Should().Be(DrawingShapeKind.Rectangle);

    [Fact]
    public void Catalog_IsNonEmpty_AndAllKindsAreDefined()
    {
        InsertShapeCommandFactory.Catalog.Should().NotBeEmpty();
        InsertShapeCommandFactory.Catalog.Should().OnlyContain(item => Enum.IsDefined(item.Kind));
        InsertShapeCommandFactory.Catalog.Select(i => i.Label).Should().OnlyContain(l => !string.IsNullOrWhiteSpace(l));
    }

    [Theory]
    [InlineData(DrawingShapeKind.Rectangle)]
    [InlineData(DrawingShapeKind.Ellipse)]
    [InlineData(DrawingShapeKind.Line)]
    [InlineData(DrawingShapeKind.Star5)]
    public void Build_AddsShapeOfRequestedKind_OnApply(DrawingShapeKind kind)
    {
        var workbook = new Workbook("Shapes");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var command = InsertShapeCommandFactory.Build(sheet.Id, anchor, kind);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.DrawingShapes.Should().ContainSingle();
        var shape = sheet.DrawingShapes[0];
        shape.Kind.Should().Be(kind);
        shape.Anchor.Should().Be(anchor);
        shape.Width.Should().Be(InsertShapeCommandFactory.DefaultWidth);
        shape.Height.Should().Be(InsertShapeCommandFactory.DefaultHeight);
    }
}
