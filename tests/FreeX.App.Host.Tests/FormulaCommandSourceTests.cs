using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormulaCommandSourceTests
{
    [Theory]
    [InlineData("Insert Function", "F", "InsertFunctionBtn_Click")]
    [InlineData("AutoSum", "U", "FormulasAutoSumPickerBtn_Click")]
    [InlineData("Recently Used", "RU", "InsertFunctionBtn_Click")]
    [InlineData("Financial", "Y", "FormulaFinancialBtn_Click")]
    [InlineData("Logical", "L", "FormulaLogicalBtn_Click")]
    [InlineData("Text", "TF", "FormulaTextBtn_Click")]
    [InlineData("Date &amp; Time", "DT", "FormulaDateBtn_Click")]
    [InlineData("Lookup &amp; Reference", "K", "FormulaLookupBtn_Click")]
    [InlineData("Math &amp; Trig", "MT", "FormulaMathBtn_Click")]
    [InlineData("More Functions", "MF", "FormulaMoreBtn_Click")]
    public void FunctionLibraryCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractCommandElementByTitle(ReadMainWindowXaml(), title, handler);

        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Name Manager", "N", "NamedRangesButton_Click")]
    [InlineData("Define Name", "DN", "DefineNameBtn_Click")]
    [InlineData("Use in Formula", "I", "UseInFormulaBtn_Click")]
    [InlineData("Create from Selection", "CS", "CreateNamesFromSelectionBtn_Click")]
    public void DefinedNameCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractCommandElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"Content=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Sum", "S", "AutoSumSumMenuItem_Click")]
    [InlineData("Average", "A", "AutoSumAvgMenuItem_Click")]
    [InlineData("Count Numbers", "C", "AutoSumCountMenuItem_Click")]
    [InlineData("Count All", "T", "AutoSumCountAllMenuItem_Click")]
    [InlineData("Max", "X", "AutoSumMaxMenuItem_Click")]
    [InlineData("Min", "M", "AutoSumMinMenuItem_Click")]
    [InlineData("More Functions...", "F", "AutoSumMoreMenuItem_Click")]
    public void FormulaAutoSumMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var item = ExtractMenuItemElementByHeader(ReadMainWindowXaml(), header);

        item.Should().Contain($"Header=\"{header}\"");
        item.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        item.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void FormulaCommandHandlers_RouteThroughExpectedDialogsMenusAndServices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs"));

        source.Should().Contain("var dlg = new InsertFunctionDialog();");
        source.Should().Contain("FormulaBar.Text = \"=\" + dlg.SelectedFormula;");
        source.Should().Contain("new NamedRangeDialog(");
        source.Should().Contain("new CreateNamesFromSelectionDialog { Owner = this }");
        source.Should().Contain("new CreateNamedRangesFromSelectionCommand(");
        source.Should().Contain("FormulaInsertionService.InsertDefinedName(");
        source.Should().Contain("MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>())");
        source.Should().Contain("FormulaFinancialBtn_Click(object sender, RoutedEventArgs e) => OpenFormulaFunctionMenu(sender, [\"PMT\", \"NPV\", \"IRR\", \"RATE\", \"PV\", \"FV\"]);");
        source.Should().Contain("FormulaMoreBtn_Click(object sender, RoutedEventArgs e)    => InsertFunctionBtn_Click(sender, e);");
        source.Should().Contain("FormulaBar.Text = $\"={funcName}(\";");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractCommandElementByTitle(string xaml, string title, string? handler = null)
    {
        var searchIndex = 0;
        while (true)
        {
            var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", searchIndex, StringComparison.Ordinal);
            titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} formula command should be present");

            var start = xaml.LastIndexOf('<', titleIndex);
            while (start >= 0 &&
                   !xaml[start..].StartsWith("<Button", StringComparison.Ordinal) &&
                   !xaml[start..].StartsWith("<local:AutomationInvokeButton", StringComparison.Ordinal))
            {
                start = xaml.LastIndexOf('<', start - 1);
            }

            start.Should().BeGreaterThanOrEqualTo(0, $"the {title} formula command should be a Button or AutomationInvokeButton");

            var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
            var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
            var nextButton = xaml.IndexOf("<Button", titleIndex + 1, StringComparison.Ordinal);
            var end = closingEnd >= 0 && (nextButton < 0 || closingEnd < nextButton)
                ? closingEnd + "</Button>".Length
                : selfClosingEnd + 2;

            end.Should().BeGreaterThan(titleIndex, $"the {title} formula command should have a closing marker");
            var element = xaml[start..end];
            if (handler is null || element.Contains($"Click=\"{handler}\"", StringComparison.Ordinal))
                return element;

            searchIndex = end;
        }
    }

    private static string ExtractMenuItemElementByHeader(string xaml, string header)
    {
        var headerIndex = xaml.IndexOf($"Header=\"{header}\"", StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"the {header} formula menu item should be present");

        var start = xaml.LastIndexOf("<MenuItem", headerIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {header} formula menu item should be a MenuItem");

        var end = xaml.IndexOf("/>", headerIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThan(headerIndex, $"the {header} formula menu item should be self-closing");
        return xaml[start..(end + 2)];
    }
}
