using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum StyleDialogSortOrder
{
    Alphabetical,
    ByType,
    ByUse,
}

public enum StyleDialogValidationError
{
    EmptyName,
}

public sealed record StyleDialogFontSizeChoice(string Label, double? Points);

public sealed record StyleDialogColorChoice(string Label, string? Hex);

public sealed record StyleDialogInput(
    string? Name,
    string? BasedOnId,
    string? NextStyleId,
    bool Bold,
    bool Italic,
    bool Underline,
    int FontSizeIndex,
    int ColorIndex,
    int AlignmentIndex);

public sealed record StyleDefinitionResult(
    string Name,
    string? BasedOnId,
    RunFormatting Run,
    ParagraphFormatting Paragraph,
    string? NextStyleId);

public sealed record StyleDialogRow(string Id, string Display, bool IsBuiltIn);

public sealed record StyleDialogInitialState(
    string Title,
    string Name,
    bool NameIsReadOnly,
    IReadOnlyList<KeyValuePair<string, string>> BasedOnOptions,
    int BasedOnIndex,
    IReadOnlyList<KeyValuePair<string, string>> NextStyleOptions,
    int NextStyleIndex,
    bool Bold,
    bool Italic,
    bool Underline,
    int FontSizeIndex,
    int ColorIndex,
    int AlignmentIndex);

public sealed record StyleDialogControlState(
    string? Name,
    int BasedOnIndex,
    int NextStyleIndex,
    bool Bold,
    bool Italic,
    bool Underline,
    int FontSizeIndex,
    int ColorIndex,
    int AlignmentIndex);

public sealed record StyleDialogAcceptance(
    StyleDefinitionResult? Result,
    string? ErrorMessage)
{
    public bool IsAccepted => Result is not null && ErrorMessage is null;
}

/// <summary>
/// Owns New/Modify Style option projection and acceptance for the paired desktop dialogs.
/// </summary>
public sealed class StyleDialogSession
{
    public const string ValidationTitle = "New Style";

    private readonly RunFormatting _seedRun;
    private readonly ParagraphFormatting _seedParagraph;

    internal StyleDialogSession(
        string title,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? fixedName,
        string? defaultBasedOnId,
        RunFormatting seedRun,
        ParagraphFormatting seedParagraph,
        string? defaultNextStyleId)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(styleNamesById);
        ArgumentNullException.ThrowIfNull(seedRun);
        ArgumentNullException.ThrowIfNull(seedParagraph);

        _seedRun = seedRun;
        _seedParagraph = seedParagraph;
        var basedOnOptions = StyleDialogPlanner.BuildStyleOptions(styleNamesById, "(none)");
        var nextStyleOptions = StyleDialogPlanner.BuildStyleOptions(styleNamesById, "(same style)");
        InitialState = new StyleDialogInitialState(
            title,
            fixedName ?? string.Empty,
            NameIsReadOnly: fixedName is not null,
            basedOnOptions,
            IndexOfId(basedOnOptions, defaultBasedOnId),
            nextStyleOptions,
            IndexOfId(nextStyleOptions, defaultNextStyleId),
            seedRun.Bold,
            seedRun.Italic,
            seedRun.Underline,
            StyleDialogPlanner.IndexOfSize(seedRun.FontSizePt),
            StyleDialogPlanner.IndexOfColor(seedRun.ColorHex),
            (int)seedParagraph.Alignment);
    }

    public StyleDialogInitialState InitialState { get; }

    public StyleDialogAcceptance PlanAcceptance(StyleDialogControlState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var input = new StyleDialogInput(
            state.Name,
            SelectedId(InitialState.BasedOnOptions, state.BasedOnIndex),
            SelectedId(InitialState.NextStyleOptions, state.NextStyleIndex),
            state.Bold,
            state.Italic,
            state.Underline,
            state.FontSizeIndex,
            state.ColorIndex,
            state.AlignmentIndex);

        return StyleDialogPlanner.TryBuildDefinition(
            input,
            _seedRun,
            _seedParagraph,
            out var result,
            out var validation)
            ? new StyleDialogAcceptance(result, ErrorMessage: null)
            : new StyleDialogAcceptance(
                Result: null,
                StyleDialogPlanner.ValidationMessageFor(validation));
    }

    private static string? SelectedId(IReadOnlyList<KeyValuePair<string, string>> entries, int index) =>
        index > 0 && index < entries.Count ? entries[index].Value : null;

    private static int IndexOfId(IReadOnlyList<KeyValuePair<string, string>> entries, string? id)
    {
        if (string.IsNullOrEmpty(id))
            return 0;

        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i].Value == id)
                return i;
        }

        return 0;
    }
}

