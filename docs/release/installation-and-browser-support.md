# Installation And Browser Support

## Current Desktop Releases

FreeX, FreeW, and FreeP are currently released as self-contained desktop applications:

| Operating system | Runtime | Package |
| --- | --- | --- |
| Windows 10/11 x64 | `win-x64` | Self-contained `.exe` |
| Linux x64 | `linux-x64` | Self-contained Avalonia `.zip` |
| Linux ARM64 | `linux-arm64` | Self-contained Avalonia `.zip` |
| macOS Intel | `osx-x64` | Self-contained Avalonia `.zip` |
| macOS Apple silicon | `osx-arm64` | Self-contained Avalonia `.zip` |

Download the matching app/version asset and its adjacent `.sha256` file from the app's tester release page. The canonical release matrix and asset names are documented in [app-platform-publish-lanes.md](app-platform-publish-lanes.md).

### Windows

1. Download `<App>-v<version>-win-x64.exe` and `<App>-v<version>-win-x64.exe.sha256`.
2. Verify the checksum:

   ```powershell
   Get-FileHash .\<App>-v<version>-win-x64.exe -Algorithm SHA256
   ```

3. Compare the output with the `.sha256` file, then run the executable from a stable program directory.
4. The tester executable is self-contained and does not require a separate .NET installation. Windows SmartScreen may warn because tester binaries are not necessarily signed; only continue when the download was expected and the checksum matches.

### Linux

1. Choose `linux-x64` for Intel/AMD 64-bit Linux or `linux-arm64` for ARM64 Linux.
2. Download the `.zip` and matching `.zip.sha256` file.
3. Verify and extract it:

   ```bash
   sha256sum -c <App>-v<version>-linux-<architecture>.zip.sha256
   unzip <App>-v<version>-linux-<architecture>.zip -d ~/Apps/<App>
   chmod +x ~/Apps/<App>/<App>
   ~/Apps/<App>/<App>
   ```

4. Keep the extracted directory intact. Replace it only after closing the app when updating.

### macOS

1. Choose `osx-x64` for Intel Macs or `osx-arm64` for Apple silicon.
2. Download the `.zip` and matching `.zip.sha256` file.
3. Verify and extract it:

   ```bash
   shasum -a 256 -c <App>-v<version>-osx-<architecture>.zip.sha256
   ditto -x -k <App>-v<version>-osx-<architecture>.zip .
   chmod +x <App>.app/Contents/MacOS/<App>
   open <App>.app
   ```

4. These tester archives may be unsigned or not notarized. If Gatekeeper blocks an expected internal build, use Control-click or right-click > Open, or approve it in System Settings > Privacy & Security. Do not disable Gatekeeper globally.

## Chromium And Browser Releases

There is currently **no Chromium release**. The repository contains no WebAssembly, browser-host, PWA, Electron, or Chromium packaging target, and the release workflow has no browser lane. Chromium can be used to visit GitHub and download the desktop release assets, but the downloaded `.exe` or Avalonia archive remains a native desktop application; it does not run inside a Chromium tab.

A real Chromium release would require a separate browser product boundary: a browser-compatible UI host, a web-safe storage/file-access model, a web build pipeline, browser-focused tests, and a web distribution artifact. It should not be represented by renaming the existing desktop packages.

## Other Operating Systems

| Target | Current position | Notes |
| --- | --- | --- |
| ChromeOS | Feasible through the built-in Linux environment | The existing Linux x64/ARM64 Avalonia package may work inside a compatible Linux container, but this is not a native ChromeOS or Chromium release and needs device-specific validation. |
| FreeBSD | Possible future Avalonia port | No current FreeBSD runtime lane, packaging, or validation exists. |
| Android | New port required | The desktop shell, file dialogs, windowing, keyboard model, and packaging are not Android-ready. |
| iOS/iPadOS | New port required | The desktop shell and file-access/windowing assumptions require a dedicated mobile UI and storage integration. |
| Web browsers | New web host required | This includes Chromium, Firefox, Safari, and WebKit; there is no current browser target. |

The practical next OS experiment is ChromeOS via Linux-container validation, followed by FreeBSD if a desktop Avalonia target is desired. Android, iOS/iPadOS, and browser releases are larger product ports rather than packaging-only extensions.
