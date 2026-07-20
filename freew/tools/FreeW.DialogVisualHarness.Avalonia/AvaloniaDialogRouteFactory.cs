using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Avalonia.Controls;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
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
        ["cell-edit"] = "CellEditDialog",
        ["chart-axis-titles"] = "ChartAxisTitlesDialog",
        ["chart-size"] = "ChartSizeDialog",
        ["chart-title"] = "ChartTitleDialog",
        ["citation-source-picker"] = "CitationSourcePickerDialog",
        ["comment-list"] = "CommentListDialog",
        ["comment-reply"] = "CommentReplyDialog",
        ["compare-documents"] = "CompareDocumentsDialog",
        ["cross-reference"] = "CrossReferenceDialog",
        ["customize-theme-colors"] = "CustomizeThemeColorsDialog",
        ["customize-theme-fonts"] = "CustomizeThemeFontsDialog",
        ["date-time"] = "DateTimeDialog",
        ["document-inspector"] = "DocumentInspectorDialog",
        ["draw-table-dimension"] = "DrawTableDimensionDialog",
        ["field-picker"] = "FieldPickerDialog",
        ["find-replace"] = "FindReplaceDialog",
        ["font"] = "FontDialog",
        ["footnote-endnote-options"] = "FootnoteEndnoteOptionsDialog",
        ["about"] = "FreeWInfoDialog",
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
        ["link-bookmark"] = "LinkBookmarkDialog",
        ["manage-sources"] = "ManageSourcesDialog",
        ["manage-styles"] = "ManageStylesDialog",
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
        ["table-of-authorities"] = "TableOfAuthoritiesDialog",
        ["table-text-conversion"] = "TableTextConversionDialog",
        ["tabs"] = "TabsDialog",
        ["theme-effects"] = "ThemeEffectsDialog",
        ["thesaurus"] = "ThesaurusDialog",
        ["watermark"] = "WatermarkDialog",
        ["word-count"] = "WordCountDialog",
        ["zoom"] = "ZoomDialog",
    };

    public static Window? Create(string routeId, string state)
    {
        if (routeId is "options" or "page-setup" or "columns" or "custom-paragraph-spacing" or "drop-cap-options" or "hyphenation-options" or "line-number-options")
            return CreateKnown(routeId, state);

        if (routeId.StartsWith("backstage-", StringComparison.OrdinalIgnoreCase))
            return CreateBackstage(routeId);

        if (routeId == "notes-pane")
            return CreateType("NotesPane", state);
        if (routeId == "cups-print")
            return CreateCupsPrint();
        if (routeId == "compare-documents")
            return CreateCompareDocuments(state);

        if (!DialogTypes.TryGetValue(routeId, out var typeName))
            return null;
        return CreateType(typeName, state);
    }

    private static Window CreateKnown(string routeId, string state)
    {
        var assembly = typeof(MainWindow).Assembly;
        if (routeId == "options")
            return (Window)Activator.CreateInstance(assembly.GetType("FreeW.App.Avalonia.OptionsDialog", true)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [new FreeWOptions()], null)!;
        if (routeId == "page-setup")
            return new PageSetupDialog(new PageSettings());

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
        var callbacksType = assembly.GetType("FreeW.App.Avalonia.Backstage.BackstageCallbacks", true)!;
        var callbackConstructor = callbacksType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderByDescending(candidate => candidate.GetParameters().Length).First();
        var callbackValues = callbackConstructor.GetParameters()
            .Select(parameter => BackstageCallbackValue(parameter.ParameterType, parameter.Name))
            .ToArray();
        var callbacks = callbackConstructor.Invoke(callbackValues);
        var paneType = assembly.GetType("FreeW.App.Avalonia.Backstage.BackstagePane", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderByDescending(candidate => candidate.GetParameters().Length).First();
        if (routeId.Equals("backstage-print", StringComparison.OrdinalIgnoreCase))
        {
            // Build the real app-owned pane without activating the action-bearing Backstage entry.
            // The entry invokes host print plumbing when activated; the pane itself is safe to render.
            var home = Enum.Parse(paneType, "Home");
            var backstage = (Window)constructor.Invoke([callbacks, home]);
            var buildPrintPane = type.GetMethod("BuildPrintPane", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(type.FullName, "BuildPrintPane");
            var control = (Control)buildPrintPane.Invoke(backstage, null)!;
            backstage.Close();
            return WrapControl(control);
        }
        var pane = Enum.Parse(paneType, routeId switch
        {
            "backstage-open" => "Open",
            "backstage-save-as" => "SaveAs",
            "backstage-print" => "Print",
            "backstage-share" => "Share",
            "backstage-export" => "Export",
            "backstage-info" => "Info",
            "backstage-account" => "Account",
            _ => "Home",
        });
        return (Window)constructor.Invoke([callbacks, pane]);
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

    private static Window CreateCompareDocuments(string state)
    {
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType("FreeW.App.Avalonia.CompareDocumentsDialog", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .First();
        var arguments = constructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(string)
                ? "C:\\Harness\\Original.docx"
                : ValueFor(parameter.ParameterType, state)).ToArray();
        return (Window)constructor.Invoke(arguments);
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