/// <summary>
/// Shared geometry for the compact New/Modify Style dialog. Keeping these values in the
/// presentation layer makes the WPF and Avalonia shells consume the same layout contract while
/// retaining their native control implementations.
/// </summary>
public static class StyleDialogMetrics
{
    public const double DialogMargin = 16;
    public const double FieldBottomMargin = 10;
    public const double NameTextBoxHeight = 20;
    // WPF's native ComboBox template measures these fields at 22 logical pixels.
    public const double ComboBoxHeight = 22;
    // The three formatting toggles occupy a 15-pixel WPF checkbox row.
    public const double CheckBoxHeight = 15;
    // The WPF shared button row paints a 20-pixel button surface in this dialog.
    public const double ButtonHeight = 20;
    public const double ActionRowTopMargin = 12;
}

public abstract record ManageStyleAction
{
    public sealed record Apply(string StyleId) : ManageStyleAction;
    public sealed record Modify(string StyleId) : ManageStyleAction;
    public sealed record Delete(string StyleId) : ManageStyleAction;
}

public enum ManageStyleActionKind
{
    Apply,
    Modify,
    Delete,
}

public sealed record ManageStyleButtonState(bool ApplyEnabled, bool ModifyEnabled, bool DeleteEnabled);

public sealed record ManageStylesDialogState(
    StyleDialogSortOrder SortOrder,
    IReadOnlyList<StyleDialogRow> Rows,
    int SelectedIndex,
    ManageStyleButtonState Buttons)
{
    public StyleDialogRow? SelectedRow =>
        SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null;
}

/// <summary>
/// Owns list ordering, selection preservation, button state, and action acceptance for Manage Styles.
/// </summary>
public sealed class ManageStylesDialogSession
{
    public const string Title = "Manage Styles";

    private readonly TextDocument _document;

    internal ManageStylesDialogSession(TextDocument document, string? preselectStyleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        State = BuildState(StyleDialogSortOrder.Alphabetical, preselectStyleId);
    }

    public ManageStylesDialogState State { get; private set; }

    public ManageStylesDialogState PlanSort(int selectedIndex)
    {
        State = BuildState(StyleDialogPlanner.SortOrderForIndex(selectedIndex), State.SelectedRow?.Id);
        return State;
    }

    public ManageStylesDialogState SelectRow(int selectedIndex)
    {
        var normalizedIndex = selectedIndex >= 0 && selectedIndex < State.Rows.Count ? selectedIndex : -1;
        State = State with
        {
            SelectedIndex = normalizedIndex,
            Buttons = ButtonsFor(normalizedIndex >= 0 ? State.Rows[normalizedIndex] : null),
        };
        return State;
    }

    public ManageStyleAction? PlanAction(ManageStyleActionKind kind, int selectedIndex)
    {
        var row = SelectRow(selectedIndex).SelectedRow;
        return (kind, row) switch
        {
            (_, null) => null,
            (ManageStyleActionKind.Apply, { } selected) => new ManageStyleAction.Apply(selected.Id),
            (ManageStyleActionKind.Modify, { } selected) => new ManageStyleAction.Modify(selected.Id),
            (ManageStyleActionKind.Delete, { IsBuiltIn: false } selected) =>
                new ManageStyleAction.Delete(selected.Id),
            _ => null,
        };
    }

