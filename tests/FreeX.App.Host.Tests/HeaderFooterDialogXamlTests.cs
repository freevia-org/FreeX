using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Xml.Linq;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HeaderFooterDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeysForOptionsAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("HeaderFooterDialog.xaml");
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

    [Fact]
    public void Dialog_UsesHeaderFooterTabsAndResponsiveChrome()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("HeaderFooterDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document.Root!.Attribute("ResizeMode")?.Value.Should().Be("CanResize");
        document.Root!.Attribute("MinWidth")?.Value.Should().NotBeNullOrWhiteSpace();
        document.Root!.Attribute("MinHeight")?.Value.Should().NotBeNullOrWhiteSpace();

        var tabs = document.Descendants(presentation + "TabControl")
            .Single(element => element.Attribute(x + "Name")?.Value == "HeaderFooterTabs");
        tabs.Elements(presentation + "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Contain(["Header", "Footer"]);

        document.Descendants(presentation + "StackPanel")
            .SingleOrDefault(element => element.Attribute("Grid.Column")?.Value == "1")
            .Should()
            .BeNull("the insert commands should no longer live in a tall right-side button column");
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
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("HeaderFooterDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document.Descendants(presentation + "Button").ToList();

        buttons.Select(element => element.Attribute("Content")?.Value).Should().Contain(label);
        buttons.Select(element => element.Attribute("Tag")?.Value).Should().Contain(token);
    }

    [Fact]
    public void Dialog_ExposesHeaderFooterPresets()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("HeaderFooterDialog.xaml");
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
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("HeaderFooterDialog.xaml");
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

        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("UiText.Get(\"HeaderFooterPicture_InsertPictureTitle\")");
        source.Should().Contain("UiText.Get(\"HeaderFooterPicture_OpenFileFilter\")");
        source.Should().NotContain("new OpenFileDialog");
        source.Should().Contain("HeaderFooterPictureFormatDialog");
        source.Should().Contain("SetPictureForActiveBox");
        source.Should().Contain("UpdatePictureButtonState");
        source.Should().Contain("HeaderFooterEditorPlanner.ContainsPictureToken(");
        source.Should().Contain("HeaderFooterEditorPlanner.GetPicture(");
        source.Should().Contain("HeaderFooterEditorPlanner.SetPicture(");
        source.Should().Contain("HeaderFooterEditorPlanner.ScopeLabelResourceKey(");
        source.Should().NotContain("private const string PictureToken");
        source.Should().NotContain("private static WorksheetHeaderFooterPictureSet PrunePicturesWithoutTokens(");
        source.Should().Contain("UiText.Format(\"HeaderFooterPicture_FormatPictureToolTip\", ActiveBoxLabel(target))");
        source.Should().Contain("UiText.Format(\"HeaderFooterPicture_InsertBeforeFormattingToolTip\"");
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
                var button = DialogSourceTestSupport.GetPrivateField<Button>(dialog, "FormatPictureButton");
                var status = DialogSourceTestSupport.GetPrivateField<TextBlock>(dialog, "PictureTargetStatusText");
                button.IsEnabled.Should().BeTrue();
                status.Text.Should().Be("Target: Header center section has a picture.");

                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "HeaderLeftBox").Focus();

                button.IsEnabled.Should().BeFalse();
                status.Text.Should().Be("Target: Header left section has no picture.");
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
        source.Should().Contain("Content = UiText.Get(\"FormatPicture_LockAspectRatio\")");
        source.Should().Contain("Content = UiText.Get(\"HeaderFooterPicture_ResetButton\")");
        source.Should().Contain("HeaderFooterPictureFormatPlanner.CreateState(");
        source.Should().Contain("HeaderFooterPictureFormatPlanner.ResetSize(_pictureState)");
        source.Should().Contain("CalculateLockedAspectHeight");
        source.Should().Contain("CalculateLockedAspectWidth");
        source.Should().Contain("DialogButtonRowFactory.Create(Accept, 72)");
        source.Should().NotContain("InsertChartDialog.CreateButtonRow(Accept)");
    }

    [Fact]
    public void FormatPictureWithoutPicture_ReturnsFocusToActiveSection()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("FocusActiveTextBox();");
        source.Should().Contain("private void FocusActiveTextBox()");
        source.Should().Contain("var target = GetActiveTextBox();");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("Keyboard.Focus(target);");
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
        source.Should().Contain("FocusSizeInput(_pictureState.InitialFocusField);");
        source.Should().Contain("private void FocusSizeInput(ObjectSizeDialogField field)");
        source.Should().Contain("DialogFocus.FocusAndSelect(field == ObjectSizeDialogField.Width ? _widthBox : _heightBox);");
    }

    [Fact]
    public void PictureFormatDialogInvalidSize_RefocusesAndSelectsInvalidSizeBox()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("HeaderFooterPictureFormatPlanner.TryCreateResult(");
        source.Should().Contain("FocusSizeInput(invalidField);");
        source.Should().Contain("private void FocusSizeInput(ObjectSizeDialogField field)");
        source.Should().Contain("DialogFocus.FocusAndSelect(field == ObjectSizeDialogField.Width ? _widthBox : _heightBox);");
        source.Should().NotContain("private static void FocusAndSelect(TextBox box)");
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
    public void PictureFormatDialog_SizeControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", 120, 48);
            var dialog = new HeaderFooterPictureFormatDialog(picture);
            try
            {
                var widthBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_widthBox");
                AutomationProperties.GetName(widthBox).Should().Be("Header/footer picture width");
                AutomationProperties.GetAutomationId(widthBox).Should().Be("HeaderFooterPictureWidthBox");
                AutomationProperties.GetHelpText(widthBox).Should().Be("Enter the header or footer picture width.");

                var heightBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_heightBox");
                AutomationProperties.GetName(heightBox).Should().Be("Header/footer picture height");
                AutomationProperties.GetAutomationId(heightBox).Should().Be("HeaderFooterPictureHeightBox");
                AutomationProperties.GetHelpText(heightBox).Should().Be("Enter the header or footer picture height.");

                var lockAspectRatioBox = DialogSourceTestSupport.GetPrivateField<CheckBox>(dialog, "_lockAspectRatioBox");
                AutomationProperties.GetName(lockAspectRatioBox).Should().Be("Lock aspect ratio");
                AutomationProperties.GetAutomationId(lockAspectRatioBox).Should().Be("HeaderFooterPictureLockAspectRatioCheckBox");
                AutomationProperties.GetHelpText(lockAspectRatioBox).Should().Be("Keep the header or footer picture width and height proportional.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PictureFormatDialog_ResetButtonExposesAutomationMetadata()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("AutomationProperties.SetName(resetButton, UiText.Get(\"HeaderFooterPicture_ResetSizeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(resetButton, \"HeaderFooterPictureResetSizeButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(resetButton, UiText.Get(\"HeaderFooterPicture_ResetSizeHelpText\"));");
    }

    [Fact]
    public void OptionalFirstAndEvenSections_AreEnabledOnlyWhenTheirOptionsAreChecked()
    {
        var source = ReadHeaderFooterDialogSource();

        source.Should().Contain("DifferentFirstPageBox.Checked += (_, _) => RefreshOptionalSectionState()");
        source.Should().Contain("DifferentOddEvenBox.Checked += (_, _) => RefreshOptionalSectionState()");
        source.Should().Contain("FirstPageHeaderGroup.Visibility = firstEnabled ? Visibility.Visible : Visibility.Collapsed;");
        source.Should().Contain("FirstPageFooterGroup.Visibility = firstEnabled ? Visibility.Visible : Visibility.Collapsed;");
        source.Should().Contain("EvenPageHeaderGroup.Visibility = evenEnabled ? Visibility.Visible : Visibility.Collapsed;");
        source.Should().Contain("EvenPageFooterGroup.Visibility = evenEnabled ? Visibility.Visible : Visibility.Collapsed;");
        source.Should().Contain("SetControlsEnabled(firstEnabled");
        source.Should().Contain("FirstHeaderLeftBox");
        source.Should().Contain("SetControlsEnabled(evenEnabled");
        source.Should().Contain("EvenFooterRightBox");
        source.Should().Contain("HeaderFooterEditorPlanner.CoerceToEnabledTarget(");
        source.Should().Contain("CoerceActiveTextBox(_activeTextBox)");
    }

    [Fact]
    public void OptionalFirstAndEvenSections_AreCollapsedUntilTheirOptionsAreChecked()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new HeaderFooterDialog(new Sheet(SheetId.New(), "Sheet1"));
            dialog.Show();
            try
            {
                var firstHeaderGroup = DialogSourceTestSupport.GetPrivateField<GroupBox>(dialog, "FirstPageHeaderGroup");
                var firstFooterGroup = DialogSourceTestSupport.GetPrivateField<GroupBox>(dialog, "FirstPageFooterGroup");
                var evenHeaderGroup = DialogSourceTestSupport.GetPrivateField<GroupBox>(dialog, "EvenPageHeaderGroup");
                var evenFooterGroup = DialogSourceTestSupport.GetPrivateField<GroupBox>(dialog, "EvenPageFooterGroup");

                firstHeaderGroup.Visibility.Should().Be(Visibility.Collapsed);
                firstFooterGroup.Visibility.Should().Be(Visibility.Collapsed);
                evenHeaderGroup.Visibility.Should().Be(Visibility.Collapsed);
                evenFooterGroup.Visibility.Should().Be(Visibility.Collapsed);

                DialogSourceTestSupport.GetPrivateField<CheckBox>(dialog, "DifferentFirstPageBox").IsChecked = true;
                DialogSourceTestSupport.GetPrivateField<CheckBox>(dialog, "DifferentOddEvenBox").IsChecked = true;

                firstHeaderGroup.Visibility.Should().Be(Visibility.Visible);
                firstFooterGroup.Visibility.Should().Be(Visibility.Visible);
                evenHeaderGroup.Visibility.Should().Be(Visibility.Visible);
                evenFooterGroup.Visibility.Should().Be(Visibility.Visible);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void InsertToolbar_TargetsTheSelectedHeaderFooterTabCenterByDefault()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new HeaderFooterDialog(new Sheet(SheetId.New(), "Sheet1"));
            dialog.Show();
            try
            {
                var tabs = DialogSourceTestSupport.GetPrivateField<TabControl>(dialog, "HeaderFooterTabs");
                var footerTab = DialogSourceTestSupport.GetPrivateField<TabItem>(dialog, "FooterTab");
                var headerCenter = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "HeaderCenterBox");
                var footerCenter = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "FooterCenterBox");

                tabs.SelectedItem = footerTab;
                DialogSourceTestSupport.InvokePrivateHandler(
                    dialog,
                    "InsertTokenButton_Click",
                    new Button { Tag = "&[Page]" });

                footerCenter.Text.Should().Be("&[Page]");
                headerCenter.Text.Should().BeEmpty();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void FirstAndEvenHeadersAndFooters_UseSectionBoxesWithoutPipeParsing()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("HeaderFooterDialog.xaml");
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
        source.Should().Contain("new WorksheetHeaderFooter(FirstHeaderLeftBox.Text");
        source.Should().Contain("new WorksheetHeaderFooter(EvenFooterLeftBox.Text");
    }

    [Fact]
    public void InsertToken_InsertsAtCaret()
    {
        HeaderFooterEditorPlanner.InsertToken("Page  of", caretIndex: 5, "&[Page]").Should().Be("Page &[Page] of");
        ReadHeaderFooterDialogSource().Should().Contain("HeaderFooterEditorPlanner.InsertToken(");
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
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "HeaderCenterBox").Text = "";

                InvokePrivateAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.HeaderPictures.Center.Should().BeNull();
                dialog.FooterPictures.Left.Should().NotBeNull();
                ReadHeaderFooterDialogSource().Should().Contain(".PrunePicturesWithoutTokens();");
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

    private static string ReadHeaderFooterDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "HeaderFooterDialog.xaml.cs",
            "HeaderFooterDialog.TextHelpers.cs",
            "HeaderFooterDialog.Result.cs",
            "HeaderFooterDialog.Pictures.cs",
            "HeaderFooterPictureFormatDialog.cs");

    private static void InvokePrivateAllowingNonModalDialogResult(HeaderFooterDialog dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, methodName);
}
