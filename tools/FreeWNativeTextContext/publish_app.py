#!/usr/bin/env python3
"""Create a local FreeW ARM64 preview using a verified pre-signed native text-context bridge."""

import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import zipfile

MANAGED_NATIVE_SHA256 = "bd80972b554098a50fb95395659fdd9bcc9d6dfb24ea856c5676e5f8444b45e9"
MANAGED_NATIVE_VERSION = "12.0.4+a8dd6417fd8918570edefdbecd92d16ac7620069"
MANIFEST_NAME = "FreeW.NativeTextContext.json"
METADATA_KEY = "FreeW.NativeTextContext.Sha256"


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def ser_string(value):
    """ECMA-335 custom-attribute SerString, for the two known string attribute constructors."""
    data = value.encode("utf-8")
    length = len(data)
    if length < 0x80:
        prefix = bytes([length])
    elif length < 0x4000:
        prefix = bytes([(length >> 8) | 0x80, length & 0xff])
    else:
        raise ValueError("Unexpectedly large metadata value")
    return prefix + data


def verify_managed_metadata(assembly, signed_hash):
    # Do not load the product or its native backend merely to inspect metadata. The SDK-generated
    # AssemblyMetadataAttribute has this unique fixed-string pair and no named arguments.
    expected = b"\x01\x00" + ser_string(METADATA_KEY) + ser_string(signed_hash) + b"\x00\x00"
    if assembly.read_bytes().count(expected) != 1:
        raise RuntimeError("Published FreeW.dll does not contain the unique expected native SHA256 assembly metadata")


def verify_managed_native(path):
    if digest(path) != MANAGED_NATIVE_SHA256:
        raise RuntimeError("Published Avalonia.Native.dll is not the pinned net10.0 asset; refusing an older native ABI override")
    informational = b"\x01\x00" + ser_string(MANAGED_NATIVE_VERSION) + b"\x00\x00"
    if path.read_bytes().count(informational) != 1:
        raise RuntimeError("Published Avalonia.Native informational version does not match the pinned upstream source")


def read_current_pins(tool, repo):
    names = {"COMMIT", "SOURCE_SHA256", "IDL_SHA256", "PATCH_SHA256", "MARKER", "LIBRARY"}
    pins = {}
    for node in ast.parse((tool / "build_native.py").read_text()).body:
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name):
            name = node.targets[0].id
            if name in names:
                pins[name] = ast.literal_eval(node.value)
    if set(pins) != names:
        raise RuntimeError("Cannot establish all tracked native source pins")
    patch = tool / "avalonia-12.0.4-text-context.patch"
    if digest(patch) != pins["PATCH_SHA256"]:
        raise RuntimeError("Tracked patch bytes disagree with the native builder pin")
    managed = (repo / "freew/FreeW.App.Avalonia/Editing/FreeWNativeTextContextCapability.cs").read_text()
    for name, value in [("UpstreamCommit", pins["COMMIT"]), ("PatchSha256", pins["PATCH_SHA256"]),
            ("IdlSha256", pins["IDL_SHA256"]), ("MarkerSymbol", pins["MARKER"]), ("LibraryFileName", pins["LIBRARY"])]:
        if name + ' = "' + value + '"' not in managed:
            raise RuntimeError("Managed capability disagrees with current native pin: " + name)
    return pins


