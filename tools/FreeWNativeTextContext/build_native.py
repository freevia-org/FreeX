#!/usr/bin/env python3
"""Build and verify FreeW's pinned, package-local Cocoa text-context safety patch."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tarfile
import tempfile
from xml.sax.saxutils import escape

EXTERNAL = Path("/Volumes/MacMini Work")
COMMIT = "a8dd6417fd8918570edefdbecd92d16ac7620069"
SOURCE_SHA256 = "d435fc8c18e9f6259786a8c5c09bc5f528754a215c9ab97996505f1eb6aa4206"
IDL_SHA256 = "e0c7b0e2b1155b38cb04306a91b992d7c89ccd78ef0905007bc1fd198fd76224"
PATCH_SHA256 = "2f45f5945e3d2fbe64bdc02c6fd8f0c84146caa8a18f62c2a04c96ed1a7ab8c4"
MARKER = "FreeW_AvaloniaNative_TextContext_V1"
LIBRARY = "libAvaloniaNative.dylib"


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def external_path(value):
    path = Path(value).resolve()
    if not path.is_relative_to(EXTERNAL):
        raise ValueError("Task source, caches, outputs and scratch must be on the external drive: " + str(path))
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-git", required=True, help="Read-only cached upstream bare Git repository")
    parser.add_argument("--output", required=True, help="New ignored artifact directory; never overwritten")
    parser.add_argument("--dotnet", required=True, help="Installed external exact 10.0.400 dotnet executable")
    parser.add_argument("--packages", required=True, help="External global NuGet packages directory")
    parser.add_argument("--fallback-packages", action="append", default=[], help="Existing expanded read-only package cache; repeatable")
    parser.add_argument("--stock-library", required=True, help="Existing Avalonia.Native 12.0.4 library for the failing baseline")
    parser.add_argument("--defer-build-server-shutdown", action="store_true",
        help="Coordinator will shut down SDK400 servers after concurrent application verification ends")
    args = parser.parse_args()
    if sys.platform != "darwin" or not EXTERNAL.is_dir():
        raise SystemExit("This task requires macOS and the mounted external drive.")
    (EXTERNAL / "Tmp").mkdir(exist_ok=True)

    tool = Path(__file__).resolve().parent
    repo = tool.parents[1]
    source_git = external_path(args.source_git)
    output = external_path(args.output)
    dotnet = external_path(args.dotnet)
    packages = external_path(args.packages)
    fallbacks = [Path(value).resolve() for value in args.fallback_packages]
    stock = Path(args.stock_library).resolve()  # Existing internal cache may be read, never modified.
    patch = tool / "avalonia-12.0.4-text-context.patch"
    if output.parent == EXTERNAL:
        raise SystemExit("Use an ignored project artifacts folder, not the top of the external drive.")
    if output.exists():
        raise SystemExit("Refusing to overwrite an existing artifact directory: " + str(output))
    if digest(patch) != PATCH_SHA256:
        raise SystemExit("Native patch hash differs from the managed capability pin.")
    managed = (repo / "freew/FreeW.App.Avalonia/Editing/FreeWNativeTextContextCapability.cs").read_text()
    if 'PatchSha256 = "' + PATCH_SHA256 + '"' not in managed:
        raise SystemExit("Managed capability and native patch pins differ.")
    for path in [source_git, dotnet, packages, stock, *fallbacks]:
        if not path.exists():
            raise SystemExit("Required existing dependency is missing: " + str(path))

    output.mkdir(parents=True)
    commands = []
    env = dict(os.environ)
    env.update(DOTNET_ROOT=str(dotnet.parent), PATH=str(dotnet.parent) + os.pathsep + env.get("PATH", ""),
        TMPDIR=str(EXTERNAL / "Tmp"), NUGET_PACKAGES=str(packages),
        NUGET_FALLBACK_PACKAGES=";".join(str(path) for path in fallbacks),
        NUGET_HTTP_CACHE_PATH=str(packages.parent / "v3-cache"),
        DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
    throttle = ["pwsh", "-NoProfile", "-File", str(repo / "tools/Invoke-Throttled.ps1")]
    git = ["git", "--git-dir=" + str(source_git)]

    def run(label, arguments, cwd=None, expected=0, kind=None):
        arguments = [str(value) for value in arguments]
        if kind:
            arguments = throttle + ["-Kind", kind, "--"] + arguments
        stdout = output / (label + ".stdout.log")
        stderr = output / (label + ".stderr.log")
        record = {"label": label, "arguments": arguments, "expected_exit": expected}
        commands.append(record)
        print(label + ": running", flush=True)
        with stdout.open("wb") as out, stderr.open("wb") as err:
            process = subprocess.Popen(arguments, cwd=cwd or repo, env=env, stdout=out, stderr=err,
                start_new_session=True)
            try:
                code = process.wait()
            except BaseException:
                # Only this command's owned process group is stopped. Reap before scratch cleanup.
                if process.poll() is None:
                    try:
                        os.killpg(process.pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
                    try:
                        process.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        os.killpg(process.pid, signal.SIGKILL)
                        process.wait()
                raise
        record["exit_code"] = code
        if code != expected:
            raise RuntimeError(label + " exited " + str(code) + "; see " + str(stdout) + " and " + str(stderr))
        print(label + ": exit " + str(code), flush=True)
        return stdout.read_text(errors="replace")

    def interrupted(_signal, _frame):
        raise KeyboardInterrupt()

    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    result = {"upstreamCommit": COMMIT, "patchSha256": PATCH_SHA256, "idlSha256": IDL_SHA256,
        "generatorVersion": "0.11.4", "runtime": "osx-arm64", "commands": commands, "result": "failed"}
    dotnet_build_started = False
    try:
        sdk = run("sdk", [dotnet, "--version"]).strip()
        if sdk != "10.0.400":
            raise RuntimeError("Expected exact SDK 10.0.400, found " + sdk)
        result["sdk"] = sdk
        run("source-pin", git + ["cat-file", "-e", COMMIT + "^{commit}"])
        with tempfile.TemporaryDirectory(prefix="freew-native-text-context-", dir=env["TMPDIR"]) as task:
            scratch = Path(task)
            source = scratch / "source"
            source.mkdir()
            archive = scratch / "native-source.tar"
            run("source-archive", git + ["archive", "--format=tar", "--output=" + str(archive), COMMIT,
                "native/Avalonia.Native", "src/Avalonia.Native/avn.idl"])
            with tarfile.open(archive) as contents:
                for member in contents:
                    target = (source / member.name).resolve()
                    if not target.is_relative_to(source) or not (member.isdir() or member.isfile()):
                        raise RuntimeError("Unexpected upstream archive entry: " + member.name)
                    if member.isdir():
                        target.mkdir(parents=True, exist_ok=True)
                    else:
                        target.parent.mkdir(parents=True, exist_ok=True)
                        with contents.extractfile(member) as stream, target.open("wb") as dest:
                            shutil.copyfileobj(stream, dest)
            native_source = source / "native/Avalonia.Native/src/OSX/AvnView.mm"
            idl = source / "src/Avalonia.Native/avn.idl"
            if digest(native_source) != SOURCE_SHA256 or digest(idl) != IDL_SHA256:
                raise RuntimeError("Extracted native source or IDL does not match the pinned upstream input.")
            run("patch-check", ["git", "apply", "--check", str(patch)], cwd=source)
            run("patch-apply", ["git", "apply", str(patch)], cwd=source)
            license_text = run("license", git + ["show", COMMIT + ":licence.md"])
            (output / "Avalonia-LICENSE.txt").write_text(license_text)
            shutil.copy2(patch, output / patch.name)

            generator = scratch / "generator"
            generator.mkdir()
            env["DOTNET_CLI_HOME"] = str(scratch / "cli-home")
            env["NUGET_SCRATCH"] = str(scratch / "nuget-scratch")
            (generator / "GenerateNativeHeader.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="MicroCom.CodeGenerator" Version="0.11.4" /></ItemGroup>
</Project>
''')
            shutil.copy2(tool / "GenerateNativeHeader.cs", generator / "Program.cs")
            config = generator / "NuGet.Config"
            # Only already cached packages are used. A missing dependency fails without downloading
            # unexpected tools or changing the existing fallback caches.
            config.write_text('<configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear />'
                + ''.join('<add key="cache' + str(index) + '" value="' + escape(str(path), {'"': '&quot;'}) + '" />'
                    for index, path in enumerate(fallbacks)) + '</fallbackPackageFolders></configuration>')
            dotnet_build_started = True
            run("header-generator-build", [dotnet, "build", generator / "GenerateNativeHeader.csproj", "--configuration", "Release",
                "--configfile", config, "--disable-build-servers", "-p:UseSharedCompilation=false", "-p:NodeReuse=false", "/nr:false", "-m:1"], kind="Build")
            header = source / "native/Avalonia.Native/inc/avalonia-native.h"
            run("generate-header", [dotnet, generator / "bin/Release/net10.0/GenerateNativeHeader.dll", idl, header])
            result["generatedHeaderSha256"] = digest(header)
            xcconfig = scratch / "PathNormalization.xcconfig"
            intermediates = scratch / "xcode/Intermediates"
            xcconfig.write_text('OTHER_CFLAGS = $(inherited) "-ffile-prefix-map=' + str(source) + '=/_/Avalonia" "-fdebug-prefix-map='
                + str(source) + '=/_/Avalonia"\nOTHER_LDFLAGS = $(inherited) -Xlinker -oso_prefix -Xlinker "'
                + str(intermediates) + '/"\nDSYMUTIL_FLAGS = $(inherited) --oso-prepend-path "' + str(intermediates) + '/"\n')
            products = scratch / "native"
            run("native-build", ["xcodebuild", "-project", source / "native/Avalonia.Native/src/OSX/Avalonia.Native.OSX.xcodeproj",
                "-scheme", "Avalonia.Native.OSX", "-configuration", "Release", "-destination", "generic/platform=macOS",
                "-derivedDataPath", scratch / "xcode/DerivedData", "OBJROOT=" + str(intermediates),
                "CONFIGURATION_BUILD_DIR=" + str(products), "ARCHS=arm64", "ONLY_ACTIVE_ARCH=YES",
                "MACOSX_DEPLOYMENT_TARGET=12.0", "CODE_SIGNING_ALLOWED=NO", "INDEX_ENABLE_DATA_STORE=NO",
                "-disableAutomaticPackageResolution", "-xcconfig", xcconfig], cwd=source, kind="Build")
            library = output / LIBRARY
            shutil.copy2(products / "libAvalonia.Native.OSX.dylib", library)
            result["preSigningLibrarySha256"] = digest(library)
            run("native-sign", ["codesign", "--force", "--sign", "-", "--identifier",
                "org.freesuite.freew.avalonia-native.text-context-v1", library])
            run("native-signature-verify", ["codesign", "--verify", "--strict", library])
            result["signedLibrarySha256"] = digest(library)
            architecture = run("native-architecture", ["lipo", "-archs", library]).strip()
            if architecture != "arm64":
                raise RuntimeError("Unexpected native architecture: " + architecture)
            run("native-dependencies", ["otool", "-L", library])

            harness = scratch / "TextContextNativeTests"
            sdk_path = run("macos-sdk", ["xcrun", "--sdk", "macosx", "--show-sdk-path"]).strip()
            run("harness-build", ["xcrun", "clang++", "-std=c++17", "-fobjc-arc", "-arch", "arm64",
                "-isysroot", sdk_path, "-mmacosx-version-min=12.0", "-framework", "AppKit",
                tool / "TextContextNativeTests.mm", "-o", harness], kind="Build")
            stock_json = output / "stock-regression.json"
            patched_json = output / "patched-regression.json"
            run("stock-regression", [harness, stock, stock_json], expected=1, kind="Test")
            stock_report = json.loads(stock_json.read_text())
            if stock_report["rangeFailures"] <= 0 or stock_report["selectionFailures"] <= 0:
                raise RuntimeError("The existing stock binary did not reproduce both pinned native defects.")
            run("patched-regression", [harness, library, patched_json], kind="Test")
            patched_report = json.loads(patched_json.read_text())
            result["stockLibrarySha256"] = digest(stock)
            result["stockRegression"] = stock_report
            result["patchedRegression"] = patched_report
            if digest(library) != result["signedLibrarySha256"]:
                raise RuntimeError("Verified native library bytes changed after signing.")
            manifest = {"schemaVersion": 1, "contractVersion": 1, "upstreamCommit": COMMIT,
                "patchSha256": PATCH_SHA256, "idlSha256": IDL_SHA256, "generatorVersion": "0.11.4",
                "libraryFile": LIBRARY, "librarySha256": result["signedLibrarySha256"],
                "markerSymbol": MARKER, "markerAbi": "cdecl:int32(void)"}
            (output / "FreeW.NativeTextContext.json").write_text(json.dumps(manifest, indent=2) + "\n")
            result["result"] = "passed"
            print("Verified pre-signed library SHA256: " + result["signedLibrarySha256"], flush=True)
    except BaseException as error:
        result["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        # No persistent servers are requested for the generator. Still shut down after an attempted
        # build in this coordinator-granted slot. Use a separate temporary CLI home: the task's
        # original scratch was already removed, and cleanup must not recreate that deleted tree.
        try:
            if dotnet_build_started and args.defer_build_server_shutdown:
                result["buildServerShutdownDeferred"] = True
                print("SDK shutdown deferred to the coordinator; generator persistent servers were disabled.", flush=True)
            elif dotnet_build_started:
                with tempfile.TemporaryDirectory(prefix="freew-native-shutdown-", dir=env["TMPDIR"]) as shutdown:
                    env["DOTNET_CLI_HOME"] = str(Path(shutdown) / "cli-home")
                    env["NUGET_SCRATCH"] = str(Path(shutdown) / "nuget-scratch")
                    run("build-server-shutdown", [dotnet, "build-server", "shutdown"])
        finally:
            (output / "build-verification.json").write_text(json.dumps(result, indent=2) + "\n")


if __name__ == "__main__":
    main()
