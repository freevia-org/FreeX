# FreeW packaging / release

Windows packaging uses the FreeW WPF word processor (`freew/FreeW.App.Host`, a
`net10.0-windows...` WinExe). The output is a **self-contained** `win-x64`
folder publish zipped into `artifacts/`. Self-contained means the target machine
needs no .NET runtime installed.

## Why self-contained, folder publish (not single-file)

- **Self-contained** (vs framework-dependent): a downloaded FreeW build runs on a
  clean Windows machine with no separate .NET 10 install. This mirrors how the
  Linux/Avalonia release lane ships a self-contained runtime.
- **Folder publish + zip** (`PublishSingleFile=false`): single-file packaging with
  WPF is finicky (native COM/WinRT bits and XAML resource extraction), so a plain
  self-contained folder + zip is the safe, reproducible default.

## Local build

From the repo root, run the publish script:

```pwsh
pwsh freew/build/publish-windows.ps1
```

This publishes `freew/FreeW.App.Host` for `win-x64` (self-contained, Release) and
writes `artifacts/FreeW-win-x64-0.1.0.zip`. The final line printed is the absolute
artifact path. Unzip it and run `FreeW.App.Host.exe`.

### Options

```pwsh
pwsh freew/build/publish-windows.ps1 `
  -Version 1.2.3 `          # version stamp in the zip name + assembly Version
  -Configuration Release `  # default Release
  -Runtime win-x64 `        # default win-x64
  -OutputDir C:\out `       # default <repo>/artifacts
  -PublishDir C:\stage      # default <repo>/artifacts/publish/FreeW-win-x64
```

The script is idempotent (it cleans its publish + zip outputs before each run)
and CI-friendly (no prompts; non-zero exit on failure).

## Local macOS build

macOS uses `freew/FreeW.App.Avalonia`. From the repository root, these PowerShell
commands create a self-contained Apple Silicon app bundle with the product's
Finder document associations:

```pwsh
pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Build -- dotnet publish freew/FreeW.App.Avalonia/FreeW.App.Avalonia.csproj --configuration Release --runtime osx-arm64 --self-contained true --output artifacts/freew-macos/publish
Compress-Archive -Path artifacts/freew-macos/publish/* -DestinationPath artifacts/freew-macos/FreeW-v0.1.0-osx-arm64.zip
pwsh -NoProfile -File tools/packaging/New-AppInstallers.ps1 -Apps FreeW -Platform macos -Version 0.1.0 -Runtime osx-arm64 -InputRoot artifacts/freew-macos -OutputDir artifacts/freew-macos/installer
```

Extract `artifacts/freew-macos/installer/FreeW-v0.1.0-osx-arm64-apps.zip` and open
`FreeW.app`. Use `osx-x64` throughout for an Intel build. These local bundles are
unsigned and unnotarized; distribution signing uses
`tools/packaging/New-SignedMacOsReleasePackages.ps1` in the release workflow.

The macOS shell exposes native File and Edit menus and Command-key shortcuts,
including Command+Shift+Z for Redo. Finder file activation opens documents in
separate windows and preserves unsaved documents already open in FreeW.

## Release workflow

The release lane is **manual-only** (`workflow_dispatch`) — it does not run on
push or PR. It runs on `windows-latest`, sets up .NET 10, runs
`publish-windows.ps1`, and uploads the zip via `actions/upload-artifact@v7`.

Trigger it with the GitHub CLI:

```sh
gh workflow run freew-release.yml --ref <branch> -f release_version=0.1.0
```

The uploaded artifact is named `FreeW-win-x64-<release_version>` and contains
`FreeW-win-x64-<release_version>.zip`. Download it from the workflow run's
Artifacts section.

Use explicit artifact hygiene settings in workflow snippets:

```yaml
- uses: actions/upload-artifact@v7
  with:
    name: FreeW-win-x64-${{ inputs.release_version }}
    path: artifacts/FreeW-win-x64-${{ inputs.release_version }}.zip
    if-no-files-found: error
    include-hidden-files: false
    compression-level: 0
    retention-days: 14
```
