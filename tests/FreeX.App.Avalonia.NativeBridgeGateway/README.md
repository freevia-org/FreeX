# Optional macOS native bridge component tests

This helper accepts real worksheet peers from `WorksheetGridProviderTests` and checks the matched
packaged managed/native bridge without launching a native application. Ordinary test builds do
not reference it unless `FreeXGridNativeTestHelper` is supplied.

Prerequisites:

- macOS with Xcode command-line tools, PowerShell7 and the repository .NET SDK. Use an isolated
  linked FreeX worktree on the external disk; keep outputs/cache separate from ordinary builds.
- The exact reviewed Avalonia Grid package set required by the current pins, with the repository
  dependency bootstrap already provisioned. `AVALONIA_GRID_FEED` points to its verified nupkgs.
  Distribution approval is separate; this README does not supply or authorize a publication URL.
- `AVALONIA_SOURCE` points to the reused matching patched Avalonia source worktree, including the
  generated `native/Avalonia.Native/inc/avalonia-native.h`. Generate it through the matching
  review archive's reproduction recipe; do not mix official12.0.4 headers with new extension IDs.
- `FREEX_EXTERNAL_TMP` points to an external temporary directory owned by the caller.

The helper uses the existing packaged `Avalonia.Native.UnitTests` friend identity. Its default
`Fixtures/Avalonia.Public.snk` contains only the160 public-key bytes extracted from the matched
packaged `Avalonia.Native.dll`; SHA256 is `34d062cd8b0bbdf600f86c7857ec9c1a776bea9be9b94665ef9b6a939de4f518`. It contains no private key.
The upstream identity comes from [Avalonia12.0.4 commit a8dd6417fd8918570edefdbecd92d16ac7620069](https://github.com/AvaloniaUI/Avalonia/tree/a8dd6417fd8918570edefdbecd92d16ac7620069).
`PublicSign` records that test identity; no FreeX production friend or private managed reflection
is added. `AvaloniaPublicSigningKey` may explicitly override the fixture with the same public key.
The bootstrap requires a project-local NuGet cache; these commands use the isolated worktree's
normal ignored cache, which remains reusable after disposable QA scratch is removed.

From the isolated repository root, in a dedicated Bash session:

```bash
set -euo pipefail
: "${AVALONIA_SOURCE:?Matching patched source with generated header required}"
: "${AVALONIA_GRID_FEED:?Verified matching package feed required}"
: "${FREEX_EXTERNAL_TMP:?External temporary directory required}"
repo="$PWD"
QA_ROOT=$(mktemp -d "$FREEX_EXTERNAL_TMP/freex-grid-bridge-XXXXXXXX")
export QA_ROOT AVALONIA_GRID_FEED
trap 'dotnet build-server shutdown || true; rm -rf "$QA_ROOT"' EXIT
mkdir -p "$QA_ROOT"/{tmp,cli-home,manifests,production}
export TMPDIR="$QA_ROOT/tmp" DOTNET_CLI_HOME="$QA_ROOT/cli-home" NUGET_PACKAGES="$repo/.dependencies/nuget-packages"
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
key="$repo/tests/FreeX.App.Avalonia.NativeBridgeGateway/Fixtures/Avalonia.Public.snk"
printf '%s  %s\n' 34d062cd8b0bbdf600f86c7857ec9c1a776bea9be9b94665ef9b6a939de4f518 "$key" | shasum -a256 -c -

pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Build -- dotnet build \
  tests/FreeX.App.Avalonia.NativeBridgeGateway/FreeX.App.Avalonia.NativeBridgeGateway.csproj \
  --configuration Release "-p:AvaloniaPublicSigningKey=$key" "-p:RestorePackagesPath=$NUGET_PACKAGES" \
  --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1
pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Build -- xcrun clang++ \
  -dynamiclib -std=c++17 -fobjc-arc -Wno-deprecated-declarations \
  "-fmodules-cache-path=$QA_ROOT/clang-cache" -framework AppKit -framework Foundation \
  -I "$AVALONIA_SOURCE/native/Avalonia.Native/inc" -I "$AVALONIA_SOURCE/native/Avalonia.Native/src/OSX" \
  tests/FreeX.App.Avalonia.NativeBridgeGateway/FreeXGridBridgeProbe.mm -o "$QA_ROOT/libFreeXGridBridgeProbe.dylib"

helper="$repo/tests/FreeX.App.Avalonia.NativeBridgeGateway/bin/Release/net10.0/Avalonia.Native.UnitTests.dll"
pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Build -- dotnet build \
  tests/FreeX.App.Avalonia.Tests/FreeX.App.Avalonia.Tests.csproj --configuration Release \
  "-p:FreeXGridNativeTestHelper=$helper" "-p:RestorePackagesPath=$NUGET_PACKAGES" \
  --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1
```

Generate the required SHA256 manifest and extract the final packaged dylib, still in that session:

```bash
python3 - <<'PY_HASHES'
import hashlib, json, os, pathlib, xml.etree.ElementTree as ET, zipfile
qa = pathlib.Path(os.environ['QA_ROOT'])
feed = pathlib.Path(os.environ['AVALONIA_GRID_FEED'])
version = next(p.attrib['Version'] for p in ET.parse('Directory.Packages.props').findall('.//PackageVersion') if p.attrib.get('Include') == 'Avalonia')
expected = {}
for package, names in {'Avalonia': ['Avalonia.Controls', 'Avalonia.Base'], 'Avalonia.Native': ['Avalonia.Native']}.items():
    with zipfile.ZipFile(feed / f'{package}.{version}.nupkg') as archive:
        for name in names:
            expected[name] = hashlib.sha256(archive.read(f'lib/net10.0/{name}.dll')).hexdigest()
        if package == 'Avalonia.Native':
            native = archive.read('runtimes/osx/native/libAvaloniaNative.dylib')
            (qa / 'libAvaloniaNative.dylib').write_bytes(native)
            expected['libAvaloniaNative.dylib'] = hashlib.sha256(native).hexdigest()
(qa / 'hashes.json').write_text(json.dumps(expected, indent=2))
PY_HASHES
export FREEX_GRID_NATIVE_LIBRARY="$QA_ROOT/libAvaloniaNative.dylib"
export FREEX_GRID_NATIVE_HELPER="$QA_ROOT/libFreeXGridBridgeProbe.dylib"
export FREEX_GRID_EXPECTED_HASHES="$QA_ROOT/hashes.json"
export FREEX_GRID_NATIVE_MANIFEST_DIRECTORY="$QA_ROOT/manifests"
export FREEX_GRID_PRODUCTION_QA_ROOT="$QA_ROOT/production"
pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Test -- dotnet test \
  tests/FreeX.App.Avalonia.Tests/FreeX.App.Avalonia.Tests.csproj --configuration Release --no-build \
  --filter 'FullyQualifiedName~WorksheetGridProviderTests' --results-directory "$QA_ROOT/results"
```

`hashes.json` maps `Avalonia.Controls`, `Avalonia.Base`, `Avalonia.Native` and
`libAvaloniaNative.dylib` to lowercase SHA256 strings. The gateway verifies actual loaded assembly
locations/hashes and the loaded image owning the native grid selector. Inspect/export needed
small evidence before exiting the shell. Do not enable crash/hang dumps or kill other sessions.

Lifetime limit: fixture windows close and dispatcher work drains before native node retirement.
Because the upstream wrapper has no supported detach/reset API, retired wrapper objects remain
alive until this bounded testhost exits, preventing finalizers from revisiting deliberately
retired node proxies. This workaround is test-only. These cases prove lookup/identity/events/
actions across the managed/native bridge; they do **not** prove production native-window or peer
release, external AX behavior, VoiceOver behavior, or native-process shutdown. Those require
separate native application QA without this retirement workaround.
