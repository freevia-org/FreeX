using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsAppReadinessPreflightTests
{
    [Fact]
    public void MacOsAppReadinessPreflight_DeclaresMacOsBundleWorkflowAndSourceContracts()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1"));

        script.Should().Contain("Avalonia app TargetFramework must be net10.0");
        script.Should().Contain("Avalonia app RuntimeIdentifiers");
        script.Should().Contain("ApplicationTitle");
        script.Should().Contain("CFBundleName");
        script.Should().Contain("CFBundleIconFile");
        script.Should().Contain("FreeX.icns");
        script.Should().Contain("Test-MacOsIcon");
        script.Should().Contain("NSHighResolutionCapable");
        script.Should().Contain("dotnet-version: 10.0.x");
        script.Should().Contain("--framework net10.0");
        script.Should().Contain("--output \"$app/Contents/MacOS\"");
        script.Should().Contain("native_fill_color_swatch_count=69");
        script.Should().Contain("native_font_color_swatch_count=69");
        script.Should().Contain("native_cell_styles_menu_item=true");
        script.Should().Contain("native_cell_styles_preset_count=33");
        script.Should().Contain("shasum -a 256 -c \"$zip_name.sha256\"");
        script.Should().Contain("zip_sha256=$zip_sha256");
        script.Should().Contain("freex-$runtime-macos-tester-instructions.md");
        script.Should().Contain("native_horizontal_text_menu_item=true");
        script.Should().Contain("native_rotate_text_down_menu_item=");
        script.Should().Contain("PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode)");
        script.Should().Contain("StartWithClassicDesktopLifetime(startupArguments)");
        script.Should().Contain("IActivatableLifetime");
        script.Should().Contain("OpenActivatedFilesAsync");
        script.Should().Contain("CreateNativePasteSpecialMenu()");
        script.Should().Contain("PasteSpecialClipboardAtActiveCell(text, mode, options)");
        script.Should().Contain("AddStyledCellBorderOverlay(content, style);");
        script.Should().Contain("CreateDrawingObjectVisual(drawingObject, width, height)");
        script.Should().Contain("TryCreateDrawingBitmap(imageBytes, out var bitmap)");
        script.Should().Contain("private static bool HasVisibleCellBorder(CellStyle? style)");
        script.Should().Contain("Portable macOS source contains forbidden token");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated macOS app source wiring markers.");
        result.Output.Should().Contain("Validated portable macOS source hygiene");
        result.Output.Should().Contain("macOS app readiness preflight passed.");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForWindowsSpecificAvaloniaTargetFramework()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path, targetFramework: "net10.0-windows");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("Avalonia app TargetFramework must be net10.0");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForUnexpectedWorkflowRuntime()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path, workflowExtraRuntime: "osx-ppc");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("macOS workflow runtime markers must not include unexpected value 'osx-ppc'");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForForbiddenPortableSourceToken()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(
            temp.Path,
            extraAvaloniaSource: """
            namespace FreeX.App.Avalonia;

            internal static class WindowsOnlyLeak
            {
                private const string Token = "System.Windows";
            }
            """);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        var combinedOutput = result.Output + result.Error;
        combinedOutput.Should().Contain("Portable macOS source contains forbidden token 'System.Windows'");
        combinedOutput.Should().Contain("src/FreeX.App.Avalonia/WindowsOnlyLeak.cs");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForMalformedMacOsIcon()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path);
        File.WriteAllText(
            Path.Combine(temp.Path, "src", "FreeX.App.Avalonia", "Packaging", "macos", "FreeX.icns"),
            "not-an-icns");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("macOS app icon must start with the icns magic header");
    }

    private static PowerShellResult RunScriptFromTemporaryWorkingDirectory(string scriptPath, string arguments)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, arguments);
    }

    private static void CreateMinimalMacOsReadinessRepo(
        string root,
        string targetFramework = "net10.0",
        string workflowExtraRuntime = "",
        string extraAvaloniaSource = "")
    {
        WriteFile(
            root,
            "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\FreeX.App.Services\FreeX.App.Services.csproj" />
                <ProjectReference Include="..\FreeX.Core.Calc\FreeX.Core.Calc.csproj" />
                <ProjectReference Include="..\FreeX.Core.Commands\FreeX.Core.Commands.csproj" />
                <ProjectReference Include="..\FreeX.Core.IO\FreeX.Core.IO.csproj" />
                <ProjectReference Include="..\FreeX.Core.Model\FreeX.Core.Model.csproj" />
              </ItemGroup>
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.0.4" />
                <PackageReference Include="Avalonia.Desktop" Version="12.0.4" />
                <PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.4" />
                <PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.4" />
              </ItemGroup>
              <ItemGroup>
                <Content Include="Packaging\macos\FreeX.icns" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
              </ItemGroup>
              <PropertyGroup>
                <AssemblyName>FreeX</AssemblyName>
                <ApplicationTitle>FreeX</ApplicationTitle>
                <OutputType>Exe</OutputType>
                <RuntimeIdentifiers>osx-arm64;osx-x64</RuntimeIdentifiers>
                <TargetFramework>{{TargetFramework}}</TargetFramework>
              </PropertyGroup>
            </Project>
            """.Replace("{{TargetFramework}}", targetFramework));

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/Packaging/macos/Info.plist",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
              <key>CFBundleDisplayName</key>
              <string>FreeX</string>
              <key>CFBundleDocumentTypes</key>
              <array>
                <dict>
                  <key>CFBundleTypeExtensions</key>
                  <array>
                    <string>fxl</string>
                  </array>
                  <key>CFBundleTypeName</key>
                  <string>FreeX Workbook</string>
                  <key>CFBundleTypeRole</key>
                  <string>Editor</string>
                  <key>LSHandlerRank</key>
                  <string>Owner</string>
                </dict>
                <dict>
                  <key>CFBundleTypeExtensions</key>
                  <array>
                    <string>xlsx</string>
                    <string>xlsm</string>
                    <string>xltx</string>
                    <string>xltm</string>
                    <string>xls</string>
                    <string>xlsb</string>
                    <string>xlt</string>
                    <string>csv</string>
                    <string>tsv</string>
                    <string>tab</string>
                  </array>
                  <key>CFBundleTypeName</key>
                  <string>Spreadsheet Workbooks</string>
                  <key>CFBundleTypeRole</key>
                  <string>Viewer</string>
                  <key>LSHandlerRank</key>
                  <string>Alternate</string>
                </dict>
              </array>
              <key>CFBundleExecutable</key>
              <string>FreeX</string>
              <key>CFBundleIdentifier</key>
              <string>io.github.tony-xmelon.freex</string>
              <key>CFBundleIconFile</key>
              <string>FreeX.icns</string>
              <key>CFBundleName</key>
              <string>FreeX</string>
              <key>CFBundlePackageType</key>
              <string>APPL</string>
              <key>LSMinimumSystemVersion</key>
              <string>12.0</string>
              <key>NSHighResolutionCapable</key>
              <true/>
            </dict>
            </plist>
            """);

        WriteFile(
            root,
            ".github/workflows/macos-app.yml",
            $"""
            name: macOS App Preview
            jobs:
              macos-app:
                runs-on: macos-latest
                strategy:
                  matrix:
                    runtime:
                      - osx-arm64
                      - osx-x64
                      {FormatWorkflowRuntimeLine(workflowExtraRuntime)}
                steps:
                  - uses: actions/setup-dotnet@v5
                    with:
                      dotnet-version: 10.0.x
                  - run: dotnet build src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj --configuration Release
                  - shell: bash
                    run: |
                      app="$RUNNER_TEMP/FreeX.app"
                      artifact_root="$GITHUB_WORKSPACE/artifacts"
                      runtime="osx-arm64"
                      zip_name="freex-$runtime-macos-app.zip"
                      zip_path="$artifact_root/$zip_name"
                      unzip_root="$RUNNER_TEMP/freex-$runtime-unzip"
                      echo "Developer ID signing is disabled for pull_request events; using ad-hoc signing."
                      dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj \
                        --configuration Release \
                        --framework net10.0 \
                        --runtime "$runtime" \
                        --self-contained true \
                        -p:UseAppHost=true \
                        -p:PublishReadyToRun=false \
                        -p:PublishSingleFile=false \
                        --output "$app/Contents/MacOS"
                      cp src/FreeX.App.Avalonia/Packaging/macos/Info.plist "$app/Contents/Info.plist"
                      cp src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns "$app/Contents/Resources/FreeX.icns"
                      plutil -lint "$app/Contents/Info.plist"
                      test -f "$app/Contents/MacOS/FreeX"
                      test -x "$app/Contents/MacOS/FreeX"
                      test -f "$app/Contents/MacOS/FreeX.dll"
                      test -f "$app/Contents/Resources/FreeX.icns"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0' "$app/Contents/Info.plist"
                      lipo -archs "$app/Contents/MacOS/FreeX"
                      codesign --verify --deep --strict "$app"
                      ditto -c -k --sequesterRsrc --keepParent "$app" "$zip_path"
                      (cd "$artifact_root" && shasum -a 256 "$zip_name" > "$zip_name.sha256")
                      test -x "$unzip_root/FreeX.app/Contents/MacOS/FreeX"
                      test -f "$unzip_root/FreeX.app/Contents/MacOS/FreeX.dll"
                      xcrun notarytool submit "$zip_path"
                      xcrun stapler validate "$app"
                      tester_instructions_path="$artifact_root/freex-$runtime-macos-tester-instructions.md"
                      shasum -a 256 -c "$zip_name.sha256"
                      zip_sha256="$(cut -d ' ' -f 1 "$artifact_root/$zip_name.sha256")"
                      echo "zip_sha256=$zip_sha256"
                      cat > "$tester_instructions_path" <<EOF
                      This artifact is a preview build for macOS port validation. It is not a public release channel.
                      Use osx-arm64 for Apple Silicon Macs and osx-x64 for Intel Macs.
                      Unzip the GitHub Actions artifact wrapper first; these files are inside it.
                      Ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.
                      EOF
                      "$unzip_root/FreeX.app/Contents/MacOS/FreeX" --packaging-smoke "$RUNNER_TEMP/smoke.csv"
                      grep -q "Packaging smoke opened" "$artifact_root/smoke.log"
                      grep -q "edited, saved, and reopened" "$artifact_root/smoke.log"
                      /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$unzip_root/FreeX.app"
                      open -W -n -b io.github.tony-xmelon.freex "$RUNNER_TEMP/launch.csv" --args --macos-launch-smoke "$artifact_root/launch.txt"
                      osascript -e 'tell application id "io.github.tony-xmelon.freex" to quit' || true
                      grep -q "native_file_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_edit_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_format_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_cut_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_copy_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_contents_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_bold_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_font_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_color_swatch_count=69" "$artifact_root/launch.txt"
                      grep -q "native_font_color_swatch_count=69" "$artifact_root/launch.txt"
                      grep -q "native_cell_styles_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_cell_styles_preset_count=33" "$artifact_root/launch.txt"
                      grep -q "native_horizontal_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_angle_counterclockwise_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_angle_clockwise_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_vertical_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_rotate_text_up_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_rotate_text_down_menu_item=true" "$artifact_root/launch.txt"
                      echo "bundle_icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist")"
                  - uses: actions/upload-artifact@v7
                    with:
                      if-no-files-found: error
                      path: artifacts/freex-osx-arm64-macos-tester-instructions.md
            """);

        WriteMinimalIcns(root, "src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns");

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/Program.cs",
            """
            namespace FreeX.App.Avalonia;

            internal static class Program
            {
                public static int Main(string[] args)
                {
                    if (PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode))
                        return smokeExitCode;

                    MacOsLaunchSmokeOptions.TryParse(args, out var launchSmokeOptions, out var startupArguments, out var launchSmokeError);
                    App.StartupArguments = startupArguments;
                    App.LaunchSmokeOptions = launchSmokeOptions;
                    BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);
                    return 0;
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/App.cs",
            """
            namespace FreeX.App.Avalonia;

            public sealed class App
            {
                private static async Task ActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
                {
                    this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime;
                    if (args is not FileActivatedEventArgs fileArgs || fileArgs.Kind != ActivationKind.File)
                        return;

                    await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/MainWindow.cs",
            """
            namespace FreeX.App.Avalonia;

            public sealed class MainWindow
            {
                private const string NativeWorkbookExtension = ".fxl";
                public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files) => await Task.CompletedTask;
                private static void RenderCell(CellStyle? style)
                {
                    CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true);
                    CreateNativePasteSpecialMenu();
                    PasteSpecialClipboardAtActiveCell(text, mode, options);
                    CellColorPalettePlanner.BuildDefaultSwatches();
                    CreateDrawingObjectVisual(drawingObject, width, height);
                    TryCreateDrawingBitmap(imageBytes, out var bitmap);
                    AddStyledCellBorderOverlay(content, style);
                }
                private static bool HasVisibleCellBorder(CellStyle? style) => true;
                internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot() => new();
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/MacOsLaunchSmoke.cs",
            """
            namespace FreeX.App.Avalonia;

            internal sealed class MacOsLaunchSmokeOptions
            {
                public const string Argument = "--macos-launch-smoke";
                public static void Parse(List<string> filteredArguments, out string[] startupArguments)
                {
                    startupArguments = filteredArguments.ToArray();
                }
            }

            internal sealed class MacOsLaunchSmokeSnapshot
            {
                public bool IsPassed => HasNativeFileMenu && HasNativeEditMenu && HasNativeFormatMenu && HasNativeCellStylesMenuItem && HasNativeCopyMenuItem;
                private bool HasNativeFileMenu { get; }
                private bool HasNativeEditMenu { get; }
                private bool HasNativeFormatMenu { get; }
                private bool HasNativeCellStylesMenuItem { get; }
                private bool HasNativeCopyMenuItem { get; }
                public int NativeCellStylesPresetCount { get; }
                public string Report => "native_cut_menu_item= native_copy_menu_item= native_paste_special_menu_item= native_clear_contents_menu_item= native_bold_menu_item= native_fill_color_swatch_count= native_font_color_swatch_count= native_cell_styles_menu_item= native_cell_styles_preset_count= native_horizontal_text_menu_item= native_angle_counterclockwise_menu_item= native_angle_clockwise_menu_item= native_vertical_text_menu_item= native_rotate_text_up_menu_item= native_rotate_text_down_menu_item=";
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookStartupSmokeService.cs",
            """
            namespace FreeX.App.Services;

            internal sealed class WorkbookStartupSmokeService
            {
                private const string RoundTripExtension = ".fxl";
                private const string Result = "Packaging smoke opened and edited, saved, and reopened";
            }

            public static class PackagingSmokeCommand
            {
                public const string Argument = "--packaging-smoke";
            }
            """);

        WriteFile(
            root,
            "src/FreeX.Core.IO/NativeJsonAdapter.cs",
            """
            namespace FreeX.Core.IO;

            public sealed class NativeJsonAdapter
            {
                public string Extension => ".fxl";
                public string FormatName => "FreeX Workbook";
            }
            """);

        if (!string.IsNullOrWhiteSpace(extraAvaloniaSource))
        {
            WriteFile(root, "src/FreeX.App.Avalonia/WindowsOnlyLeak.cs", extraAvaloniaSource);
        }
    }

    private static string FormatWorkflowRuntimeLine(string runtime)
    {
        return string.IsNullOrWhiteSpace(runtime)
            ? ""
            : $"- {runtime}";
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteMinimalIcns(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(
            path,
            [
                (byte)'i', (byte)'c', (byte)'n', (byte)'s',
                0, 0, 0, 32,
                (byte)'i', (byte)'c', (byte)'p', (byte)'4',
                0, 0, 0, 8,
                (byte)'i', (byte)'c', (byte)'p', (byte)'5',
                0, 0, 0, 8,
                (byte)'i', (byte)'c', (byte)'0', (byte)'8',
                0, 0, 0, 8
            ]);
    }
}
