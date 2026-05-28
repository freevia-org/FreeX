using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FreexcelOptionsPersistenceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "FreexcelOptionsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadFromPath_WhenJsonIsInvalid_ReturnsDefaultsWithObservableError()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "options.json");
        File.WriteAllText(path, "{ not-json");

        var options = FreexcelOptions.LoadFromPath(path);

        options.DefaultFormat.Should().Be(".xlsx");
        options.LastPersistenceError.Should().Contain("Failed to load options");
        options.LastPersistenceError.Should().Contain(path);
    }

    [Fact]
    public void SaveToPath_WhenTargetCannotBeWritten_ReturnsFalseWithObservableError()
    {
        Directory.CreateDirectory(_tempDirectory);
        var options = new FreexcelOptions();

        var saved = options.SaveToPath(_tempDirectory);

        saved.Should().BeFalse();
        options.LastPersistenceError.Should().Contain("Failed to save options");
        options.LastPersistenceError.Should().Contain(_tempDirectory);
    }

    [Fact]
    public void SaveToPath_WritesAtomicallyAndClearsPreviousError()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "options.json");
        var options = new FreexcelOptions
        {
            DefaultFormat = ".fxl"
        };

        options.SaveToPath(_tempDirectory).Should().BeFalse();
        options.SaveToPath(path).Should().BeTrue();

        options.LastPersistenceError.Should().BeNull();
        JsonDocument.Parse(File.ReadAllText(path))
            .RootElement.GetProperty(nameof(FreexcelOptions.DefaultFormat))
            .GetString()
            .Should()
            .Be(".fxl");
        Directory.EnumerateFiles(_tempDirectory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void FreexcelOptions_DoesNotUseDebugWriteLineForPersistenceFailures()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FreexcelOptions.cs"));

        source.Should().NotContain("Debug.WriteLine");
        source.Should().Contain(nameof(FreexcelOptions.LastPersistenceError));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
