# FreeX

FreeX is a free Windows spreadsheet app for `.xlsx` files. It opens and saves Excel-compatible workbooks while keeping the project, branding, icons, and release artifacts independent from Microsoft.

## Current Scope

- Native Windows desktop app built with .NET 10 and WPF.
- Spreadsheet editing, formulas, charts, PivotTables, conditional formatting, data tools, printing, and export.
- `.xlsx`, `.csv`, and FreeX native `.fxl` workflows.
- Local files by default; Microsoft 365 cloud services, account integration, and proprietary Microsoft runtimes are outside the app scope.

## Downloads

Tester builds are published on the [FreeX releases page](https://github.com/tony-xmelon/FreeX/releases). The stable latest tester asset is:

`FreeX-latest-win-x64.exe`

## Documentation

Start with the [user guide](docs/USER_GUIDE.md) and the [documentation index](docs/README.md). Current build scope and known limitations are tracked in [OUTSTANDING_BUILD.md](docs/OUTSTANDING_BUILD.md) and [FIDELITY_CONTRACT.md](docs/FIDELITY_CONTRACT.md).

## Development

```powershell
dotnet restore FreeX.slnx --disable-parallel
dotnet build FreeX.slnx --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1
dotnet test FreeX.slnx --no-restore --no-build --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1
```

## Trademark Notice

FreeX is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft Excel is a trademark of Microsoft Corporation.

Microsoft's trademark guidance allows truthful plain-text compatibility references, but Microsoft logos, app icons, product icons, and branding may not be used without permission. See the [Microsoft Trademark and Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks) and [Windows app trademark guidance](https://learn.microsoft.com/windows/apps/publish/partner-center/trademark-and-copyright-protection).
