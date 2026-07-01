# FreeW for Linux

Self-contained Linux build of FreeW, the cross-platform word processor in the Free suite.
No system .NET runtime is required.

## Install (per-user)

```bash
tar xzf freew-<version>-<runtime>.tar.gz
./freew-<version>-<runtime>/install.sh    # installs into ~/.local
freew                                      # ensure ~/.local/bin is on PATH
```

`install.sh <prefix>` installs into a custom prefix. `uninstall.sh <prefix>` removes it.

## Contents

- `lib/freew/` — the self-contained app payload (native apphost `FreeW`).
- `bin/freew` — relocatable launcher wrapper.
- `share/applications/…desktop` — desktop entry (associates Word `.docx` and OpenDocument Text files).
- `share/icons/hicolor/scalable/apps/…svg` — application icon.
- `share/metainfo/…metainfo.xml` — AppStream metadata.

A desktop session with X11 or Wayland is required.
