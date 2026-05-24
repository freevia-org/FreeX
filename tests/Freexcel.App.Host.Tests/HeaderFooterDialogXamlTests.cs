using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class HeaderFooterDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeysForOptionsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "_Header preset:", "HeaderPresetBox");
        AssertLabelTargets(document, presentation, "_Footer preset:", "FooterPresetBox");
        AssertLabelTargets(document, presentation, "Header _left:", "HeaderLeftBox");
        AssertLabelTargets(document, presentation, "Header _center:", "HeaderCenterBox");
        AssertLabelTargets(document, presentation, "Header _right:", "HeaderRightBox");
        AssertLabelTargets(document, presentation, "Footer l_eft:", "FooterLeftBox");
        AssertLabelTargets(document, presentation, "Footer c_enter:", "FooterCenterBox");
        AssertLabelTargets(document, presentation, "Footer r_ight:", "FooterRightBox");
        AssertLabelTargets(document, presentation, "First header _left:", "FirstHeaderLeftBox");
        AssertLabelTargets(document, presentation, "First header _center:", "FirstHeaderCenterBox");
        AssertLabelTargets(document, presentation, "First header _right:", "FirstHeaderRightBox");
        AssertLabelTargets(document, presentation, "First footer le_ft:", "FirstFooterLeftBox");
        AssertLabelTargets(document, presentation, "First footer cent_er:", "FirstFooterCenterBox");
        AssertLabelTargets(document, presentation, "First footer righ_t:", "FirstFooterRightBox");
        AssertLabelTargets(document, presentation, "Even header le_ft:", "EvenHeaderLeftBox");
        AssertLabelTargets(document, presentation, "Even header ce_nter:", "EvenHeaderCenterBox");
        AssertLabelTargets(document, presentation, "Even header rig_ht:", "EvenHeaderRightBox");
        AssertLabelTargets(document, presentation, "Even footer lef_t:", "EvenFooterLeftBox");
        AssertLabelTargets(document, presentation, "Even footer cent_er:", "EvenFooterCenterBox");
        AssertLabelTargets(document, presentation, "Even footer rig_ht:", "EvenFooterRightBox");

        document.Descendants(presentation + "CheckBox")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain([
                "_Different first page",
                "Different _odd and even pages",
                "_Scale with document",
                "_Align with page margins"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element =>
                    element.Attribute("Content")?.Value == content &&
                    element.Attribute("Target")?.Value == $"{{Binding ElementName={target}}}");

            label.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData("_Page number", "&[Page]")]
    [InlineData("Number of pa_ges", "&[Pages]")]
    [InlineData("_Date", "&[Date]")]
    [InlineData("_Time", "&[Time]")]
    [InlineData("File _path", "&[Path]&[File]")]
    [InlineData("File _name", "&[File]")]
    [InlineData("_Sheet name", "&[Tab]")]
    [InlineData("P_icture", "&[Picture]")]
    [InlineData("For_mat picture", "&[Picture]")]
    public void Dialog_ExposesExcelHeaderFooterTokenButtons(string label, string token)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document.Descendants(presentation + "Button").ToList();

        buttons.Select(element => element.Attribute("Content")?.Value).Should().Contain(label);
        buttons.Select(element => element.Attribute("Tag")?.Value).Should().Contain(token);
    }

    [Fact]
    public void Dialog_ExposesHeaderFooterPresets()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Descendants(presentation + "ComboBox")
            .Select(element => element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value)
            .Should()
            .Contain(["HeaderPresetBox", "FooterPresetBox"]);

        var headerPresets = GetPresetContents(document, presentation, x, "HeaderPresetBox");
        var footerPresets = GetPresetContents(document, presentation, x, "FooterPresetBox");

        headerPresets.Should().Contain(["Book1.xlsx, Sheet1", "Confidential, Page 1", "Date, Page 1", "File path"]);
        footerPresets.Should().Contain(["Book1.xlsx, Sheet1", "Time", "Date, Page 1", "File name"]);
    }

    [Fact]
    public void PictureButtons_UseDedicatedPictureHandlers()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        var source = ReadHeaderFooterDialogSource();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "P_icture")
            .Attribute("Click")?.Value
            .Should()
            .Be("PictureButton_Click");
        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "For_mat picture")
            .Attribute("Click")?.Value
            .Should()
            .Be("FormatPictureButton_Click");
        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "For_mat picture")
            .Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value
            .Should()
            .Be("FormatPictureButton");
        document.Descendants(presentation + "TextBlock")
            .Any(element => element.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "PictureTargetStatusText"))
            .Should().BeTrue();

        source.Should().Contain("new OpenFileDialog");
        source.Should().Contain("HeaderFooterPictureFormatDialog");
        source.Should().Contain("SetPictureForActiveBox");
        source.Should().Contain("UpdatePictureButtonState");
        source.Should().Contain("Insert a picture in");
    }

    [Fact]
    public void FormatPictureButton_TracksActiveSectionPictureState()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Sheet(SheetId.New(), "Sheet1")
            {
                PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
                    Left: null,
                    Center: new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", 120, 48),
                    Right: null)
            };
            var dialog = new HeaderFooterDialog(sheet);
            dialog.Show();
            try
            {
                var button = GetControl<Button>(dialog, "FormatPictureButton");
                var status = GetControl<TextBlock>(dialog, "PictureTargetStatusText");
                button.IsEnabled.Should().BeTrue();
                status.Text.Should().Be("Target: center section has a picture.");

                GetControl<TextBox>(dialog, "HeaderLeftBox").Focus();

                button.IsEnabled.Should().BeFalse();
                status.Text.Should().Be("Target: left section has no picture.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PictureFormatDialog_ExposesExcelLikeSizeControls()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("private readonly CheckBox _lockAspectRatioBox");
        source.Should().Contain("Content = \"_Lock aspect ratio\"");
        source.Should().Contain("Content = \"_Reset\"");
        source.Should().Contain("CalculateLockedAspectHeight");
        source.Should().Contain("CalculateLockedAspectWidth");
        source.Should().Contain("DialogButtonRowFactory.Create(Accept, 72)");
        source.Should().NotContain("InsertChartDialog.CreateButtonRow(Accept)");
    }

    [Fact]
    public void HeaderFooterDialogsOpenedFromKeyboard_FocusInitialTextFields()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("HeaderCenterBox.Focus();");
        source.Should().Contain("HeaderCenterBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(HeaderCenterBox);");
        source.Should().Contain("_widthBox.Focus();");
        source.Should().Contain("_widthBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_widthBox);");
    }

    [Fact]
    public void PictureFormatDialogInvalidSize_RefocusesAndSelectsInvalidSizeBox()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("FocusInvalidSizeInput();");
        source.Should().Contain("private void FocusInvalidSizeInput()");
        source.Should().Contain("FocusAndSelect(string.IsNullOrWhiteSpace(_widthBox.Text) ? _widthBox : _heightBox);");
        source.Should().Contain("private static void FocusAndSelect(TextBox box)");
        source.Should().Contain("Keyboard.Focus(box);");
    }

    [Fact]
    public void PictureFormatDialog_CalculatesLockedAspectSize()
    {
        HeaderFooterPictureFormatDialog.CalculateLockedAspectHeight(200, originalWidth: 100, originalHeight: 50)
            .Should()
            .Be(100);
        HeaderFooterPictureFormatDialog.CalculateLockedAspectWidth(75, originalWidth: 100, originalHeight: 50)
            .Should()
            .Be(150);
    }

    [Fact]
    public void OptionalFirstAndEvenSections_AreEnabledOnlyWhenTheirOptionsAreChecked()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("DifferentFirstPageBox.Checked += (_, _) => RefreshOptionalSectionState()");
        source.Should().Contain("DifferentOddEvenBox.Checked += (_, _) => RefreshOptionalSectionState()");
        source.Should().Contain("SetControlsEnabled(firstEnabled");
        source.Should().Contain("FirstHeaderLeftBox");
        source.Should().Contain("SetControlsEnabled(evenEnabled");
        source.Should().Contain("EvenFooterRightBox");
        source.Should().Contain("_activeTextBox = HeaderCenterBox");
    }

    [Fact]
    public void FirstAndEvenHeadersAndFooters_UseSectionBoxesWithoutPipeParsing()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        var source = ReadHeaderFooterDialogSource();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[]
        {
            "FirstHeaderLeftBox",
            "FirstHeaderCenterBox",
            "FirstHeaderRightBox",
            "FirstFooterLeftBox",
            "FirstFooterCenterBox",
            "FirstFooterRightBox",
            "EvenHeaderLeftBox",
            "EvenHeaderCenterBox",
            "EvenHeaderRightBox",
            "EvenFooterLeftBox",
            "EvenFooterCenterBox",
            "EvenFooterRightBox"
        })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist so first/even pages keep left/center/right sections");
        }

        foreach (var oldFlattenedName in new[] { "FirstHeaderBox", "FirstFooterBox", "EvenHeaderBox", "EvenFooterBox" })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == oldFlattenedName)
                .Should().BeFalse($"{oldFlattenedName} loses literal pipe characters and should be replaced");
        }

        source.Should().NotContain("Split('|'");
        source.Should().NotContain("ToCombinedText");
        source.Should().NotContain("FromCombinedText");
        source.Should().Contain("FirstPageHeader = new WorksheetHeaderFooter(");
        source.Should().Contain("EvenPageFooter = new WorksheetHeaderFooter(");
    }

    [Fact]
    public void InsertToken_InsertsAtCaret()
    {
        HeaderFooterDialog.InsertToken("Page  of", caretIndex: 5, "&[Page]").Should().Be("Page &[Page] of");
    }

    [Fact]
    public void OkButton_RemovesHeaderFooterPicturesWhenPictureTokenIsDeleted()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Sheet(SheetId.New(), "Sheet1")
            {
                PageHeader = new WorksheetHeaderFooter("", "&[Picture]", ""),
                PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
                    Left: null,
                    Center: new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", 120, 48),
                    Right: null),
                PageFooter = new WorksheetHeaderFooter("&[Picture]", "", ""),
                PageFooterPictures = new WorksheetHeaderFooterPictureSet(
                    Left: new WorksheetHeaderFooterPicture([4, 5, 6], "image/png", "footer.png", 80, 40),
                    Center: null,
                    Right: null)
            };
            var dialog = new HeaderFooterDialog(sheet);
            dialog.Show();
            try
            {
                GetControl<TextBox>(dialog, "HeaderCenterBox").Text = "";

                InvokePrivateAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.HeaderPictures.Center.Should().BeNull();
                dialog.FooterPictures.Left.Should().NotBeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static IReadOnlyList<string?> GetPresetContents(
        XDocument document,
        XNamespace presentation,
        XNamespace x,
        string comboBoxName) =>
        document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == comboBoxName)
            .Elements(presentation + "ComboBoxItem")
            .Select(element => element.Attribute("Content")?.Value)
            .ToList();

    private static T GetControl<T>(HeaderFooterDialog dialog, string name)
        where T : class
    {
        var field = typeof(HeaderFooterDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static string ReadHeaderFooterDialogSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "HeaderFooterDialog.xaml.cs",
            "HeaderFooterPictureFormatDialog.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", file))));

    private static void InvokePrivateAllowingNonModalDialogResult(HeaderFooterDialog dialog, string methodName)
    {
        var method = typeof(HeaderFooterDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation &&
                                                   invalidOperation.Message.Contains("DialogResult", StringComparison.Ordinal))
        {
        }
    }
}
