using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class FormatFidelityHarnessSourceTests
{
    [Fact]
    public void Solution_IncludesFormatFidelityToolProject()
    {
        var solution = TestWorkspaceFiles.ReadRepoText("FreeX.slnx");

        solution.Should().Contain("tools/FreeX.FormatFidelity/FreeX.FormatFidelity.csproj");
    }

    [Fact]
    public void RebuiltXlsxChain_DetachesSourcePackageBeforeSaving()
    {
        var chainRunner = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FormatFidelity", "ChainRunner.cs");
        var adapter = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");

        chainRunner.Should().Contain("string.Equals(hop.ProfileKey, \"xlsx-rebuilt\", StringComparison.OrdinalIgnoreCase)");
        chainRunner.Should().Contain("XlsxFileAdapter.DetachSourcePackage(current)");
        adapter.Should().Contain("public static void DetachSourcePackage(Workbook workbook)");
        adapter.Should().Contain("SourcePackages.Remove(workbook);");
    }

    [Fact]
    public void VbaPresence_IsLoadedIntoWorkbookAndCapturedByFormatFidelity()
    {
        var workbook = TestWorkspaceFiles.ReadCoreModelRepoSource("Workbook.cs");
        var adapter = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var snapshot = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FormatFidelity", "WorkbookSnapshot.cs");

        workbook.Should().Contain("public bool HasVbaProjectPackage { get; set; }");
        adapter.Should().Contain("HasVbaProjectPackage");
        adapter.Should().Contain("archive.GetEntry(\"xl/vbaProject.bin\") is not null");
        snapshot.Should().Contain("HasVba = wb.HasVbaProjectPackage");
    }

    [Fact]
    public void LossyScalarDrops_AreReportedAsExpectedLossNotOk()
    {
        var comparer = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FormatFidelity", "DimensionComparer.cs");

        comparer.Should().Contain("cap == Cap.Lossy && gotVal < refVal");
        comparer.Should().Contain("ResultKind.ExpectedLoss");
        comparer.Should().NotContain("cap == Cap.Lossy && gotVal <= refVal) ok = true");
    }
}
