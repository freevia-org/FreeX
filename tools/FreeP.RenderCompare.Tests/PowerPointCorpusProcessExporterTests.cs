namespace FreeP.RenderCompare.Tests;

public sealed class PowerPointCorpusProcessExporterTests
{
    [Fact]
    public void BuildStartInfo_uses_dll_prefix_only_for_dotnet_host_and_preserves_paths()
    {
        var startInfo = PowerPointCorpusProcessExporter.BuildStartInfo(
            @"C:\Corpus With Spaces\deck.pptx",
            @"C:\Output With Spaces",
            1280,
            720,
            @"C:\Output With Spaces\result.json",
            processPath: @"C:\Program Files\dotnet\dotnet.exe",
            entryAssemblyPath: @"C:\Render Compare\FreeP.RenderCompare.dll");

        startInfo.FileName.Should().EndWith("dotnet.exe");
        startInfo.ArgumentList.Should().Equal(
            @"C:\Render Compare\FreeP.RenderCompare.dll",
            "--powerpoint-export-one",
            @"C:\Corpus With Spaces\deck.pptx",
            @"C:\Output With Spaces",
            "--width",
            "1280",
            "--height",
            "720",
            "--result",
            @"C:\Output With Spaces\result.json");
    }

    [Fact]
    public void BuildStartInfo_does_not_add_dll_prefix_for_compiled_executable()
    {
        var startInfo = PowerPointCorpusProcessExporter.BuildStartInfo(
            "deck.pptx",
            "out",
            1,
            2,
            "result.json",
            processPath: @"C:\Tools\FreeP.RenderCompare.exe",
            entryAssemblyPath: @"C:\Tools\FreeP.RenderCompare.dll");

        startInfo.ArgumentList[0].Should().Be("--powerpoint-export-one");
        PowerPointCorpusProcessExporter.IsDotnetHost(startInfo.FileName).Should().BeFalse();
    }

    [Fact]
    public void EnsureOutputDirectory_creates_missing_directory_for_direct_exports()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeP.RenderCompare.Tests-");
        var root = temporaryDirectory.Path;
        var expected = Path.GetFullPath(Path.Combine(root, "slides"));

        PowerPointInterop.EnsureOutputDirectory(expected).Should().Be(expected);
        Directory.Exists(expected).Should().BeTrue();
    }
}
