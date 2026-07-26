using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static void ValidateSchemaHeader(WorkbookDto dto)
    {
        if (dto.FileFormat is { Length: > 0 } fileFormat &&
            !string.Equals(fileFormat, NativeFileFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported FreeX file format '{fileFormat}'.");
        }

        if (dto.SchemaVersion is > CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported FreeX native JSON schema version {dto.SchemaVersion}.");

        if (dto.MinimumReaderVersion is > CurrentSchemaVersion)
            throw new InvalidDataException($"FreeX native JSON requires reader schema version {dto.MinimumReaderVersion}.");
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
        workbook.FullPrecision = dto.FullPrecision;
    }

    private static void PopulateCalculationOptions(Workbook workbook, WorkbookDto dto)
    {
        dto.CalculationMode = NativeJsonValueSanitizer.ValidEnumOrDefault(workbook.CalculationMode, WorkbookCalculationMode.Automatic);
        dto.FullCalculationOnLoad = workbook.FullCalculationOnLoad;
        dto.ForceFullCalculation = workbook.ForceFullCalculation;
        dto.IterativeCalculation = workbook.IterativeCalculation;
        dto.MaxCalculationIterations = workbook.MaxCalculationIterations;
        dto.MaxCalculationChange = workbook.MaxCalculationChange;
        dto.FullPrecision = workbook.FullPrecision;
    }

    private static WorkbookCountrySettingsModel? ToWorkbookCountrySettings(WorkbookCountrySettingsDto? dto)
    {
        if (dto is null)
            return null;

        var defaultCountryId = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(dto.DefaultCountryId, ushort.MaxValue);
        var currentCountryId = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(dto.CurrentCountryId, ushort.MaxValue);
        return defaultCountryId is null && currentCountryId is null
            ? null
            : new WorkbookCountrySettingsModel
            {
                DefaultCountryId = defaultCountryId,
                CurrentCountryId = currentCountryId
            };
    }

    private static WorkbookCountrySettingsDto? FromWorkbookCountrySettings(WorkbookCountrySettingsModel? countrySettings)
    {
        if (countrySettings is null)
            return null;

        var defaultCountryId = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(countrySettings.DefaultCountryId, ushort.MaxValue);
        var currentCountryId = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(countrySettings.CurrentCountryId, ushort.MaxValue);
        return defaultCountryId is null && currentCountryId is null
            ? null
            : new WorkbookCountrySettingsDto
            {
                DefaultCountryId = defaultCountryId,
                CurrentCountryId = currentCountryId
            };
    }

    private static WorkbookLegacyMenuSettingsModel? ToWorkbookLegacyMenuSettings(WorkbookLegacyMenuSettingsDto? dto)
    {
        if (dto is null)
            return null;

        var addMenuCount = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(dto.AddMenuCount, ushort.MaxValue);
        var deleteMenuCount = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(dto.DeleteMenuCount, ushort.MaxValue);
        return addMenuCount is null && deleteMenuCount is null
            ? null
            : new WorkbookLegacyMenuSettingsModel
            {
                AddMenuCount = addMenuCount,
                DeleteMenuCount = deleteMenuCount
            };
    }

    private static WorkbookLegacyMenuSettingsDto? FromWorkbookLegacyMenuSettings(WorkbookLegacyMenuSettingsModel? menuSettings)
    {
        if (menuSettings is null)
            return null;

        var addMenuCount = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(menuSettings.AddMenuCount, ushort.MaxValue);
        var deleteMenuCount = NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(menuSettings.DeleteMenuCount, ushort.MaxValue);
        return addMenuCount is null && deleteMenuCount is null
            ? null
            : new WorkbookLegacyMenuSettingsDto
            {
                AddMenuCount = addMenuCount,
                DeleteMenuCount = deleteMenuCount
            };
    }

    private static WorkbookLegacyWorkbookSettingsModel? ToWorkbookLegacyWorkbookSettings(WorkbookLegacyWorkbookSettingsDto? dto)
    {
        if (dto is null)
            return null;

        var sheetTabIds = (dto.SheetTabIds ?? [])
            .Select(value => NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(value, ushort.MaxValue))
            .OfType<int>()
            .ToList();
        var useNaturalLanguageFormulas = dto.UseNaturalLanguageFormulas;
        return sheetTabIds.Count == 0 && useNaturalLanguageFormulas is null
            ? null
            : new WorkbookLegacyWorkbookSettingsModel
            {
                SheetTabIds = sheetTabIds,
                UseNaturalLanguageFormulas = useNaturalLanguageFormulas
            };
    }

    private static WorkbookLegacyWorkbookSettingsDto? FromWorkbookLegacyWorkbookSettings(WorkbookLegacyWorkbookSettingsModel? settings)
    {
        if (settings is null)
            return null;

        var sheetTabIds = (settings.SheetTabIds ?? [])
            .Select(value => NativeJsonValueSanitizer.ValidNonNegativeIntOrNull(value, ushort.MaxValue))
            .OfType<int>()
            .ToList();
        var useNaturalLanguageFormulas = settings.UseNaturalLanguageFormulas;
        return sheetTabIds.Count == 0 && useNaturalLanguageFormulas is null
            ? null
            : new WorkbookLegacyWorkbookSettingsDto
            {
                SheetTabIds = sheetTabIds,
                UseNaturalLanguageFormulas = useNaturalLanguageFormulas
            };
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