    private ManageStylesDialogState BuildState(StyleDialogSortOrder order, string? selectedStyleId)
    {
        var rows = StyleDialogPlanner.BuildRows(_document, order);
        var selectedIndex = FindIndex(rows, selectedStyleId);
        if (selectedIndex < 0 && rows.Count > 0)
            selectedIndex = 0;

        return new ManageStylesDialogState(
            order,
            rows,
            selectedIndex,
            ButtonsFor(selectedIndex >= 0 ? rows[selectedIndex] : null));
    }

    private static ManageStyleButtonState ButtonsFor(StyleDialogRow? row) =>
        new(row is not null, row is not null, row is { IsBuiltIn: false });

    private static int FindIndex(IReadOnlyList<StyleDialogRow> rows, string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return -1;

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == styleId)
                return i;
        }

        return -1;
    }
}

/// <summary>
/// Shared planning for Word-style New Style / Modify Style / Manage Styles dialog surfaces.
/// It keeps validation, palette choices, and sort semantics independent of WPF or Avalonia UI.
/// </summary>
public static class StyleDialogPlanner
{
    public static readonly IReadOnlyList<StyleDialogFontSizeChoice> FontSizes =
    [
        new("(default)", null),
        new("8", 8),
        new("9", 9),
        new("10", 10),
        new("11", 11),
        new("12", 12),
        new("14", 14),
        new("16", 16),
        new("18", 18),
        new("24", 24),
        new("28", 28),
        new("36", 36),
    ];

    public static readonly IReadOnlyList<StyleDialogColorChoice> Colors =
    [
        new("Automatic", null),
        new("Black", "#000000"),
        new("Dark Red", "#C00000"),
        new("Red", "#FF0000"),
        new("Blue accent", "#2F5496"),
        new("Blue", "#0070C0"),
        new("Green", "#00B050"),
        new("Purple", "#7030A0"),
        new("Grey", "#7F7F7F"),
    ];

    public static readonly IReadOnlyList<string> AlignmentLabels =
        ["Left", "Center", "Right", "Justify"];

    public static readonly IReadOnlyList<string> ManageStyleSortLabels =
        ["Alphabetical", "By type (built-ins first)", "By use (most-used first)"];

    public static StyleDialogSession CreateNewSession(
        IReadOnlyDictionary<string, string> styleNamesById,
        string? defaultBasedOnId) =>
        new(
            "New Style",
            styleNamesById,
            fixedName: null,
            defaultBasedOnId,
            RunFormatting.Default,
            ParagraphFormatting.Default,
            defaultNextStyleId: null);

    public static StyleDialogSession CreateSession(
        string title,
        IReadOnlyDictionary<string, string> styleNamesById,
        string? fixedName,
        string? defaultBasedOnId,
        RunFormatting seedRun,
        ParagraphFormatting seedParagraph,
        string? defaultNextStyleId) =>
        new(
            title,
            styleNamesById,
            fixedName,
            defaultBasedOnId,
            seedRun,
            seedParagraph,
            defaultNextStyleId);

    public static StyleDialogSession CreateModifySession(
        IReadOnlyDictionary<string, string> styleNamesById,
        DocumentStyle existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return new StyleDialogSession(
            $"Modify Style \u2014 {existing.Name}",
            styleNamesById,
            existing.Name,
            existing.BasedOnStyleId,
            existing.Run,
            existing.Paragraph,
            existing.NextStyleId);
    }

    public static IReadOnlyDictionary<string, string> BuildStyleNamesById(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Styles.ToDictionary(kv => kv.Key, kv => kv.Value.Name, StringComparer.Ordinal);
    }

    public static ManageStylesDialogSession CreateManageStylesSession(
        TextDocument document,
        string? preselectStyleId) =>
        new(document, preselectStyleId);

