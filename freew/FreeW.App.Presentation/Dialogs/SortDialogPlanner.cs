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

public static class SortDialogPlanner
{
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
