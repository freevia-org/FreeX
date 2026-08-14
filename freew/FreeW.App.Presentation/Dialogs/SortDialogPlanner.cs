using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record SortDialogChoice<TValue>(TValue Value, string Label);

public readonly record struct SortDialogKey(SortKind Kind, bool Ascending);

public readonly record struct SortDialogResult(
    SortDialogKey Key1,
    SortDialogKey? Key2,
    SortDialogKey? Key3,
    bool CaseSensitive,
    bool HasHeaderRow)
{
    public SortKind Kind => Key1.Kind;

    public bool Ascending => Key1.Ascending;
}

public sealed record SortDialogInput(
    int Key1TypeIndex,
    bool Key1Ascending,
    bool UseKey2,
    int Key2TypeIndex,
    bool Key2Ascending,
    bool UseKey3,
    int Key3TypeIndex,
    bool Key3Ascending,
    bool CaseSensitive,
    bool HasHeaderRow);

public sealed record SortDialogEnabledState(bool Key2Enabled, bool Key3Enabled);

/// <summary>
/// WPF-authority geometry for the paired Sort dialogs. Renderers translate these neutral values
/// into native controls while the shared session continues to own state and acceptance behavior.
/// </summary>
public static class SortDialogVisualMetrics
{
    public const double WindowWidth = 380;
    public const double RootInset = 14;
    public const double PromptBottomMargin = 10;
    public const double PrimaryHeadingBottomMargin = 4;
    public const double OptionalKeyTopMargin = 8;
    public const double OptionalKeyBottomMargin = 4;
    public const double TypeMinimumWidth = 120;
    public const double TypeControlBottomMargin = 4;
    public const double KeyRowBottomMargin = 4;
    public const double TypeLabelTrailingMargin = 8;
    public const double RadioLeftMargin = 4;
    public const double AscendingRightMargin = 8;
    public const double RadioBottomMargin = 4;
    public const double CaseSensitiveTopMargin = 10;
    public const double CaseSensitiveBottomMargin = 4;
    public const double ActionButtonWidth = 72;
    public const double ActionRowTopMargin = 14;
    public const double ActionSpacing = 8;
}

public sealed class SortDialogSession
{
    public SortDialogSession(bool forTable)
    {
        Prompt = SortDialogPlanner.PromptLabel(forTable);
    }

    public string Prompt { get; }

    public IReadOnlyList<SortDialogChoice<SortKind>> TypeChoices => SortDialogPlanner.TypeChoices;

    public SortDialogEnabledState PlanEnabledState(bool useKey2, bool useKey3) =>
        new(useKey2, useKey3);

    public SortDialogResult PlanAcceptance(SortDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SortDialogPlanner.BuildResult(
            input.Key1TypeIndex,
            input.Key1Ascending,
            input.UseKey2,
            input.Key2TypeIndex,
            input.Key2Ascending,
            input.UseKey3,
            input.Key3TypeIndex,
            input.Key3Ascending,
            input.CaseSensitive,
            input.HasHeaderRow);
    }
}

/// <summary>
/// Word "Sort Text" dialog planning: a fixed primary key plus two optional keys, whose identity is
/// the <em>data type</em> the key text is interpreted as (<see cref="SortKind"/> Text/Number/Date),
/// with an ascending flag, a case-sensitive flag, and a "my list has a header row" flag, projected
/// into a <see cref="SortDialogResult"/> the host feeds to <c>ParagraphSort</c> via
/// <c>SortCaretTableRows</c>/<c>SortSelectedParagraphs</c>. The sorted-on column is implicit (the
/// caret's column) rather than chosen in the dialog.
/// <para>
/// Cross-app note (assessed 2026-08-14): <c>FreeX.App.Services.SortDialogPlanner</c> shares only
/// this type's <em>name</em>. The spreadsheet planner keys an unbounded level list by column/row
/// offset within a grid range, adds a "Sort On" criterion with color/icon targets, a custom
/// first-key list and a left-to-right axis, and turns "has headers" into range geometry rather than
/// a pass-through flag. Neither planner defines any validation-error taxonomy. Ignoring braces and
/// short lines, the two files share exactly one identical line — the <c>public static class</c>
/// declaration. There is no stable neutral contract to extract; do not merge them.
/// </para>
/// </summary>
public static class SortDialogPlanner
{
    public const string Title = "Sort";
    public const string SortByLabel = "Sort by";
    public const string ThenByLabel = "Then by";
    public const string ThenBySecondLabel = "Then by (2nd)";
    public const string TypeLabel = "Type:";
    public const string AscendingLabel = "Ascending";
    public const string DescendingLabel = "Descending";
    public const string CaseSensitiveLabel = "Case sensitive";
    public const string HeaderRowLabel = "My list has a header row";
    public const string AcceptButtonLabel = "OK";
    public const string CancelButtonLabel = "Cancel";
    public const string AutomationId = "SortDialog";
    public const string Key1TypeAutomationId = "SortKey1TypeComboBox";
    public const string Key2TypeAutomationId = "SortKey2TypeComboBox";
    public const string Key3TypeAutomationId = "SortKey3TypeComboBox";

    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
    [
        new(AcceptButtonLabel, IsDefault: true),
        new(CancelButtonLabel, IsCancel: true),
    ];
    public static readonly IReadOnlyList<SortDialogChoice<SortKind>> TypeChoices =
    [
        new(SortKind.Text, "Text"),
        new(SortKind.Number, "Number"),
        new(SortKind.Date, "Date")
    ];

    public static string PromptLabel(bool forTable) =>
        forTable
            ? "Sort the table rows by the current column:"
            : "Sort the selected paragraphs:";

    public static SortDialogResult BuildResult(
        int key1TypeIndex,
        bool key1Ascending,
        bool useKey2,
        int key2TypeIndex,
        bool key2Ascending,
        bool useKey3,
        int key3TypeIndex,
        bool key3Ascending,
        bool caseSensitive,
        bool hasHeaderRow) =>
        new(
            new SortDialogKey(KindAt(key1TypeIndex), key1Ascending),
            useKey2 ? new SortDialogKey(KindAt(key2TypeIndex), key2Ascending) : null,
            useKey3 ? new SortDialogKey(KindAt(key3TypeIndex), key3Ascending) : null,
            caseSensitive,
            hasHeaderRow);

    private static SortKind KindAt(int index) =>
        TypeChoices[Math.Clamp(index, 0, TypeChoices.Count - 1)].Value;
}
