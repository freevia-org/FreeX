using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
    [Fact]
    public void HyperlinkDialog_CreateResult_UsesTargetAsDisplayTextWhenLabelIsBlank()
    {
        var result = HyperlinkDialog.CreateResult("https://example.test", " ");

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.ExistingFileOrWebPage,
            "https://example.test",
            "https://example.test",
            "",
            ""));
    }

    [Fact]
    public void HyperlinkDialog_CreateResult_TrimsScreenTipAndBookmarkMetadata()
    {
        var result = HyperlinkDialog.CreateResult(
            " Sheet1!A1 ",
            " Jump ",
            HyperlinkLinkType.PlaceInThisDocument,
            "  Open budget cell  ",
            "  BudgetAnchor  ");

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.PlaceInThisDocument,
            "Sheet1!A1",
            "Jump",
            "Open budget cell",
            "BudgetAnchor"));
    }

    [Theory]
    [InlineData(HyperlinkLinkType.ExistingFileOrWebPage, "Enter an address.")]
    [InlineData(HyperlinkLinkType.CreateNewDocument, "Enter a new document name.")]
    [InlineData(HyperlinkLinkType.PlaceInThisDocument, "Enter a valid cell reference or defined name.")]
    [InlineData(HyperlinkLinkType.EmailAddress, "Enter an email address.")]
    public void HyperlinkDialog_TryCreateResult_RejectsBlankTarget(HyperlinkLinkType linkType, string expectedError)
    {
        HyperlinkDialog.TryCreateResult(" ", "Label", linkType, "", "", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expectedError);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("review@")]
    [InlineData("@example.test")]
    [InlineData("review@example test")]
    public void HyperlinkDialog_TryCreateResult_RejectsInvalidEmailTarget(string target)
    {
        HyperlinkDialog.TryCreateResult(target, "Label", HyperlinkLinkType.EmailAddress, "", "", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a valid email address.");
    }

    [Theory]
    [InlineData("review@example.test", "mailto:review@example.test")]
    [InlineData("mailto:review@example.test", "mailto:review@example.test")]
    public void HyperlinkDialog_TryCreateResult_AcceptsEmailTarget(string target, string expectedTarget)
    {
        HyperlinkDialog.TryCreateResult(target, "Label", HyperlinkLinkType.EmailAddress, "", "", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Target.Should().Be(expectedTarget);
    }

    [Theory]
    [InlineData("review@example.test", "mailto:review@example.test", "review@example.test")]
    [InlineData("mailto:review@example.test?subject=Budget", "mailto:review@example.test?subject=Budget", "review@example.test")]
    public void HyperlinkDialog_CreateResult_NormalizesEmailTargetWithoutLeakingMailtoIntoBlankDisplay(
        string target,
        string expectedTarget,
        string expectedDisplayText)
    {
        var result = HyperlinkDialog.CreateResult(target, " ", HyperlinkLinkType.EmailAddress);

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.EmailAddress,
            expectedTarget,
            expectedDisplayText,
            "",
            ""));
    }

    [Fact]
    public void HyperlinkDialog_TryCreateResult_AcceptsTrimmedTargetAndMetadata()
    {
        HyperlinkDialog.TryCreateResult(
                " https://example.test ",
                " Example ",
                HyperlinkLinkType.ExistingFileOrWebPage,
                " Tip ",
                " Bookmark ",
                out var result,
                out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.ExistingFileOrWebPage,
            "https://example.test",
            "Example",
            "Tip",
            "Bookmark"));
    }

    [Fact]
    public void HyperlinkNavigationPlanner_CreatesExternalLaunchPlanForWebLink()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var sheet = new Sheet(sheetId, "Sheet1");
        sheet.Hyperlinks[address] = "https://example.test";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();
        plan.Should().Be(new HyperlinkNavigationPlan(HyperlinkNavigationKind.External, "https://example.test", null));
    }

    [Fact]
    public void HyperlinkNavigationPlanner_CreatesWorksheetPlanForDocumentLink()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var sheet = new Sheet(sheetId, "Sheet1");
        sheet.Hyperlinks[address] = "Sheet2!C3";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();
        plan!.Kind.Should().Be(HyperlinkNavigationKind.WorksheetCell);
        plan.Target.Should().Be("Sheet2!C3");
    }

    [Fact]
    public void HyperlinkDialog_LabelsTextRowsWithAccessKeyTargets()
    {
        var source = ReadObjectDialogSources();

        source.Should().Contain("AddTextRow(grid, 0, UiText.Get(\"Hyperlink_TextToDisplay2\"), _displayBox, displayText)");
        source.Should().Contain("AddTextRow(grid, 1, UiText.Get(\"Hyperlink_Address\"), _targetBox, target)");
        source.Should().Contain("new Label");
        source.Should().Contain("Content = label");
        source.Should().Contain("Target = box");
    }

    [Fact]
    public void HyperlinkDialog_ExposesExcelLikeLinkTypeAndScreenTipAffordances()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "HyperlinkDialog.cs",
            "TextEntryDialogs.cs");

        source.Should().Contain("UiText.Get(\"Hyperlink_LinkTypeExistingFileOrWebPage\")");
        source.Should().Contain("UiText.Get(\"Hyperlink_LinkTypeCreateNewDocument\")");
        source.Should().Contain("UiText.Get(\"Hyperlink_LinkTypePlaceInThisDocument\")");
        source.Should().Contain("UiText.Get(\"Hyperlink_LinkTypeEmailAddress\")");
        source.Should().Contain("_screenTipButton");
        source.Should().Contain("_bookmarkButton");
        source.Should().Contain("Content = UiText.Get(\"Hyperlink_ScreenTip\")");
        source.Should().Contain("Content = UiText.Get(\"Hyperlink_Bookmark\")");
        source.Should().Contain("ScreenTipDialog");
        source.Should().Contain("BookmarkDialog");
        source.Should().Contain("_screenTipButton.Click +=");
        source.Should().Contain("_bookmarkButton.Click +=");
        source.Should().Contain("HyperlinkDialogPlanner.LinkTypeColumnWidth");
        source.Should().Contain("HyperlinkDialogPlanner.LabelColumnWidth");
        source.Should().Contain("HyperlinkDialogPlanner.ActionButtonWidth");
    }

    [Fact]
    public void HyperlinkDialog_LabelsLinkTypeListWithAccessKeyTarget()
    {
        var source = DialogSourceTestSupport.ReadHostSources("HyperlinkDialog.cs");

        source.Should().Contain("new Label { Content = UiText.Get(\"Hyperlink_LinkTo\"), Target = _linkTypes");
        source.Should().Contain("AutomationProperties.SetName(_linkTypes, UiText.Get(\"Hyperlink_LinkTo2\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_linkTypes, \"HyperlinkLinkTypeList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_linkTypes, UiText.Get(\"Hyperlink_ChooseTheKindOfHyperlinkToInsert\"));");
    }

    [Fact]
    public void HyperlinkDialog_TextEditorsExposeAutomationNames()
    {
        var source = DialogSourceTestSupport.ReadHostSources("HyperlinkDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_displayBox, UiText.Get(\"Hyperlink_TextToDisplay\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_displayBox, \"HyperlinkDisplayTextBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_displayBox, UiText.Get(\"Hyperlink_EnterTheTextShownInTheCellForTheHyperlink\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_targetBox, \"HyperlinkTargetTextBox\");");
        source.Should().Contain("AutomationProperties.SetName(_targetBox, automationName);");
    }

    [Theory]
    [InlineData(0, "Hyperlink_Address", "Hyperlink_AddressAutomationName", "Hyperlink_AddressHelpText")]
    [InlineData(1, "Hyperlink_NewDocumentLabel", "Hyperlink_NewDocumentAutomationName", "Hyperlink_NewDocumentHelpText")]
    [InlineData(2, "Hyperlink_CellReferenceLabel", "Hyperlink_CellReferenceAutomationName", "Hyperlink_CellReferenceHelpText")]
    [InlineData(3, "Hyperlink_EmailAddressLabel", "Hyperlink_EmailAddressAutomationName", "Hyperlink_EmailAddressHelpText")]
    public void HyperlinkDialog_TargetFieldTracksSelectedLinkType(
        int selectedIndex,
        string expectedLabelKey,
        string expectedAutomationNameKey,
        string expectedHelpTextKey)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new HyperlinkDialog();
            try
            {
                var linkTypes = GetField<ListBox>(dialog, "_linkTypes");
                var targetLabel = GetField<Label>(dialog, "_targetLabel");
                var targetBox = GetField<TextBox>(dialog, "_targetBox");

                linkTypes.SelectedIndex = selectedIndex;

                targetLabel.Content.Should().Be(UiText.Get(expectedLabelKey));
                targetLabel.Target.Should().BeSameAs(targetBox);
                AutomationProperties.GetName(targetBox).Should().Be(UiText.Get(expectedAutomationNameKey));
                AutomationProperties.GetAutomationId(targetBox).Should().Be("HyperlinkTargetTextBox");
                AutomationProperties.GetHelpText(targetBox).Should().Be(UiText.Get(expectedHelpTextKey));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void HyperlinkDialog_ScreenTipAndBookmarkButtonsExposeAutomationMetadata()
    {
        var source = DialogSourceTestSupport.ReadHostSources("HyperlinkDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_screenTipButton, UiText.Get(\"Hyperlink_SetScreenTip\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_screenTipButton, \"HyperlinkScreenTipButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_screenTipButton, UiText.Get(\"Hyperlink_SetTheTextShownWhenPointingToTheHyperlink\"));");
        source.Should().Contain("AutomationProperties.SetName(_bookmarkButton, UiText.Get(\"Hyperlink_SelectPlaceInDocument\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_bookmarkButton, \"HyperlinkBookmarkButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_bookmarkButton, UiText.Get(\"Hyperlink_ChooseABookmarkDefinedNameOrCellReferenceInThisWorkbook\"));");
    }

    [Fact]
    public void HyperlinkTextEntryDialogs_NameEntryBoxFromAccessKeyLabel()
    {
        var source = DialogSourceTestSupport.ReadHostSources("TextEntryDialogs.cs");

        source.Should().Contain("AutomationProperties.SetName(_textBox, automationName ?? CreateAutomationName(label));");
        source.Should().Contain("label.Replace(\"_\", string.Empty, StringComparison.Ordinal)");
        source.Should().Contain(".Replace(\":\", string.Empty, StringComparison.Ordinal)");
    }

    [Fact]
    public void HyperlinkTextEntryDialogs_ExposeStableAutomationIdsAndHelpText()
    {
        var source = DialogSourceTestSupport.ReadHostSources("TextEntryDialogs.cs");

        source.Should().Contain("AutomationProperties.SetAutomationId(_textBox, automationId ?? CreateAutomationId(title));");
        source.Should().Contain("AutomationProperties.SetHelpText(_textBox, helpText ?? CreateHelpText(label));");
        source.Should().Contain("\"SetHyperlinkScreenTipTextBox\"");
        source.Should().Contain("\"SelectPlaceinDocumentTextBox\"");
        source.Should().Contain("UiText.Get(\"Hyperlink_ScreenTipTextAutomationName\")");
        source.Should().Contain("UiText.Get(\"Hyperlink_BookmarkOrCellReferenceAutomationName\")");
        source.Should().Contain("string.Concat(title.Where(char.IsLetterOrDigit)) + \"TextBox\"");
        source.Should().Contain("$\"Enter {CreateAutomationName(label).ToLowerInvariant()}.\"");
    }

    [Fact]
    public void HyperlinkDialog_AcceptWarnsAndRefocusesBlankTarget()
    {
        var source = ReadClassSource("HyperlinkDialog.cs", "public sealed class HyperlinkDialog", "");

        source.Should().Contain("DialogButtonRowFactory.Create(Accept, HyperlinkDialogPlanner.ActionButtonWidth)");
        source.Should().Contain("if (!TryCreateResult(_targetBox.Text, _displayBox.Text, SelectedLinkType, _screenTip, _bookmark, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? UiText.Get(\"Hyperlink_EnterHyperlinkDetails\"));");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, _targetBox);");
    }

    [Fact]
    public void HyperlinkDialogOpenedFromKeyboard_FocusesAddressBox()
    {
        var source = ReadClassSource("HyperlinkDialog.cs", "public sealed class HyperlinkDialog", "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_targetBox);");
    }
}
