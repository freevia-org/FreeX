using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class MacOsLaunchSmokeReportKeyDriftGuardTests
{
    private static readonly Regex SourceReportKeyPattern = new(
        "\\$\"(?<key>(?:native|toolbar)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WorkflowGrepReportKeyPattern = new(
        "grep -q \"(?<key>(?:native|toolbar)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReadinessReportKeyMarkerPattern = new(
        "\"(?<key>(?:native|toolbar)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommandKeySourceReportKeyPattern = new(
        "\\$\"(?<key>(?:command_key|cmd|live_command_key|live_cmd)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommandKeyWorkflowGrepReportKeyPattern = new(
        "grep -q \"(?<key>(?:command_key|cmd|live_command_key|live_cmd)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DialogSourceReportKeyPattern = new(
        "\\$\"(?<key>(?:macos_dialog|find_dialog|replace_dialog|go_to_dialog|go_to_special_dialog|format_cells_dialog|sort_dialog|data_validation_dropdown|data_validation_dialog)[a-z0-9_]*)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DialogWorkflowGrepReportKeyPattern = new(
        "grep -q \"(?<key>(?:macos_dialog|find_dialog|replace_dialog|go_to_dialog|go_to_special_dialog|format_cells_dialog|sort_dialog|data_validation_dropdown|data_validation_dialog)[a-z0-9_]*)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DialogReadinessReportKeyMarkerPattern = new(
        "\"(?<key>(?:macos_dialog|find_dialog|replace_dialog|go_to_dialog|go_to_special_dialog|format_cells_dialog|sort_dialog|data_validation_dropdown|data_validation_dialog)[a-z0-9_]*)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AccessibilitySourceReportKeyPattern = new(
        "\\$\"(?<key>(?:macos_accessibility_smoke|a11y_[a-z0-9_]+))=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AccessibilityWorkflowGrepReportKeyPattern = new(
        "grep -q \"(?<key>(?:macos_accessibility_smoke|a11y_[a-z0-9_]+))=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AccessibilityReadinessReportKeyMarkerPattern = new(
        "\"(?<key>(?:macos_accessibility_smoke|a11y_[a-z0-9_]+))=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ExpectedCommandKeyReportKeys =
    [
        "cmd_bold_menu_gesture",
        "cmd_close_workbook_menu_gesture",
        "cmd_find_direct_route_source_guard",
        "cmd_find_menu_gesture",
        "cmd_italic_menu_gesture",
        "cmd_new_workbook_menu_gesture",
        "cmd_open_menu_gesture",
        "cmd_page_down_direct_route_source_guard",
        "cmd_page_up_direct_route_source_guard",
        "cmd_quit_menu_gesture",
        "cmd_save_as_menu_gesture",
        "cmd_save_menu_gesture",
        "cmd_select_all_menu_gesture",
        "cmd_underline_menu_gesture",
        "command_key_smoke",
        "command_key_smoke_attempted",
        "live_cmd_bold_received",
        "live_cmd_bold_state_changed",
        "live_cmd_italic_received",
        "live_cmd_italic_state_changed",
        "live_cmd_select_all_received",
        "live_cmd_select_all_state_changed",
        "live_cmd_underline_received",
        "live_cmd_underline_state_changed",
        "live_command_key_smoke",
        "live_command_key_smoke_attempted",
        "live_command_key_smoke_ready",
        "live_command_key_smoke_required"
    ];

    private static readonly string[] ExpectedHostedWorkflowCommandKeyGrepKeys =
    [
        "cmd_bold_menu_gesture",
        "cmd_close_workbook_menu_gesture",
        "cmd_find_direct_route_source_guard",
        "cmd_find_menu_gesture",
        "cmd_italic_menu_gesture",
        "cmd_new_workbook_menu_gesture",
        "cmd_open_menu_gesture",
        "cmd_page_down_direct_route_source_guard",
        "cmd_page_up_direct_route_source_guard",
        "cmd_quit_menu_gesture",
        "cmd_save_as_menu_gesture",
        "cmd_save_menu_gesture",
        "cmd_select_all_menu_gesture",
        "cmd_underline_menu_gesture",
        "command_key_smoke",
        "command_key_smoke_attempted",
        "live_command_key_smoke",
        "live_command_key_smoke_required"
    ];

    [Fact]
    public void MacOsLaunchSmoke_NativeAndToolbarReportKeysMatchWorkflowGrepsAndReadinessMarkers()
    {
        var sourceReportKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs")),
            SourceReportKeyPattern);
        var workflowGrepKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "macos-app.yml")),
            WorkflowGrepReportKeyPattern);
        var readinessMarkerKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1")),
            ReadinessReportKeyMarkerPattern);

        sourceReportKeys.Should().NotBeEmpty("MacOsLaunchSmoke should emit native_ and toolbar_ report keys");
        workflowGrepKeys.Should().NotBeEmpty("the macOS workflow should grep native_ and toolbar_ report keys");
        readinessMarkerKeys.Should().NotBeEmpty("the readiness preflight should track native_ and toolbar_ report keys");

        FindSetDrift(sourceReportKeys, workflowGrepKeys, "MacOsLaunchSmoke report", "macOS workflow grep")
            .Should()
            .BeEmpty("the macOS workflow grep contract should match the native_ and toolbar_ report keys emitted by MacOsLaunchSmoke");

        FindMissingKeys(readinessMarkerKeys, sourceReportKeys, "readiness report marker", "MacOsLaunchSmoke report")
            .Should()
            .BeEmpty("readiness report markers should stay backed by MacOsLaunchSmoke native_ and toolbar_ report keys");
    }

    [Fact]
    public void MacOsLaunchSmoke_DialogReportKeysMatchWorkflowGrepsAndReadinessMarkers()
    {
        var sourceReportKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs")),
            DialogSourceReportKeyPattern);
        var workflowGrepKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "macos-app.yml")),
            DialogWorkflowGrepReportKeyPattern);
        var readinessMarkerKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1")),
            DialogReadinessReportKeyMarkerPattern);

        sourceReportKeys.Should().NotBeEmpty("MacOsLaunchSmoke should emit dialog report keys");
        workflowGrepKeys.Should().NotBeEmpty("the macOS workflow should grep dialog report keys");
        readinessMarkerKeys.Should().NotBeEmpty("the readiness preflight should track dialog report keys");

        FindSetDrift(sourceReportKeys, workflowGrepKeys, "MacOsLaunchSmoke dialog report", "macOS workflow grep")
            .Should()
            .BeEmpty("the macOS workflow grep contract should match the dialog report keys emitted by MacOsLaunchSmoke");

        FindSetDrift(sourceReportKeys, readinessMarkerKeys, "MacOsLaunchSmoke dialog report", "readiness report marker")
            .Should()
            .BeEmpty("the readiness dialog marker contract should match the dialog report keys emitted by MacOsLaunchSmoke");
    }

    [Fact]
    public void MacOsLaunchSmoke_AccessibilityReportKeysMatchWorkflowGrepsAndReadinessMarkers()
    {
        var sourceReportKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs")),
            AccessibilitySourceReportKeyPattern);
        var workflowGrepKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "macos-app.yml")),
            AccessibilityWorkflowGrepReportKeyPattern);
        var readinessMarkerKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1")),
            AccessibilityReadinessReportKeyMarkerPattern);

        sourceReportKeys.Should().NotBeEmpty("MacOsLaunchSmoke should emit accessibility report keys");
        workflowGrepKeys.Should().NotBeEmpty("the macOS workflow should grep accessibility report keys");
        readinessMarkerKeys.Should().NotBeEmpty("the readiness preflight should track accessibility report keys");

        FindSetDrift(sourceReportKeys, workflowGrepKeys, "MacOsLaunchSmoke accessibility report", "macOS workflow grep")
            .Should()
            .BeEmpty("the macOS workflow grep contract should match the accessibility report keys emitted by MacOsLaunchSmoke");

        FindSetDrift(sourceReportKeys, readinessMarkerKeys, "MacOsLaunchSmoke accessibility report", "readiness report marker")
            .Should()
            .BeEmpty("the readiness accessibility marker contract should match the accessibility report keys emitted by MacOsLaunchSmoke");
    }

    [Fact]
    public void MacOsLaunchSmoke_CommandKeyReportKeysMatchSourcePlanningAndHostedSafeWorkflowMarkers()
    {
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs"));
        var workflow = File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "macos-app.yml"));
        var planning = File.ReadAllText(RepositoryFileLocator.Find("docs", "planning", "multiplatform-macos-port.md"));

        var sourceReportKeys = ExtractDistinctKeys(smokeSource, CommandKeySourceReportKeyPattern);
        var workflowGrepKeys = ExtractDistinctKeys(workflow, CommandKeyWorkflowGrepReportKeyPattern);

        sourceReportKeys.Should().Equal(ExpectedCommandKeyReportKeys);
        workflowGrepKeys.Should().Equal(ExpectedHostedWorkflowCommandKeyGrepKeys);
        workflow.Should().Contain("live_command_key_smoke=not_required");

        FindMissingKeys(
                ExpectedHostedWorkflowCommandKeyGrepKeys,
                sourceReportKeys,
                "hosted workflow grep marker",
                "MacOsLaunchSmoke report")
            .Should()
            .BeEmpty("every hosted workflow grep should stay backed by a MacOsLaunchSmoke report key");

        foreach (var key in ExpectedCommandKeyReportKeys)
            planning.Should().Contain($"{key}=");

        smokeSource.Should().Contain("internal sealed record MacOsLaunchSmokeCommandKeySnapshot(");
        smokeSource.Should().Contain("internal sealed record MacOsLaunchSmokeLiveCommandKeySnapshot(");
        smokeSource.Should().Contain("commandKeyEvidence = CaptureCommandKeyEvidence(access);");
        smokeSource.Should().Contain("liveCommandKeyEvidence = access.BeginLiveCommandKeyProbe();");
        smokeSource.Should().Contain("commandKeyEvidence.IsPassed");
        smokeSource.Should().Contain("liveCommandKeyEvidence.IsPassed");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_newWorkbookMenuItem\", Key.N, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_openMenuItem\", Key.O, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_saveMenuItem\", Key.S, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_saveAsMenuItem\", Key.S, KeyModifiers.Meta | KeyModifiers.Shift)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_closeWorkbookMenuItem\", Key.W, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_quitMenuItem\", Key.Q, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_selectAllMenuItem\", Key.A, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_findMenuItem\", Key.F, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_boldMenuItem\", Key.B, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_italicMenuItem\", Key.I, KeyModifiers.Meta)");
        smokeSource.Should().Contain("access.HasNativeMenuItemGesture(\"_underlineMenuItem\", Key.U, KeyModifiers.Meta)");
        smokeSource.Should().Contain("HasFindDirectRouteSourceGuard: MainWindow.LaunchSmokeAccessAdapter.HasMethods(");
        smokeSource.Should().Contain("HasPageUpDirectRouteSourceGuard: MainWindow.LaunchSmokeAccessAdapter.HasMethods(");
        smokeSource.Should().Contain("HasPageDownDirectRouteSourceGuard: MainWindow.LaunchSmokeAccessAdapter.HasMethods(");
        smokeSource.Should().Contain("cmd_find_direct_route_source_guard={FormatBool(commandKeyEvidence.HasFindDirectRouteSourceGuard)}");
        smokeSource.Should().Contain("cmd_page_up_direct_route_source_guard={FormatBool(commandKeyEvidence.HasPageUpDirectRouteSourceGuard)}");
        smokeSource.Should().Contain("cmd_page_down_direct_route_source_guard={FormatBool(commandKeyEvidence.HasPageDownDirectRouteSourceGuard)}");
    }

    private static string[] ExtractDistinctKeys(string text, Regex pattern) =>
        pattern.Matches(text)
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    private static string[] FindSetDrift(
        IReadOnlyCollection<string> expectedKeys,
        IReadOnlyCollection<string> actualKeys,
        string expectedLabel,
        string actualLabel)
    {
        var missing = expectedKeys
            .Where(key => !actualKeys.Contains(key))
            .Select(key => $"{actualLabel} is missing '{key}' from {expectedLabel}");
        var unexpected = actualKeys
            .Where(key => !expectedKeys.Contains(key))
            .Select(key => $"{actualLabel} has unexpected '{key}' not found in {expectedLabel}");

        return missing.Concat(unexpected).ToArray();
    }

    private static string[] FindMissingKeys(
        IReadOnlyCollection<string> expectedKeys,
        IReadOnlyCollection<string> actualKeys,
        string expectedLabel,
        string actualLabel) =>
        expectedKeys
            .Where(key => !actualKeys.Contains(key))
            .Select(key => $"{actualLabel} is missing '{key}' from {expectedLabel}")
            .ToArray();
}
