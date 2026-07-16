# FreeX Linux Interaction Smoke (Docker)

An on-demand, durable interaction-smoke harness that launches the real FreeX Avalonia app
inside Docker (Ubuntu 24.04, Xvfb + openbox + xdotool) and drives it with synthetic keyboard
input to prove that UI flows actually work on Linux — not just that the app opens.

For a browser-based desktop that supports direct mouse and keyboard interaction, use
`tools/Run-LinuxInteractiveDocker.ps1`; see `tools/LinuxInteractiveDocker/README.md`.

## What it tests

Each *flow* performs an action, asserts an observable outcome, and captures a screenshot:

| Flow | Action | Assertion |
|------|--------|-----------|
| `launch` | App starts | FreeX window found via `xdotool search --onlyvisible --name FreeX` |
| `type-cell` | Type `LinuxSmokeTest` + Enter in cell B2 | Window still present after typing |
| `format-cells` | `Ctrl+1` | "Format Cells" child window appears (`wmctrl -l`); `Escape` closes it |
| `find-replace` | `Ctrl+F` | "Find" / "Find and Replace" window appears; `Escape` closes it |
| `goto` | `Ctrl+G` then `F5` | "Go To" window appears; `Escape` closes it |
| `name-box-nav` | `F5` → type `D10` + Enter | Navigate to D10; window still present |
| `app-stable` | Final check | FreeX window + process still alive at end of run |

Assertions are window-presence (deterministic) — no pixel-level image comparisons.
Screenshots are the human-readable evidence. `result.json` is the machine-readable verdict.

## What it does NOT cover

- **Wayland** — runs under Xvfb (X11 software rendering). Real Wayland compositor not tested.
- **Real GPU** — `LIBGL_ALWAYS_SOFTWARE=1` (Mesa swrast). GPU rendering paths untested.
- **Orca / AT-SPI accessibility** — no screen reader driven. Separate accessibility suite needed.
- **Human judgment** — flow assertions are binary window-presence checks. Visual correctness
  (colors, layout, text rendering quality) requires human review of the screenshots.
- **Performance** — no timing assertions; the harness uses fixed sleeps.
- **Non-keyboard UI** — no mouse-click flows yet (xdotool supports them; extend as needed).

## Requirements

- Docker Desktop running (WSL2 backend on Windows).
- PowerShell 7+ (`pwsh`).
- `dotnet` SDK 10 (for publish).
- ~2 GB RAM available for the container.

## Running

From the repo root:

```powershell
pwsh -ExecutionPolicy Bypass -File tools/FreeX.LinuxLiveTest/Run-LinuxLiveTest.ps1
```

The runner:
1. Publishes FreeX for `linux-x64` (self-contained) to `$env:TEMP\FreeX-LinuxLiveTest-<stamp>\app\`
   (OUTSIDE the repo/OneDrive tree — avoids Files-On-Demand placeholder issues).
2. Runs `ubuntu:24.04` with the work dir mounted as `/work`.
3. Installs dependencies, starts Xvfb + openbox, launches FreeX in normal mode.
4. Executes each flow and writes `/work/out/result.json` + screenshots.
5. Collects artifacts to `artifacts/linux-live-test/`.
6. Prints a PASS/FAIL summary and exits non-zero on failure.

### Options

```
-OutputDir <path>        Where to collect artifacts (default: artifacts/linux-live-test/)
-TempPublishDir <path>   Publish destination (default: $TEMP\FreeX-LinuxLiveTest-<stamp>)
-SkipPublish             Reuse an existing TempPublishDir (skip ~60s publish step)
-Image <tag>             Docker image (default: ubuntu:24.04)
-TimeoutSeconds <n>      Docker run timeout (default: 300)
```

### Re-running faster (skip publish)

```powershell
pwsh -ExecutionPolicy Bypass -File tools/FreeX.LinuxLiveTest/Run-LinuxLiveTest.ps1 `
  -SkipPublish -TempPublishDir "C:\Temp\FreeX-LinuxLiveTest-20260625T120000"
```

## Artifacts

After a run, `artifacts/linux-live-test/` contains:

| File | Description |
|------|-------------|
| `result.json` | Machine-readable PASS/FAIL per flow + overall verdict |
| `01-launch.png` | Screenshot after launch |
| `02-type-cell.png` | After typing in a cell |
| `03-format-cells-open.png` | Format Cells dialog open |
| `03-format-cells-closed.png` | After Escape |
| `04-find-open.png` | Find dialog open |
| `05-goto-open.png` | Go To dialog open |
| `06-name-box-nav.png` | After navigating to D10 |
| `07-app-stable-final.png` | Final state of running app |
| `app.log` | FreeX stdout/stderr from inside the container |

## Adding new flows

Edit `linux-live-test.sh`. Pattern:

```bash
echo "=== FLOW: my-flow ==="
focus_main
xdotool key --window "$WID" ctrl+something
sleep 1
if wait_for_window "Expected Window Title" 6; then
  screenshot "NN-my-flow-open"
  xdotool key Escape
  sleep 1
  record_pass "my-flow"
else
  screenshot "NN-my-flow-missing"
  record_fail "my-flow" "Expected window did not appear after Ctrl+something"
fi
```

## CI integration

This harness is **on-demand only** — it is NOT part of `FreeX.DefaultTests.slnx` or the default
test gate (`dotnet test FreeX.DefaultTests.slnx`). To add it to CI:

1. The CI runner must have Docker available (e.g., GitHub Actions `ubuntu-latest` with Docker pre-installed).
2. Add a step:
   ```yaml
   - name: Linux Interaction Smoke
     run: |
       pwsh -ExecutionPolicy Bypass \
         -File tools/FreeX.LinuxLiveTest/Run-LinuxLiveTest.ps1 \
         -OutputDir /tmp/linux-live-test-out
     shell: bash
   - uses: actions/upload-artifact@v7
     if: always()
     with:
       name: linux-live-test-screenshots
       path: /tmp/linux-live-test-out/
       if-no-files-found: error
       include-hidden-files: false
       compression-level: 6
       retention-days: 14
   ```
3. On Windows runners with Docker Desktop, use the PowerShell form shown in *Running* above.
4. The step exits non-zero on failure, so CI will mark the job failed appropriately.
