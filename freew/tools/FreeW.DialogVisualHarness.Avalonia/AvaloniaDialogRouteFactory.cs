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
using FreeW.App.Avalonia.Backstage;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Printing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.Model;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.DialogVisualHarness;

internal static class AvaloniaDialogRouteFactory
{
    [ThreadStatic]
    private static int _bindingDepth;

    public static Window? Create(string routeId, string state, string? tab = null)
    {
        if (!FreeWDialogEvidenceCatalog.TryGet(routeId, out var route))
            return null;

        if (routeId.Equals("save-compatibility-warning", StringComparison.OrdinalIgnoreCase))
            return CreatePrivateWindow("SaveCompatibilityWarningDialog", CreateCompatibilityPlan());

        return route.Avalonia.OpenAction switch
        {
            FreeWDialogOpenAction.ReflectedDialog => CreateType(route.Avalonia.DialogTypeName, state),
            FreeWDialogOpenAction.KnownDialog => CreateType(route.Avalonia.DialogTypeName, state),
            FreeWDialogOpenAction.Options => CreateOptions(),
            FreeWDialogOpenAction.PageSetup => CreatePageSetup(),
            FreeWDialogOpenAction.BackstagePane => CreateBackstage(route),
            FreeWDialogOpenAction.BookmarkManager => CreateBookmarkManager(state),
            FreeWDialogOpenAction.NotesPane => CreateNotesPane(),
            FreeWDialogOpenAction.CupsPrint => CreateCupsPrint(),
            FreeWDialogOpenAction.CompareDocuments => CreateCompareDocuments(state, tab),
            FreeWDialogOpenAction.PasswordPrompt => CreatePasswordPrompt(),
            FreeWDialogOpenAction.ScreenClipOverlay => CreateScreenClipOverlay(),
            FreeWDialogOpenAction.TableFormula => CreateTableFormula(state),
            FreeWDialogOpenAction.TableProperties => CreateTableProperties(tab),
            FreeWDialogOpenAction.Style => CreateStyle(state),
            FreeWDialogOpenAction.CharacterFormattingPicker => CreateCharacterFormattingPicker(state),
            FreeWDialogOpenAction.ManualHyphenation => CreateManualHyphenation(state),
            _ => throw new InvalidOperationException($"Unsupported Avalonia dialog harness action {route.Avalonia.OpenAction} for {routeId}."),
        };
    }

    private static Window CreatePrivateWindow(string typeName, params object?[] arguments)
    {
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType($"FreeW.App.Avalonia.{typeName}", throwOnError: true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);
        return (Window)(constructor.Invoke(arguments)
            ?? throw new InvalidOperationException($"Avalonia visual-harness constructor returned null for {typeName}."));
    }

    private static DocumentSaveCompatibilityPlan CreateCompatibilityPlan() =>
        DocumentSaveCompatibilityPlan.Warning(
            "Word 97-2003 Document",
            "This document contains features that may not be supported by the selected file format.",
            [new DocumentSaveCompatibilityWarning(
                DocumentSaveCompatibilityWarningKind.CompatibilityTarget,
                "Compatibility check",
                "Continue to save using the selected format.")]);

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

        return new BookmarkManagerDialog(editor);
    }

    private static Window CreateOptions()
    {
        return new OptionsDialog(new FreeWOptions());
    }

    private static Window CreatePageSetup() => new PageSetupDialog(
        new PageSettings(),
        sectionStart: PageSetupDialogPlanner.VisualHarnessSectionStart);

    private static Window CreateBackstage(FreeWDialogEvidenceRoute route)
    {
        // Use the real production shell to obtain the same sample document,
        // recent-file workflow, file formats, and persisted options as the WPF
        // authority. Synthesizing empty callbacks makes the panes look unlike
        // the application users actually see.
        var shell = new MainWindow();
        var callbacks = shell.BuildBackstageCallbacks();
        // Keep this capture contract aligned with the WPF authority: invoke the
        // production pane builder and capture the pane in a neutral host. Capturing
        // the full Avalonia Backstage window here would compare the navigation rail
        // and frame chrome instead of the actual pane surface.
        BackstageView? backstage = null;
        try
        {
            backstage = new BackstageView(callbacks, BackstagePane.Home);
            return WrapControl(backstage.BuildPaneForVisualHarness(route.RouteId));
        }
        finally
        {
            backstage?.Close();
            shell.Close();
        }
    }

    private static Window CreateNotesPane()
    {
        var editor = new DocumentView();
        editor.LoadDocument(TextDocument.CreateEmpty());
        return WrapControl(NotesPane.CreateForVisualHarness(editor));
    }

    private static Window CreateType(string typeName, string state)
    {
        // The catalog intentionally proves that every generic dialog remains constructible.
        // Product-specific behavior and private UI state use typed harness access above.
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
        return state.Equals("populated", StringComparison.OrdinalIgnoreCase)
            ? CharacterFormattingPickerDialog.ForTestShading()
            : CharacterFormattingPickerDialog.ForTestBorder();
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

        return new ManualHyphenationDialog(candidate);
    }

    private static Window CreateCupsPrint()
    {
        return CupsPrintDialog.CreateForVisualHarness();
    }

    private static Window CreateCompareDocuments(string state, string? tab)
    {
        var promptState = new CompareDocumentsPromptState("Reviewer", "Revised.docx");
        var dialog = CompareDocumentsDialog.CreateForTest("C:\\Harness\\Original.docx", promptState);
        if (state == "validation-error")
            dialog.AcceptForTest(" ");
        if (tab?.Equals("More", StringComparison.OrdinalIgnoreCase) == true)
            dialog.GetLogicalDescendants().OfType<Expander>().Single(expander => expander.Header?.ToString() == "More").IsExpanded = true;
        return dialog;
    }

    private static Window CreatePasswordPrompt()
    {
        return PasswordPromptDialog.CreateForTest("Unprotect Document", "Enter the password:");
    }

    private static Window CreateScreenClipOverlay()
    {
        return ScreenClipOverlay.CreateForVisualHarness();
    }

    private static Window CreateTableFormula(string state)
    {
        var initialState = state == "initial"
            ? new TableFormulaDialogInitialState("=", 0)
            : new TableFormulaDialogInitialState("=SUM(ABOVE)", 3);
        var dialog = new TableFormulaDialog(initialState);
        if (state == "validation-error")
            dialog.AcceptForTest(" ", "0");
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
        var initialTab = Enum.Parse<TablePropertiesDialogTabKind>(tab ?? "Table", true);
        var dialog = new TablePropertiesDialog(context, initialTab);

        // Keep state setup in the shared harness Populate pass, exactly as WPF does.
        // The Avalonia adapter previously mutated these fields here, which made the
        // populated and validation captures represent different documents on each host.
        return dialog;
    }

    private static Window CreateStyle(string state)
    {
        var catalog = state == "populated"
            ? new Dictionary<string, string>
            {
                ["Normal"] = "Normal",
                ["Heading1"] = "Heading 1",
            }
            : new Dictionary<string, string>();
        var session = StyleDialogPlanner.CreateNewSession(catalog, defaultBasedOnId: null);
        return StyleDialog.CreateForVisualHarness(session);
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
