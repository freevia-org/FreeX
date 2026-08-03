using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaCompactDialogChromeClusterASourceTests
{
    [Fact]
    public void ResidualClusterADialogs_DelegateCompactControlChromeToSharedHelper()
    {
        var evaluateSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.EvaluateFormula.cs"));
        var commentsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Comments.cs"));
        var insertDeleteSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.InsertDeleteCells.cs"));
        var rowColumnSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.RowColumnVisibility.cs"));
        var proofingSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Proofing.cs"));
        var spellingSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Spelling.cs"));
        var sparklineSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Sparklines.cs"));
        var textToColumnsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TextToColumns.cs"));

        evaluateSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle EvaluateFormulaDialogChromeStyle =>");
        evaluateSource.Should().Contain("ActionSpacing = EvaluateFormulaDialogPlanner.ActionSpacing");
        evaluateSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");
        evaluateSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, EvaluateFormulaDialogChromeStyle);");
        evaluateSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, EvaluateFormulaDialogChromeStyle, width, isDefault);");
        evaluateSource.Should().Contain("formulaText.Inlines!.Clear();");

        commentsSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle CommentDialogChromeStyle => new(FormulaBarFontFamily);");
        commentsSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(box, CommentDialogChromeStyle, fixedHeight: false);");
        commentsSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 10, 0, 0))");

        insertDeleteSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle InsertDeleteCellsDialogChromeStyle => new(FormulaBarFontFamily);");
        insertDeleteSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(first, InsertDeleteCellsDialogChromeStyle);");
        insertDeleteSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0))");

        rowColumnSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle RowColumnDialogChromeStyle => new(FormulaBarFontFamily);");
        rowColumnSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(valueBox, RowColumnDialogChromeStyle);");
        rowColumnSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyValidationStatus(validationText, RowColumnDialogChromeStyle);");
        rowColumnSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([cancelButton, okButton]);");

        proofingSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle ProofingDialogChromeStyle => new(FormulaBarFontFamily);");
        proofingSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(list, ProofingDialogChromeStyle);");
        proofingSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(fromBox, ProofingDialogChromeStyle);");
        proofingSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(translationBox, ProofingDialogChromeStyle, fixedHeight: false);");
        proofingSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([insert, close])");
        proofingSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel])");

        spellingSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle SpellingDialogChromeStyle => new(FormulaBarFontFamily);");
        spellingSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyListBox(suggestionList, SpellingDialogChromeStyle);");
        spellingSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(replacementBox, SpellingDialogChromeStyle);");
        spellingSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");
        spellingSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton])");

        sparklineSource.Should().Contain("private static AvaloniaCompactDialogChromeStyle SparklineDialogChromeStyle =>");
        sparklineSource.Should().Contain("ButtonHeight = 20,");
        sparklineSource.Should().Contain("ComboBoxHeight = 22,");
        sparklineSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 16, 0, 0))");
        sparklineSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(box, SparklineDialogChromeStyle);");
        sparklineSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(button, SparklineDialogChromeStyle, width, isDefault);");
        sparklineSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(textBox, SparklineDialogChromeStyle);");
        sparklineSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, SparklineDialogChromeStyle);");
        sparklineSource.Should().Contain("Width = 190,");
        sparklineSource.Should().Contain("ApplySparklineButtonChrome(selectDataRangeButton, 132);");
        sparklineSource.Should().Contain("ApplySparklineButtonChrome(selectLocationRangeButton, 152);");
        sparklineSource.Should().Contain("typeBox.Width = 333;");
        sparklineSource.Should().Contain("typeBox.Margin = new Thickness(-1, 0, 0, 0);");
        sparklineSource.Should().Contain("HorizontalAlignment = AvaloniaHorizontalAlignment.Left,");
        sparklineSource.Should().Contain("Margin = new Thickness(0, 3, 0, 1),");
        sparklineSource.Should().Contain("row.Margin = new Thickness(0, 0, 0, 13);");
        sparklineSource.Should().Contain("row.ClipToBounds = true;");
        sparklineSource.Should().Contain("ApplySparklineButtonChrome(ok, 72, isDefault: true);");
        sparklineSource.Should().Contain("ApplySparklineButtonChrome(cancel, 72);");
        sparklineSource.Should().Contain("button.CornerRadius = new CornerRadius(0);");
        sparklineSource.Should().Contain("textBox.CornerRadius = new CornerRadius(0);");
        sparklineSource.Should().Contain("comboBox.CornerRadius = new CornerRadius(0);");

        textToColumnsSource.Should().Contain("using Free.Shared.Shell.Avalonia;");
        textToColumnsSource.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow(");
        textToColumnsSource.Should().Contain("[backButton, nextButton, applyButton, cancelButton]");

        foreach (var source in new[]
                 {
                     evaluateSource,
                     commentsSource,
                     insertDeleteSource,
                     rowColumnSource,
                     proofingSource,
                     spellingSource,
                     sparklineSource,
                     textToColumnsSource,
                 })
        {
            source.Should().Contain("using Free.Shared.Shell.Avalonia;");
            source.Should().NotContain("ApplyDialogButtonChrome(");
            source.Should().NotContain("button.Height = 24;");
            source.Should().NotContain("button.Padding = new Thickness(4, 1);");
            source.Should().NotContain("BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);");
        }

        sparklineSource.Should().NotContain("ApplyDataToolsButtonChrome");
        sparklineSource.Should().NotContain("ApplyDataToolsTextBoxChrome");
        sparklineSource.Should().NotContain("ApplyDataToolsComboBoxChrome");
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
