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

## Environment Boundaries

This is an X11 software-rendering session using Xvfb, Openbox, and the XRender
backend of picom. When a VNC client connects, the harness briefly refreshes the
maximized app window to clear stale X11 damage regions. The harness is suitable
for deterministic interaction and layout comparisons, but it does not validate
Wayland, GPU rendering, distribution-specific desktop integration, accessibility
screen readers, or real monitor scaling.
