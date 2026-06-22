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
              --generate-freex-feature-fixtures
                                            Generate representative FreeX feature XLSX smoke files, including PivotTables.
              --generate-supported-corpus-fixtures
                                            Generate supported generated corpus fixtures from --corpus-manifest.
                                            Defaults to supported-pass, supported-metadata-pass,
                                            supported-pivot-metadata-pass, and public-pass; only
                                            generated rows with available fixtures are materialized.
              --generate-chart-fixtures     Generate FreeX histogram and waterfall XLSX smoke files.
              --generate-excel-fixture      Generate an Excel-authored XLSX fixture through COM, including
                                            a native PivotTable, then load/save it through FreeX.
              --generate-excel-pivot-corpus-fixtures
                                            Generate Excel-authored native PivotTable fixtures through COM,
                                            excluding external, Data Model, and OLAP pivots.
              --freex-resave-before-excel   For user inputs, corpus rows, and generated FreeX fixtures,
                                            load/save through FreeX before Excel validation.
              --corpus-manifest <csv>       Add existing .xlsx/.xlsm rows from the XLSX corpus manifest.
              --corpus-source <source_type> Filter corpus rows by source_type. Repeatable.
              --corpus-status <status>      Filter corpus rows by expected_status. Repeatable.
                                            Defaults to supported-pass, supported-metadata-pass,
                                            supported-pivot-metadata-pass, and public-pass.
              --corpus-id <id>              Filter corpus rows by manifest id. Repeatable.
              --out <directory>             Run output directory. Must be under %USERPROFILE%.
              --pattern <glob>              Directory input glob. Defaults to *.xlsx.
              --help                        Show this help text.

            Examples:
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-freex-fixture --generate-excel-fixture
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-freex-feature-fixtures
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-excel-pivot-corpus-fixtures
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --freex-resave-before-excel --generate-freex-feature-fixtures
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --freex-resave-before-excel --generate-supported-corpus-fixtures --corpus-manifest test-corpus\manifest.csv
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --freex-resave-before-excel --generate-supported-corpus-fixtures --corpus-manifest test-corpus\manifest.csv --corpus-id generated-slicers-001
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-chart-fixtures
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --freex-resave-before-excel --corpus-manifest test-corpus\manifest.csv --corpus-source public --corpus-source regression
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --freex-resave-before-excel C:\Users\quinn\freex-xlsx-verify\excel-authored.xlsx
            """);
    }
}
