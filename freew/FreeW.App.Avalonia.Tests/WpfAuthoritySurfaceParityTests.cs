using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Tests.TestSupport;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Free.Shared.Shell;
using FreeW.App.Presentation;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class WpfAuthoritySurfaceParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task About_uses_the_full_WPF_authority_content_and_modal_keyboard_shape()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new AboutDialog();

            dialog.Title.Should().Be("About FreeW");
            dialog.Width.Should().Be(AboutDialogMetrics.Width);
            dialog.Height.Should().Be(AboutDialogMetrics.Height);
            dialog.MinWidth.Should().Be(AboutDialogMetrics.MinWidth);
            dialog.MinHeight.Should().Be(AboutDialogMetrics.MinHeight);
            AutomationProperties.GetAutomationId(dialog).Should().Be("AboutFreeWDialog");
            var text = dialog.GetLogicalDescendants().OfType<TextBox>()
                .Single(textBox => AutomationProperties.GetAutomationId(textBox) == "AboutFreeWText");
            var root = dialog.GetLogicalDescendants().OfType<DockPanel>().Single();
            text.IsReadOnly.Should().BeTrue();
            text.AcceptsReturn.Should().BeTrue();
            text.Focusable.Should().BeTrue();
            text.HorizontalContentAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Left);
            text.VerticalContentAlignment.Should().Be(global::Avalonia.Layout.VerticalAlignment.Center);
            root.Margin.Should().Be(new Thickness(
                AboutDialogMetrics.RootMargin,
                AboutDialogMetrics.RootMargin,
                AboutDialogMetrics.FreeWAvaloniaRootRightMargin,
                AboutDialogMetrics.RootMargin));
            text.FontSize.Should().Be(AboutDialogMetrics.AvaloniaTextFontSize);
            text.Text.Should().Contain("A free word processor for DOCX editing and format-fidelity work.");
            text.Text.Should().Contain(FreeWProductInfo.DesktopRendererDescription);
            text.Text.Should().Contain("Help > Legal Notices");
            text.Text.Should().NotContain("Microsoft 365");
            AssertDefaultCancelButtons(dialog);
            var viewportAlignment = text.Styles
                .OfType<global::Avalonia.Styling.Style>()
                .SelectMany(style => style.Setters.OfType<global::Avalonia.Styling.Setter>())
                .Single(setter => setter.Property == ScrollViewer.VerticalContentAlignmentProperty);
            viewportAlignment.Value.Should().Be(global::Avalonia.Layout.VerticalAlignment.Center);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Compare_documents_preserves_defaults_validation_focus_and_explicit_result()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CompareDocumentsDialog.CreateForTest(
                "C:\\Docs\\Original.docx",
                new CompareDocumentsPromptState("Reviewer", "Revised.docx"));

            dialog.Title.Should().Be("Compare Documents");
            dialog.AuthorBoxForTest.Text.Should().Be("Reviewer");
            dialog.AuthorBoxForTest.Focusable.Should().BeTrue();
            dialog.MoreExpanderForTest.Template.Should().NotBeNull();
            dialog.MoreExpanderForTest.IsExpanded.Should().BeFalse();
            dialog.MoreExpanderForTest.IsExpanded = true;
            dialog.MoreExpanderForTest.IsExpanded.Should().BeTrue();
            AssertDefaultCancelButtons(dialog);

            dialog.AcceptForTest("   ").Should().BeNull();
            dialog.ValidationForTest.IsVisible.Should().BeTrue();

            var result = dialog.AcceptForTest(" Alice ");
            result.Should().NotBeNull();
            result!.Author.Should().Be("Alice");
            result.OriginalFilePath.Should().Be("C:\\Docs\\Original.docx");
            result.Settings.Insertions.Should().BeTrue();
            result.Settings.Deletions.Should().BeTrue();
            result.Settings.ShowChangesIn.Should().Be(CompareShowChangesIn.NewDocument);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Legal_notices_load_all_WPF_resources_and_keep_modal_keyboard_shape()
    {
        await Session.Dispatch(() =>
        {
            var documents = FreeWLegalNoticeProvider.GetDocuments(typeof(LegalNoticesDialog).Assembly);
            documents.Select(document => document.Title).Should().Equal(
                "Project License",
                "Legal Notices",
                "Privacy Notice",
                "Third-Party Notices",
                "Third-Party License Texts");
            documents.Should().OnlyContain(document => !string.IsNullOrWhiteSpace(document.Text));

            var dialog = new LegalNoticesDialog(documents);
            var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
            tabs.Items.Count.Should().Be(5);
            tabs.Items.OfType<TabItem>()
                .Should().OnlyContain(tab => tab.Content is TextBox,
                    "the WPF authority puts each scrolling textbox directly in its tab pane");
            dialog.GetLogicalDescendants().OfType<TextBox>()
                .Should().OnlyContain(textBox => textBox.IsReadOnly && textBox.Focusable);
            dialog.MinWidth.Should().Be(620);
            AssertDefaultCancelButtons(dialog);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Password_prompt_masks_focuses_and_distinguishes_accept_from_cancel()
    {
        await Session.Dispatch(() =>
        {
            var dialog = PasswordPromptDialog.CreateForTest(
                "Unprotect Document",
                "Enter the password:");

            dialog.Result.Should().BeNull("closing with Cancel must not produce a password");
            dialog.PasswordBoxForTest.PasswordChar.Should().Be('*');
            dialog.PasswordBoxForTest.Focusable.Should().BeTrue();
            AssertDefaultCancelButtons(dialog);
            dialog.AcceptForTest("secret").Should().Be("secret");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Restrict_editing_uses_the_password_prompt_result_for_stop_protection()
    {
        await Session.Dispatch(() =>
        {
            var current = ProtectionPasswordHelper.CreateWithPassword(
                ProtectionMode.ReadOnly,
                "secret");
            var cancelled = new RestrictEditingDialog(
                current,
                (_, _, _) => Task.FromResult<string?>(null));
            cancelled.StopProtectionForTestAsync().GetAwaiter().GetResult();
            cancelled.Result.Should().BeNull();

            var accepted = new RestrictEditingDialog(
                current,
                (_, _, _) => Task.FromResult<string?>("secret"));
            accepted.StopProtectionForTestAsync().GetAwaiter().GetResult();
            accepted.Result.Should().Be(ProtectionSettings.Unprotected);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Symbol_picker_matches_the_WPF_grid_and_applies_only_an_explicit_result()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new SymbolPickerDialog();
            dialog.Result.Should().BeNull();
            dialog.GlyphButtonsForTest.Select(button => (string)button.Content!)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs);
            dialog.GlyphButtonsForTest.Should().HaveCount(36);
            dialog.GlyphButtonsForTest[0].Focusable.Should().BeTrue();
            dialog.SelectGlyphForTest("\u03a9").Should().Be("\u03a9");

            var editor = new DocumentView();
            MainWindow.ApplySymbolPickerResult(editor, null);
            ((Paragraph)editor.Document.Blocks[0]).PlainText.Should().BeEmpty();
            MainWindow.ApplySymbolPickerResult(editor, "\u03a9");
            ((Paragraph)editor.Document.Blocks[0]).PlainText.Should().Contain("\u03a9");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Table_formula_validates_pastes_focuses_and_applies_the_dialog_result()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new TableFormulaDialog(
                new TableFormulaDialogInitialState("=SUM(ABOVE)", 3));
            dialog.FormulaBoxForTest.Text.Should().Be("=SUM(ABOVE)");
            dialog.FormatBoxForTest.SelectedIndex.Should().Be(3);
            dialog.FormulaBoxForTest.Focusable.Should().BeTrue();
            AssertDefaultCancelButtons(dialog);

            dialog.AcceptForTest(" ", "0").Should().BeNull();
            dialog.ValidationForTest.IsVisible.Should().BeTrue();
            dialog.PasteFunctionForTest("AVERAGE");
            dialog.FormulaBoxForTest.Text.Should().Contain("AVERAGE()");
            var result = dialog.AcceptForTest("=SUM(ABOVE)", "#,##0");
            result.Should().Be(new TableFormulaField("=SUM(ABOVE)", "#,##0"));

            var (editor, table) = CreateTableEditor();
            MainWindow.ApplyTableFormulaResult(editor, null);
            table.Rows[2].Cells[0].Paragraphs[0].Runs.Should()
                .NotContain(run => run.TableFormula != null);
            MainWindow.ApplyTableFormulaResult(editor, result);
            table.Rows[2].Cells[0].Paragraphs[0].Runs.Should()
                .ContainSingle(run => run.TableFormula == result);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Table_properties_has_four_tabs_validation_focus_and_result_application()
    {
        await Session.Dispatch(() =>
        {
            var (editor, table) = CreateTableEditor();
            table.FloatingPosition = new TableFloatingPosition(
                HorizontalAnchor: TableHorizontalAnchor.Page,
                VerticalAnchor: TableVerticalAnchor.Margin,
                HorizontalAlignment: TableHorizontalPositionAlignment.Outside,
                VerticalOffsetPt: -18,
                LeftFromTextPt: 3,
                RightFromTextPt: 4,
                TopFromTextPt: 5,
                BottomFromTextPt: 6);
            table.FloatingTableAllowsOverlap = false;
            var row = table.Rows[2];
            var cell = row.Cells[0];
            var dialog = new TablePropertiesDialog(
                new ModelTableContext(table, row, cell),
                TablePropertiesDialogTabKind.Cell);

            dialog.TabsForTest.Items.Count.Should().Be(4);
            dialog.TabsForTest.SelectedIndex.Should().Be(3);
            AutomationProperties.GetAutomationId(dialog.InitialFocusTargetForTest)
                .Should().Be("TablePropertiesCellWidthBox");
            dialog.GetLogicalDescendants().OfType<CheckBox>().Single(checkBox =>
                    AutomationProperties.GetAutomationId(checkBox) == "TablePropertiesCellWrapTextCheckBox")
                .IsChecked.Should().BeTrue();
            dialog.GetLogicalDescendants().OfType<CheckBox>().Single(checkBox =>
                    AutomationProperties.GetAutomationId(checkBox) == "TablePropertiesCellFitTextCheckBox")
                .IsChecked.Should().BeFalse();
            dialog.GetLogicalDescendants().OfType<ComboBox>().Single(comboBox =>
                    AutomationProperties.GetAutomationId(comboBox) == "TablePropertiesHorizontalAnchorBox")
                .SelectedIndex.Should().Be(2);
            dialog.GetLogicalDescendants().OfType<ComboBox>().Single(comboBox =>
                    AutomationProperties.GetAutomationId(comboBox) == "TablePropertiesHorizontalModeBox")
                .SelectedIndex.Should().Be(5);
            TextBox(dialog, "TablePropertiesHorizontalOffsetBox").IsEnabled.Should().BeFalse();
            TextBox(dialog, "TablePropertiesVerticalOffsetBox").IsEnabled.Should().BeTrue();
            AssertDefaultCancelButtons(dialog);

            TextBox(dialog, "TablePropertiesIndentBox").Text = "-1";
            dialog.AcceptForTest().Should().BeNull();
            dialog.ValidationForTest.IsVisible.Should().BeTrue();
            TextBox(dialog, "TablePropertiesIndentBox").Text = "0";
            var accepted = dialog.AcceptForTest();
            accepted.Should().NotBeNull();
            accepted!.FloatingPosition.Should().Be(table.FloatingPosition);
            accepted.FloatingTableAllowsOverlap.Should().BeFalse();

            var values = new TablePropertiesValues(
                PreferredWidthPt: 300,
                Alignment: TableAlignment.Center,
                TextWrapping: true,
                IndentFromLeftPt: 12,
                DefaultCellMargins: new TableCellMargins(2, 4, 2, 4),
                CellSpacingPt: 1,
                RowHeightPt: 24,
                RowHeightRule: TableRowHeightRule.Exact,
                AllowRowBreak: false,
                RepeatHeaderRow: true,
                ColumnWidthPt: 144,
                CellPreferredWidthPt: 144,
                CellVerticalAlignment: TableCellVerticalAlignment.Center,
                CellMargins: new TableCellMargins(3, 5, 3, 5),
                CellWrapText: false,
                CellFitText: true,
                FloatingTableAllowsOverlap: false,
                FloatingPosition: table.FloatingPosition);
            MainWindow.ApplyTablePropertiesResult(editor, values);
            table.PreferredWidthPt.Should().Be(300);
            cell.WidthPt.Should().Be(144);
            cell.Margins.Should().Be(new TableCellMargins(3, 5, 3, 5));
            cell.WrapText.Should().BeFalse();
            cell.FitText.Should().BeTrue();

            editor.CanUndo.Should().BeTrue();
            editor.Undo();
            table.PreferredWidthPt.Should().BeNull();
            cell.WidthPt.Should().BeNull();
            cell.Margins.Should().BeNull();
            cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Top);
            cell.WrapText.Should().BeTrue();
            cell.FitText.Should().BeFalse();

            editor.Redo();
            table.PreferredWidthPt.Should().Be(300);
            cell.WidthPt.Should().Be(144);
            cell.Margins.Should().Be(new TableCellMargins(3, 5, 3, 5));
            cell.WrapText.Should().BeFalse();
            cell.FitText.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Table_properties_uses_Wpf_action_row_and_checkbox_chrome()
    {
        await Session.Dispatch(() =>
        {
            var (_, table) = CreateTableEditor();
            var dialog = new TablePropertiesDialog(
                new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]));

            var buttons = dialog.GetLogicalDescendants().OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .ToArray();
            var ok = buttons.Single(button => button.IsDefault);
            var row = ok.Parent.Should().BeOfType<StackPanel>().Subject;

            row.Spacing.Should().Be(14);
            ok.BorderBrush.Should().BeOfType<SolidColorBrush>();
            ((SolidColorBrush)ok.BorderBrush!).Color.Should().Be(Color.FromRgb(200, 200, 200));
            dialog.GetLogicalDescendants().OfType<CheckBox>()
                .Should().OnlyContain(check => check.Margin.Left == 0);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Table_properties_cell_tab_keeps_Wpf_checkbox_geometry()
    {
        await Session.Dispatch(() =>
        {
            var (_, table) = CreateTableEditor();
            var dialog = new TablePropertiesDialog(
                new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]),
                TablePropertiesDialogTabKind.Cell);

            var cellTab = dialog.TabsForTest.Items.OfType<TabItem>().Single(tab =>
                AutomationProperties.GetAutomationId(tab) == "TablePropertiesCellTab");
            var cellChecks = cellTab.GetLogicalDescendants().OfType<CheckBox>().ToArray();

            cellChecks.Should().HaveCount(5);
            cellChecks.Should().OnlyContain(check => check.Margin.Left == 0);
            cellChecks.Single(check =>
                    AutomationProperties.GetAutomationId(check) == "TablePropertiesCellWrapTextCheckBox")
                .Margin.Should().Be(new Thickness(0, 4, 8, 4));
            cellChecks.Single(check =>
                    AutomationProperties.GetAutomationId(check) == "TablePropertiesCellFitTextCheckBox")
                .Margin.Should().Be(new Thickness(0, 4, 8, 4));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Table_properties_keeps_positioning_section_on_the_Wpf_cell_tab()
    {
        await Session.Dispatch(() =>
        {
            var (_, table) = CreateTableEditor();
            var dialog = new TablePropertiesDialog(
                new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]));

            var tabs = dialog.TabsForTest.Items.OfType<TabItem>().ToArray();
            tabs.Should().HaveCount(4);

            tabs[0].GetLogicalDescendants().OfType<Expander>()
                .Should().BeEmpty();
            tabs[3].GetLogicalDescendants().OfType<Expander>()
                .Select(expander => Convert.ToString(expander.Header))
                .Should().ContainSingle("Positioning");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Table_properties_normalizes_disabled_positioning_combo_surfaces_on_cell_tab()
    {
        await Session.Dispatch(() =>
        {
            var (_, table) = CreateTableEditor();
            var dialog = new TablePropertiesDialog(
                new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]),
                TablePropertiesDialogTabKind.Cell);
            try
            {
                dialog.Width = 560;
                dialog.Height = 600;
                dialog.Show();
                dialog.Measure(new Size(560, 600));
                dialog.Arrange(new Rect(0, 0, 560, 600));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var positioningCombos = dialog.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .Where(combo => (AutomationProperties.GetAutomationId(combo) ?? string.Empty).StartsWith("TablePropertiesHorizontal", StringComparison.Ordinal)
                        || (AutomationProperties.GetAutomationId(combo) ?? string.Empty).StartsWith("TablePropertiesVertical", StringComparison.Ordinal))
                    .ToArray();
                positioningCombos.Should().HaveCount(4);
                positioningCombos.Should().OnlyContain(combo => !combo.IsEffectivelyEnabled);
                positioningCombos.SelectMany(combo => combo.GetVisualDescendants()
                        .OfType<Border>()
                        .Where(border => border.Name is "PART_LayoutRoot" or "Background"))
                    .Should().OnlyContain(surface => surface.Background == positioningCombos[0].Background);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Page_setup_preserves_Wpf_action_semantics_and_child_order()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new PageSetupDialog(new PageSettings());
            var buttons = dialog.GetLogicalDescendants().OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .Select(button => AvaloniaActionLabelInspector.Inspect(button).DisplayText)
                .ToArray();

            buttons.Should().Equal("OK", "Cancel", "Line Numbers\u2026", "Borders\u2026");
            dialog.GetLogicalDescendants().OfType<Button>()
                .Should().ContainSingle(button => button.IsDefault && AutomationProperties.GetName(button) == "OK")
                .And.ContainSingle(button => button.IsCancel && AutomationProperties.GetName(button) == "Cancel");
        }, CancellationToken.None);
    }

    [Fact]
    public void Table_properties_visual_harness_defers_state_population_to_the_common_pass()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "AvaloniaDialogRouteFactory.cs"));
        var start = source.IndexOf(
            "private static Window CreateTableProperties",
            StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf("private static Window CreateStyle", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var method = source[start..end];

        method.Should().Contain("return dialog;");
        method.Should().NotContain("_preferredWidth");
        method.Should().NotContain("AcceptForTest");
        method.Should().NotContain("state ==");
    }

    [Fact]
    public async Task Screen_clip_geometry_overlay_adapter_and_image_application_are_deterministic()
    {
        ScreenClipPlanner.BuildPhysicalSelection(
                30, 45, 10, 20, -100, -50, renderScale: 2)
            .Should().Be(new ScreenPixelRect(-80, -10, 40, 50));
        ScreenClipPlanner.BuildPhysicalSelection(
                10, 10, 10, 15, 0, 0, renderScale: 1)
            .Should().BeNull();
        ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(
                140.4, 250.4, 100.4, 200.4)
            .Should().Be(new ScreenPixelRect(100, 200, 40, 50));
        ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(
                10.1, 20, 10.4, 30)
            .Should().BeNull();
        ScreenClipPlanner.BuildImageInsertionPlan(1600, 900)
            .Should().Be(new ScreenClipImageInsertionPlan(ImageFormat.Png, 400, 225, 1600, 900));

        await Session.Dispatch(() =>
        {
            var overlay = new ScreenClipOverlay(new PixelRect(-100, -50, 800, 600), 2);
            overlay.BeginSelectionForTest(new Point(10, 20));
            overlay.CompleteSelectionForTest(new Point(30, 45), 2)
                .Should().Be(new ScreenPixelRect(-80, -10, 40, 50));
            overlay.CancelForTest();
            overlay.ResultForTest.Should().BeNull();

            var capture = new ScreenClipCapture(OnePixelPng(), 1600, 900);
            var service = new DeterministicScreenClipService(capture);
            service.CaptureAsync(new Window()).GetAwaiter().GetResult().Should().Be(capture);
            service.CallCount.Should().Be(1);

            var editor = new DocumentView();
            var result = new ScreenClipWorkflowCoordinator().Execute(
                () => capture,
                image => MainWindow.ApplyScreenClipImage(editor, image));
            result.Outcome.Should().Be(ScreenClipWorkflowOutcome.Inserted);
            var image = ((Paragraph)editor.Document.Blocks[0]).Runs
                .Single(run => run.Image is not null).Image!;
            image.Bytes.Should().Equal(capture.PngBytes);
            image.Format.Should().Be(ImageFormat.Png);
            image.WidthPt.Should().Be(400);
            image.HeightPt.Should().Be(225);
            image.OriginalPixelWidth.Should().Be(1600);
            image.OriginalPixelHeight.Should().Be(900);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Authority_routes_are_registered_to_real_host_callbacks_and_disabled_without_them()
    {
        await Session.Dispatch(() =>
        {
            var symbol = 0;
            var clips = 0;
            var legal = 0;
            var about = 0;
            var compare = 0;
            var callbacks = Callbacks() with
            {
                OpenSymbolPickerDialog = () => symbol++,
                CaptureScreenClip = () => clips++,
                OpenAbout = () => about++,
                OpenLegalNotices = () => legal++,
                CompareDocuments = () => compare++,
            };
            var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

            Execute(registry, "freew.symbol");
            Execute(registry, "freew.screenshot");
            Execute(registry, "freew.screen-clipping");
            Execute(registry, "freew.about");
            Execute(registry, "freew.legal-notices");
            Execute(registry, "freew.compare");
            (symbol, clips, about, legal, compare).Should().Be((1, 2, 1, 1, 1));

            var unavailable = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), Callbacks());
            foreach (var id in new[]
                     {
                         "freew.symbol", "freew.screenshot", "freew.screen-clipping",
                         "freew.about", "freew.legal-notices", "freew.compare", "freew.table-formula",
                         "freew.table-properties",
                     })
            {
                unavailable.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
                command.Should().BeAssignableTo<IRibbonStatefulCommand>();
                ((IRibbonStatefulCommand)command!).GetState().IsEnabled.Should().BeFalse();
                var action = () => command.Execute(RibbonCommandContext.Empty);
                action.Should().NotThrow("disabled shared commands are inert when invoked programmatically");
            }

            (symbol, clips, about, legal, compare).Should().Be((1, 2, 1, 1, 1));
        }, CancellationToken.None);
    }

    private static void AssertDefaultCancelButtons(Control dialog)
    {
        var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
        buttons.Should().ContainSingle(button => button.IsDefault);
        buttons.Should().ContainSingle(button => button.IsCancel);
    }

    private static TextBox TextBox(Control dialog, string automationId) =>
        dialog.GetLogicalDescendants().OfType<TextBox>().Single(
            textBox => AutomationProperties.GetAutomationId(textBox) == automationId);

    private static (DocumentView Editor, Table Table) CreateTableEditor()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        table.Rows.Add(Row("1"));
        table.Rows.Add(Row("2"));
        table.Rows.Add(Row(string.Empty));
        document.Blocks.Add(table);

        var editor = new DocumentView();
        editor.LoadDocument(document);
        editor.Measure(new Size(800, 1200));
        editor.PlaceCaretInCell(0, row: 2, col: 0, paraIdx: 0, offset: 0);
        return (editor, table);
    }

    private static TableRow Row(string text)
    {
        var row = new TableRow();
        row.Cells.Add(new TableCell(text));
        return row;
    }

    private static void Execute(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static FreeWRibbonHostExecutionPorts Callbacks() => new(
        Open: () => { },
        Save: () => { },
        Cut: () => { },
        Copy: () => { },
        Paste: () => { },
        Backstage: () => { },
        NewDocument: () => { },
        ToggleNavigationPane: () => { },
        ToggleReviewingPane: () => { },
        ToggleRevealFormatting: () => { },
        OpenFindReplaceDialog: () => { },
        SetPrintLayout: () => { },
        SetWebLayout: () => { },
        SetDraftView: () => { },
        OpenFontDialog: () => { },
        OpenParagraphDialog: () => { },
        OpenPageSetupDialog: () => { },
        ToggleOrientation: () => { },
        ApplyMarginPreset: _ => { },
        ApplyPaperSize: _ => { },
        InsertPicture: () => { },
        OpenWordCountDialog: () => { },
        ApplyZoom: (_, _) => { });
}
