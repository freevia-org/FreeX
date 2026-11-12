using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO.RecipientSources;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The WPF surfaces of Word's Select Recipients &gt; Use an Existing List / Type a New List and Edit
/// Recipient List: the dialogs project their presentation sessions, and the ribbon routes to them.
/// </summary>
public sealed class MailMergeRecipientSourceHostTests
{
    private static T Find<T>(DependencyObject root, string automationId) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(element =>
            System.Windows.Automation.AutomationProperties.GetAutomationId(element) == automationId);

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static string[] Headers(DataGrid grid) =>
        grid.Columns.Select(column => ((TextBlock)column.Header).Text).ToArray();

    private static MergeData People() => new(
        ["Name", "City"],
        [["Grace", "New York"], ["Ada", "London"], ["Alan", "Wilmslow"]]);

    [StaFact]
    public void ImportDialog_ShowsTablesAndPreview_AndFollowsOptionChanges()
    {
        var session = new RecipientSourceImportSession(
            "people.xlsx",
            new RecipientSourceInfo(
                RecipientSourceKind.Workbook,
                [
                    new RecipientSourceTable("sheet:People", "People$", RecipientSourceTableKind.Worksheet),
                    new RecipientSourceTable("sheet:Empty", "Empty$", RecipientSourceTableKind.Worksheet),
                ],
                ',',
                string.Empty),
            options => options.TableKey == "sheet:Empty"
                ? []
                : [["Name", "City"], ["Ada", "London"], ["Grace", "New York"]]);

        var view = RecipientSourceImportDialog.Create(owner: null, session);
        var tables = Find<ComboBox>(view.Window, RecipientSourceImportSession.TableAutomationId);
        var headers = Find<CheckBox>(view.Window, RecipientSourceImportSession.FirstRowHeadersAutomationId);
        var preview = Find<DataGrid>(view.Window, RecipientSourceImportSession.PreviewAutomationId);
        var ok = Descendants(view.Window).OfType<Button>().Single(button => button.IsDefault);

        view.Window.Title.Should().Be(MailMergeRecipientSourceText.ImportTitle);
        tables.Items.Count.Should().Be(2);
        Headers(preview).Should().Equal("Name", "City");
        preview.Items.Count.Should().Be(2);
        ((RecipientGridRow)preview.Items[0])[1].Should().Be("London");
        ok.IsEnabled.Should().BeTrue();

        headers.IsChecked = false;
        headers.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Headers(preview).Should().Equal("F1", "F2");
        preview.Items.Count.Should().Be(3);

        tables.SelectedIndex = 1;
        ok.IsEnabled.Should().BeFalse("an empty list cannot be loaded");
        view.TryAccept().Should().BeFalse();

        tables.SelectedIndex = 0;
        view.TryAccept().Should().BeTrue();
        view.Result!.Count.Should().Be(3);
        view.Window.Close();
    }

    [StaFact]
    public void ImportDialog_ForTextFile_OffersDelimiters_NotTables()
    {
        var session = new RecipientSourceImportSession(
            "people.csv",
            new RecipientSourceInfo(RecipientSourceKind.DelimitedText, [], ';', "Unicode (UTF-8)"),
            options => MergeRecipientTable.ParseDelimited("Name;City\nAda;London\n", options.Delimiter ?? ','));

        var view = RecipientSourceImportDialog.Create(owner: null, session);

        var delimiters = Find<ComboBox>(view.Window, RecipientSourceImportSession.DelimiterAutomationId);
        ((RecipientSourceChoice<char>)delimiters.SelectedItem).Value.Should().Be(';');
        Descendants(view.Window).OfType<ComboBox>().Should().ContainSingle("there is no table to choose");
        Descendants(view.Window).OfType<TextBlock>().Select(text => text.Text)
            .Should().Contain(MailMergeRecipientSourceText.Encoding("Unicode (UTF-8)"));
        view.Window.Close();
    }

    [StaFact]
    public void NewListDialog_EditsEntriesAndColumns_AndSavesBeforeAccepting()
    {
        var session = new RecipientListEditorSession();
        MergeData? saved = null;
        var allowSave = false;
        var view = RecipientListEditorDialog.Create(
            owner: null,
            session,
            (_, data) =>
            {
                saved = data;
                return allowSave;
            });
        var grid = view.Grid;

        view.Window.Title.Should().Be(MailMergeRecipientSourceText.NewListTitle);
        Headers(grid).Should().Equal(RecipientListEditorSession.DefaultColumns);
        grid.Items.Count.Should().Be(1);

        // Typing in a cell writes through the row to the session.
        var firstName = session.Columns.ToList().IndexOf("First Name");
        ((RecipientGridRow)grid.Items[0])[firstName] = "Ada";
        session.Cell(0, firstName).Should().Be("Ada");

        view.NewEntry();
        grid.Items.Count.Should().Be(2);
        ((RecipientGridRow)grid.Items[1])[firstName] = "Grace";

        view.AddColumn("Nickname").Should().BeTrue();
        Headers(grid).Should().EndWith("Nickname");
        view.RenameColumn(0, "Salutation").Should().BeTrue();
        Headers(grid)[0].Should().Be("Salutation");
        view.RemoveColumn(0).Should().BeTrue();
        Headers(grid).Should().NotContain("Salutation");
        ((RecipientGridRow)grid.Items[0])[session.Columns.ToList().IndexOf("First Name")].Should().Be("Ada");

        grid.SelectedIndex = 1;
        view.DeleteEntry();
        grid.Items.Count.Should().Be(1);

        view.TryAccept().Should().BeFalse("cancelling the save keeps the list open");
        view.Result.Should().BeNull();
        saved!.Rows.Should().ContainSingle().Which["First Name"].Should().Be("Ada");

        allowSave = true;
        view.TryAccept().Should().BeTrue();
        view.Result.Should().BeSameAs(saved);
        view.Window.Close();
    }

