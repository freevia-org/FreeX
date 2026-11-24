# FreeW Cocoa text-context safety bridge

The patch is pinned to Avalonia `a8dd6417fd8918570edefdbecd92d16ac7620069` (12.0.4). It changes only native substring bounds/output handling and Cocoa selection-range normalization, and adds the unique `FreeW_AvaloniaNative_TextContext_V1` export. The managed/native COM interfaces remain unchanged.

This is a text-context safety contract. Inline preedit, native replacement ranges and Cocoa reset behavior remain separate IME work.

## Build and verify

Use the existing bare source cache and expanded package cache. The script archives only the native source and pinned IDL into a temporary folder on the external drive. It regenerates the header with MicroCom.CodeGenerator 0.11.4, builds the ARM64 library using installed Xcode, signs it ad hoc, and runs the native harness against both the stock and patched libraries. The stock run must reproduce both defects; the patched run must pass all cases.

```sh
TMPDIR='/Volumes/MacMini Work/Tmp' python3 tools/FreeWNativeTextContext/build_native.py \
  --source-git '/Volumes/MacMini Work/Internal Offload/Source Caches/Avalonia.git' \
  --dotnet '/Volumes/MacMini Work/Internal Offload/dotnet-sdk-10.0.400/dotnet' \
  --packages '/Volumes/MacMini Work/Internal Offload/NuGet/packages' \
  --fallback-packages '/Volumes/MacMini Work/Internal Offload/Source Caches/Avalonia-grid-bridge-build/packages' \
  --fallback-packages "$HOME/.nuget/packages" \
  --stock-library "$HOME/.nuget/packages/avalonia.native/12.0.4/runtimes/osx/native/libAvaloniaNative.dylib" \
  --output artifacts/freew-native-text-context
```

Heavy commands use the repository throttle. Scratch folders are removed on success, failure or interruption; child commands are reaped before removal. Compiled libraries, manifests and verification logs stay in ignored artifacts. Existing source/prototype and NuGet fallback caches are read only. The generator restores from cached packages with no package feed configured; a missing dependency fails the task.

When another coordinated application build is active, pass `--defer-build-server-shutdown` and arrange the final SDK400 shutdown with its owner. The tiny generator disables persistent build servers. The script records this explicit cleanup handoff rather than stopping an active application compiler.

## Publish the verified bytes

Require `build-verification.json` with `result: passed`. Its pre-signing and signed hashes are distinct evidence. The artifact's `FreeW.NativeTextContext.json` pins the signed library bytes, upstream commit, IDL, patch, generator and export ABI.

`publish_app.py` implements the complete local packaging sequence below. Run it only after the application changes have passed verification and are committed. It refuses dirty source, a different full commit SHA, an existing output directory, mismatched native evidence or a different managed native dependency. All writable cache, SDK, scratch and artifact paths must be on the project's external volume. The existing upstream bare repository and fallback package caches are read only; no source checkout or operating-system font/library installation is performed.

```sh
python3 tools/FreeWNativeTextContext/publish_app.py \
  --expected-commit <verified-full-source-commit> \
  --native-artifacts <verified-native-artifacts-directory> \
  --source-git <cached-Avalonia-bare-repository> \
  --output artifacts/freew-local-preview \
  --dotnet <external-exact-sdk-directory>/dotnet \
  --sdk-version 10.0.400 \
  --runtime osx-arm64 \
  --scratch-root <external-temporary-root> \
  --packages <external-NuGet-package-cache> \
  --http-cache <external-NuGet-http-cache> \
  --fallback-packages <existing-expanded-package-cache>
```

Repeat `--fallback-packages` for additional existing caches. These are restore fallbacks; new package downloads use the external primary cache. The publisher checks the original native source and IDL hashes in the cached upstream commit before building. Every heavy publish/staging command uses the repository throttle internally. Do not wrap the whole script in a second throttle. Use `--defer-build-server-shutdown` only when a coordinator has accepted the cleanup handoff to avoid interrupting another active build.

The output contains `FreeW.app`, a ZIP and SHA256 sidecar, command logs, a copy of the native patch/license/regression evidence, and `publish-verification.json` with source commit/tree, SDK/runtime, unsigned and signed native hashes, managed dependency pins and ZIP-roundtrip verification. Scratch directories and owned child processes are cleaned on completion or interruption. This produces an ad-hoc-signed local preview; it performs no upload or notarization and does not establish full IME support.

Before installing the override, verify the actual published `Avalonia.Native.dll`: the current net10.0 asset must have SHA256 `bd80972b554098a50fb95395659fdd9bcc9d6dfb24ea856c5676e5f8444b45e9` and informational version `12.0.4+a8dd6417fd8918570edefdbecd92d16ac7620069`. A different managed package must not start with this older native ABI. The package's net8.0 DLL has a different hash and is not the net10.0 publish input. The runtime gate independently checks the already loaded managed informational version.

1. Pass the pre-signed library's SHA256 to `dotnet publish` as `-p:FreeWNativeTextContextSha256=<signed hash>`. The FreeW project emits the optional `FreeW.NativeTextContext.Sha256` assembly metadata.
2. Package the stock publish output and perform the normal deep ad-hoc signing of the app.
3. Replace only `FreeW.app/Contents/MacOS/libAvaloniaNative.dylib` with the exact pre-signed artifact, then copy `FreeW.NativeTextContext.json` beside it. Check that the copied hash still matches the managed metadata and manifest.
4. Sign the outer app only (`codesign --force --sign - FreeW.app`), without `--deep`. A later deep signing would change the pinned nested library bytes.
5. Verify the app using `codesign --verify --deep --strict`, then verify again after ZIP extraction. Confirm the nested library hash remains unchanged and perform native app QA.

The product gate applies only to actual Cocoa native handles. It validates the managed hash, manifest and file bytes once, then uses `RTLD_NOLOAD` to inspect the already loaded app-local library. It never loads a candidate library or changes global native exports. Stock or incomplete packages keep the Cocoa client detached; headless and other platform client plumbing remain testable.

## Upstream contract

The patched `attributedSubstringForProposedRange:actualRange:` treats `actualRange` as an output, intersects oversized requests with storage, returns `nil` for an outside location, and avoids unsigned addition before clipping. Empty ranges at storage end remain valid. `setSelection` clamps signed endpoints to storage bounds and converts either direction into a valid unsigned Cocoa range.

The native harness uses the actual loaded `AvnView`, including null and non-null output pointers, out-of-bounds and overflowing requests, empty storage, UTF-16 surrogate/combining text, reversed selection and signed endpoint bounds. It catches the stock Objective-C range exceptions to retain a safe failing baseline.
