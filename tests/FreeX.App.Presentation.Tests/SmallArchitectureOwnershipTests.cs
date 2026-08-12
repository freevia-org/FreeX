using FluentAssertions;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests;

public sealed class SmallArchitectureOwnershipTests
{
    [Fact]
    public void PageSetup_UsesOneHeaderFooterSettingsContract()
    {
        typeof(PageSetupDialogFields).GetProperty(nameof(PageSetupDialogFields.HeaderFooter))
            .Should().NotBeNull();
        typeof(PageSetupDialogSurfaceInput).GetProperty(nameof(PageSetupDialogSurfaceInput.HeaderFooter))
            .Should().NotBeNull();
        typeof(PageSetupCommandRequest).GetProperty(nameof(PageSetupCommandRequest.HeaderFooter))!
            .PropertyType.Should().Be(typeof(HeaderFooterEditorState));

        var duplicatedNames = new[]
        {
            "HeaderPictures",
            "FooterPictures",
            "FirstPageHeaderPictures",
            "FirstPageFooterPictures",
            "EvenPageHeaderPictures",
            "EvenPageFooterPictures",
            "DifferentFirstPage",
            "DifferentOddEvenPages",
        };
        typeof(PageSetupDialogFields).GetProperties().Select(property => property.Name)
            .Should().NotIntersectWith(duplicatedNames);
        typeof(PageSetupDialogSurfaceInput).GetProperties().Select(property => property.Name)
            .Should().NotIntersectWith(duplicatedNames);
    }

    [Fact]
    public void FileDialogFacade_IsRemoved()
    {
        typeof(FreeX.Core.IO.FileFormatResolver).Assembly
            .GetType("FreeX.Core.IO.FileDialogFilterBuilder")
            .Should().BeNull();
    }
}
