using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaClusterBDialogChromeSourceTests
{
    [Fact]
    public void PivotTableAndPictureDialogs_DelegateResidualChromeToSharedHelper()
    {
        var pivotOptionsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotOptions.cs"));
        var pivotStyleGallerySource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotStyleGallery.cs"));
        var calcFieldSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotCalculatedField.cs"));
        var calcItemSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotCalculatedItem.cs"));
        var tableStyleGallerySource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableStyleGallery.cs"));
        var pictureShapeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PictureShapeTabs.cs"));

        pivotOptionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, PivotDialogChromeStyle);");
        pivotOptionsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(listBox, PivotDialogChromeStyle);");
        pivotStyleGallerySource.Should().Contain("ApplyPivotListBoxChrome(gallery);");
        pivotStyleGallerySource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));");
        calcFieldSource.Should().Contain("ApplyPivotTextBoxChrome(formulaBox, fixedHeight: false);");
        calcFieldSource.Should().Contain("ApplyPivotListBoxChrome(fieldsList);");
        calcFieldSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([save, delete, cancel]");
        calcItemSource.Should().Contain("ApplyPivotTextBoxChrome(formulaBox, fixedHeight: false);");
        calcItemSource.Should().Contain("ApplyPivotListBoxChrome(fieldsList);");
        calcItemSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([save, delete, cancel]");
        tableStyleGallerySource.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(gallery, TableStyleGalleryChromeStyle);");
        tableStyleGallerySource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0));");
        pictureShapeSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle PictureShapeDialogChromeStyle => new(FormulaBarFontFamily);");
        pictureShapeSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(input, PictureShapeDialogChromeStyle, fixedHeight: !multiline);");
        pictureShapeSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyValidationStatus(warning, PictureShapeDialogChromeStyle);");
        pictureShapeSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton])");

        AssertNoLocalButtonChrome(tableStyleGallerySource);
        AssertNoLocalButtonChrome(pictureShapeSource);
        AssertNoLocalTextBoxChrome(pictureShapeSource, "input");
    }

    private static void AssertNoLocalButtonChrome(string source)
    {
        source.Should().NotContain("button.Height = 24;");
        source.Should().NotContain("button.MinHeight = 24;");
        source.Should().NotContain("button.MaxHeight = 24;");
        source.Should().NotContain("button.Padding = new Thickness(4, 1);");
        source.Should().NotContain("button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
    }

    private static void AssertNoLocalTextBoxChrome(string source, string variableName)
    {
        source.Should().NotContain($"{variableName}.Height = 24;");
        source.Should().NotContain($"{variableName}.MinHeight = 24;");
        source.Should().NotContain($"{variableName}.MaxHeight = 24;");
        source.Should().NotContain($"{variableName}.Padding = new Thickness(4, 1);");
        source.Should().NotContain($"{variableName}.BorderBrush = Brush(130, 130, 130);");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
