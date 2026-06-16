# FreeX Linux Public-Preview Checklist

**Last updated:** 2026-06-16

This checklist gates a Linux preview artifact toward public-preview eligibility. Hosted CI
proves build, packaging, and headless/Xvfb smoke evidence; the remaining items require human
validation on real Linux desktops. See
[../planning/multiplatform-linux-port.md](../planning/multiplatform-linux-port.md).

## Channel

- Internal-preview artifacts are not a public release channel. Trust is established by SHA-256
  checksum only; Linux has no Gatekeeper/notarization equivalent.

## Hosted (CI-provable) gates

- [ ] `Linux App Preview` workflow green for `linux-x64` (`ubuntu-latest`) and `linux-arm64`
      (`ubuntu-24.04-arm`).
- [ ] Portable + Linux app-service test slices pass, including `LinuxPackagingMetadataTests`,
      `LinuxAppReadinessPreflightTests`, and `LinuxPlatformPathTests`.
- [ ] Self-contained publish produces a native ELF apphost and bundled native libs.
- [ ] `desktop-file-validate` passes; MIME definition and icon present.
- [ ] Tarball (+ installer) and AppImage built with matching SHA-256 checksums.
- [ ] Headless `--packaging-smoke` passes twice (`format_cells_style_roundtrip_count >= 2`).
- [ ] Xvfb `--launch-smoke` passes: window shown, file opened, viewport populated, native
      menus present, Open Recent populated, Find/Replace/Go To/Format Cells dialogs present,
      accessibility automation metadata present, diagnostics `events.jsonl` written.
- [ ] `tools/Test-LinuxPublicPreviewReadiness.ps1` passes and emits the readiness manifest.

## Human validation gates (real Linux hardware)

- [ ] Install via tarball `install.sh` into `~/.local`; `freex` launches from the menu and PATH.
- [ ] AppImage launches by double-click and from the terminal.
- [ ] Desktop entry, icon, and `.fxl` file association appear correctly (GNOME and KDE).
- [ ] Double-click open of `.fxl` and a spreadsheet (xlsx/csv) from the file manager works.
- [ ] Open/Save/Save As file dialogs work (GTK portal where applicable); recent files persist.
- [ ] Clipboard copy/cut/paste (including image paste) works against other Linux apps.
- [ ] Drag-and-drop open from the file manager works.
- [ ] Verified on both X11 and Wayland sessions.
- [ ] Keyboard-only operation of menus, dialogs, and the grid.
- [ ] Screen reader pass (Orca / AT-SPI): formula box, status text, cell address, selection
      stats are announced.
- [ ] External links / help / feedback open via the system browser (xdg-open).
- [ ] Known accessibility or behavior issues recorded in the release record.

## Verify (testers)

```bash
sha256sum -c freex-<version>-<runtime>.tar.gz.sha256
```

## Distribution follow-ups (out of preview scope)

- [ ] Decide signing/trust per channel (AppImage signature, distro package signing).
- [ ] Evaluate `.deb`/`.rpm`/Flatpak/Snap packaging once the tarball/AppImage preview is
      validated.
