# Installation And Browser Support

## Current Desktop Releases

FreeX, FreeW, and FreeP are released as self-contained desktop applications for Windows x64, Linux x64/ARM64, and macOS Intel/Apple Silicon. The canonical asset names and release matrix are documented in [app-platform-publish-lanes.md](app-platform-publish-lanes.md).

For ChromeOS, use the existing Linux package in the ChromeOS Linux development environment. Follow [the ChromeOS installation guide](../user/chromeos-install.md) for setup, architecture selection, checksum verification, file sharing, and troubleshooting.

## Chromium And Browser Releases

There is currently no Chromium, WebAssembly, PWA, Electron, or Chrome Web Store target. Chromium can be used to download the native desktop assets, but those assets do not run inside a browser tab. A browser release would require a separate web host, browser-safe storage/file access, web packaging, and browser-focused tests.

## ChromeOS Support Boundary

ChromeOS support currently means Linux-container compatibility:

- Intel/AMD Chromebooks use `linux-x64`.
- ARM64 Chromebooks use `linux-arm64` when the Chromebook's Linux container exposes that architecture.
- The release is not a native ChromeOS package, Android app, browser app, or Chrome Web Store listing.
- Human validation is still required across representative Chromebook models, ChromeOS versions, display scaling, suspend/resume, Linux file sharing, and workbook open/save behavior.

The existing Linux CI and packaging lanes remain the build and release source. No separate ChromeOS artifact is needed for this compatibility path.
