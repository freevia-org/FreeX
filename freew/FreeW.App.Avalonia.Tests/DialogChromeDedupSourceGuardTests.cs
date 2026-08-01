using System.IO;

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
        source.Should().Contain("TabItem.MarginProperty, new Thickness(0, 0, -1, -1)");
        source.Should().Contain("TabItem.BorderThicknessProperty, new Thickness(1, 1, 1, 0)");
        source.Should().Contain("Layoutable.MinHeightProperty, style.TabHeight ?? style.ControlHeight");
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
