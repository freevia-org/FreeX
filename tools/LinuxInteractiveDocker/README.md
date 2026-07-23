# Interactive Linux Desktop Harness

This harness runs the Avalonia FreeX-family applications inside an Ubuntu 24.04
Docker container and exposes the Linux desktop through noVNC. The browser session
supports mouse and keyboard input, so menus, dialogs, grids, editors, and native
Linux file workflows can be exercised directly from Windows.

## Start FreeX

From the repository root:

```powershell
powershell -File tools/Run-LinuxInteractiveDocker.ps1 -App FreeX -OpenBrowser
```

The default desktop is `1280x820` at 96 DPI and is available at:

```text
http://127.0.0.1:6080/vnc.html?autoconnect=true&resize=scale
```

The noVNC port is bound to `127.0.0.1`, not all network interfaces. The desktop
has no VNC password because it is reachable only from the local machine.

## Lifecycle

```powershell
# Show session status and URL.
powershell -File tools/Run-LinuxInteractiveDocker.ps1 -Action Status -App FreeX

# Capture the current virtual desktop.
powershell -File tools/Run-LinuxInteractiveDocker.ps1 -Action Screenshot -App FreeX

# Stop only the harness-owned FreeX container.
powershell -File tools/Run-LinuxInteractiveDocker.ps1 -Action Stop -App FreeX

# Stop and remove the harness-owned cached app image.
powershell -File tools/Run-LinuxInteractiveDocker.ps1 -Action Clean -App FreeX
```

Use `-Replace` on `Start` to replace an existing harness-owned container. The
runner refuses to stop or replace a same-named container without the ownership
label.

## Options

```text
-App FreeX|FreeW|FreeP
-Port 6080
-Width 1280
-Height 820
-Dpi 96
-DocumentPath <host-file>
-PublishDir <path-outside-OneDrive>
-SkipPublish
-SkipImageBuild
-OpenBrowser
-Replace
```

The first run builds the Ubuntu image and publishes the selected app. The default
publish directory is under `%TEMP%\FreeX-LinuxInteractive\`, outside OneDrive,
because large self-contained publish trees can block when Docker reads them
through a Files-On-Demand bind mount. Later runs can use `-SkipImageBuild` and
`-SkipPublish`.

## Artifacts

Files are written under `artifacts/linux-interactive/<app>/`:

- `documents/`: files mounted read/write at `/documents`.
- `sessions/<timestamp>/ready.json`: window and display metadata.
- `sessions/<timestamp>/screenshots/`: initial and manual screenshots.
- `sessions/<timestamp>/logs/`: app, Xvfb, Openbox, x11vnc, and noVNC logs.

The self-contained app publish stays in the configured temporary `PublishDir`.
The runner packs it into one compressed archive and builds a harness-owned app
image before launch. This avoids executing or copying hundreds of .NET files
through a Windows bind mount.

## Exhaustive FreeX Interaction Validation

Run the production FreeX desktop through both real X11 input probes and the
authoritative in-process interaction inventories:

```powershell
powershell -File tools/Run-FreeXLinuxInteractionValidation.ps1
```

The runner uses port `6082` by default, stops only its own container, and writes
`interaction-validation.json`, a searchable `interaction-validation.html`, and
the supporting screenshots into the session's `validation/` directory. It
covers every declared ribbon placement, dialog and context-menu family, every
documented keyboard gesture, inline and formula-bar edit/point modes, and every
dialog field that accepts a worksheet range.

The report keeps evidence strength explicit. `invoked-with-mutation` and real
X11 probes exercise production behavior; `opened-and-rendered` proves a dialog
can be reached and laid out; `registry-bound` and planner-backed rows prove
structural routing but do not claim that a command's full workflow was executed.
Failed and skipped rows remain visible so an inventory gap cannot silently look
like parity.

Do not use `-SkipImageBuild` for the first run after changing the desktop image.
The physical probes require `xclip`; the runner verifies the schema and rejects
clipboard-free or uncalibrated evidence instead of merging it into the report.

### Physical X11 manifest

`x11-validation/x11-input-results.json` uses schema version 2. Its top-level
contract is:

- `schemaVersion`, `platform`, and `shell`: `2`, `linux`, and `avalonia`.
- `calibration`: status/reason, selection color, window bounds, calibrated A1
  origin, cell pitch, and the three calibration screenshots.
- `summary`: exact passed, failed, and total counts for the physical rows.
- `results`: unique `x11-input` rows with `physical-x11-input` evidence, a
  `passed` or `failed` status, and an `artifacts` array naming retained evidence
  files. The runner verifies those files exist and are non-empty for the native
  boundary rows.

Calibration is derived from visible Ctrl+Home, A1-to-B1, and A1-to-A2 selection
transitions. The aggregator accepts the physical stream only when calibration
passes, geometry is positive, counts match, row IDs are unique, required
physical probes are present, and required native-boundary artifacts exist and
are non-empty. Those probes cover F2 cancel and commit, Ctrl+S, Shift+F12 Save,
F12 Save As cancel, inline and formula-bar point mode, standalone Alt and F10
keytips, rendered Shift+F10 and right-click worksheet context menus, physical
Copy and Clear activation in that rendered popup, clipboard Copy/Paste and
Cut/Paste roundtrips, deterministic View key-tip New Window/Arrange All/Ctrl+F6
switching, Format Cells keyboard traversal, Ctrl+F12 Open cancel, and
Ctrl+Shift+F12 Print Preview cancel. Cell value/formula, clipboard, and save
assertions use concrete X11 clipboard and harness-owned CSV/file-hash
postconditions. Empty-cell cancellation uses exact calibrated cell-pixel
restoration because copying an empty grid cell does not guarantee a new X11
clipboard owner. The worksheet popup rows do not claim activation of platform
native Avalonia `NativeMenuItem` menus; that boundary remains explicitly skipped
where only an application-native menu can be tested.

The New Window row is deliberately physical-shell scoped: it proves an
additional workbook-shaped top-level window, valid Arrange All geometry, and
physical window switching. Shared-workbook model identity, local view state,
document detach, title numbering, and close lifecycle are proven separately by
`AvaloniaSharedWorkbookWindowTests`; the physical row does not substitute for
those behavior assertions.

## Environment Boundaries

This is an X11 software-rendering session using Xvfb, Openbox, and the XRender
backend of picom. When a VNC client connects, the harness briefly refreshes the
maximized app window to clear stale X11 damage regions. The harness is suitable
for deterministic interaction and layout comparisons, but it does not validate
Wayland, GPU rendering, distribution-specific desktop integration, accessibility
screen readers, or real monitor scaling.
