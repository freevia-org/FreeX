using System.IO;
using System.Text.RegularExpressions;

namespace FreeW.App.Avalonia.Tests;

public sealed class DialogChromeDedupSourceGuardTests
{
    [Fact]
    public void ResidualAvaloniaDialogs_DelegateCompactChromeToSharedHelper()
    {
        var expectations = new (string FileName, string[] RequiredSnippets)[]
        {
            ("AutosaveAdapter.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyButton(yes, DialogChromeStyle, minWidth: 82, isDefault: true);",
                "AvaloniaCompactDialogChrome.CreateActionRow([yes, no], new Thickness(16, 0, 16, 16));",
            ]),
            ("CellEditDialog.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyTextBox(_box, DialogChromeStyle);",
                "AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 10, 0, 0));",
            ]),
            ("DesignDialogs.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyComboBox(_style, InsertDialogLayout.ChromeStyle);",
                "AvaloniaCompactDialogChrome.ApplyCheckBox(_semitransparent, InsertDialogLayout.ChromeStyle);",
                "AvaloniaCompactDialogChrome.CreateActionRow([okButton, noneButton, cancelButton], new Thickness(14, 12, 14, 12));",
            ]),
            ("FindReplaceDialog.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyTextBox(_findBox, DialogChromeStyle);",
                "AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_matchCase, DialogChromeStyle);",
                "AvaloniaCompactDialogChrome.ApplyButton(btn, DialogChromeStyle, minWidth: 84);",
                "AvaloniaCompactDialogChrome.CreateActionRow(",
            ]),
            ("FontDialog.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);",
                "FontParagraphDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);",
                "AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle",
                "AvaloniaCompactDialogChrome.CreateOkCancelRow(",
            ]),
            ("FontParagraphDialogChrome.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyTextBox(textBox, style);",
                "AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, style);",
                "AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, style);",
            ]),
            ("InsertDialogs.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "public static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;",
                "AvaloniaCompactDialogChrome.ApplyTextBox(_displayBox, InsertDialogLayout.ChromeStyle);",
                "AvaloniaCompactDialogChrome.ApplyButton(btn, ChromeStyle, minWidth: 84);",
                "AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(14, 12, 14, 12));",
            ]),
            ("MailMergeDialogs.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyTextBox(editor, DialogChromeStyle, fixedHeight: false);",
                "AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);",
                "AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 10, 16, 14));",
            ]),
            ("PageSetupDialog.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "PageLayoutDialogChrome.Configure(this, PageSetupDialogPlanner.Title",
                "PageLayoutDialogChrome.NumberBox(",
                "PageLayoutDialogChrome.Combo(",
                "AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle",
                "PageLayoutDialogChrome.Actions(",
            ]),
            ("PictureFormattingDialogs.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "ImageSizeDialogPlanner.BuildInitialState(",
                "ImageSizeDialogPlanner.TryBuildResult(",
                "ImageBorderDialogPlanner.BuildInitialState(",
                "ImageBorderDialogPlanner.TryBuildResult(",
                "AvaloniaCompactDialogChrome.ApplyTextBox(",
                "AvaloniaCompactDialogChrome.ApplyComboBox(",
                "AvaloniaCompactDialogChrome.ApplyCheckBox(",
                "AvaloniaCompactDialogChrome.ApplyValidationStatus(",
                "AvaloniaCompactDialogChrome.CreateActionRow(",
            ]),
            ("ParagraphDialog.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "PageLayoutDialogChrome.Configure(this, \"Paragraph\"",
                "PageLayoutDialogChrome.NumberBox(",
                "PageLayoutDialogChrome.Combo(",
                "PageLayoutDialogChrome.Actions(",
            ]),
            ("PageLayoutDialogs.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyTextBox(box, style ?? Style);",
                "AvaloniaCompactDialogChrome.ApplyComboBox(combo, style ?? Style);",
                "AvaloniaDialogButtonRowFactory.CreateOkCancel(",
            ]),
            ("WordCountDialog.cs",
            [
                "using Free.Shared.Shell.Avalonia;",
                "AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);",
                "AvaloniaCompactDialogChrome.CreateActionRow([ok], new Thickness(16, 12, 16, 14));",
            ]),
        };

        foreach (var (fileName, requiredSnippets) in expectations)
        {
            var source = ReadAvaloniaSource(fileName);

            foreach (var snippet in requiredSnippets)
                source.Should().Contain(snippet, $"{fileName} should reuse the shared compact dialog chrome");

            AssertNoLocalCompactChrome(source, fileName);
        }
    }

    // r122 fixed ManualHyphenationDialog's hand-rolled action-row StackPanel, but the
    // ResidualAvaloniaDialogs_DelegateCompactChromeToSharedHelper guard above only scans a
    // fixed allowlist of files, so the identical drift was free to persist (and did) in any
    // Avalonia dialog file the allowlist didn't happen to name. This test closes that gap by
    // scanning the whole freew/FreeW.App.Avalonia tree for the same pattern, the same way
    // FreeW_dialog_chrome_uses_the_shared_Windows_authority_font already does a directory-wide
    // scan for the WPF-authority-font drift.
    [Fact]
    public void R123_NoAvaloniaDialogSourceHandRollsTheActionRowStackPanel_TreeWide()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sourceRoot = Path.Combine(root, "freew", "FreeW.App.Avalonia");
        var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);

        sourceFiles.Should().NotBeEmpty();

        var offenders = sourceFiles
            .Where(path => HandRolledActionRowPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .ToArray();

        offenders.Should().BeEmpty(
            "every Avalonia dialog action row should delegate to AvaloniaCompactDialogChrome.CreateActionRow " +
            "instead of hand-rolling the Orientation=Horizontal/HorizontalAlignment=Right StackPanel it replaces");
    }

    // Whitespace-insensitive so it catches the pattern regardless of the enclosing method's
    // indentation depth (the fixed-allowlist guard's literal string match did not).
    private static readonly Regex HandRolledActionRowPattern = new(
        @"new\s+StackPanel\s*\{\s*Orientation\s*=\s*Orientation\.Horizontal\s*,\s*HorizontalAlignment\s*=\s*HorizontalAlignment\.Right\s*,",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // No-regression sibling: plenty of legitimate Avalonia rows (e.g. CupsPrintDialog's
    // labeled "Printer:"/"Copies:" rows, ParagraphCommandDialogs' sort-key type row) are
    // horizontal StackPanels that are NOT action rows -- they have no HorizontalAlignment.Right.
    // The tree-wide scan must not flag those, or the guard becomes noisy and gets muted/deleted.
    [Fact]
    public void R123_HandRolledActionRowRegex_DoesNotFlagUnrelatedHorizontalStackPanels()
    {
        const string labeledRow =
            "var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { label, control } };";

        HandRolledActionRowPattern.IsMatch(labeledRow).Should().BeFalse(
            "a horizontal StackPanel without HorizontalAlignment.Right (e.g. a labeled field row) is not the action-row drift pattern");
    }

    [Fact]
    public void Shared_tab_chrome_removes_the_header_body_gap()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        source.Should().Contain("ContentPresenter.PaddingProperty, new Thickness(0)");
        source.Should().Contain("TabItem.MarginProperty,");
        source.Should().Contain("new Thickness(0, 0, -DialogTabChromeMetrics.AdjacentTabOverlap, 0)");
        source.Should().Contain("TabItem.BorderThicknessProperty,");
        source.Should().Contain("DialogTabChromeMetrics.PaneBorderThickness,");
        source.Should().Contain("var tabHeight = style.TabHeight ?? style.ControlHeight;");
        source.Should().Contain("Layoutable.MinHeightProperty, tabHeight");
        source.Should().Contain("TabItem.PaddingProperty, new Thickness(6, 2)");
    }

    [Fact]
    public void FreeW_dialog_chrome_uses_the_shared_Windows_authority_font()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sourceRoot = Path.Combine(root, "freew", "FreeW.App.Avalonia");
        var dialogSources = Directory
            .GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                Path.DirectorySeparatorChar + "Editing" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        dialogSources.Should().NotBeEmpty();
        dialogSources.Should().OnlyContain(source =>
            !source.Contains("new(FontFamily.Default)", StringComparison.Ordinal)
            && !source.Contains("new(global::Avalonia.Media.FontFamily.Default)", StringComparison.Ordinal),
            "dialog chrome must use AvaloniaCompactDialogChrome.WindowsStyle so Linux resolves the WPF-authority font family");
    }

    private static void AssertNoLocalCompactChrome(string source, string fileName)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        normalized.Should().NotContain(
            "new StackPanel\n        {\n            Orientation = Orientation.Horizontal,\n            HorizontalAlignment = HorizontalAlignment.Right,",
            $"{fileName} should use AvaloniaCompactDialogChrome.CreateActionRow for action rows");
        source.Should().NotContain("Margin = new Thickness(8, 0, 0, 0)", $"{fileName} should let CreateActionRow own button spacing");
        source.Should().NotContain("Padding = new Thickness(6, 3, 6, 3)", $"{fileName} should let ApplyButton own button padding");
        source.Should().NotContain(
            "Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x00, 0x00))",
            $"{fileName} should let ApplyValidationStatus own validation status chrome");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Avalonia", fileName);
        return File.ReadAllText(path);
    }

}