    public static StyleDialogSortOrder SortOrderForIndex(int selectedIndex) => selectedIndex switch
    {
        1 => StyleDialogSortOrder.ByType,
        2 => StyleDialogSortOrder.ByUse,
        _ => StyleDialogSortOrder.Alphabetical,
    };

    public static IReadOnlyList<KeyValuePair<string, string>> BuildStyleOptions(
        IReadOnlyDictionary<string, string> styleNamesById,
        string emptyLabel)
    {
        ArgumentNullException.ThrowIfNull(styleNamesById);

        var result = new List<KeyValuePair<string, string>> { new(emptyLabel, string.Empty) };
        result.AddRange(styleNamesById
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new KeyValuePair<string, string>(kv.Value, kv.Key)));
        return result;
    }

    public static IReadOnlyList<StyleDialogRow> BuildRows(TextDocument model, StyleDialogSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(model);

        var usageCounts = order == StyleDialogSortOrder.ByUse
            ? model.Paragraphs
                .GroupBy(paragraph => NormalizeStyleId(paragraph.StyleId) ?? "Normal", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = model.Styles.Values
            .Select(style =>
            {
                var builtIn = StyleManager.IsBuiltIn(style.Id);
                var display = builtIn ? $"{style.Name}  (built-in)" : style.Name;
                return new StyleDialogRow(style.Id, display, builtIn);
            });

        return order switch
        {
            StyleDialogSortOrder.ByType =>
                rows.OrderBy(row => row.IsBuiltIn ? 0 : 1)
                    .ThenBy(row => row.Display, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),

            StyleDialogSortOrder.ByUse =>
                rows.OrderByDescending(row => usageCounts.TryGetValue(row.Id, out var count) ? count : 0)
                    .ThenBy(row => row.Display, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),

            _ =>
                rows.OrderBy(row => row.Display, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
        };
    }

    public static bool TryBuildDefinition(
        StyleDialogInput input,
        RunFormatting seedRun,
        ParagraphFormatting seedParagraph,
        out StyleDefinitionResult? result,
        out StyleDialogValidationError? validation)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            result = null;
            validation = StyleDialogValidationError.EmptyName;
            return false;
        }

        var size = FontSizes[Math.Clamp(input.FontSizeIndex, 0, FontSizes.Count - 1)].Points;
        var color = Colors[Math.Clamp(input.ColorIndex, 0, Colors.Count - 1)].Hex;
        var alignment = (TextAlignment)Math.Clamp(input.AlignmentIndex, 0, AlignmentLabels.Count - 1);

        result = new StyleDefinitionResult(
            name,
            NormalizeStyleId(input.BasedOnId),
            seedRun with
            {
                Bold = input.Bold,
                Italic = input.Italic,
                Underline = input.Underline,
                FontSizePt = size,
                ColorHex = color,
            },
            seedParagraph with { Alignment = alignment },
            NormalizeStyleId(input.NextStyleId));
        validation = null;
        return true;
    }

    public static string ValidationMessageFor(StyleDialogValidationError? validation) =>
        validation switch
        {
            StyleDialogValidationError.EmptyName => "Please enter a style name.",
            _ => string.Empty,
        };

    public static int IndexOfSize(double? sizePt)
    {
        if (sizePt is not { } pt)
            return 0;

        for (var i = 0; i < FontSizes.Count; i++)
        {
            if (FontSizes[i].Points is { } candidate && Math.Abs(candidate - pt) < 0.01)
                return i;
        }

        return 0;
    }

    public static int IndexOfColor(string? hex)
    {
        if (hex is null)
            return 0;

        for (var i = 0; i < Colors.Count; i++)
        {
            if (string.Equals(Colors[i].Hex, hex, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private static string? NormalizeStyleId(string? styleId) =>
        string.IsNullOrWhiteSpace(styleId) ? null : styleId;
}
