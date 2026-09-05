# Lean Linux container for running the FreeX manifest-driven test gates
# (eng/test-gates.json / tools/Invoke-TestGate.ps1) that already run on Linux in CI.
#
# This is deliberately NOT the interactive GUI/VNC image in tools/LinuxInteractiveDocker
# (x11vnc, novnc, openbox, ffmpeg, vlc, ...). Those tools support manual/visual probes.
# This image only needs to run `dotnet test` for the linux commit gates headlessly.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DEBIAN_FRONTEND=noninteractive

# PowerShell 7 (pwsh) already ships inside this SDK image (verified: 7.6.4 at /usr/bin/pwsh via
# /usr/share/powershell/pwsh), so tools/Invoke-TestGate.ps1 and the ToolScriptSupport.ps1 it
# dot-sources run without installing anything extra for that. Everything below is additional to
# the base image.
#
# Package rationale (kept minimal -- determined by running the gates and fixing failures, not by
# guessing):
#   libicu74                - .NET globalization is already pulled in by the SDK image, but pinned
#                             explicitly here so this image does not silently regress if the base
#                             image's own copy is ever dropped.
#   libx11-6, libice6,
#   libsm6, libfontconfig1  - Avalonia's headless test platform still loads a real X11/Skia font
#                             stack (Free.Shared.Shell.Avalonia.Tests, FreeX.App.Avalonia.Tests);
#                             without these the process fails to load libSkiaSharp/Avalonia.X11
#                             even under Avalonia.Headless.
#   fontconfig,
#   fonts-dejavu-core       - font enumeration/measurement in the Avalonia tests needs at least one
#                             real font available; DejaVu is small and matches what the interactive
#                             image already uses for text-layout parity.
#   rsync                   - tools/Run-LinuxTestGate.ps1 syncs the bind-mounted repo into a
#                             container-local volume with rsync (excluding bin/obj) so MSBuild's many
#                             small file writes never hit the slow Windows-bind-mounted filesystem.
RUN apt-get update -qq \
    && apt-get install -y -qq --no-install-recommends \
        libicu74 \
        libx11-6 \
        libice6 \
        libsm6 \
        libfontconfig1 \
        fontconfig \
        fonts-dejavu-core \
        rsync \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /repo
