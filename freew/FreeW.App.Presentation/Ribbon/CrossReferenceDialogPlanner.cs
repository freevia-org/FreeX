using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CrossReferenceTypeChoice(CrossRefType Type, string Label)
{
    public override string ToString() => Label;
}

public sealed record CrossReferenceInsertAsChoice(CrossRefInsertAs InsertAs, string Label)
{
    public override string ToString() => Label;
}

public sealed record CrossReferenceTargetChoice(CrossRefTarget Target, string Label)
{
    public override string ToString() => Label;
}

public sealed record CrossReferenceDialogChoice(
    CrossRefType Type,
    CrossRefTarget Target,
    CrossRefInsertAs InsertAs,
    bool Hyperlink);

/// <summary>
/// WPF-authority geometry for the paired Cross-reference dialogs. Avalonia-prefixed values are
/// native-template compensations required to realize that same layout.
/// </summary>
public readonly record struct CrossReferenceDialogVisualMetrics(
    double TypeListMinWidth,
    double InsertAsListMinWidth,
    double TargetListMinWidth,
    double ChoiceListHeight,
    double TargetListHeight,
    double HyperlinkTopMargin,
    double TopRowBottomMargin,
    double ColumnSpacing,
    double LabelTopMargin,
    double LabelBottomMargin,
    double OuterMargin,
    double ActionButtonWidth,
    double ActionRowTopMargin,
    double AvaloniaListItemHeight,
    string AvaloniaInactiveSelectionBackgroundHex,
    string AvaloniaInactiveSelectionBorderHex);

public static class CrossReferenceDialogPlanner
{
    public const string AutomationId = "CrossReferenceDialog";
    public const string TypeAutomationId = "CrossReferenceTypeList";
    public const string InsertAsAutomationId = "CrossReferenceInsertAsList";
    public const string TargetAutomationId = "CrossReferenceTargetList";
    public const string HyperlinkAutomationId = "CrossReferenceHyperlinkCheckBox";
    public const string Title = "Cross-reference";
    public const string AcceptButtonLabel = "OK";
    public const string CancelButtonLabel = "Cancel";
    public const string ReferenceTypeLabel = "Reference type:";
    public const string InsertReferenceToLabel = "Insert reference to:";
    public const string TargetLabel = "For which item:";
    public const string HyperlinkLabel = "Insert as hyperlink";
    public const string MissingTargetMessage = "Select an item to reference.";

    public static CrossReferenceDialogVisualMetrics VisualMetrics { get; } = new(
        TypeListMinWidth: 150,
        InsertAsListMinWidth: 180,
        TargetListMinWidth: 300,
        ChoiceListHeight: 170,
        TargetListHeight: 200,
        HyperlinkTopMargin: 10,
        TopRowBottomMargin: 10,
        ColumnSpacing: 12,
        LabelTopMargin: 8,
        LabelBottomMargin: 4,
        OuterMargin: 16,
        ActionButtonWidth: 80,
        ActionRowTopMargin: 14,
        AvaloniaListItemHeight: 21,
        AvaloniaInactiveSelectionBackgroundHex: "#F0F0F0",
        AvaloniaInactiveSelectionBorderHex: "#ABADB3");

    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
    [
        new(AcceptButtonLabel, IsDefault: true),
        new(CancelButtonLabel, IsCancel: true),
    ];

    private static readonly IReadOnlyList<CrossRefType> TypeOrder =
    [
        CrossRefType.Heading,
        CrossRefType.Bookmark,
        CrossRefType.Figure,
        CrossRefType.Table,
        CrossRefType.Equation,
        CrossRefType.Footnote,
        CrossRefType.Endnote,
        CrossRefType.NumberedItem
    ];

    public static IReadOnlyList<CrossReferenceTypeChoice> BuildTypeChoices() =>
        TypeOrder
            .Select(type => new CrossReferenceTypeChoice(type, LabelForType(type)))
            .ToArray();

    public static IReadOnlyList<CrossReferenceInsertAsChoice> BuildInsertAsChoices(CrossRefType type) =>
        CrossReferences.InsertOptions(type)
            .Select(insertAs => new CrossReferenceInsertAsChoice(insertAs, LabelForInsertAs(type, insertAs)))
            .ToArray();

    public static IReadOnlyList<CrossReferenceTargetChoice> BuildTargetChoices(
        TextDocument document,
        CrossRefType type)
    {
        ArgumentNullException.ThrowIfNull(document);

        return CrossReferences.Targets(document, type)
            .Select(target => new CrossReferenceTargetChoice(target, target.Display))
            .ToArray();
    }

    public static int PreserveInsertAsSelection(
        IReadOnlyList<CrossReferenceInsertAsChoice> choices,
        CrossRefInsertAs? previous)
    {
        ArgumentNullException.ThrowIfNull(choices);

        if (choices.Count == 0)
            return -1;

        if (previous is { } value)
        {
            for (var i = 0; i < choices.Count; i++)
            {
                if (choices[i].InsertAs == value)
                    return i;
            }
        }

        return 0;
    }

    public static bool TryCreateChoice(
        TextDocument document,
        CrossRefType type,
        CrossRefInsertAs insertAs,
        int selectedTargetIndex,
        bool hyperlink,
        out CrossReferenceDialogChoice? choice)
    {
        var targets = BuildTargetChoices(document, type);
        if (selectedTargetIndex < 0 || selectedTargetIndex >= targets.Count)
        {
            choice = null;
            return false;
        }

        choice = CreateChoice(type, targets[selectedTargetIndex].Target, insertAs, hyperlink);
        return true;
    }

    public static CrossReferenceDialogChoice CreateChoice(
        CrossRefType type,
        CrossRefTarget target,
        CrossRefInsertAs insertAs,
        bool hyperlink) =>
        new(type, target, insertAs, hyperlink);

    public static string LabelForType(CrossRefType type) =>
        type switch
        {
            CrossRefType.NumberedItem => "Numbered item",
            _ => type.ToString()
        };

    public static string LabelForInsertAs(CrossRefInsertAs insertAs) =>
        insertAs switch
        {
            CrossRefInsertAs.Text => "Text",
            CrossRefInsertAs.PageNumber => "Page number",
            CrossRefInsertAs.HeadingNumber => "Heading number",
            CrossRefInsertAs.AboveBelow => "Above/below",
            CrossRefInsertAs.ParagraphNumber => "Paragraph number",
            CrossRefInsertAs.CaptionLabelAndNumber => "Only label and number",
            CrossRefInsertAs.CaptionText => "Only caption text",
            _ => insertAs.ToString()
        };

    public static string LabelForInsertAs(CrossRefType type, CrossRefInsertAs insertAs) =>
        type is CrossRefType.Figure or CrossRefType.Table or CrossRefType.Equation
        && insertAs == CrossRefInsertAs.Text
            ? "Entire caption"
            : LabelForInsertAs(insertAs);
}
