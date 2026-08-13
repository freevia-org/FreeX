using System.Reflection;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;
using FreeW.Validation.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class SmokeValidationOwnershipTests
{
    [Fact]
    public void ShippingAssemblyRetainsNoSmokeCommandOwnership()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var shipping = Path.Combine(root, "freew", "FreeW.App.Avalonia");
        var program = File.ReadAllText(Path.Combine(shipping, "Program.cs"));
        var app = File.ReadAllText(Path.Combine(shipping, "App.cs"));
        var speech = File.ReadAllText(Path.Combine(shipping, "AvaloniaSpeechEngine.cs"));

        var smokeDirectory = Path.Combine(shipping, "Smoke");
        (Directory.Exists(smokeDirectory)
                ? Directory.EnumerateFiles(smokeDirectory, "*.cs", SearchOption.AllDirectories)
                : [])
            .Should().BeEmpty();
        program.Should().NotContain(SisterAppPackagingSmoke.Argument);
        program.Should().NotContain(ReadAloudPauseSmoke.Argument);
        program.Should().NotContain(SisterAppLaunchSmokeOptions.Argument);
        program.Should().NotContain("PackagingSmoke.TryRun");
        program.Should().NotContain("ReadAloudPauseSmoke.TryRun");
        program.Should().NotContain("RunToolHost");
        app.Should().NotContain("LaunchSmokeOptions");
        app.Should().NotContain("LaunchSmokeCoordinator");
        app.Should().NotContain("ExternalStartupCoordinator");
        speech.Should().NotContain("OwnedProcessIdForSmoke");

        typeof(FreeW.App.Avalonia.Program).GetMethod(
                "RunToolHost",
                BindingFlags.Static | BindingFlags.NonPublic)
            .Should().BeNull();
        typeof(App).GetProperty(
                "ExternalStartupCoordinator",
                BindingFlags.Static | BindingFlags.NonPublic)
            .Should().BeNull();
        typeof(MainWindow).GetNestedType(
                "ValidationAccessAdapter",
                BindingFlags.NonPublic)
            .Should().BeNull();
        typeof(AvaloniaSpeechEngine).GetProperty(
                "OwnedProcessIdForSmoke",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().BeNull();
    }

    [Fact]
    public void ValidationHostOwnsConditionalAppAccessSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var shipping = Path.Combine(root, "freew", "FreeW.App.Avalonia");
        var validation = Path.Combine(root, "freew", "TestSupport", "Validation.Avalonia");

        File.Exists(Path.Combine(shipping, "MainWindow.ValidationAccessAdapter.cs"))
            .Should().BeFalse();
        foreach (var fileName in new[]
                 {
                     "Program.ValidationHost.cs",
                     "MainWindow.ValidationAccessAdapter.cs",
                     "AvaloniaSpeechEngine.ValidationAccess.cs",
                 })
        {
            File.Exists(Path.Combine(validation, fileName)).Should().BeTrue(fileName);
        }

        var shippingProject = File.ReadAllText(Path.Combine(shipping, "FreeW.App.Avalonia.csproj"));
        var validationProject = File.ReadAllText(Path.Combine(validation, "FreeW.Validation.Avalonia.csproj"));
        shippingProject.Should().Contain("Condition=\"'$(FreeWValidationHost)' == 'true'\"");
        shippingProject.Should().Contain("..\\TestSupport\\Validation.Avalonia\\MainWindow.ValidationAccessAdapter.cs");
        shippingProject.Should().Contain("..\\TestSupport\\Validation.Avalonia\\AvaloniaSpeechEngine.ValidationAccess.cs");
        shippingProject.Should().Contain("<GlobalPropertiesToRemove>FreeWValidationHost</GlobalPropertiesToRemove>");
        validationProject.Should().Contain("AdditionalProperties=\"FreeWValidationHost=true\"");
        validationProject.Should().Contain("Compile Remove=\"MainWindow.ValidationAccessAdapter.cs\"");
    }

    [Fact]
    public void ValidationHostOwnsSmokeParsingFixturesReportsAndProcessProbe()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var validation = Path.Combine(root, "freew", "TestSupport", "Validation.Avalonia");
        var program = File.ReadAllText(Path.Combine(validation, "Program.cs"));
        var packaging = File.ReadAllText(Path.Combine(validation, "PackagingSmoke.cs"));
        var readAloud = File.ReadAllText(Path.Combine(validation, "ReadAloudPauseSmoke.cs"));
        var launch = File.ReadAllText(Path.Combine(validation, "LaunchSmoke.cs"));

        program.Should().Contain("ValidationHostCommandRouteExecutor.Run(");
        program.Should().Contain("PackagingSmoke.TryRun");
        program.Should().Contain("ReadAloudPauseSmoke.TryRun");
        program.Should().Contain("SisterAppLaunchSmokeOptions.TryParse");
        packaging.Should().Contain("SisterAppPackagingSmoke.TryRun(args, output, error, Execute, HandleException, out exitCode)");
        packaging.Should().Contain("SampleDocument.Create()");
        packaging.Should().Contain("DocxWriter.Write(doc, stream)");
        packaging.Should().NotContain("SisterAppPackagingSmoke.HasArgument(");
        packaging.Should().NotContain("output.Write(");
        packaging.Should().NotContain("error.WriteLine(");
        readAloud.Should().Contain("new AvaloniaSpeechEngine(");
        readAloud.Should().Contain("ReadLinuxProcessState(");
        launch.Should().Contain("new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport())");
        launch.Should().Contain("access.StartLaunchSmoke(");
    }

    [Fact]
    public void WorkflowsRouteExistingArgumentsThroughValidationHost()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "freew-linux.yml"));
        var speechRunner = File.ReadAllText(Path.Combine(root, "tools", "Run-FreeWReadAloudPauseValidation.ps1"));

        workflow.Should().Contain("freew/TestSupport/Validation.Avalonia/FreeW.Validation.Avalonia.csproj");
        workflow.Should().Contain("$validation_published/FreeW.Validation.Avalonia\" --packaging-smoke");
        workflow.Should().Contain("$validation_published/FreeW.Validation.Avalonia\" --launch-smoke");
        speechRunner.Should().Contain("\"-Host\", \"Validation\"");
        speechRunner.Should().Contain(ReadAloudPauseSmoke.Argument);
    }
}