def verify_native_artifact(directory, pins, runtime, sdk):
    verification = json.loads((directory / "build-verification.json").read_text())
    manifest = json.loads((directory / MANIFEST_NAME).read_text())
    expected = {"schemaVersion": 1, "contractVersion": 1, "upstreamCommit": pins["COMMIT"],
        "patchSha256": pins["PATCH_SHA256"], "idlSha256": pins["IDL_SHA256"], "generatorVersion": "0.11.4",
        "libraryFile": pins["LIBRARY"], "markerSymbol": pins["MARKER"], "markerAbi": "cdecl:int32(void)"}
    if any(manifest.get(key) != value for key, value in expected.items()):
        raise RuntimeError("Native capability manifest disagrees with the current pinned source/ABI")
    if verification.get("result") != "passed" or verification.get("runtime") != runtime or verification.get("sdk") != sdk:
        raise RuntimeError("Native build verification is not a passing matching runtime/SDK result")
    for name in ["upstreamCommit", "patchSha256", "idlSha256", "generatorVersion"]:
        if verification.get(name) != expected[name]:
            raise RuntimeError("Native build verification disagrees with source pin: " + name)
    green = verification.get("patchedRegression", {})
    red = verification.get("stockRegression", {})
    if green.get("cases") != 19 or green.get("contractVersion") != 1 or green.get("rangeFailures") != 0 \
            or green.get("selectionFailures") != 0 or green.get("failures") != []:
        raise RuntimeError("Native patched regression evidence is not 19 green cases with the v1 marker")
    if red.get("cases") != 19 or red.get("contractVersion") != 0 or red.get("rangeFailures", 0) <= 0 \
            or red.get("selectionFailures", 0) <= 0:
        raise RuntimeError("Native stock evidence does not reproduce both unpatched defects")
    if json.loads((directory / "patched-regression.json").read_text()) != green \
            or json.loads((directory / "stock-regression.json").read_text()) != red:
        raise RuntimeError("Native regression reports disagree with build verification")
    if digest(directory / "avalonia-12.0.4-text-context.patch") != pins["PATCH_SHA256"]:
        raise RuntimeError("Native artifact patch bytes do not match the tracked patch")
    signed_hash = manifest.get("librarySha256")
    if not isinstance(signed_hash, str) or not re.fullmatch(r"[0-9a-f]{64}", signed_hash) \
            or verification.get("signedLibrarySha256") != signed_hash:
        raise RuntimeError("Native signed library hashes are missing or inconsistent")
    library = directory / pins["LIBRARY"]
    if library.is_symlink() or digest(library) != signed_hash:
        raise RuntimeError("Native signed library bytes differ from the verified artifact")
    if not (directory / "Avalonia-LICENSE.txt").is_file():
        raise RuntimeError("Native artifact is missing the preserved framework copyright/license")
    return verification, manifest, signed_hash


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-commit", required=True, help="Full immutable clean source commit SHA")
    parser.add_argument("--native-artifacts", required=True, help="Passing build_native.py output directory")
    parser.add_argument("--source-git", required=True, help="Read-only cached upstream bare Git repository")
    parser.add_argument("--output", required=True, help="New ignored artifacts directory in a repository worktree")
    parser.add_argument("--dotnet", required=True, help="Installed exact SDK dotnet executable")
    parser.add_argument("--sdk-version", required=True, help="Exact SDK version, matching global.json and native evidence")
    parser.add_argument("--runtime", choices=["osx-arm64"], default="osx-arm64")
    parser.add_argument("--scratch-root", required=True, help="External temporary root on the project volume")
    parser.add_argument("--packages", required=True, help="External global NuGet package cache")
    parser.add_argument("--http-cache", required=True, help="External NuGet HTTP cache")
    parser.add_argument("--fallback-packages", action="append", default=[], help="Existing expanded read-only package cache; repeatable")
    parser.add_argument("--defer-build-server-shutdown", action="store_true", help="Coordinator completes SDK shutdown after concurrent work")
    args = parser.parse_args()
    if sys.platform != "darwin":
        raise SystemExit("Local macOS packaging requires macOS")
    if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit):
        raise SystemExit("Expected a full lowercase immutable commit SHA")
    tool = Path(__file__).resolve().parent
    repo = tool.parents[1]
    output = Path(args.output).resolve()
    native = Path(args.native_artifacts).resolve()
    source_git = Path(args.source_git).resolve()
    dotnet = Path(args.dotnet).resolve()
    scratch_root = Path(args.scratch_root).resolve()
    packages = Path(args.packages).resolve()
    http_cache = Path(args.http_cache).resolve()
    fallbacks = [Path(value).resolve() for value in args.fallback_packages]

    def read_git(arguments, cwd=repo):
        return subprocess.check_output(["git", *arguments], cwd=cwd, text=True).strip()

    def verify_source():
        if read_git(["rev-parse", "HEAD"]) != args.expected_commit:
            raise RuntimeError("Source HEAD does not match the requested immutable commit")
        if read_git(["status", "--porcelain"]):
            raise RuntimeError("Publishing requires committed, clean source")

    verify_source()
    tree = read_git(["rev-parse", "HEAD^{tree}"])
    if output.exists():
        raise SystemExit("Refusing to replace an existing output directory")
    # Generic paths, but no internal-drive fallback: all writable locations must be on the same
    # mounted volume as this external-drive project. Existing read-only fallback caches may differ.
    project_device = repo.stat().st_dev
    for path in [output, scratch_root, packages, http_cache, dotnet]:
        ancestor = path
        while not ancestor.exists():
            ancestor = ancestor.parent
        if ancestor.stat().st_dev != project_device:
            raise SystemExit("Writable task paths and SDK must stay on the project volume: " + str(path))
    existing = output.parent
    while not existing.exists():
        existing = existing.parent
    output_repo = Path(read_git(["rev-parse", "--show-toplevel"], cwd=existing)).resolve()
    relative_output = output.relative_to(output_repo)
    if not relative_output.parts or relative_output.parts[0] != "artifacts":
        raise SystemExit("Output must be under a repository's ignored artifacts directory")
    ignored = subprocess.run(["git", "check-ignore", "--quiet", "--", str(relative_output)], cwd=output_repo)
    if ignored.returncode != 0:
        raise SystemExit("Output artifacts directory is not ignored by Git")
    for path in [dotnet, native, source_git, *fallbacks]:
        if not path.exists():
            raise SystemExit("Required existing dependency is missing: " + str(path))
    source_sdk = json.loads((repo / "global.json").read_text())["sdk"]["version"]
    if source_sdk != args.sdk_version:
        raise SystemExit("Exact SDK parameter differs from the committed global.json pin")
    pins = read_current_pins(tool, repo)
    for path, pin in [("native/Avalonia.Native/src/OSX/AvnView.mm", "SOURCE_SHA256"),
            ("src/Avalonia.Native/avn.idl", "IDL_SHA256")]:
        source_bytes = subprocess.check_output(["git", "--git-dir", str(source_git), "show", pins["COMMIT"] + ":" + path])
        if hashlib.sha256(source_bytes).hexdigest() != pins[pin]:
            raise RuntimeError("Cached pinned upstream source differs from the tracked hash: " + path)
    native_verification, native_manifest, signed_hash = verify_native_artifact(native, pins, args.runtime, args.sdk_version)

    for path in [scratch_root, packages, http_cache]:
        path.mkdir(parents=True, exist_ok=True)
    output.mkdir(parents=True)
    evidence = output / "native-evidence"
    evidence.mkdir()
    for name in ["build-verification.json", "stock-regression.json", "patched-regression.json",
            "avalonia-12.0.4-text-context.patch", "Avalonia-LICENSE.txt", MANIFEST_NAME]:
        shutil.copy2(native / name, evidence / name)
    commands = []
    env = dict(os.environ, TMPDIR=str(scratch_root), DOTNET_ROOT=str(dotnet.parent),
        PATH=str(dotnet.parent) + os.pathsep + os.environ.get("PATH", ""), NUGET_PACKAGES=str(packages),
        NUGET_HTTP_CACHE_PATH=str(http_cache), NUGET_FALLBACK_PACKAGES=";".join(str(path) for path in fallbacks),
        DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
    throttle = ["pwsh", "-NoProfile", "-File", str(repo / "tools/Invoke-Throttled.ps1")]

    def run(label, arguments, kind=None):
        command = list(map(str, arguments))
        if kind:
            command = throttle + ["-Kind", kind, "--"] + command
        record = {"label": label, "arguments": command}
        commands.append(record)
        print(label + ": running", flush=True)
        stdout = output / (label + ".stdout.log")
        stderr = output / (label + ".stderr.log")
        with stdout.open("wb") as out, stderr.open("wb") as err:
            child = subprocess.Popen(command, cwd=repo, env=env, stdout=out, stderr=err, start_new_session=True)
            try:
                code = child.wait()
            except BaseException:
                if child.poll() is None:
                    try:
                        os.killpg(child.pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
                    try:
                        child.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        os.killpg(child.pid, signal.SIGKILL)
                        child.wait()
                raise
        record["exitCode"] = code
        if code:
            raise RuntimeError(label + " exited " + str(code) + "; inspect " + str(stdout) + " and " + str(stderr))
        print(label + ": exit 0", flush=True)
        return stdout.read_text(errors="replace").strip()

    def verify_app_inputs(app):
        contents = app / "Contents/MacOS"
        verify_managed_native(contents / "Avalonia.Native.dll")
        verify_managed_metadata(contents / "FreeW.dll", signed_hash)
        if digest(contents / pins["LIBRARY"]) != signed_hash:
            raise RuntimeError("Packaged native library bytes changed after the verified leaf signature")
        if (contents / MANIFEST_NAME).read_bytes() != (native / MANIFEST_NAME).read_bytes():
            raise RuntimeError("Packaged native capability manifest changed")
        if digest(contents / "FreeW.NativeTextContext.LICENSE.txt") != digest(native / "Avalonia-LICENSE.txt"):
            raise RuntimeError("Packaged framework copyright/license changed")

    def interrupted(_signal, _frame):
        raise KeyboardInterrupt()

    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    result = {"commit": args.expected_commit, "sourceTree": tree, "runtime": args.runtime,
        "distribution": "local preview; ad-hoc signed; not notarized", "commands": commands, "result": "failed",
        "nativeCapability": native_manifest, "nativeBuildVerificationSha256": digest(native / "build-verification.json"),
        "preSigningNativeSha256": native_verification.get("preSigningLibrarySha256"),
        "signedNativeSha256": signed_hash, "managedNativeSha256": MANAGED_NATIVE_SHA256,
        "upstreamSourceSha256": pins["SOURCE_SHA256"], "idlSha256": pins["IDL_SHA256"],
        "patchSha256": pins["PATCH_SHA256"],
        "managedNativeInformationalVersion": MANAGED_NATIVE_VERSION,
        "publisherSourceSha256": digest(Path(__file__).resolve())}
    dotnet_build_started = False
    try:
        with tempfile.TemporaryDirectory(prefix="freew-local-publish-", dir=scratch_root) as task:
            scratch = Path(task)
            env["DOTNET_CLI_HOME"] = str(scratch / "cli-home")
            env["NUGET_SCRATCH"] = str(scratch / "nuget-scratch")
            sdk = run("sdk", [dotnet, "--version"])
            if sdk != args.sdk_version:
                raise RuntimeError("Selected dotnet does not provide the exact requested SDK")
            result["sdk"] = sdk
            run("native-leaf-signature", ["codesign", "--verify", "--strict", native / pins["LIBRARY"]])
            if run("native-leaf-architecture", ["lipo", "-archs", native / pins["LIBRARY"]]) != "arm64":
                raise RuntimeError("Native artifact is not the requested ARM64 library")
            project = "freew/FreeW.App.Avalonia/FreeW.App.Avalonia.csproj"
            metadata_property = "-p:FreeWNativeTextContextSha256=" + signed_hash
            dotnet_build_started = True
            version = run("project-version", [dotnet, "msbuild", project, "-nologo", "-getProperty:Version", metadata_property])
            if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){2,3}(?:[-+][A-Za-z0-9.\-]+)?", version):
                raise RuntimeError("Cannot establish a single valid project version")
            result["version"] = version
            publish = scratch / "publish"
            run("dotnet-publish", [dotnet, "publish", project, "--configuration", "Release", "--runtime", args.runtime,
                "--self-contained", "true", "--output", publish, metadata_property, "--disable-build-servers",
                "-p:UseSharedCompilation=false", "-p:NodeReuse=false", "/nr:false", "-m:1"], kind="Build")
            verify_managed_native(publish / "Avalonia.Native.dll")
            verify_managed_metadata(publish / "FreeW.dll", signed_hash)
            result["assemblyMetadataVerification"] = "unique ECMA-335 AssemblyMetadata fixed-string pair"
            inputs = scratch / "input"
            inputs.mkdir()
            portable = inputs / ("FreeW-v" + version + "-" + args.runtime + ".zip")
            with zipfile.ZipFile(portable, "w", zipfile.ZIP_DEFLATED) as archive:
                for item in sorted(publish.rglob("*")):
                    if item.is_symlink():
                        raise RuntimeError("Unexpected symlink in portable publish output")
                    if item.is_file():
                        archive.write(item, item.relative_to(publish))
            installers = scratch / "installers"
            run("stage-installer", ["pwsh", "-NoProfile", "-File", "tools/packaging/New-AppInstallers.ps1",
                "-Apps", "FreeW", "-Platform", "macos", "-Version", version, "-Runtime", args.runtime,
                "-InputRoot", inputs, "-OutputDir", installers], kind="Build")
            staged = installers / ".installer-work/FreeW-osx-arm64/FreeW.app"
            if not staged.is_dir():
                raise RuntimeError("Installer staging did not produce the expected FreeW.app")
            run("plist", ["plutil", "-lint", staged / "Contents/Info.plist"])
            app = output / "FreeW.app"
            run("copy-stock-app", ["ditto", staged, app])
            run("stock-app-deep-sign", ["codesign", "--force", "--deep", "--sign", "-", app])
            contents = app / "Contents/MacOS"
            # Only after deep signing the stock app: preserve the EXACT already signed leaf bytes.
            shutil.copy2(native / pins["LIBRARY"], contents / pins["LIBRARY"])
            shutil.copy2(native / MANIFEST_NAME, contents / MANIFEST_NAME)
            shutil.copy2(native / "Avalonia-LICENSE.txt", contents / "FreeW.NativeTextContext.LICENSE.txt")
            verify_app_inputs(app)
            run("outer-app-sign", ["codesign", "--force", "--sign", "-", app])
            run("app-deep-strict-verify", ["codesign", "--verify", "--deep", "--strict", app])
            verify_app_inputs(app)
            run("app-executable", ["file", contents / "FreeW"])
            if run("app-architecture", ["lipo", "-archs", contents / "FreeW"]) != "arm64":
                raise RuntimeError("Published application is not ARM64")
            archive = output / "FreeW-macos-arm64-local.zip"
            run("zip-app", ["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, archive])
            roundtrip = scratch / "roundtrip"
            run("extract-roundtrip", ["ditto", "-x", "-k", archive, roundtrip])
            run("roundtrip-deep-strict-verify", ["codesign", "--verify", "--deep", "--strict", roundtrip / "FreeW.app"])
            verify_app_inputs(roundtrip / "FreeW.app")
            verify_source()
            result["archiveSha256"] = digest(archive)
            (output / (archive.name + ".sha256")).write_text(result["archiveSha256"] + "  " + archive.name + "\n")
            result["nativeHashAfterOuterSigning"] = digest(contents / pins["LIBRARY"])
            result["nativeHashAfterZipRoundtrip"] = digest(roundtrip / "FreeW.app/Contents/MacOS" / pins["LIBRARY"])
            result["sourceCleanAtEnd"] = True
            result["result"] = "passed"
            print("Verified local preview at " + str(app), flush=True)
    except BaseException as error:
        result["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        try:
            if dotnet_build_started and args.defer_build_server_shutdown:
                result["buildServerShutdownDeferred"] = True
                print("SDK shutdown handed to the coordinator; persistent publish build servers were disabled.", flush=True)
            elif dotnet_build_started:
                with tempfile.TemporaryDirectory(prefix="freew-publish-shutdown-", dir=scratch_root) as shutdown:
                    env["DOTNET_CLI_HOME"] = str(Path(shutdown) / "cli-home")
                    env["NUGET_SCRATCH"] = str(Path(shutdown) / "nuget-scratch")
                    run("build-server-shutdown", [dotnet, "build-server", "shutdown"])
        except BaseException as error:
            result["result"] = "failed"
            result["cleanupError"] = type(error).__name__ + ": " + str(error)
            raise
        finally:
            (output / "publish-verification.json").write_text(json.dumps(result, indent=2) + "\n")


if __name__ == "__main__":
    main()
