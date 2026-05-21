using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static void ValidateSchemaHeader(WorkbookDto dto)
    {
        if (dto.FileFormat is { Length: > 0 } fileFormat &&
            !string.Equals(fileFormat, NativeFileFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported Freexcel file format '{fileFormat}'.");
        }

        if (dto.SchemaVersion is > CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported Freexcel native JSON schema version {dto.SchemaVersion}.");

        if (dto.MinimumReaderVersion is > CurrentSchemaVersion)
            throw new InvalidDataException($"Freexcel native JSON requires reader schema version {dto.MinimumReaderVersion}.");
    }

    private static void ApplyCalculationOptions(WorkbookDto dto, Workbook workbook)
    {
        if (dto.CalculationMode is { } calculationMode && Enum.IsDefined(calculationMode))
            workbook.CalculationMode = calculationMode;

        workbook.FullCalculationOnLoad = dto.FullCalculationOnLoad;
        workbook.ForceFullCalculation = dto.ForceFullCalculation;
        workbook.IterativeCalculation = dto.IterativeCalculation;
        workbook.MaxCalculationIterations = dto.MaxCalculationIterations;
        workbook.MaxCalculationChange = dto.MaxCalculationChange;
    }

    private static void PopulateCalculationOptions(Workbook workbook, WorkbookDto dto)
    {
        dto.CalculationMode = NativeJsonValueSanitizer.ValidEnumOrDefault(workbook.CalculationMode, WorkbookCalculationMode.Automatic);
        dto.FullCalculationOnLoad = workbook.FullCalculationOnLoad;
        dto.ForceFullCalculation = workbook.ForceFullCalculation;
        dto.IterativeCalculation = workbook.IterativeCalculation;
        dto.MaxCalculationIterations = workbook.MaxCalculationIterations;
        dto.MaxCalculationChange = workbook.MaxCalculationChange;
    }

    private static bool IsSupportedFormulaErrorCode(string? errorCode) =>
        string.Equals(errorCode, ErrorValue.DivByZero.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Value.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Ref.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Name.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.NA.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Num.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Null.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Spill.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, ErrorValue.Circular.Code, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, NumberStoredAsTextCode, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, FormulaRefersToBlankCellsCode, StringComparison.OrdinalIgnoreCase);
}
