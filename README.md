# FreeX

FreeX is a free spreadsheet app for local workbook files. It opens and saves standard Excel-compatible and tabular formats while keeping the project, branding, icons, and release artifacts independent from Microsoft.

## Current Scope

- Native Windows desktop app built with .NET 10 and WPF, with Avalonia app-preview lanes for macOS/Linux work.
- Spreadsheet editing, formulas, charts, PivotTables, conditional formatting, data tools, printing, and export.
- Workbook and interchange workflows for `.xlsx`, `.xltx`, `.xlsm`/`.xltm` open and save (including macro-enabled package save), `.xls/.xlsb/.xlt` open, `.ods`, SpreadsheetML `.xml`, CSV variants, tabular text, Formatted Text `.prn` (space-delimited), SYLK, DIF, DBF open, HTML tables, Single File Web Page `.mht`/`.mhtml`, PDF tabular-data import (read-only), and FreeX native `.fxl`.
- Local files by default; Microsoft 365 cloud services, account integration, and proprietary Microsoft runtimes are outside the app scope.

## Downloads

Tester builds are published on the [FreeX releases page](https://github.com/tony-xmelon/FreeX/releases). The current tester release is **v0.8.170** (2026-08-08), published for all three apps on Windows, Linux, and macOS.

FreeX v0.8.170 downloads (each asset has a matching `.sha256`):

- [FreeX-v0.8.170-win-x64.exe](https://github.com/tony-xmelon/FreeX/releases/download/freex-v0.8.170/FreeX-v0.8.170-win-x64.exe)
- [FreeX-v0.8.170-linux-x64.zip](https://github.com/tony-xmelon/FreeX/releases/download/freex-v0.8.170/FreeX-v0.8.170-linux-x64.zip) / [linux-arm64](https://github.com/tony-xmelon/FreeX/releases/download/freex-v0.8.170/FreeX-v0.8.170-linux-arm64.zip)
- [FreeX-v0.8.170-osx-arm64.zip](https://github.com/tony-xmelon/FreeX/releases/download/freex-v0.8.170/FreeX-v0.8.170-osx-arm64.zip) / [osx-x64](https://github.com/tony-xmelon/FreeX/releases/download/freex-v0.8.170/FreeX-v0.8.170-osx-x64.zip)

Per-app release tags, all non-prerelease:

- [FreeX v0.8.170](https://github.com/tony-xmelon/FreeX/releases/tag/freex-v0.8.170)
- [FreeW v0.8.170](https://github.com/tony-xmelon/FreeX/releases/tag/freew-v0.8.170)
- [FreeP v0.8.170](https://github.com/tony-xmelon/FreeX/releases/tag/freep-v0.8.170)

Note: link to the versioned assets above rather than `releases/latest/download/...`. Releases are now published per app (`freex-`/`freew-`/`freep-` tags), so GitHub's repo-wide `releases/latest` redirect resolves to whichever app published most recently and is not FreeX-specific.

## Documentation

Start with the [user guide](docs/user/guide.md), [documentation index](docs/README.md), and [current status snapshot](docs/history/status-2026-08-08.md). Current build scope and known limitations are tracked in [outstanding build](docs/planning/outstanding-build.md), [fidelity workstream summary](docs/fidelity/README.md), and [fidelity contract](docs/formats/fidelity-contract.md).

This monorepo also hosts **FreeW**, a sibling `.docx` word processor built on the same shared tier. See [freew/README.md](freew/README.md).

## Legal And Privacy

- [Legal notices](docs/legal/legal-notices.md)
- [Privacy notice](docs/legal/privacy.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [Third-party license texts](THIRD_PARTY_LICENSES.md)
- [Project license](LICENSE)

## Feedback And Security

- [Feedback and support](docs/support/feedback.md)
- [Private vulnerability reporting policy](SECURITY.md)
- [Public-preview release gate](docs/release/public-preview-readiness.md)
- [Public-preview acceptance, rollback, and incident runbook](docs/release/public-preview-operations.md)

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

FreeX, FreeW, and FreeP are independent projects. They are not affiliated with, authorized, sponsored, endorsed, or approved by Microsoft Corporation.

Microsoft, Excel, Microsoft 365, Microsoft Office, OneDrive, PowerPoint, SharePoint, Visual Basic, Windows, and Word are trademarks of the Microsoft group of companies. All other trademarks are the property of their respective owners.

Microsoft's published guidelines permit truthful plain-text compatibility references subject to their conditions; they do not permit use of Microsoft logos, product icons, trade dress, or other brand assets as project branding without authorization. See the project [legal and trademark notice](docs/legal/legal-notices.md), the [Microsoft Trademark and Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks), and [Windows app trademark guidance](https://learn.microsoft.com/windows/apps/publish/partner-center/trademark-and-copyright-protection).
