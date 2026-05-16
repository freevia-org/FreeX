using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AltTextTargetResolverTests
{
    [Fact]
    public void Resolve_ReturnsObjectAnchoredAtSelectedCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 3);
        var picture = new PictureModel
        {
            Anchor = anchor,
            AltText = "Existing"
        };
        sheet.Pictures.Add(picture);

        var target = AltTextTargetResolver.Resolve(sheet, anchor);

        target.Should().NotBeNull();
        target!.Kind.Should().Be(AltTextObjectKind.Picture);
        target.Id.Should().Be(picture.Id);
        target.AltText.Should().Be("Existing");
    }

    [Fact]
    public void Resolve_ReturnsNullWhenSelectionHasNoAnchoredObject()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 3)
        });

        var target = AltTextTargetResolver.Resolve(sheet, new CellAddress(sheet.Id, 5, 5));

        target.Should().BeNull("Alt Text should not silently edit the last object on the sheet");
    }

    [Fact]
    public void Resolve_HonorsPreferredKindForGroupedSheetTargets()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 3);
        sheet.Pictures.Add(new PictureModel { Anchor = anchor });
        var textBox = new TextBoxModel
        {
            Anchor = anchor,
            Text = "Callout",
            AltText = "Text box alt"
        };
        sheet.TextBoxes.Add(textBox);

        var target = AltTextTargetResolver.Resolve(sheet, anchor, AltTextObjectKind.TextBox);

        target.Should().NotBeNull();
        target!.Kind.Should().Be(AltTextObjectKind.TextBox);
        target.Id.Should().Be(textBox.Id);
    }
}
