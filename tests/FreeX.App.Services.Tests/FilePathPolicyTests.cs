using FluentAssertions;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Shell;

namespace FreeX.App.Services.Tests;

public sealed class FilePathPolicyTests
{
    [Fact]
    public void TryGetFullPath_TrimsAndNormalizesRelativePath()
    {
        FilePathPolicy.TryGetFullPath("  reports/../Budget.xlsx  ", out var fullPath)
            .Should()
            .BeTrue();

        fullPath.Should().Be(Path.GetFullPath("Budget.xlsx"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Budget")]
    [InlineData("folder/")]
    [InlineData("bad\0Budget.xlsx")]
    public void TryGetExtension_RejectsMissingOrUnusableExtension(string? path)
    {
        FilePathPolicy.TryGetExtension(path, out var extension).Should().BeFalse();
        extension.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Budget.xlsx", ".xlsx")]
    [InlineData("reports/Quarterly.PPTX", ".PPTX")]
    [InlineData("../drafts/Letter.DoCx", ".DoCx")]
    public void TryGetExtension_PreservesExtensionCaseAndAcceptsRelativePaths(
        string path,
        string expected)
    {
        FilePathPolicy.TryGetExtension(path, out var extension).Should().BeTrue();
        extension.Should().Be(expected);
    }

    [Fact]
    public void TryChangeExtension_IsExceptionFreeAndPreservesMalformedInput()
    {
        FilePathPolicy.TryChangeExtension("report.output", ".pdf", out var changed).Should().BeTrue();
        changed.Should().Be("report.pdf");

        const string malformed = "bad\0report.output";
        FilePathPolicy.TryChangeExtension(malformed, ".pdf", out changed).Should().BeFalse();
        changed.Should().Be(malformed);
    }

    [Fact]
    public void FileNameHelpers_UsePortableFallbacksForDirectoriesAndInvalidText()
    {
        FilePathPolicy.FileNameOrPath("reports/Quarterly Review.pptx")
            .Should()
            .Be("Quarterly Review.pptx");
        FilePathPolicy.FileNameOrPath("reports/").Should().Be("reports/");
        FilePathPolicy.FileNameOrPath("bad\0report.pptx").Should().Be("bad\0report.pptx");
        FilePathPolicy.FileNameWithoutExtensionOr("reports/Quarterly Review.pptx", "Untitled")
            .Should()
            .Be("Quarterly Review");
        FilePathPolicy.FileNameWithoutExtensionOr("   ", "Untitled").Should().Be("Untitled");
    }

    [Fact]
    public void AreEquivalent_NormalizesRelativeSegmentsAndUsesPlatformCaseRules()
    {
        FilePathPolicy.AreEquivalent("reports/../Budget.xlsx", "Budget.xlsx").Should().BeTrue();

        FilePathPolicy.AreEquivalent("Budget.xlsx", "BUDGET.xlsx")
            .Should()
            .Be(OperatingSystem.IsWindows());
    }

    [Fact]
    public void BackstageFolderIdentity_UsesTheEstablishedPlatformComparer()
    {
        var entries = new[]
        {
            new RecentFileEntry { Path = Path.Combine("root", "Docs", "one.docx") },
            new RecentFileEntry { Path = Path.Combine("root", "docs", "two.docx") },
        };

        var rows = BackstageRecentActionRowsPlanner.BuildFolderRows(
            entries,
            maxRows: 10,
            _ => { });

        rows.Should().HaveCount(OperatingSystem.IsWindows() ? 1 : 2);
    }

    [Fact]
    public void CrossAppFileWorkflows_ConsumeSharedPathPolicy()
    {
        var sources = new[]
        {
            RepositoryFileLocator.Find("src", "FreeX.Core.IO", "FileSavePlanner.cs"),
            RepositoryFileLocator.Find("src", "FreeX.App.Services", "WorkbookOpenTargetPlanner.cs"),
            RepositoryFileLocator.Find("freew", "FreeW.App.Presentation", "Shell", "DocumentPersistenceWorkflow.cs"),
            RepositoryFileLocator.Find("freep", "FreeP.App.Presentation", "PresentationFilePersistenceWorkflow.cs"),
        };

        foreach (var sourcePath in sources)
        {
            var source = File.ReadAllText(sourcePath);
            source.Should().Contain("FilePathPolicy");
            source.Should().NotContain("private static bool TryGetExtension");
        }

        var root = Path.GetDirectoryName(RepositoryFileLocator.Find("FreeX.slnx"))!;
        File.Exists(Path.Combine(root, "shared", "Free.Shared.Shell", "PlannerPathHelpers.cs"))
            .Should()
            .BeFalse();
    }
}
