using System.IO;
using FreeX.Core.IO;
using FreeX.Core.Model;

internal sealed class TestFileAdapter : IFileAdapter
{
    private readonly Func<Stream, Workbook>? _load;
    private readonly Action<Workbook, Stream>? _save;

    public TestFileAdapter(
        Func<Stream, Workbook>? load = null,
        Action<Workbook, Stream>? save = null,
        string extension = ".fxjson",
        string formatName = "Fake",
        IReadOnlyList<global::Free.Shared.IO.FileFormatDescriptor>? formats = null)
    {
        _load = load;
        _save = save;
        Extension = extension;
        FormatName = formatName;
        Formats = formats ?? [new global::Free.Shared.IO.FileFormatDescriptor(extension, formatName)];
    }

    public TestFileAdapter(IReadOnlyList<global::Free.Shared.IO.FileFormatDescriptor> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        if (formats.Count == 0)
            throw new ArgumentException("At least one file format is required.", nameof(formats));

        Formats = formats;
        Extension = formats[0].Extension;
        FormatName = formats[0].FormatName;
    }

    public string Extension { get; }

    public string FormatName { get; }

    public IReadOnlyList<global::Free.Shared.IO.FileFormatDescriptor> Formats { get; }

    public Workbook Load(Stream stream) =>
        _load?.Invoke(stream) ?? throw new NotSupportedException();

    public void Save(Workbook workbook, Stream stream)
    {
        if (_save is null)
            throw new NotSupportedException();

        _save(workbook, stream);
    }
}
