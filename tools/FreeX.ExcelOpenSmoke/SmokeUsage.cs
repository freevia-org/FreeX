internal static class SmokeUsage
{
    public static void WriteUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- [options] <xlsx-file-or-directory> [...]

            Options:
              --save-reopen                 Open each workbook in Excel, SaveCopyAs, close, reopen in Excel,
                                            and load the Excel-saved copy through FreeX.
              --generate-freex-fixture      Generate a non-chart FreeX XLSX smoke file.
              --generate-chart-fixtures     Generate FreeX histogram and waterfall XLSX smoke files.
              --generate-excel-fixture      Generate an Excel-authored XLSX fixture through COM, then load/save it through FreeX.
              --freex-resave-before-excel   For user inputs, load/save through FreeX before Excel validation.
              --out <directory>             Run output directory. Must be under %USERPROFILE%.
              --pattern <glob>              Directory input glob. Defaults to *.xlsx.
              --help                        Show this help text.

            Examples:
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-freex-fixture --generate-excel-fixture
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-chart-fixtures
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --freex-resave-before-excel C:\Users\quinn\freex-xlsx-verify\excel-authored.xlsx
            """);
    }
}
