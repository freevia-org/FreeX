# FreeX Linux Packaging

These assets package the cross-platform Avalonia shell (`FreeX.App.Avalonia`) as a
self-contained Linux application. They mirror the macOS bundle's identity and
document-type associations using freedesktop / XDG conventions.

## Assets

| File | Purpose |
| --- | --- |
| `io.github.tony-xmelon.freex.desktop` | Desktop entry: launcher name, icon, categories, and MIME associations for `.fxl` plus common spreadsheet formats. |
| `io.github.tony-xmelon.freex.xml` | `shared-mime-info` definition for the native `application/vnd.freex.workbook+json` (`*.fxl`) type. |
| `../../../../../shared/Free.Shared.Shell/Resources/FreeX.svg` | Canonical scalable application icon shared by every host and package. |
| `package-linux-app.sh` | Build a relocatable `.tar.gz` with `install.sh`/`uninstall.sh` for a per-user (`~/.local`) or system prefix. |
| `build-appimage.sh` | Build a single-file `.AppImage` (requires `appimagetool`, fetched by CI). |
| `build-deb.sh` | Build a distro-native `.deb` (control + postinst/postrm cache refresh; requires `dpkg-deb`). |

## Identity

- Application ID / reverse-DNS name: `io.github.tony-xmelon.freex`
- Apphost executable: `FreeX`
- Native workbook type: `application/vnd.freex.workbook+json` → `*.fxl`

## Building locally

```bash
dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj \
  --configuration Release --framework net10.0 --runtime linux-x64 \
  --self-contained true -p:UseAppHost=true -p:PublishReadyToRun=false \
  -p:PublishSingleFile=false --output out/linux-x64

src/FreeX.App.Avalonia/Packaging/linux/package-linux-app.sh \
  --runtime linux-x64 --published out/linux-x64 --version 0.1.0 --output dist
```

Then `tar xzf dist/freex-0.1.0-linux-x64.tar.gz` and run `freex-0.1.0-linux-x64/install.sh`.

## Notes

- The runtime requires a desktop session with X11 or Wayland. Avalonia uses the
  bundled SkiaSharp/HarfBuzz native libraries; no system .NET is needed.
- Linux has no Gatekeeper/notarization equivalent. Distribution trust is left to
  the channel (tarball checksum, AppImage signature, or distro package signing).
- The native share sheet is macOS-only; on Linux the shared share action planner
  falls back to opening the containing folder, and external links use the
  Avalonia launcher (xdg-open).
