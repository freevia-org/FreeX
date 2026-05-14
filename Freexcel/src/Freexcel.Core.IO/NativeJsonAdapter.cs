using System.Text.Json;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// Native JSON adapter for Freexcel.
/// Serializes the workbook to a simple, human-readable JSON format.
/// </summary>
public sealed class NativeJsonAdapter : IFileAdapter
{
    public string Extension => ".fxl";
    public string FormatName => "Freexcel Workbook";

    public Workbook Load(Stream stream)
    {
        var dto = JsonSerializer.Deserialize<WorkbookDto>(stream)
            ?? throw new InvalidDataException("Invalid Freexcel file");

        var workbook = new Workbook(dto.Name);
        foreach (var sDto in dto.Sheets ?? [])
        {
            if (string.IsNullOrEmpty(sDto?.Name)) continue;
            var sheet = workbook.AddSheet(sDto.Name);
            foreach (var cDto in sDto.Cells ?? [])
            {
                if (string.IsNullOrEmpty(cDto?.Address)) continue;
                try
                {
                    var addr = CellAddress.Parse(cDto.Address, sheet.Id);
                    var cell = cDto.Formula != null
                        ? Cell.FromFormula(cDto.Formula)
                        : Cell.FromValue(DeserializeValue(cDto.Value, cDto.ValueType));
                    sheet.SetCell(addr, cell);
                }
                catch (FormatException) { /* skip cells with unparseable addresses */ }
            }
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        var dto = new WorkbookDto
        {
            Name = workbook.Name,
            Sheets = workbook.Sheets.Select(s => new SheetDto
            {
                Name = s.Name,
                Cells = s.GetUsedCells().Select(pair => new CellDto
                {
                    Address   = pair.Key.ToA1(),
                    Value     = SerializeValue(pair.Value.Value),
                    ValueType = GetValueType(pair.Value.Value),
                    Formula   = pair.Value.HasFormula ? pair.Value.FormulaText : null
                }).ToList()
            }).ToList()
        };

        JsonSerializer.Serialize(stream, dto, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string? SerializeValue(ScalarValue value) => value switch
    {
        BlankValue  => null,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b   => b.Value ? "TRUE" : "FALSE",
        TextValue t   => t.Value,
        ErrorValue e  => e.Code,
        _             => null,
    };

    private static string? GetValueType(ScalarValue value) => value switch
    {
        NumberValue => "n",
        BoolValue   => "b",
        TextValue   => "t",
        ErrorValue  => "e",
        _           => null,
    };

    private static ScalarValue DeserializeValue(string? val, string? type)
    {
        if (val == null) return BlankValue.Instance;
        return type switch
        {
            "n" => double.TryParse(val, System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out var d)
                   ? new NumberValue(d) : new TextValue(val),
            "b" => new BoolValue(val == "TRUE"),
            "t" => new TextValue(val),
            "e" => val switch {
                       "#DIV/0!" => ErrorValue.DivByZero,
                       "#VALUE!" => ErrorValue.Value,
                       "#REF!"   => ErrorValue.Ref,
                       "#NAME?"  => ErrorValue.Name,
                       "#NULL!"  => ErrorValue.Null,
                       "#N/A"    => ErrorValue.NA,
                       "#NUM!"   => ErrorValue.Num,
                       _         => new ErrorValue(val)
                   },
            // Legacy files without ValueType: sniff the value
            _   => double.TryParse(val, System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out var dn)
                   ? new NumberValue(dn)
                   : bool.TryParse(val, out var db) ? new BoolValue(db)
                   : new TextValue(val)
        };
    }

    private class WorkbookDto
    {
        public string Name { get; set; } = "";
        public List<SheetDto> Sheets { get; set; } = [];
    }

    private class SheetDto
    {
        public string Name { get; set; } = "";
        public List<CellDto> Cells { get; set; } = [];
    }

    private class CellDto
    {
        public string Address { get; set; } = "";
        public string? Value { get; set; }
        public string? ValueType { get; set; }
        public string? Formula { get; set; }
    }
}
