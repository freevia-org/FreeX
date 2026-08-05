using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Owns the renderer-neutral mail-merge session and record shaping used by both FreeW hosts.
/// Renderers remain responsible for dialogs, editor mutations, focus, and native status presentation.
/// </summary>
public sealed class MailMergeSession
{
    public MergeData? Data { get; set; }

    public MailMergeOutputMode Mode { get; set; } = MailMergeOutputMode.Letters;

    public TextDocument? Template { get; set; }

    public int CurrentIndex { get; set; }

    public FieldMapping? Mapping { get; set; }

    public bool IsPreviewing => Template is not null;

    public MergeData Load(MergeData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Data = data;
        Mapping = MailMerge.AutoMatchFields(data.Header);
        Template = null;
        CurrentIndex = 0;
        return data;
    }

    public void SetMode(MailMergeOutputMode mode)
    {
        Mode = mode;
        Template = null;
        CurrentIndex = 0;
    }

    public void Clear()
    {
        Data = null;
        Template = null;
        CurrentIndex = 0;
        Mode = MailMergeOutputMode.Letters;
        Mapping = null;
    }

    public IReadOnlyDictionary<string, string> AugmentRow(
        IReadOnlyDictionary<string, string> row,
        string greetingFormat = "Dear")
    {
        ArgumentNullException.ThrowIfNull(row);

        var augmented = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
        var mapping = Mapping ?? new FieldMapping();
        augmented["AddressBlock"] = MailMerge.ComposeAddressBlock(row, mapping);
        augmented["GreetingLine"] = MailMerge.ComposeGreetingLine(row, mapping, greetingFormat);
        return augmented;
    }

    public MergeData BuildAugmentedData(IReadOnlyList<int> rowIndexes)
    {
        ArgumentNullException.ThrowIfNull(rowIndexes);
        if (Data is not { } data)
            throw new InvalidOperationException("Mail merge recipients have not been loaded.");
        if (rowIndexes.Any(index => index < 0 || index >= data.Count))
            throw new ArgumentOutOfRangeException(nameof(rowIndexes));

        var header = data.Header.ToList();
        AddHeaderIfMissing(header, "AddressBlock");
        AddHeaderIfMissing(header, "GreetingLine");

        var rows = rowIndexes
            .Select(index =>
            {
                var augmented = AugmentRow(data.Rows[index]);
                return (IReadOnlyList<string>)header
                    .Select(name => augmented.TryGetValue(name, out var value) ? value : string.Empty)
                    .ToList();
            })
            .ToList();

        return new MergeData(header, rows);
    }

    public IReadOnlyList<IReadOnlyList<Paragraph>> BuildLabelCellContents(
        TextDocument template,
        int capacity)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (capacity <= 0 || Data is not { Count: > 0 } data)
            return [];

        var state = new MergeState();
        var contents = new List<IReadOnlyList<Paragraph>>(Math.Min(capacity, data.Count));
        var recordIndex = 0;

        while (contents.Count < capacity && recordIndex < data.Count)
        {
            state.SequenceNumber++;
            var merged = MailMerge.MergeRecordWithRules(
                template,
                AugmentRow(data.Rows[recordIndex]),
                state,
                recordIndex + 1);
            if (state.SkipRecordRequested)
            {
                state.SequenceNumber--;
                recordIndex++;
                continue;
            }

            contents.Add(merged.Blocks.OfType<Paragraph>().ToList());
            recordIndex += state.AdvanceRecordRequested ? 2 : 1;
        }

        return contents;
    }

    private static void AddHeaderIfMissing(List<string> header, string name)
    {
        if (!header.Contains(name, StringComparer.OrdinalIgnoreCase))
            header.Add(name);
    }
}
