using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class NewWorkbookFactoryTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(0, 1)]
    [InlineData(300, 255)]
    public void Create_HonorsNormalizedDefaultSheetCount(
        int defaultSheetCount,
        int expectedSheetCount)
    {
        var workbook = WorkbookFactory.Create(
            new WorkbookCreationOptions(DefaultSheetCount: defaultSheetCount));

        workbook.SheetCount.Should().Be(expectedSheetCount);
        workbook.Sheets
            .Select(sheet => sheet.Name)
            .Should()
            .Equal(Enumerable.Range(1, expectedSheetCount).Select(index => $"Sheet{index}"));
    }

    [Theory]
    [InlineData(" Aptos ", 14, "Aptos", 14)]
    [InlineData("", 0, "Calibri", 11)]
    [InlineData("Arial", 500, "Arial", 409)]
    public void Create_HonorsNormalizedDefaultFontOptions(
        string defaultFontName,
        int defaultFontSize,
        string expectedFontName,
        int expectedFontSize)
    {
        var workbook = WorkbookFactory.CreateFromAppOptions(new AppOptions
        {
            DefaultFontName = defaultFontName,
            DefaultFontSize = defaultFontSize,
            DefaultSheetCount = 2
        });

        var defaultStyle = workbook.GetStyle(StyleId.Default);

        workbook.SheetCount.Should().Be(2);
        defaultStyle.FontName.Should().Be(expectedFontName);
        defaultStyle.FontSize.Should().Be(expectedFontSize);
    }

    [Fact]
    public void Create_FromOptions_HonorsNormalizedUserNameMetadata()
    {
        var workbook = WorkbookFactory.CreateFromAppOptions(new AppOptions
        {
            UserName = "  Analyst  ",
            DefaultSheetCount = 1
        });

        workbook.FileSharing.Should().BeEquivalentTo(new WorkbookFileSharingModel
        {
            UserName = "Analyst"
        });
    }

    [Fact]
    public void AppAndFileNew_RouteFullOptionsIntoFactory()
    {
        var appSource = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");

        // K4: every MainWindow resolved from DI now gets its own document context, so the app-level
        // factory call resolves AppOptions straight from the service provider at the call site
        // rather than through a locally-bound "options" variable -- but it still routes the full
        // AppOptions object into the factory, not just DefaultSheetCount.
        appSource.Should().Contain("WorkbookFactory.CreateFromAppOptions(sp.GetRequiredService<AppOptions>())");
        // File > New now also threads the chosen workbook name through the factory, but still routes
        // the full options object (font, sheet count, user name) rather than only DefaultSheetCount.
        backstageSource.Should().Contain("WorkbookFactory.CreateFromAppOptions(_options, workbookName)");
        File.Exists(Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host",
            "NewWorkbookFactory.cs")).Should().BeFalse();
    }
}
