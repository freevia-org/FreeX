using FreeW.Core.IO;
using FreeW.Core.Model;

const string expectedTitle = "FreeW deterministic field shortcut title";
const string staleTitle = "STALE-CACHED-TITLE";

if (args.Length < 2 ||
    (!string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase)
     && !string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("usage: FreeW.FieldShortcutFixture generate <path> | inspect <path> <expected-title>");
    return 2;
}

var path = Path.GetFullPath(args[1]);
if (string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var document = TextDocument.CreateEmpty();
    document.Blocks.Clear();
    document.Properties.Title = expectedTitle;
    document.Blocks.Add(new Paragraph
    {
        Runs =
        {
            new Run("Field result: "),
            Run.ComplexFieldRun(" TITLE ", staleTitle),
            new Run(".")
        }
    });
    DocxWriter.Write(document, path);
    Console.WriteLine($"generated={path}");
    Console.WriteLine($"expected-title={expectedTitle}");
    Console.WriteLine($"stale-cache={staleTitle}");
    return 0;
}

if (!string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase) || args.Length < 3)
{
    Console.Error.WriteLine("usage: FreeW.FieldShortcutFixture generate <path> | inspect <path> <expected-title>");
    return 2;
}

var expected = args[2];
var reloaded = DocxReader.Read(path);
var field = reloaded.Blocks
    .OfType<Paragraph>()
    .SelectMany(paragraph => paragraph.Runs)
    .SingleOrDefault(run => run.ComplexField?.Keyword == "TITLE");
var cached = field?.Text ?? string.Empty;
Console.WriteLine($"inspected={path}");
Console.WriteLine($"field-keyword={field?.ComplexField?.Keyword ?? "<missing>"}");
Console.WriteLine($"field-cache={cached}");
Console.WriteLine($"document-title={reloaded.Properties.Title ?? string.Empty}");
if (field is null || !string.Equals(cached, expected, StringComparison.Ordinal))
{
    Console.Error.WriteLine($"expected TITLE cache '{expected}' but found '{cached}'.");
    return 1;
}

return 0;
