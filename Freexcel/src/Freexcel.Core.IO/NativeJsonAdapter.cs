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
        var dto = JsonSerializer.Deserialize<WorkbookDto>(stream);
        if (dto == null) throw new InvalidDataException("Invalid Freexcel file");

        var workbook = new Workbook(dto.Name);
        foreach (var sDto in dto.Sheets)
        {
            var sheet = workbook.AddSheet(sDto.Name);
            foreach (var cDto in sDto.Cells)
            {
                var addr = CellAddress.Parse(cDto.Address, sheet.Id);
                var cell = cDto.Formula != null 
                    ? Cell.FromFormula(cDto.Formula) 
                    : Cell.FromValue(DeserializeValue(cDto.Value));
                
                sheet.SetCell(addr, cell);
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
                    Address = pair.Key.ToA1(),
                    Value = pair.Value.Value.ToString(), // Simple for now
                    Formula = pair.Value.HasFormula ? pair.Value.FormulaText : null
                }).ToList()
            }).ToList()
        };

        JsonSerializer.Serialize(stream, dto, new JsonSerializerOptions { WriteIndented = true });
    }

    private static ScalarValue DeserializeValue(string? val)
    {
        if (val == null) return BlankValue.Instance;
        if (double.TryParse(val, out var d)) return new NumberValue(d);
        if (bool.TryParse(val, out var b)) return new BoolValue(b);
        return new TextValue(val);
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
        public string? Formula { get; set; }
    }
}
