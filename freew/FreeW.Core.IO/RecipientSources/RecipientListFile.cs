using System.IO;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.RecipientSources;

/// <summary>What kind of file a mail-merge recipient list is read from.</summary>
public enum RecipientSourceKind
{
    /// <summary>An Excel workbook (.xlsx / .xlsm): pick a worksheet, named range or table.</summary>
    Workbook,

    /// <summary>A delimited text file (.csv / .tsv / .tab / .txt): pick a delimiter.</summary>
    DelimitedText,

    /// <summary>A document (.docx / .docm / .rtf) whose first table is the list.</summary>
    Document,
}

public enum RecipientSourceTableKind
{
    Worksheet,
    NamedRange,
    Table,
}

/// <summary>One selectable list inside a workbook.</summary>
/// <param name="Key">Opaque key to pass back as <see cref="RecipientSourceOptions.TableKey"/>.</param>
/// <param name="DisplayName">The name to show (worksheets carry Excel's trailing <c>$</c>, as in Word's Select Table).</param>
public sealed record RecipientSourceTable(string Key, string DisplayName, RecipientSourceTableKind Kind);

/// <summary>What <see cref="RecipientListFile.Inspect(string)"/> found: the choices the user can make.</summary>
/// <param name="Delimiter">The detected field delimiter (delimited text only).</param>
/// <param name="EncodingName">The detected text encoding's display name (delimited text only).</param>
public sealed record RecipientSourceInfo(
    RecipientSourceKind Kind,
    IReadOnlyList<RecipientSourceTable> Tables,
    char Delimiter,
    string EncodingName);

/// <summary>How to read a recipient list from its file.</summary>
/// <param name="TableKey">The workbook list to read; null reads the first worksheet.</param>
/// <param name="FirstRowIsHeader">Whether the first row holds the field names.</param>
/// <param name="Delimiter">The field delimiter for delimited text; null uses the detected one.</param>
public sealed record RecipientSourceOptions(
    string? TableKey = null,
    bool FirstRowIsHeader = true,
    char? Delimiter = null);

public enum RecipientSourceError
{
    /// <summary>The file type cannot be used as a recipient list (e.g. .xls, .mdb, .odc).</summary>
    UnsupportedFormat,

    /// <summary>The file could not be read as its declared format.</summary>
    Unreadable,

    /// <summary>The requested worksheet / range is not in the workbook, or the document has no table.</summary>
    NoTable,

    /// <summary>The source holds no records.</summary>
    Empty,

    /// <summary>The source is larger than a recipient list may be.</summary>
    TooLarge,
}

/// <summary>A recipient source could not be used; <see cref="Error"/> says why in a localizable form.</summary>
public sealed class RecipientSourceException : Exception
{
    public RecipientSourceException(RecipientSourceError error, Exception? innerException = null)
        : base("The mail merge recipient list could not be read: " + error + ".", innerException)
    {
        Error = error;
    }

    public RecipientSourceError Error { get; }
}

