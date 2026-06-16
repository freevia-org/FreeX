# FreeX Linux Human Validation Checklist

**Last updated:** 2026-06-16

A tester completes this on real Linux hardware before a build can be promoted to a
public-preview candidate. Hosted CI proves build/packaging/headless+Xvfb smokes; this
checklist covers what only a human on a real desktop can confirm. See
[linux-public-preview-checklist.md](linux-public-preview-checklist.md) for the full gate list
and [linux-release.md](linux-release.md) for the release flow.

Fill in the **Validation Record** below: set each gate to `pass`, or `na` (not applicable, with
a note in the table). `tools/Test-LinuxHumanValidationChecklist.ps1` validates the record; a
candidate is blocked unless every gate is `pass`/`na`.

## Gates

| Gate | What to confirm |
| --- | --- |
| `install_tarball` | `tar xzf` then `install.sh` installs to `~/.local`; `freex` launches from the app menu and PATH. |
| `appimage_launch` | `chmod +x` then run the `.AppImage`; it launches by double-click and from the terminal. |
| `desktop_association` | Desktop entry, icon, and `.fxl` association appear (GNOME and KDE). |
| `file_open` | Double-click open of `.fxl` and a spreadsheet (xlsx/csv) from the file manager works. |
| `file_dialogs` | Open / Save / Save As dialogs work (GTK portal where applicable); recent files persist. |
| `clipboard` | Copy/cut/paste (including image paste) works against other Linux apps. |
| `drag_drop` | Drag-and-drop open from the file manager works. |
| `x11_session` | Verified in an X11 session. |
| `wayland_session` | Verified in a Wayland session. |
| `keyboard_only` | Menus, dialogs, and the grid are operable keyboard-only. |
| `screen_reader_orca` | Orca / AT-SPI announces formula box, status text, cell address, selection stats. |
| `external_links` | Help / feedback / external links open via the system browser (xdg-open). |
| `known_issues_reviewed` | Known accessibility/behavior issues recorded in the release record. |

## Validation Record

<!-- freex-linux-validation -->
```
runtime: linux-x64
run_id:
run_attempt:
install_tarball: pending
appimage_launch: pending
desktop_association: pending
file_open: pending
file_dialogs: pending
clipboard: pending
drag_drop: pending
x11_session: pending
wayland_session: pending
keyboard_only: pending
screen_reader_orca: pending
external_links: pending
known_issues_reviewed: pending
```

Validate locally:

```powershell
pwsh -File tools/Test-LinuxHumanValidationChecklist.ps1 -ChecklistPath docs/release/linux-human-validation-checklist.md
```
