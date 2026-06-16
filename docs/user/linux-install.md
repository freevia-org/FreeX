# Installing FreeX on Linux

FreeX ships a self-contained Linux build — no system .NET runtime is required. A desktop
session with **X11 or Wayland** is needed. Three install options are published with each
release (pick one); verify the SHA-256 first.

Download the assets for your CPU: **x86_64/amd64** for most PCs, **aarch64/arm64** for ARM.

## Option 1 — Debian/Ubuntu package (`.deb`)

```bash
sha256sum -c freex_<version>_<arch>.deb.sha256
sudo apt install ./freex_<version>_<arch>.deb     # amd64 or arm64
freex
```

Installs to `/usr`, registers the app menu entry, icon, and the `.fxl` file association.
Remove with `sudo apt remove freex`.

## Option 2 — AppImage (portable, no install)

```bash
sha256sum -c FreeX-<version>-<arch>.AppImage.sha256
chmod +x FreeX-<version>-<arch>.AppImage
./FreeX-<version>-<arch>.AppImage
```

A single self-contained file — run it from anywhere. (On some distros, install `libfuse2`
or run with `--appimage-extract-and-run`.)

## Option 3 — Tarball (per-user install, any distro)

```bash
sha256sum -c freex-<version>-<runtime>.tar.gz.sha256
tar xzf freex-<version>-<runtime>.tar.gz
./freex-<version>-<runtime>/install.sh           # installs to ~/.local
freex                                             # ensure ~/.local/bin is on PATH
```

Uninstall with `./freex-<version>-<runtime>/uninstall.sh`. Pass a prefix to install
system-wide, e.g. `sudo ./install.sh /usr/local`.

## Opening files

After install, double-click a `.fxl` workbook in your file manager, or open spreadsheets
(`.xlsx`, `.csv`, …) via **File → Open**. FreeX also registers as a handler for common
spreadsheet types.

## Trust

Linux has no Gatekeeper/notarization equivalent; trust is established by the published
**SHA-256 checksum** next to each asset. Always verify it before installing.

## Notes

- PDF export (**File → Export to PDF**) embeds fonts automatically, including non-Latin
  scripts (Cyrillic, Greek, CJK) when the matching system fonts are installed.
- Diagnostics and options are stored under XDG paths (`~/.config/FreeX`,
  `~/.local/share/FreeX`).