/// <summary>
/// Word's Select Recipients &gt; "Use an Existing List": reads a recipient list from an Excel workbook, a
/// delimited text file, or the first table of a Word/RTF document, and saves a typed list back to
/// delimited text. Database, Outlook and ODBC sources are not supported and are reported as
/// <see cref="RecipientSourceError.UnsupportedFormat"/> rather than approximated.
/// </summary>
public static class RecipientListFile
{
    /// <summary>The largest text or document source that will be read (64 MiB).</summary>
    public const long MaxSourceBytes = 64L * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Lazy<Encoding> Windows1252 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    });

    public static IReadOnlyList<string> WorkbookExtensions { get; } = [".xlsx", ".xlsm"];

    public static IReadOnlyList<string> DelimitedTextExtensions { get; } = [".csv", ".tsv", ".tab", ".txt"];

    public static IReadOnlyList<string> DocumentExtensions { get; } = [".docx", ".docm", ".rtf"];

    /// <summary>The extensions a typed list can be saved to.</summary>
    public static IReadOnlyList<string> SaveExtensions { get; } = [".csv", ".tsv", ".txt"];

    /// <summary>The source kind for a file name, or null when its type is not a supported recipient list.</summary>
    public static RecipientSourceKind? KindForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        if (Matches(WorkbookExtensions, extension))
            return RecipientSourceKind.Workbook;
        if (Matches(DelimitedTextExtensions, extension))
            return RecipientSourceKind.DelimitedText;
        if (Matches(DocumentExtensions, extension))
            return RecipientSourceKind.Document;
        return null;
    }

    public static RecipientSourceInfo Inspect(string path)
    {
        using var stream = OpenRead(path);
        return Inspect(stream, Path.GetExtension(path));
    }

    /// <summary>Reports what can be chosen for the source in <paramref name="stream"/> (a seekable stream).</summary>
    public static RecipientSourceInfo Inspect(Stream stream, string extension)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var kind = KindForPath("x" + extension)
            ?? throw new RecipientSourceException(RecipientSourceError.UnsupportedFormat);
        switch (kind)
        {
            case RecipientSourceKind.Workbook:
                var tables = XlsxRecipientReader.ListTables(stream);
                if (tables.Count == 0)
                    throw new RecipientSourceException(RecipientSourceError.NoTable);
                return new RecipientSourceInfo(kind, tables, ',', string.Empty);

            case RecipientSourceKind.DelimitedText:
                var text = DecodeText(ReadAllBytes(stream), out var encoding);
                var fallback = IsTabExtension(extension) ? '\t' : ',';
                return new RecipientSourceInfo(
                    kind,
                    [],
                    MergeRecipientTable.DetectDelimiter(text, fallback),
                    encoding.EncodingName);

            default:
                return new RecipientSourceInfo(kind, [], ',', string.Empty);
        }
    }

    /// <summary>The raw rows of the chosen list (header row included), e.g. for a preview.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(string path, RecipientSourceOptions? options = null)
    {
        using var stream = OpenRead(path);
        return ReadRows(stream, Path.GetExtension(path), options);
    }

    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(
        Stream stream,
        string extension,
        RecipientSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new RecipientSourceOptions();
        var kind = KindForPath("x" + extension)
            ?? throw new RecipientSourceException(RecipientSourceError.UnsupportedFormat);
        switch (kind)
        {
            case RecipientSourceKind.Workbook:
                return XlsxRecipientReader.ReadRows(stream, options.TableKey);

            case RecipientSourceKind.DelimitedText:
                var text = DecodeText(ReadAllBytes(stream), out _);
                var delimiter = options.Delimiter
                    ?? MergeRecipientTable.DetectDelimiter(text, IsTabExtension(extension) ? '\t' : ',');
                return MergeRecipientTable.ParseDelimited(text, delimiter);

            default:
                return ReadDocumentTable(stream, extension);
        }
    }

    public static MergeData Load(string path, RecipientSourceOptions? options = null)
    {
        using var stream = OpenRead(path);
        return Load(stream, Path.GetExtension(path), options);
    }

    /// <summary>Reads the chosen list as merge data. Throws <see cref="RecipientSourceError.Empty"/> when it has no fields.</summary>
    public static MergeData Load(Stream stream, string extension, RecipientSourceOptions? options = null)
    {
        options ??= new RecipientSourceOptions();
        var data = MergeRecipientTable.FromTable(ReadRows(stream, extension, options), options.FirstRowIsHeader);
        if (data.Header.Count == 0)
            throw new RecipientSourceException(RecipientSourceError.Empty);
        return data;
    }

    /// <summary>
    /// Saves a typed recipient list as delimited text (UTF-8 with a byte-order mark so Excel and Word
    /// read non-ASCII names correctly): comma-separated for .csv, tab-separated for .tsv and .txt.
    /// </summary>
    public static void Save(string path, MergeData data)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Save(stream, Path.GetExtension(path), data);
    }

    public static void Save(Stream stream, string extension, MergeData data)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(data);
        if (!Matches(SaveExtensions, extension))
            throw new RecipientSourceException(RecipientSourceError.UnsupportedFormat);

        var delimiter = string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) ? ',' : '\t';
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(MergeRecipientTable.WriteDelimited(data, delimiter)))
            .ToArray();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Decodes recipient text: a byte-order mark decides when present; otherwise UTF-16 is recognised
    /// by its interleaved zero bytes, valid UTF-8 is taken as UTF-8, and anything else is read as
    /// Windows-1252 (what Excel writes for "CSV (Comma delimited)" on a Western system).
    /// </summary>
    public static string DecodeText(byte[] bytes, out Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
            return encoding.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            encoding = Encoding.UTF32;
            return encoding.GetString(bytes, 4, bytes.Length - 4);
        }
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            return encoding.GetString(bytes, 4, bytes.Length - 4);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            return encoding.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            return encoding.GetString(bytes, 2, bytes.Length - 2);
        }

        if (LooksLikeUtf16(bytes, out var bigEndian))
        {
            encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
            return encoding.GetString(bytes);
        }

        try
        {
            var text = StrictUtf8.GetString(bytes);
            encoding = Encoding.UTF8;
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = Windows1252.Value;
            return encoding.GetString(bytes);
        }
    }

    // BOM-less UTF-16 text made mostly of Latin characters has a zero in every other byte. Requires an
    // even length and a clear majority of zeros on one side only, so ordinary 8-bit text never matches.
    private static bool LooksLikeUtf16(byte[] bytes, out bool bigEndian)
    {
        bigEndian = false;
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;

        var sample = Math.Min(bytes.Length, 4096) & ~1;
        var evenZeros = 0;
        var oddZeros = 0;
        for (var index = 0; index < sample; index += 2)
        {
            if (bytes[index] == 0)
                evenZeros++;
            if (bytes[index + 1] == 0)
                oddZeros++;
        }

        var pairs = sample / 2;
        if (oddZeros * 10 >= pairs * 7 && evenZeros * 10 <= pairs)
            return true;
        if (evenZeros * 10 >= pairs * 7 && oddZeros * 10 <= pairs)
        {
            bigEndian = true;
            return true;
        }
        return false;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadDocumentTable(Stream stream, string extension)
    {
        TextDocument document;
        try
        {
            using var buffer = new MemoryStream(ReadAllBytes(stream), writable: false);
            document = string.Equals(extension, ".rtf", StringComparison.OrdinalIgnoreCase)
                ? RtfReader.Read(buffer)
                : DocxReader.Read(buffer);
        }
        catch (RecipientSourceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or System.Xml.XmlException
            or FormatException
            or NotSupportedException
            or InvalidOperationException)
        {
            throw new RecipientSourceException(RecipientSourceError.Unreadable, exception);
        }

        var table = document.Blocks.OfType<Table>().FirstOrDefault()
            ?? throw new RecipientSourceException(RecipientSourceError.NoTable);
        return table.Rows
            .Select(row => (IReadOnlyList<string>)row.Cells
                // A cell's paragraphs are one field value; line breaks inside it stay as line breaks.
                .Select(cell => cell.PlainText.Trim())
                .ToArray())
            .ToArray();
    }

    private static FileStream OpenRead(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (KindForPath(path) is null)
            throw new RecipientSourceException(RecipientSourceError.UnsupportedFormat);
        try
        {
            // FileShare.ReadWrite: the workbook is often still open in Excel while it is being selected.
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RecipientSourceException(RecipientSourceError.Unreadable, exception);
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek && stream.Length > MaxSourceBytes)
            throw new RecipientSourceException(RecipientSourceError.TooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxSourceBytes)
                throw new RecipientSourceException(RecipientSourceError.TooLarge);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool IsTabExtension(string extension) =>
        string.Equals(extension, ".tsv", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".tab", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(IReadOnlyList<string> extensions, string? extension) =>
        extension is not null
        && extensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
}