    [StaFact]
    public void EditListDialog_IncludeSortFilter_ProduceTheMergeSelection()
    {
        var session = new RecipientListSelectionSession(People(), [0, 1, 2]);
        var view = RecipientListSelectionDialog.Create(owner: null, session);
        var grid = view.Grid;
        var sortColumn = Find<ComboBox>(view.Window, RecipientListSelectionSession.SortColumnAutomationId);
        var sortDirection = Find<ComboBox>(view.Window, RecipientListSelectionSession.SortDirectionAutomationId);
        var filterColumn = Find<ComboBox>(view.Window, RecipientListSelectionSession.FilterColumnAutomationId);
        var filterValue = Find<TextBox>(view.Window, RecipientListSelectionSession.FilterValueAutomationId);
        var clearAll = Find<Button>(view.Window, RecipientListSelectionSession.ClearAllAutomationId);
        var selectAll = Find<Button>(view.Window, RecipientListSelectionSession.SelectAllAutomationId);
        var clearFilter = Find<Button>(view.Window, RecipientListSelectionSession.ClearFilterAutomationId);

        view.Window.Title.Should().Be(MailMergeRecipientSourceText.EditListTitle);
        Headers(grid).Should().Equal(MailMergeRecipientSourceText.IncludeColumn, "Name", "City");
        grid.Items.Count.Should().Be(3);

        // Unchecking a row excludes that recipient.
        ((RecipientGridRow)grid.Items[1]).IsIncluded = false;
        session.BuildSelection().Should().Equal(0, 2);

        sortColumn.SelectedIndex = 1;
        grid.Items.Cast<RecipientGridRow>().Select(row => row[0]).Should().Equal("Ada", "Alan", "Grace");
        sortDirection.SelectedIndex = 1;
        grid.Items.Cast<RecipientGridRow>().Select(row => row[0]).Should().Equal("Grace", "Alan", "Ada");
        grid.Items.Cast<RecipientGridRow>().Select(row => row.IsIncluded).Should().Equal(true, true, false);

        filterColumn.SelectedIndex = 2;
        filterValue.Text = "w";
        grid.Items.Cast<RecipientGridRow>().Select(row => row[0]).Should().Equal("Grace", "Alan");

        clearAll.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        session.BuildSelection().Should().BeEmpty();
        selectAll.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        clearFilter.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        grid.Items.Count.Should().Be(3);
        filterValue.Text.Should().BeEmpty();

        view.TryAccept().Should().BeTrue();
        view.Result.Should().Equal([0, 2], "Ada stayed unchecked; Grace and Alan are merged in the sorted order");
        view.Window.Close();
    }

    [StaFact]
    public void Ribbon_RoutesTheRecipientCommands_ToTheirWpfImplementations()
    {
        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());

        registry.TryGet("freew.merge-use-existing-list", out var useExisting).Should().BeTrue();
        useExisting.Should().BeOfType<UseExistingRecipientListCommand>();
        registry.TryGet("freew.merge-type-new-list", out var typeNew).Should().BeTrue();
        typeNew.Should().BeOfType<TypeNewRecipientListCommand>();
        registry.TryGet("freew.merge-edit-recipients", out var edit).Should().BeTrue();
        edit.Should().BeOfType<EditRecipientListCommand>();
        registry.TryGet("freew.merge-data", out var select).Should().BeTrue();
        select.Should().NotBeSameAs(edit, "Edit Recipient List now has its own dialog");
    }

    [StaFact]
    public void LoadRecipients_FromAFile_FeedsTheMergeSession()
    {
        // The same path the Use an Existing List command takes after its dialogs: file -> session.
        var path = Path.Combine(Path.GetTempPath(), "freew-recipients-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            RecipientListFile.Save(path, People());
            var import = RecipientSourceImportSession.Open(path);
            var session = new MailMergeSession();

            new MailMergeSessionWorkflow(session).LoadRecipients(import.Accept()!);

            session.Data!.Header.Should().Equal("Name", "City");
            session.Data.Count.Should().Be(3);
            session.SourceData.Should().BeSameAs(session.Data);
            import.FileName.Should().Be(Path.GetFileName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Commands_UseTheSharedFileDialogService_AndPresentationSessions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var host = Path.Combine(root, "freew", "FreeW.App.Host");
        var commands = File.ReadAllText(Path.Combine(host, "Ribbon", "MailMergeRecipientCommands.cs"));
        var dialogs = File.ReadAllText(Path.Combine(host, "MailMergeRecipientDialogs.cs"));

        commands.Should().Contain("WpfFileDialogService.ShowOpenDialog(")
            .And.Contain("WpfFileDialogService.ShowSaveDialog(")
            .And.Contain("RecipientSourceImportSession.Open(")
            .And.Contain("MailMergeRecipientSourcePlanner.DescribeFailure(")
            .And.Contain(".LoadRecipients(data)")
            .And.Contain("workflow.ApplyRecipientSelection(selection)")
            .And.NotContain("new OpenFileDialog")
            .And.NotContain("new SaveFileDialog")
            .And.NotContain("using Microsoft.Win32;");
        dialogs.Should().Contain("session.Preview")
            .And.Contain("session.BuildSelection()")
            .And.Contain("session.Build()")
            .And.NotContain("RecipientListFile.")
            .And.NotContain("MergeRecipientTable.");
    }
}
