using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class QuickAccessToolbarCustomizationFileTests
{
    [Fact]
    public void Serialize_WritesFreeXOwnedJsonWithNormalizedCommands()
    {
        var json = QuickAccessToolbarCustomizationFile.Serialize(
            [
                QuickAccessToolbarCommandIds.Save,
                "bold",
                "MissingCommand",
                QuickAccessToolbarCommandIds.Bold
            ],
            quickAccessToolbarBelowRibbon: true);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("format").GetString().Should().Be(QuickAccessToolbarCustomizationFile.FileFormat);
        root.GetProperty("version").GetInt32().Should().Be(QuickAccessToolbarCustomizationFile.CurrentVersion);
        root.GetProperty("quickAccessToolbarBelowRibbon").GetBoolean().Should().BeTrue();
        root.GetProperty("commands")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Equal(QuickAccessToolbarCommandIds.Save, QuickAccessToolbarCommandIds.Bold);
    }

    [Fact]
    public void TryDeserialize_NormalizesKnownCommandsAndDropsDuplicates()
    {
        var json = """
            {
              "format": "FreeX.QuickAccessToolbarCustomization",
              "version": 1,
              "quickAccessToolbarBelowRibbon": true,
              "commands": [ "save", "Bold", "SAVE", "Undo" ]
            }
            """;

        var result = QuickAccessToolbarCustomizationFile.TryDeserialize(json);

        result.Success.Should().BeTrue();
        result.Customization.Should().NotBeNull();
        result.Customization!.QuickAccessToolbarBelowRibbon.Should().BeTrue();
        result.Customization.CommandIds.Should().Equal(
            QuickAccessToolbarCommandIds.Save,
            QuickAccessToolbarCommandIds.Bold,
            QuickAccessToolbarCommandIds.Undo);
    }

    [Fact]
    public void TryDeserialize_RejectsMixedUnknownCommandIdsInsteadOfSilentlyDroppingThem()
    {
        var json = """
            {
              "format": "FreeX.QuickAccessToolbarCustomization",
              "version": 1,
              "quickAccessToolbarBelowRibbon": true,
              "commands": [ "Save", "MissingCommand", "Bold", "missingcommand", "FutureCommand" ]
            }
            """;

        var result = QuickAccessToolbarCustomizationFile.TryDeserialize(json);

        result.Success.Should().BeFalse();
        result.Customization.Should().BeNull();
        result.ErrorMessage.Should().Contain("cannot add");
        result.ErrorMessage.Should().Contain("MissingCommand");
        result.ErrorMessage.Should().Contain("FutureCommand");
    }

    [Theory]
    [InlineData("""{ "format": "Office.CustomUI", "version": 1, "commands": [ "Save" ] }""", "not a FreeX")]
    [InlineData("""{ "format": "FreeX.QuickAccessToolbarCustomization", "version": 2, "commands": [ "Save" ] }""", "Unsupported")]
    [InlineData("""{ "format": "FreeX.QuickAccessToolbarCustomization", "version": 1, "commands": [ "Missing" ] }""", "cannot add")]
    [InlineData("""{ "format": "FreeX.QuickAccessToolbarCustomization", "version": 1 }""", "does not list any")]
    [InlineData("""{ """, "not valid FreeX")]
    public void TryDeserialize_RejectsInvalidCustomizationFiles(string json, string expectedErrorFragment)
    {
        var result = QuickAccessToolbarCustomizationFile.TryDeserialize(json);

        result.Success.Should().BeFalse();
        result.Customization.Should().BeNull();
        result.ErrorMessage.Should().Contain(expectedErrorFragment);
    }

    [Fact]
    public void TrySaveAndTryLoad_RoundTripThroughLocalJsonFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreeXQatCustomizationTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "toolbar.freex-qat.json");
        try
        {
            QuickAccessToolbarCustomizationFile.TrySave(
                path,
                [QuickAccessToolbarCommandIds.Open, QuickAccessToolbarCommandIds.Save],
                quickAccessToolbarBelowRibbon: false,
                out var errorMessage)
                .Should()
                .BeTrue(errorMessage);

            var result = QuickAccessToolbarCustomizationFile.TryLoad(path);

            result.Success.Should().BeTrue();
            result.Customization.Should().NotBeNull();
            result.Customization!.QuickAccessToolbarBelowRibbon.Should().BeFalse();
            result.Customization.CommandIds.Should().Equal(
                QuickAccessToolbarCommandIds.Open,
                QuickAccessToolbarCommandIds.Save);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
