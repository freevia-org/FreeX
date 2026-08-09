using System.Globalization;
using Free.ToolsShared;

internal enum FidelityStatus { Pass, Fail, Skipped }

internal enum CellKind { Blank, Number, Text, Bool, Error }

// Normalized cell value used to compare FreeX and Excel on equal footing.
internal readonly record struct CellVal(CellKind Kind, double Number, string Text)
{
    public static readonly CellVal Blank = new(CellKind.Blank, 0, "");

    public static CellVal FromNumber(double n) => new(CellKind.Number, n, "");
    public static CellVal FromText(string t) => new(CellKind.Text, 0, t);
    public static CellVal FromBool(bool b) => new(CellKind.Bool, b ? 1 : 0, "");
    public static CellVal FromError(string e) => new(CellKind.Error, 0, e);

    // Whether this cell carries no comparable content (skip from strict diff but still inventoried).
    public bool IsEmpty => Kind == CellKind.Blank || (Kind == CellKind.Text && Text.Length == 0);

    public bool Matches(CellVal other)
    {
        if (Kind == CellKind.Error || other.Kind == CellKind.Error)
            return Kind == other.Kind; // error-vs-error counts as equal; representations vary across apps
        if (Kind != other.Kind)
        {
            // Excel hands back numbers for booleans/dates; tolerate number<->bool when values agree.
            if ((Kind == CellKind.Bool && other.Kind == CellKind.Number) ||
                (Kind == CellKind.Number && other.Kind == CellKind.Bool))
                return NumbersClose(Number, other.Number);
            return false;
        }
        return Kind switch
        {
            CellKind.Number or CellKind.Bool => NumbersClose(Number, other.Number),
            CellKind.Text => string.Equals(Text.Trim(), other.Text.Trim(), StringComparison.Ordinal),
            _ => true,
        };
    }

    private static bool NumbersClose(double a, double b)
    {
        if (a == b) return true; // handles equal infinities and exact equality
        if (double.IsNaN(a) && double.IsNaN(b)) return true;
        var diff = Math.Abs(a - b);
        if (diff < 1e-9) return true;
        var scale = Math.Max(Math.Abs(a), Math.Abs(b));
        return scale > 0 && diff / scale < 1e-6;
    }

    public override string ToString() => Kind switch
    {
        CellKind.Number => Number.ToString("G15", CultureInfo.InvariantCulture),
        CellKind.Bool => Number != 0 ? "TRUE" : "FALSE",
        CellKind.Text => Text,
        CellKind.Error => Text,
        _ => "",
    };
}

internal sealed class Inventory
{
    public int Sheets;
    public int Charts;
    public int PivotTables;
    public int ConditionalFormats;
    public int DataValidations;
    public int Tables;
    public int Hyperlinks;
    public int Comments;
    public int NamedRanges;
}

internal sealed class FileResult
{
    public FileResult(string file) => File = file;

    public string File { get; }
    public string? FreeXError;
    public string? FreeXRecalcError;
    public string? ExcelError;

    public bool FreeXLoaded => FreeXError is null && FreeX is not null;
    public bool ExcelOpened => ExcelError is null && Excel is not null;

    public Inventory? FreeX;
    public Inventory? Excel;

    // sheetName -> (row,col) -> value
    public Dictionary<string, Dictionary<(int Row, int Col), CellVal>> FreeXCells = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<(int Row, int Col), CellVal>> ExcelCells = new(StringComparer.Ordinal);

    public int CellsCompared;
    public int ValueMismatches;
    public readonly List<string> MismatchSamples = [];
    public readonly List<string> InventoryDiffs = [];

    public FidelityStatus Status = FidelityStatus.Skipped;

    public string StatusLine()
    {
        if (Status == FidelityStatus.Skipped)
            return $"SKIP (freexLoaded={FreeXLoaded}, excelOpened={ExcelOpened}; {FreeXError ?? ExcelError})";
        var inv = InventoryDiffs.Count == 0 ? "inventory ok" : $"{InventoryDiffs.Count} inventory diff(s)";
        return $"{Status}: {CellsCompared} cells compared, {ValueMismatches} value mismatch(es), {inv}";
    }
}

internal sealed class FidelityOptions
{
    public bool ShowHelp;
    public string CorpusRoot = "";
    public string FilesDirectory = "";
    public string? OutputDirectory;
    public string? Filter;
    public double ValueMismatchTolerancePercent = 0.5; // allow this % of compared cells to differ before failing
    public int MaxMismatchSamples = 25;
    public bool Recalc; // recompute FreeX formulas (compute-fidelity) instead of trusting the loaded cache

    public static FidelityOptions Parse(string[] args)
    {
        var options = new FidelityOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": options.ShowHelp = true; break;
                case "--corpus": options.CorpusRoot = args[++i]; break;
                case "--filter": options.Filter = args[++i]; break;
                case "--out": options.OutputDirectory = args[++i]; break;
                case "--tolerance": options.ValueMismatchTolerancePercent = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--recalc": options.Recalc = true; break;
            }
        }

        if (string.IsNullOrEmpty(options.CorpusRoot))
            options.CorpusRoot = FindCorpusRoot();
        options.FilesDirectory = Path.Combine(options.CorpusRoot, "files");
        return options;
    }

    private static string FindCorpusRoot()
    {
        var root = RepositoryRootLocator.FindByDirectoryMarker(
            AppContext.BaseDirectory,
            "fidelity-corpus");
        return root is not null
            ? Path.Combine(root, "fidelity-corpus")
            : Path.GetFullPath("fidelity-corpus");
    }

    public static void WriteUsage()
    {
        Console.WriteLine("""
            FreeX.FidelityCompare — on-demand FreeX vs Excel functional fidelity batch

            Usage:
              FreeX.FidelityCompare [--filter <substr>] [--out <dir>] [--corpus <dir>] [--tolerance <pct>] [--recalc]

              --filter <substr>   Only run corpus files whose name contains <substr>.
              --out <dir>         Run output directory (default: fidelity-corpus/runs/<timestamp>).
              --corpus <dir>      fidelity-corpus root (default: auto-located).
              --tolerance <pct>   Max % of compared cells allowed to differ before a file FAILs (default 0.5).
              --recalc            Recompute FreeX formulas before comparing (compute-fidelity: FreeX engine
                                  vs Excel) instead of trusting the file's cached results (load-fidelity).

            Requires Microsoft Excel installed (COM automation). Not part of build/test/CI.
            Corpus discovery includes .xlsx and legacy .xls workbooks under fidelity-corpus/files/.
            Download the corpus first: pwsh tools/Fetch-FidelityCorpus.ps1
            """);
    }
}

internal static class CorpusFiles
{
    public static IReadOnlyList<string> Resolve(FidelityOptions options)
    {
        if (!Directory.Exists(options.FilesDirectory))
            return [];
        return Directory.EnumerateFiles(options.FilesDirectory, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedWorkbook)
            .Where(p => options.Filter is null || Path.GetFileName(p).Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSupportedWorkbook(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);
    }
}
