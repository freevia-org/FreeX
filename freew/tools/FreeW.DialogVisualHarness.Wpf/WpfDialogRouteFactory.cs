using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using FreeW.DialogVisualHarness;
using LinqExpression = System.Linq.Expressions.Expression;

internal static class WpfDialogRouteFactory
{
    [ThreadStatic]
    private static int _bindingDepth;

    public static Window? Create(string routeId, string state, Window owner)
    {
        if (!FreeWDialogEvidenceCatalog.TryGet(routeId, out var route) || route.Wpf is null)
            return null;

        switch (route.Wpf.OpenAction)
        {
            case FreeWDialogOpenAction.BackstagePane:
                return CreateBackstage(route);
            case FreeWDialogOpenAction.ScreenClipOverlay:
                return CreateScreenClipOverlay(owner);
            case FreeWDialogOpenAction.BookmarkManager:
                return CreateBookmarkManager(state, owner);
            case FreeWDialogOpenAction.ManualHyphenation:
                return CreateManualHyphenation(state, owner);
            case FreeWDialogOpenAction.StaticPrompt:
                return null;
            case FreeWDialogOpenAction.ReflectedDialog:
                break;
            default:
                throw new InvalidOperationException($"Unsupported WPF dialog harness action {route.Wpf.OpenAction} for {routeId}.");
        }

        var typeName = route.Wpf.DialogTypeName;
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType($"FreeW.App.Host.{typeName}", false)
            ?? assembly.GetTypes().FirstOrDefault(candidate => candidate.Name.Equals(typeName, StringComparison.Ordinal));
        if (type is null || type.IsAbstract || type.IsStatic()) return null;

        Exception? last = null;
        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(candidate => candidate.GetParameters().Length))
        {
            try
            {
                var value = constructor.Invoke(constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType, state, owner)).ToArray());
                if (value is Window window)
                {
                    if (window.Owner is null && window != owner) window.Owner = owner;
                    return window;
                }
            }
            catch (Exception ex)
            {
                last = ex.InnerException ?? ex;
            }
        }
        throw new InvalidOperationException($"No constructible WPF adapter for {typeName}: {last?.GetType().Name}: {last?.Message}", last);
    }

    private static Window CreateBookmarkManager(string state, Window owner)
    {
        var editor = new DocumentView();
        if (!state.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            editor.Model.Blocks.Clear();
            editor.Model.Blocks.Add(new Paragraph("First target") { BookmarkNames = { "FirstTarget" } });
            editor.Model.Blocks.Add(new Paragraph("Second target") { BookmarkNames = { "SecondTarget" } });
            editor.Rerender();
        }

        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Host.BookmarkManagerDialog", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 2);
        return (Window)constructor.Invoke([owner, editor]);
    }

    private static Window CreateManualHyphenation(string state, Window owner)
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("characterization"));
        var candidate = ManualHyphenationPlanner.CreateSession(editor.Model).Current
            ?? throw new InvalidOperationException("The manual-hyphenation harness fixture did not produce a real candidate.");

        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Host.ManualHyphenationDialog", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidateConstructor => candidateConstructor.GetParameters().Length == 2);
        return (Window)constructor.Invoke([owner, candidate]);
    }

    public static bool IsStaticPromptRoute(string routeId) =>
        FreeWDialogEvidenceCatalog.IsStaticPrompt(routeId, FreeWDialogHost.Wpf);

    public static void InvokeStaticPrompt(string routeId, string state, Window owner)
    {
        var route = FreeWDialogEvidenceCatalog.GetRequired(routeId);
        var hostRoute = route.Wpf;
        if (hostRoute?.OpenAction != FreeWDialogOpenAction.StaticPrompt || hostRoute.EntryPointName is null)
            throw new ArgumentOutOfRangeException(nameof(routeId));

        var assembly = typeof(MainWindow).Assembly;
        var arguments = route.Fixture switch
        {
            FreeWDialogFixtureKind.DefaultRunFormatting => new object?[] { owner, DefaultValue(typeof(RunFormatting)) },
            FreeWDialogFixtureKind.DefaultParagraphFormatting => new object?[] { owner, DefaultValue(typeof(ParagraphFormatting)) },
            FreeWDialogFixtureKind.EmptyListFormats => new object?[] { owner, Array.Empty<ListNumberFormat>() },
            FreeWDialogFixtureKind.HarnessClipboardText => new object?[] { owner },
            FreeWDialogFixtureKind.StyleCatalog => new object?[] { owner, StyleCatalogForState(state), null },
            FreeWDialogFixtureKind.EmptyTextDocument => new object?[] { owner, new TextDocument(), null },
            _ => throw new ArgumentOutOfRangeException(nameof(routeId)),
        };
        var type = assembly.GetType($"FreeW.App.Host.{hostRoute.DialogTypeName}", true)!;
        var method = type.GetMethod(hostRoute.EntryPointName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, hostRoute.EntryPointName);
        if (route.Fixture == FreeWDialogFixtureKind.HarnessClipboardText)
            System.Windows.Clipboard.SetText("Harness clipboard text");
        method.Invoke(null, arguments);
    }

    private static object? DefaultValue(Type type) =>
        type.GetField("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? type.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? Activator.CreateInstance(type);

    private static IReadOnlyDictionary<string, string> StyleCatalogForState(string state) =>
        state == "populated"
            ? new Dictionary<string, string>
            {
                ["Normal"] = "Normal",
                ["Heading1"] = "Heading 1",
            }
            : new Dictionary<string, string>();

    private static Window CreateBackstage(FreeWDialogEvidenceRoute route)
    {
        var shell = new MainWindow(new FreeWOptions());
        var backstageField = typeof(MainWindow).GetField("_backstage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(MainWindow).FullName, "_backstage");
        var backstage = backstageField.GetValue(shell)
            ?? throw new InvalidOperationException("WPF BackstageView was not initialized by MainWindow.");
        var methodName = route.BackstageMethodName
            ?? throw new InvalidOperationException($"Backstage route {route.RouteId} has no pane builder.");
        var method = backstage.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(backstage.GetType().FullName, methodName);
        var content = (System.Windows.UIElement)method.Invoke(backstage, null)!;
        shell.Close();
        return new Window
        {
            Title = "FreeW Backstage",
            Width = 720,
            Height = 600,
            Content = content,
            ShowInTaskbar = false,
        };
    }

    private static Window CreateScreenClipOverlay(Window owner)
    {
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Host.Editing.ScreenClipOverlay", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var overlay = (Window)constructor.Invoke(null);
        var canvas = (Canvas)overlay.Content;
        overlay.Content = null;
        var selection = (Rectangle)type.GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
        Canvas.SetLeft(selection, 80);
        Canvas.SetTop(selection, 90);
        selection.Width = 280;
        selection.Height = 210;
        selection.Visibility = Visibility.Visible;
        var surface = new Grid { Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xF0)) };
        surface.Children.Add(new Border { Background = overlay.Background });
        surface.Children.Add(canvas);
        return new Window
        {
            Owner = owner,
            Width = 560,
            Height = 600,
            Content = surface,
            Title = "Screen Clip Overlay Capture",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
    }

    private static object? ValueFor(Type type, string state, Window owner)
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
            if (typeof(Window).IsAssignableFrom(type)) return owner;
            if (type == typeof(string)) return state == "populated" ? "Sample text" : string.Empty;
            if (type == typeof(FreeWOptions)) return new FreeWOptions();
            if (type == typeof(PageSettings)) return new PageSettings();
            if (type == typeof(SectionBreakKind)) return PageSetupDialogPlanner.VisualHarnessSectionStart;
            if (type == typeof(TextDocument)) return new TextDocument();
            if (type == typeof(DocumentView)) return new DocumentView();
            if (type.FullName == "Free.Shared.Opc.DocumentProperties") return Activator.CreateInstance(type);
            if (type.Name == "ModelTableContext")
            {
                var table = new Table();
                var row = new TableRow();
                var cell = new TableCell();
                row.Cells.Add(cell);
                table.Rows.Add(row);
                return Activator.CreateInstance(type, table, row, cell);
            }
            if (type == typeof(DateTime)) return new DateTime(2025, 1, 1);
            if (type == typeof(CultureInfo)) return CultureInfo.CurrentCulture;
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
            if (typeof(Delegate).IsAssignableFrom(type)) return EmptyDelegate(type, state, owner);
            if (type.IsValueType || type == typeof(object)) return type == typeof(object) ? null : Activator.CreateInstance(type);
            var defaultProperty = type.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
            if (defaultProperty is not null && type.IsAssignableFrom(defaultProperty.PropertyType)) return defaultProperty.GetValue(null);
            var isAppModel = type.Namespace?.StartsWith("FreeW.Core.Model", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("FreeW.App.Presentation", StringComparison.Ordinal) == true;
            if (!isAppModel) return null;
            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(candidate => candidate.GetParameters().Length))
            {
                try { return constructor.Invoke(constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType, state, owner)).ToArray()); }
                catch { }
            }
            return null;
        }
        finally { _bindingDepth--; }
    }

    private static Delegate EmptyDelegate(Type delegateType, string state, Window owner)
    {
        var invoke = delegateType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters().Select(parameter => LinqExpression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
        LinqExpression body = invoke.ReturnType == typeof(void)
            ? LinqExpression.Empty()
            : LinqExpression.Constant(ValueFor(invoke.ReturnType, state, owner), invoke.ReturnType);
        return LinqExpression.Lambda(delegateType, body, parameters).Compile();
    }
}

file static class TypeExtensions
{
    public static bool IsStatic(this Type type) => type.IsAbstract && type.IsSealed;
}
