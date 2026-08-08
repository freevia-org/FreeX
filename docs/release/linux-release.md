# FreeX Linux Release Runbook

**Last updated:** 2026-06-16

The Linux release channel is the Linux-equivalent of the Windows tester release: a
manual, versioned, checksummed, published build of the Avalonia app. See
[../planning/linux-release-roadmap.md](../planning/linux-release-roadmap.md) and
[linux-public-preview-checklist.md](linux-public-preview-checklist.md).

## What it produces

For `linux-x64` and `linux-arm64`:

- `freex-<version>-<runtime>.tar.gz` (+ `.sha256`) — relocatable bundle with
  `install.sh`/`uninstall.sh`.
- `FreeX-<version>-<arch>.AppImage` (+ `.sha256`) — single-file launcher.
- `freex_<version>_<arch>.deb` (+ `.sha256`) — Debian/Ubuntu package.
- `freex-<runtime>-linux-evidence.txt` — build/smoke evidence.
- A draft GitHub release tagged `freex-linux-v<version>` with the above assets and
  generated notes.

## Gates

1. **Hard gates per runtime (build job):** publish succeeds, `desktop-file-validate`
   passes, tarball + checksum verify, headless `--packaging-smoke` passes (style
   round-trip ≥ 2), and the Xvfb GUI `--launch-smoke` passes.
2. **Promotion gate (publish job):** `tools/Test-LinuxPublicPreviewPromotion.ps1`
   re-validates artifact readiness + checksum integrity. For a
   `public_preview_candidate`, it additionally requires all accessibility-evidence
   inputs (keyboard-only, screen-reader/AT-SPI, X11, Wayland, known-issues-reviewed).
   Without them, promotion is **blocked**.
3. The release is always created as a **draft**; publishing it is a manual decision on
   the Releases page after reviewing assets and the promotion manifest.

## Dispatch

The workflow is manual-only (`workflow_dispatch`) and must exist on the default branch
to be dispatchable.

Internal preview (no public-preview promotion):

```bash
gh workflow run linux-release.yml --ref <branch> -f release_version=0.1.0
```

Public-preview candidate (requires completed human accessibility validation —
see [linux-public-preview-checklist.md](linux-public-preview-checklist.md)):

```bash
gh workflow run linux-release.yml --ref <branch> \
  -f release_version=0.1.0 \
  -f public_preview_candidate=true \
  -f accessibility_keyboard_only=true \
  -f accessibility_screen_reader=true \
  -f accessibility_x11=true \
  -f accessibility_wayland=true \
  -f accessibility_known_issues=true
```

(Or use the Actions UI → Linux Release → Run workflow.)

## After the run

1. Review the draft release assets and `linux-preview-promotion-manifest.json`.
2. Verify a checksum locally: `sha256sum -c freex-<version>-<runtime>.tar.gz.sha256`.
3. Publish the draft from the Releases page when satisfied.

## Trust

Linux has no Gatekeeper/notarization equivalent; trust is by SHA-256 checksum.
AppImage signing and distro-package signing are Phase R4 follow-ups
(see the roadmap).
