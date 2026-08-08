# FreeX

FreeX is a free spreadsheet app for local workbook files. It opens and saves standard Excel-compatible and tabular formats while keeping the project, branding, icons, and release artifacts independent from Microsoft.

## Current Scope

- Native Windows desktop app built with .NET 10 and WPF, with Avalonia app-preview lanes for macOS/Linux work.
- Spreadsheet editing, formulas, charts, PivotTables, conditional formatting, data tools, printing, and export.
- Workbook and interchange workflows for `.xlsx`, `.xltx`, `.xlsm`/`.xltm` open and save (including macro-enabled package save), `.xls/.xlsb/.xlt` open, `.ods`, SpreadsheetML `.xml`, CSV variants, tabular text, Formatted Text `.prn` (space-delimited), SYLK, DIF, DBF open, HTML tables, Single File Web Page `.mht`/`.mhtml`, PDF tabular-data import (read-only), and FreeX native `.fxl`.
- Local files by default; Microsoft 365 cloud services, account integration, and proprietary Microsoft runtimes are outside the app scope.

## Downloads

Tester builds are published on the [FreeX releases page](https://github.com/tony-xmelon/FreeX/releases). The stable latest non-prerelease FreeX tester assets are:

- [FreeX-latest-win-x64.exe](https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe)
- [FreeX-latest-win-x64.msix](https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.msix)
- [FreeX-latest-macos-arm64.zip](https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-macos-arm64.zip)
- [FreeX-latest-macos-x64.zip](https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-macos-x64.zip)

Latest verified FreeX tester release: [FreeX (Test Release) v0.8.166](https://github.com/tony-xmelon/FreeX/releases/tag/v0-8-166-2026-07-30-13-58-42-run166-attempt2%2B68ee50ce), published from Tester Release run 166 attempt 2 at commit `68ee50ce`. GitHub's `releases/latest` redirect remains on this latest non-prerelease tester build.

Platform tester releases (the Linux, FreeW, and FreeP tags are published as pre-releases):

- [FreeX Windows and macOS v0.8.166](https://github.com/tony-xmelon/FreeX/releases/tag/v0-8-166-2026-07-30-13-58-42-run166-attempt2%2B68ee50ce) (latest, non-prerelease)
- [FreeX Linux v0.8.150](https://github.com/tony-xmelon/FreeX/releases/tag/freex-linux-v0.8.150) (pre-release)
- [FreeW v0.8.169](https://github.com/tony-xmelon/FreeX/releases/tag/freew-v0.8.169) (pre-release)
- [FreeP v0.8.169](https://github.com/tony-xmelon/FreeX/releases/tag/freep-v0.8.169) (pre-release)

## Documentation

Start with the [user guide](docs/user/guide.md), [documentation index](docs/README.md), and [current status snapshot](docs/history/status-2026-08-08.md). Current build scope and known limitations are tracked in [outstanding build](docs/planning/outstanding-build.md), [fidelity workstream summary](docs/fidelity/README.md), and [fidelity contract](docs/formats/fidelity-contract.md).

This monorepo also hosts **FreeW**, a sibling `.docx` word processor built on the same shared tier. See [freew/README.md](freew/README.md).

## Legal And Privacy

- [Legal notices](docs/legal/legal-notices.md)
- [Privacy notice](docs/legal/privacy.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [Third-party license texts](THIRD_PARTY_LICENSES.md)
- [Project license](LICENSE)

## Development

Run the default agent verification path for routine repo changes. It uses normal .NET restore/build caching and parallelism, builds the full solution, then tests only the non-UI lane:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Test-RepositoryPreflight.ps1
dotnet build FreeX.slnx --configuration Release
dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build --logger "trx;LogFileName=default-tests.trx"
```

Run the UI lane separately only when touching WPF app/host behavior, UI tests, UI documentation/inventory, or when preparing a tester-release/public-preview candidate:

```powershell
dotnet test FreeX.UiTests.slnx --configuration Release --no-build --logger "trx;LogFileName=ui-tests.trx"
```

The full solution remains the build target; do not use `dotnet test FreeX.slnx` as the default test command. If stale local build-server state causes a lock or compiler-cache failure after clearing stale processes, rerun the failing command once with `--disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1`.

## Trademark Notice

FreeX is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft Excel is a trademark of Microsoft Corporation.

Microsoft's trademark guidance allows truthful plain-text compatibility references, but Microsoft logos, app icons, product icons, and branding may not be used without permission. See the [Microsoft Trademark and Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks) and [Windows app trademark guidance](https://learn.microsoft.com/windows/apps/publish/partner-center/trademark-and-copyright-protection).
