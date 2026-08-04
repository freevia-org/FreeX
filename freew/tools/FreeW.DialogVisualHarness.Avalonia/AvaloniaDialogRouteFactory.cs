using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;

internal static class AvaloniaDialogRouteFactory
{
    [ThreadStatic]
    private static int _bindingDepth;

    private static readonly IReadOnlyDictionary<string, string> DialogTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["accessibility-report"] = "AccessibilityReportDialog",
        ["bookmark"] = "BookmarkDialog",
        ["bookmark-manager"] = "BookmarkManagerDialog",
        ["borders-and-shading"] = "BordersAndShadingDialog",
        ["building-blocks-organizer"] = "BuildingBlocksOrganizerDialog",
        ["caption"] = "CaptionDialog",
        ["cell-edit"] = "CellEditDialog",
        ["chart-axis-titles"] = "ChartAxisTitlesDialog",
        ["chart-size"] = "ChartSizeDialog",
        ["chart-title"] = "ChartTitleDialog",
        ["cell-shading"] = "CellShadingDialog",
        ["citation-source-picker"] = "CitationSourcePickerDialog",
        ["comment-list"] = "CommentListDialog",
        ["comment-reply"] = "CommentReplyDialog",
        ["compare-documents"] = "CompareDocumentsDialog",
        ["cross-reference"] = "CrossReferenceDialog",
        ["customize-theme-colors"] = "CustomizeThemeColorsDialog",
        ["customize-theme-fonts"] = "CustomizeThemeFontsDialog",
        ["character-formatting-picker"] = "CharacterFormattingPickerDialog",
        ["date-time"] = "DateTimeDialog",
        ["document-inspector"] = "DocumentInspectorDialog",
        ["draw-table-dimension"] = "DrawTableDimensionDialog",
        ["field-picker"] = "FieldPickerDialog",
        ["find-replace"] = "FindReplaceDialog",
        ["font"] = "FontDialog",
        ["footnote-endnote-options"] = "FootnoteEndnoteOptionsDialog",
        ["header-footer-text"] = "HeaderFooterTextDialog",
        ["about"] = "AboutDialog",
        ["hyperlink"] = "HyperlinkDialog",
        ["icon-picker"] = "IconPickerDialog",
        ["image-adjust"] = "ImageAdjustDialog",
        ["image-alt-text"] = "ImageAltTextDialog",
        ["image-border"] = "ImageBorderDialog",
        ["image-crop"] = "ImageCropDialog",
        ["image-position"] = "ImagePositionDialog",
        ["image-size"] = "ImageSizeDialog",
        ["insert-chart"] = "InsertChartDialog",
        ["insert-smart-art"] = "InsertSmartArtDialog",
        ["legal-notices"] = "LegalNoticesDialog",
        ["link-bookmark"] = "LinkBookmarkDialog",
        ["manage-sources"] = "ManageSourcesDialog",
        ["manage-styles"] = "ManageStylesDialog",
        ["manual-hyphenation"] = "ManualHyphenationDialog",
        ["mark-citation"] = "MarkCitationDialog",
        ["multilevel-list"] = "MultilevelListDialog",
        ["note-text"] = "NoteTextDialog",
        ["page-borders"] = "PageBordersDialog",
        ["page-color"] = "PageColorDialog",
        ["page-number-format"] = "PageNumberFormatDialog",
        ["paragraph"] = "ParagraphDialog",
        ["paste-special"] = "PasteSpecialDialog",
        ["print-preview"] = "PrintPreviewDialog",
        ["proofing-language"] = "ProofingLanguageDialog",
        ["properties"] = "PropertiesDialog",
        ["quick-part"] = "QuickPartDialog",
        ["quick-part-name"] = "QuickPartNameDialog",
        ["restrict-editing"] = "RestrictEditingDialog",
        ["save-compatibility-warning"] = "SaveCompatibilityWarningDialog",
        ["screen-tip"] = "ScreenTipDialog",
        ["set-as-default-confirmation"] = "SetAsDefaultConfirmationDialog",
        ["smart-art-edit"] = "SmartArtEditDialog",
        ["sort"] = "SortDialog",
        ["source-author-editor"] = "SourceAuthorEditorDialog",
        ["source-conflict-resolution"] = "SourceConflictResolutionDialog",
        ["source-entry"] = "SourceEntryDialog",
        ["style"] = "StyleDialog",
        ["style-set"] = "StyleSetDialog",
        ["symbol-picker"] = "SymbolPickerDialog",
        ["table-formula"] = "TableFormulaDialog",
        ["table-of-authorities"] = "TableOfAuthoritiesDialog",
        ["table-properties"] = "TablePropertiesDialog",
        ["table-text-conversion"] = "TableTextConversionDialog",
        ["tabs"] = "TabsDialog",
        ["theme-effects"] = "ThemeEffectsDialog",
        ["thesaurus"] = "ThesaurusDialog",
        ["watermark"] = "WatermarkDialog",
        ["word-count"] = "WordCountDialog",
        ["zoom"] = "ZoomDialog",
    };

    public static Window? Create(string routeId, string state, string? tab = null)
    {
        if (routeId is "options" or "page-setup" or "columns" or "custom-paragraph-spacing" or "drop-cap-options" or "hyphenation-options" or "line-number-options")
            return CreateKnown(routeId, state);

        if (routeId.StartsWith("backstage-", StringComparison.OrdinalIgnoreCase))
            return CreateBackstage(routeId);

        if (routeId == "bookmark-manager")
            return CreateBookmarkManager(state);

        if (routeId == "notes-pane")
            return CreateNotesPane();
        if (routeId == "cups-print")
            return CreateCupsPrint();
        if (routeId == "compare-documents")
            return CreateCompareDocuments(state, tab);
        if (routeId == "password-prompt")
            return CreatePasswordPrompt();
        if (routeId == "screen-clip-overlay")
            return CreateScreenClipOverlay();
        if (routeId == "table-formula")
            return CreateTableFormula(state);
        if (routeId == "table-properties")
            return CreateTableProperties(tab);

        if (routeId == "style")
            return CreateStyle(state);
        if (routeId == "character-formatting-picker")
            return CreateCharacterFormattingPicker(state);
        if (routeId == "manual-hyphenation")
            return CreateManualHyphenation(state);

        if (!DialogTypes.TryGetValue(routeId, out var typeName))
            return null;
        return CreateType(typeName, state);
    }

    private static Window CreateBookmarkManager(string state)
    {
        var editor = new DocumentView();
        if (!state.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("First target") { BookmarkNames = { "FirstTarget" } });
            document.Blocks.Add(new Paragraph("Second target") { BookmarkNames = { "SecondTarget" } });
            editor.LoadDocument(document);
        }

        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.BookmarkManagerDialog", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 1);
        return (Window)constructor.Invoke([editor]);
    }

    private static Window CreateKnown(string routeId, string state)
    {
        var assembly = typeof(MainWindow).Assembly;
        if (routeId == "options")
            return (Window)Activator.CreateInstance(assembly.GetType("FreeW.App.Avalonia.OptionsDialog", true)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [new FreeWOptions()], null)!;
        if (routeId == "page-setup")
            return new PageSetupDialog(
                new PageSettings(),
                sectionStart: PageSetupDialogPlanner.VisualHarnessSectionStart);

        var typeName = routeId switch
        {
            "columns" => "ColumnsDialog",
            "custom-paragraph-spacing" => "CustomParagraphSpacingDialog",
            "drop-cap-options" => "DropCapOptionsDialog",
            "hyphenation-options" => "HyphenationOptionsDialog",
            "line-number-options" => "LineNumberOptionsDialog",
            _ => throw new ArgumentOutOfRangeException(nameof(routeId)),
        };
        return CreateType(typeName, state);
    }

    private static Window CreateBackstage(string routeId)
    {
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType("FreeW.App.Avalonia.Backstage.BackstageView", true)!;
        // Use the real production shell to obtain the same sample document,
        // recent-file workflow, file formats, and persisted options as the WPF
        // authority. Synthesizing empty callbacks makes the panes look unlike
        // the application users actually see.
        var shell = new MainWindow();
        var callbacks = typeof(MainWindow)
            .GetMethod("BuildBackstageCallbacks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(shell, null)
            ?? throw new MissingMethodException(typeof(MainWindow).FullName, "BuildBackstageCallbacks");
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderByDescending(candidate => candidate.GetParameters().Length).First();
        // Keep this capture contract aligned with the WPF authority: invoke the
        // production pane builder and capture the pane in a neutral host. Capturing
        // the full Avalonia Backstage window here would compare the navigation rail
        // and frame chrome instead of the actual pane surface.
        var methodName = routeId switch
        {
            "backstage-home" => "BuildHomePane",
            "backstage-new" => "BuildNewPane",
            "backstage-open" => "BuildOpenPane",
            "backstage-info" => "BuildInfoPane",
            "backstage-share" => "BuildSharePane",
            "backstage-save-as" => "BuildSaveAsPane",
            "backstage-print" => "BuildPrintPane",
            "backstage-export" => "BuildExportPane",
            "backstage-account" => "BuildAccountPane",
            "backstage-options" => "BuildOptionsPane",
            _ => null,
        };
        if (methodName is null) throw new ArgumentOutOfRangeException(nameof(routeId));

        var home = Enum.Parse(assembly.GetType("FreeW.App.Avalonia.Backstage.BackstagePane", true)!, "Home");
        Window? backstage = null;
        try
        {
            backstage = (Window)constructor.Invoke([callbacks, home]);
            var method = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .SingleOrDefault(candidate => candidate.Name.Equals(methodName, StringComparison.Ordinal)
                    && candidate.GetParameters().Length == 0)
                ?? throw new MissingMethodException(type.FullName, methodName);
            var control = (Control)method.Invoke(backstage, null)!;
            return WrapControl(control);
        }
        finally
        {
            backstage?.Close();
            shell.Close();
        }
    }

    private static Window CreateNotesPane()
    {
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType("FreeW.App.Avalonia.NotesPane", true)!;
        var editor = new DocumentView();
        editor.LoadDocument(TextDocument.CreateEmpty());
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var pane = (Control)constructor.Invoke([editor]);
        type.GetMethod("Toggle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(pane, null);
        return WrapControl(pane);
    }

    private static Window CreateType(string typeName, string state)
    {
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType($"FreeW.App.Avalonia.{typeName}", false)
            ?? assembly.GetTypes().FirstOrDefault(candidate => candidate.Name.Equals(typeName, StringComparison.Ordinal));
        if (type is null) throw new InvalidOperationException($"Avalonia route type not found: {typeName}.");
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(constructor => constructor.GetParameters().Length);
        Exception? last = null;
        foreach (var constructor in constructors)
        {
            try
            {
                var args = constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType, state)).ToArray();
                var value = constructor.Invoke(args);
                if (value is Window window) return window;
                if (value is Control control) return WrapControl(control);
            }
            catch (Exception ex)
            {
                last = ex.InnerException ?? ex;
            }
        }
        throw new InvalidOperationException($"No constructible Avalonia adapter for {typeName}: {last?.GetType().Name}: {last?.Message}", last);
    }

    private static Window CreateCharacterFormattingPicker(string state)
    {
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.CharacterFormattingPickerDialog", true)!;
        var methodName = state.Equals("populated", StringComparison.OrdinalIgnoreCase)
            ? "ForTestShading"
            : "ForTestBorder";
        return (Window)type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, null)!;
    }

    private static Window CreateManualHyphenation(string state)
    {
        var editor = new DocumentView();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("characterization"));
        editor.LoadDocument(document);
        var candidate = ManualHyphenationPlanner.CreateSession(document).Current
            ?? throw new InvalidOperationException("The manual-hyphenation harness fixture did not produce a real candidate.");

        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.ManualHyphenationDialog", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidateConstructor => candidateConstructor.GetParameters().Length == 1);
        return (Window)constructor.Invoke([candidate]);
    }

    private static Window CreateCupsPrint()
    {
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType("FreeW.App.Avalonia.Printing.CupsPrintDialog", true)!;
        var planType = typeof(Free.Shared.AppServices.Printing.PrinterDiscoveryResult);
        var discovery = Activator.CreateInstance(
            planType,
            Free.Shared.AppServices.Printing.PrinterDiscoveryStatus.NoPrinters,
            Array.Empty<Free.Shared.AppServices.Printing.PrinterInfo>(),
            null,
            "No printers are installed or available.")!;
        var planner = typeof(FreeW.App.Presentation.Printing.PrintSelectionPlanner);
        var plan = planner.GetMethod("Build")!.Invoke(null, [discovery, null])!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(candidate => candidate.GetParameters().Length == 1);
        return (Window)constructor.Invoke([plan]);
    }

    private static Window CreateCompareDocuments(string state, string? tab)
    {
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType("FreeW.App.Avalonia.CompareDocumentsDialog", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .First();
        var promptState = new CompareDocumentsPromptState("Reviewer", "Revised.docx");
        var dialog = (Window)constructor.Invoke(["C:\\Harness\\Original.docx", promptState]);
        if (state == "validation-error")
            type.GetMethod("AcceptForTest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [" "]);
        if (tab?.Equals("More", StringComparison.OrdinalIgnoreCase) == true)
            dialog.GetLogicalDescendants().OfType<Expander>().Single(expander => expander.Header?.ToString() == "More").IsExpanded = true;
        return dialog;
    }

    private static Window CreatePasswordPrompt()
    {
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.PasswordPromptDialog", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (Window)constructor.Invoke(["Unprotect Document", "Enter the password:"]);
    }

    private static Window CreateScreenClipOverlay()
    {
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.Editing.ScreenClipOverlay", true)!;
        var overlay = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [new PixelRect(0, 0, 560, 600), 1d], null)!;
        type.GetMethod("BeginSelectionForTest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [new Point(80, 90)]);
        type.GetMethod("CompleteSelectionForTest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [new Point(360, 300), 1d]);
        var canvas = (Control)overlay.Content!;
        overlay.Content = null;
        var selection = ((Canvas)canvas).Children.OfType<Rectangle>().Single();
        Canvas.SetLeft(selection, 80);
        Canvas.SetTop(selection, 90);
        selection.Width = 280;
        selection.Height = 210;
        selection.IsVisible = true;
        var surface = new Grid { Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0)) };
        surface.Children.Add(new Border { Background = overlay.Background });
        surface.Children.Add(canvas);
        return new Window
        {
            Width = 560,
            Height = 600,
            Content = surface,
            Title = "Screen Clip Overlay Capture",
        };
    }

    private static Window CreateTableFormula(string state)
    {
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.TableFormulaDialog", true)!;
        var initialState = state == "initial"
            ? new TableFormulaDialogInitialState("=", 0)
            : new TableFormulaDialogInitialState("=SUM(ABOVE)", 3);
        var dialog = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [initialState], null)!;
        if (state == "validation-error")
            type.GetMethod("AcceptForTest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [" ", "0"]);
        return dialog;
    }

    private static Window CreateTableProperties(string? tab)
    {
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell("Harness cell");
        row.Cells.Add(cell);
        table.Rows.Add(row);
        var context = new ModelTableContext(table, row, cell);
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.TablePropertiesDialog", true)!;
        var tabType = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.TablePropertiesDialogTab", true)!;
        var initialTab = Enum.Parse(tabType, tab ?? "Table", true);
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var dialog = (Window)constructor.Invoke([context, initialTab]);

        // Keep state setup in the shared harness Populate pass, exactly as WPF does.
        // The Avalonia adapter previously mutated these fields here, which made the
        // populated and validation captures represent different documents on each host.
        return dialog;
    }

    private static Window CreateStyle(string state)
    {
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Avalonia.StyleDialog", true)!;
        var catalog = state == "populated"
            ? new Dictionary<string, string>
            {
                ["Normal"] = "Normal",
                ["Heading1"] = "Heading 1",
            }
            : new Dictionary<string, string>();
        return (Window)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            ["New Style", catalog, null, null, RunFormatting.Default, ParagraphFormatting.Default, null],
            null)!;
    }

    private static Window WrapControl(Control control)
    {
        return new Window { Width = 560, Height = 600, Content = control, Title = control.GetType().Name };
    }

    private static object? ValueFor(Type type, string state)
    {
        var depth = _bindingDepth++;
        if (depth >= 6)
        {
            _bindingDepth--;
            return null;
        }
        try
        {
        if (type == typeof(ToaOptions))
            return TableOfAuthoritiesDialogPlanner.BuildEvidenceOptions(state);
        if (type == typeof(string)) return state == "populated" ? "Sample text" : string.Empty;
        if (type == typeof(FreeWOptions)) return new FreeWOptions();
        if (type == typeof(PageSettings)) return new PageSettings();
        if (type == typeof(TextDocument)) return new TextDocument();
        if (type == typeof(DocumentView)) return new DocumentView();
        if (type.FullName == "Free.Shared.Opc.DocumentProperties") return Activator.CreateInstance(type);
        if (type.Name == "ThesaurusEntry") return ThesaurusLookup.Instance.Lookup("good");
        if (type == typeof(DateTime)) return new DateTime(2025, 1, 1);
        if (type == typeof(CultureInfo)) return CultureInfo.CurrentCulture;
        if (type.Name == "BackstageDirectPrintCapability")
        {
            var deferred = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "Deferred")
                .OrderBy(method => method.GetParameters().Length)
                .First();
            var deferredArguments = deferred.GetParameters()
                .Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : null)
                .ToArray();
            return deferred.Invoke(null, deferredArguments);
        }
        if (type.Name == "QuickPartLibrary") return type.GetMethod("LoadFromPath", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [null]);
        if (type.Name == "SourceManagementSourceEntry")
        {
            var entryConstructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderByDescending(candidate => candidate.GetParameters().Length).First();
            return entryConstructor.Invoke(entryConstructor.GetParameters().Select(parameter =>
                parameter.ParameterType.IsEnum ? Enum.GetValues(parameter.ParameterType).GetValue(0) : "Sample").ToArray());
        }
        if (type == typeof(bool)) return false;
        if (type == typeof(int)) return 1;
        if (type == typeof(double)) return 1d;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
            (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IList<>) || definition == typeof(IReadOnlyCollection<>)))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments()));
        if (typeof(Delegate).IsAssignableFrom(type)) return EmptyDelegate(type);
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type == typeof(object)) return null;
        var defaultProperty = type.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
        if (defaultProperty is not null && type.IsAssignableFrom(defaultProperty.PropertyType)) return defaultProperty.GetValue(null);
        var isAppModel = type.Namespace?.StartsWith("FreeW.Core.Model", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("FreeW.App.Presentation", StringComparison.Ordinal) == true;
        if (!isAppModel) return null;
        foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(candidate => candidate.GetParameters().Length))
        {
            try
            {
                var values = ctor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType, state)).ToArray();
                return ctor.Invoke(values);
            }
            catch
            {
                // Try the next record/model constructor with the same deterministic defaults.
            }
        }
        return null;
        }
        finally
        {
            _bindingDepth--;
        }
    }

    private static Delegate EmptyDelegate(Type delegateType)
    {
        var invoke = delegateType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
        Expression body = invoke.ReturnType == typeof(void)
            ? Expression.Empty()
            : Expression.Constant(ValueFor(invoke.ReturnType, "open"), invoke.ReturnType);
        return Expression.Lambda(delegateType, body, parameters).Compile();
    }

    private static object? BackstageCallbackValue(Type type, string? name)
    {
        if (type == typeof(string))
            return name switch
            {
                "DisplayName" => "Harness document",
                _ => string.Empty,
            };
        if (type.Name == "BackstageDirectPrintCapability")
            return ValueFor(type, "open");
        if (typeof(Delegate).IsAssignableFrom(type))
            return EmptyDelegate(type);
        return ValueFor(type, "open");
    }
}
