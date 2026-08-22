using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxFreeXInteractionValidationToolTests
{
    [Fact]
    public void RunnerExposesPhysicalOnlyModeAndKeepsTheDefaultManagedLane()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        script.Should().Contain("[switch]$PhysicalOnly");
        script.Should().Contain("validationMode = \"physical-only\"");
        script.Should().Contain("scope = \"bounded physical X11 probes\"");
        script.Should().Contain("summary = [pscustomobject]@{}");
        script.Should().Contain("-PhysicalOnly cannot be combined with -SkipX11");
        script.Should().Contain("Phase two uses a fresh X11 process for each bounded dialog slice");
        script.Should().Contain("--interaction-validation");
    }

    [Fact]
    public void PhysicalOnlyBranchDoesNotDispatchManagedInteractionValidation()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var physicalOnlyStart = script.IndexOf("if ($PhysicalOnly)", StringComparison.Ordinal);
        var managedLaneStart = script.IndexOf("} else {", physicalOnlyStart, StringComparison.Ordinal);

        physicalOnlyStart.Should().BeGreaterThanOrEqualTo(0);
        managedLaneStart.Should().BeGreaterThan(physicalOnlyStart);
        script[physicalOnlyStart..managedLaneStart]
            .Should().NotContain("--interaction-validation");
    }

    [Fact]
    public void PhysicalOnlyModeStillRunsTheBoundedX11ProbeScript()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        script.Should().Contain("run-freex-input-probes.sh");
        script.Should().Contain("\"DISPLAY=:99\"");
        script.Should().Contain("\"FREEX_X11_PROBE_SELECTOR=$PhysicalProbeSelector\"");
        script.Should().Contain("& docker exec @($probeEnvironment | ForEach-Object { \"--env\"; $_ })");
        script.Should().Contain("Physical X11 manifest does not satisfy schema v2");
        script.Should().Contain("x11-validation/x11-input-results.json");
    }

    [Fact]
    public void InlineEditFocusedSelectorUsesKeyboardReadbackAndExactArtifacts()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("\"inline-edit\"");
        runner.Should().Contain("@(" + "\"inline-edit-f2-enter-commit\"" + ")");
        probe.Should().Contain("if [[ \"$probe_selector\" == \"inline-edit\" ]]; then");
        probe.Should().Contain("copy_cell_formula_by_keyboard \"$column_offset\" \"$row_offset\"");
        probe.Should().Contain("local_artifacts+=\";inline-edit-commit-after.png;inline-edit-commit-after-cell.png\"");
        probe.Should().Contain("inline-edit-commit-before-cell.png");
    }

    [Fact]
    public void GridAutofitSelectorRequiresColumnRowAndHiddenBoundarySchemaV2Evidence()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var schema = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "freex-grid-autofit-validation.schema.json"));
        var fixture = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave164GridAutofitFixture.ps1"));

        runner.Should().Contain("Assert-GridAutofitPostcondition");
        runner.Should().Contain("grid-header-double-click-autofit-column-physical");
        runner.Should().Contain("grid-header-double-click-autofit-row-physical");
        runner.Should().Contain("grid-header-double-click-autofit-hidden-row-boundary-physical");
        runner.Should().Contain("$hiddenRowsAfterValid");
        runner.Should().Contain("Grid AutoFit hidden-row diagnostic");
        runner.Should().Contain("freex-wave164-grid-autofit.xlsx");
        probe.Should().Contain("grid-autofit-postcondition.json");
        probe.Should().Contain("hiddenRowsBefore\\\":[4,5]");
        probe.Should().Contain("hiddenRowsAfter\\\":$hidden_rows_after");
        probe.Should().Contain("hidden_row5_top=$((a1_y + 3 * cell_height + hidden_row4_height - handle_center_inset))");
        probe.Should().Contain("xdotool click --repeat 2 --delay 180 1");
        schema.Should().Contain("\"schemaVersion\": { \"const\": 2 }");
        schema.Should().Contain("\"hiddenRowBoundary\"");
        schema.Should().Contain("\"unhidden\": { \"type\": \"boolean\" }");
        fixture.Should().Contain("hidden=\"1\"");
        fixture.Should().Contain("r=\"4\"");
        fixture.Should().Contain("r=\"5\"");
    }

    [Fact]
    public void PhysicalPointerHelperWaitsForX11DeliveryBeforeDependentClicks()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var helperStart = probe.IndexOf("xdotool_mousemove_sync()", StringComparison.Ordinal);
        var helperEnd = probe.IndexOf("send_key()", helperStart, StringComparison.Ordinal);

        helperStart.Should().BeGreaterThanOrEqualTo(0);
        helperEnd.Should().BeGreaterThan(helperStart);
        probe[helperStart..helperEnd]
            .Should().Contain("timeout --foreground --kill-after=1s")
            .And.Contain("xdotool mousemove --sync \"$target_x\" \"$target_y\"")
            .And.Contain("sleep 0.12")
            .And.Contain("xdotool \"$@\"")
            .And.Contain("mousemove_timeout_count");
    }

    [Fact]
    public void SelectionDiagnosticsBoundImageMagickConnectedComponents()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var selectionStart = probe.IndexOf("selection_box()", StringComparison.Ordinal);
        var selectionEnd = probe.IndexOf("capture_selection()", selectionStart, StringComparison.Ordinal);

        selectionStart.Should().BeGreaterThanOrEqualTo(0);
        selectionEnd.Should().BeGreaterThan(selectionStart);
        probe[selectionStart..selectionEnd]
            .Should().Contain("image_tool_timeout_seconds")
            .And.Contain("timeout --foreground --kill-after=1s")
            .And.Contain("-connected-components 8 null:")
            .And.Contain("|| true");
    }

    [Fact]
    public void GridDragSeedHelper_UsesKeyboardReadbackAndEmptyAwareClipboardVerification()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var helperStart = probe.IndexOf("set_cell_text_without_save()", StringComparison.Ordinal);
        var helperEnd = probe.IndexOf("select_sheet_tab()", helperStart, StringComparison.Ordinal);

        helperStart.Should().BeGreaterThanOrEqualTo(0);
        helperEnd.Should().BeGreaterThan(helperStart);
        var helper = probe[helperStart..helperEnd];

        helper.Should().Contain("if [[ -n \"$value\" ]]; then");
        helper.Should().Contain("copy_cell_formula_by_keyboard \"$column_offset\" \"$row_offset\"");
        helper.Should().Contain("copy_cell_formula_allow_empty \"$column_offset\" \"$row_offset\" \"$address\" keyboard");
        helper.Should().NotContain("copy_cell_formula \"$column_offset\" \"$row_offset\" \"$address\"");
        helper.Should().NotContain("type_text \"$value\"\n    send_key Return");

        probe.Should().Contain("select_cell_by_keyboard()");
        probe.Should().Contain("select_cell_by_keyboard \"$column_offset\" \"$row_offset\" && selected=true");
    }

    [Fact]
    public void SplitPanePointerSelectorRequiresSharedScrollbarPhysicalEvidenceRows()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("\"split-pane-pointer\"");
        runner.Should().Contain("split-pane-divider-drag-physical");
        runner.Should().Contain("split-pane-active-pane-wheel-physical");
        runner.Should().Contain("split-pane-bottom-left-wheel-physical");
        runner.Should().Contain("split-pane-mini-scrollbar-physical");
        probe.Should().Contain("probe_split_pane_pointer()");
        probe.Should().Contain("local split_keytip_route=\"WSP\"");
        probe.Should().Contain("--window \"$window_id\" w s p");
        probe.Should().Contain("split-command-gesture=keytip-route-$split_keytip_route");
        probe.Should().NotContain("split_button_x");
        probe.Should().Contain("split-pane-before-grid.png");
        probe.Should().Contain("split-pane-open-grid.png");
        probe.Should().Contain("xdotool mousedown 1");
        probe.Should().Contain("xdotool keydown --window \"$window_id\" Shift_L");
        probe.Should().Contain("xdotool click 5");
        probe.Should().Contain("split-pane-pointer-postcondition.txt");
        probe.Should().Contain("divider-postcondition=$divider_passed");
        probe.Should().Contain("active-pane-shared-column-band-postcondition=$wheel_passed");
        probe.Should().Contain("bottom-left-shared-row-band-postcondition=$bottom_wheel_passed");
        probe.Should().Contain("mini-scrollbar-shared-column-band-postcondition=$scrollbar_passed");
    }

    [Fact]
    public void PhysicalEvidencePackagingUsesLongPathSafeExactFileCopies()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        const string longestObservedEvidenceName =
            "selection-outline-nested-column-inner-collapsed-visible-slot.png";

        var helperStart = script.IndexOf("function Copy-LongPathSafeFile", StringComparison.Ordinal);
        var helperEnd = script.IndexOf("function Get-DirectoryFingerprint", helperStart, StringComparison.Ordinal);
        var packagingStart = script.IndexOf("$x11ReportDirectory =", StringComparison.Ordinal);
        var packagingEnd = script.IndexOf("if ($PhysicalOnly)", packagingStart, StringComparison.Ordinal);

        helperStart.Should().BeGreaterThanOrEqualTo(0);
        helperEnd.Should().BeGreaterThan(helperStart);
        script[helperStart..helperEnd].Should().Contain("[IO.File]::Copy(\"\\\\?\\$sourcePath\"");
        packagingStart.Should().BeGreaterThanOrEqualTo(0);
        packagingEnd.Should().BeGreaterThan(packagingStart);
        script[packagingStart..packagingEnd]
            .Should().Contain("Copy-LongPathSafeFile -Source $evidenceFile.FullName -Destination $evidenceDestination")
            .And.NotContain("Copy-Item");

        Path.Combine(new string('x', 240), longestObservedEvidenceName)
            .Length.Should().BeGreaterThan(260,
                "the observed nested-outline evidence name must exercise the Windows MAX_PATH regression");
    }

    [Fact]
    public void NameBoxObjectValidationUsesFixedIdentityContractsAndNeutralBaselines()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        script.Should().Contain("$expectedOrder = @(");
        script.Should().Contain("$expectedContracts = [ordered]@{");
        script.Should().Contain("expectedName = \"PhysicalChart\"");
        script.Should().Contain("expectedName = \"PhysicalPicture\"");
        script.Should().Contain("expectedName = \"PhysicalShape\"");
        script.Should().Contain("expectedName = \"PhysicalTextBox\"");
        script.Should().Contain("observedSelectedObjectKind");
        script.Should().Contain("baselineStage -ne \"neutral-cell-selected\"");
        script.Should().Contain("baselineNameBox -ne \"J20\"");
        script.Should().Contain("expectedOrder='$(@($actualOrder) -join ',')'");
    }

    [Fact]
    public void NameBoxObjectPostconditionClosesItsPythonHeredocBeforeShellHelpers()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find(
                "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        script.Should().Contain(
            "    stream.write(\"\\n\")\nPY\n}\n\nread_name_box_event() {");
    }

    [Fact]
    public void NameBoxInteractionLaneRequiresNativeKeyboardAndPointerCommitEvidence()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("Assert-NameBoxDropdownInteractionPostcondition");
        runner.Should().Contain("name-box-dropdown-keyboard-physical");
        runner.Should().Contain("name-box-dropdown-mouse-physical");
        probe.Should().Contain("send_key alt+Down");
        probe.Should().Contain("keyboard-gesture=Alt+Down,Home,Down,Down,Down,Down,Enter");
        probe.Should().Contain("mouse-gesture=NameBoxChevron,PhysicalTableRow");
        probe.Should().Contain("name-box-dropdown-interaction-postcondition.txt");
    }

    [Fact]
    public void NameBoxObjectEventsPreserveEmptyFieldsAndCaptureTheSettledPointerSelection()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var window = File.ReadAllText(RepositoryFileLocator.Find(
                "src", "FreeX.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var physicalEvidence = File.ReadAllText(RepositoryFileLocator.Find(
                "tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.NameBoxPhysicalEvidence.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        probe.Should().Contain("print(\"\\x1f\".join(value(key)");
        probe.Should().Contain("IFS=$'\\x1f' read -r baseline_sequence");
        probe.Should().Contain("IFS=$'\\x1f' read -r observed_sequence");

        var releaseStart = window.IndexOf(
            "private async Task EndCellSelectionDragAsync",
            StringComparison.Ordinal);
        var releaseEnd = window.IndexOf(
            "private bool TryResolveCellPointerAddress",
            releaseStart,
            StringComparison.Ordinal);
        releaseStart.Should().BeGreaterThanOrEqualTo(0);
        releaseEnd.Should().BeGreaterThan(releaseStart);
        window[releaseStart..releaseEnd].Should().Contain(
            "RevertNameBoxAfterCellSelectionDragEnd();\n" +
            "        RecordOptionalNeutralCellSelection();");
        physicalEvidence.Should().Contain(
            "partial void RecordOptionalNeutralCellSelection() =>\n" +
            "        RecordNameBoxDropdownPhysicalEvidence(item: null, stage: \"neutral-cell-selected\");");

        var selectCellStart = window.IndexOf(
            "private void SelectCell(CellAddress address)",
            StringComparison.Ordinal);
        var selectCellEnd = window.IndexOf(
            "private void SelectRange(CellAddress address)",
            selectCellStart,
            StringComparison.Ordinal);
        window[selectCellStart..selectCellEnd].Should().NotContain(
            "RecordNameBoxDropdownPhysicalEvidence");
    }

    [Fact]
    public void NameBoxParityPhysicalLaneRequiresLiveX11CropProvenance()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("\"name-box-dropdown-parity\"");
        runner.Should().Contain("--freex-name-box-dropdown-parity-physical");
        runner.Should().Contain("Assert-NameBoxDropdownParityNativeContract");
        runner.Should().Contain("native-x11-root-crop");
        runner.Should().Contain("Name Box parity native crop pixels must be 208x136");
        runner.Should().Contain("name-box-dropdown-parity-native");

        probe.Should().Contain("probe_name_box_dropdown_parity()");
        probe.Should().Contain("name-box-dropdown-parity-before-x11.txt");
        probe.Should().Contain("name-box-dropdown-parity-open-x11.txt");
        probe.Should().Contain("xdotool mousemove --window \"$window_id\"");
        probe.Should().Contain("local home_ready=false");
        probe.Should().Contain("for _ in $(seq 1 20)");
        probe.Should().Contain("if ! $home_ready");
        probe.Should().Contain("if len(candidates) == 1");
        probe.Should().Contain("-crop \"208x136+${popup_x}+${popup_y}\" +repage");
        probe.Should().Contain("\"evidenceProvenance\": \"native-x11-root-crop\"");
        probe.Should().Contain("\"resized\": False");
        probe.Should().NotContain("-resize 208x136");
    }

    [Fact]
    public void OutlineGroupPhysicalLaneRequiresHeaderContextMenuAndStructuralHidingEvidence()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("\"outline-group\"");
        runner.Should().Contain("outline-group-physical");
        probe.Should().Contain("probe_outline_group_physical()");
        probe.Should().Contain("selection-gesture=row-header-drag-2:4");
        probe.Should().Contain("group-gesture=row-header-right-click,End,Up,Up,Up,Enter");
        probe.Should().Contain("outline_toggle_visible");
        probe.Should().Contain("row-gutter-width=$row_gutter_width");
        probe.Should().Contain("collapsed-visible-slot=$collapsed_slot");
        probe.Should().Contain("collapse-structural=$collapsed_structurally");
        probe.Should().Contain("outline-collapsed.png");
        probe.Should().Contain("outline-expanded.png");
        probe.Should().Contain("values-restored=$values_restored");
        probe.Should().Contain("xdotool_mousemove_sync \"$toggle_x\" \"$toggle_y\" click 1");
        probe.Should().MatchRegex(
            @"probe_window_management\r?\nprobe_split_pane_pointer\r?\nprobe_outline_group_physical");
        runner.Split("\"outline-group-physical\"").Should().HaveCountGreaterThanOrEqualTo(4,
            "the focused selector and default all lane must both require the physical outline result and artifacts");
    }

    [Fact]
    public void NestedOutlinePhysicalLaneRequiresBothAxesAndIndependentLevelToggles()
    {
        var runner = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        runner.Should().Contain("\"outline-nested-group\"");
        runner.Should().Contain("outline-nested-rows-group-physical");
        runner.Should().Contain("outline-nested-columns-group-physical");
        probe.Should().Contain("probe_outline_nested_rows_physical()");
        probe.Should().Contain("probe_outline_nested_columns_physical()");
        probe.Should().Contain("inner-selection=row-header-drag-11:12");
        probe.Should().Contain("outer-selection=row-header-drag-10:14");
        probe.Should().Contain("inner-selection=column-header-drag-I:K");
        probe.Should().Contain("set_expected_outline_origin \"$expected_inner_depth\" \"$column_outline_depth\" \"outline-nested-rows-inner-origin.png\"");
        probe.Should().Contain("set_expected_outline_origin \"$row_outline_depth\" \"$expected_inner_depth\" \"outline-nested-columns-inner-origin.png\"");
        probe.Should().Contain("dismiss_active_popups");
        probe.Should().NotContain("refresh_grid_origin()");
        probe.Should().Contain("[[ ! \"$target_x\" =~ ^[0-9]+$ || ! \"$target_y\" =~ ^[0-9]+$ ]]");
        probe.Should().Contain("row-gutter-width=$row_gutter_width");
        probe.Should().Contain("column-gutter-height=$column_gutter_height");
        probe.Should().Contain("inner-collapsed-address-value=$inner_collapsed_slot");
        probe.Should().Contain("outer-expanded-address-value=$outer_expanded_slot");
        probe.Should().Contain("inner-collapse-structural=$inner_collapsed");
        probe.Should().Contain("outer-expand-structural=$outer_expanded");
        probe.Should().Contain("inner_collapsed_y=\"$(cell_center_y 10)\"");
        probe.Should().Contain("outer_collapsed_y=\"$(cell_center_y 9)\"");
        probe.Should().Contain("[[ \"$inner_collapsed_slot\" == \"NestedRow13\" ]]");
        probe.Should().Contain("[[ \"$outer_collapsed_slot\" == \"NestedRowOuterSummary\" ]]");
        probe.Should().Contain("[[ \"$inner_collapsed_slot\" == \"NestedColumnL\" ]]");
        probe.Should().Contain("[[ \"$outer_collapsed_slot\" == \"NestedColumnOuterSummary\" ]]");
        probe.Should().NotContain("outline-green=$grouped_score");
        probe.Should().NotContain("inner-collapse-screen-changed=$inner_collapsed");
        probe.Should().Contain("outline-nested-rows-inner-collapsed.png");
        probe.Should().Contain("outline-nested-columns-outer-expanded.png");
        probe.Should().Contain("NestedRow10,NestedRow11,NestedRow12,NestedRow13,NestedRow14");
        probe.Should().Contain("NestedColumnH,NestedColumnI,NestedColumnJ,NestedColumnK,NestedColumnL");
        probe.Should().Contain("outline-nested-group\" ]]; then");
        runner.Split("\"outline-nested-rows-group-physical\"").Should().HaveCountGreaterThanOrEqualTo(4,
            "the focused selector and default all lane must both require nested row evidence");
    }

    [Fact]
    public void NestedOutlinePhysicalReadbackUsesExactGoToAddressesAfterHiddenRanges()
    {
        var probe = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));

        probe.Should().Contain("copy_cell_formula_by_address()");
        probe.Should().Contain("send_key ctrl+g");
        probe.Should().Contain("the production Go To route");

        var rowsStart = probe.IndexOf("probe_outline_nested_rows_physical()", StringComparison.Ordinal);
        var columnsStart = probe.IndexOf("probe_outline_nested_columns_physical()", rowsStart, StringComparison.Ordinal);
        var saveStart = probe.IndexOf("probe_outline_nested_save_reopen_physical()", columnsStart, StringComparison.Ordinal);
        var filterStart = probe.IndexOf("probe_outline_nested_filter_save_reopen_physical()", saveStart, StringComparison.Ordinal);

        rowsStart.Should().BeGreaterThanOrEqualTo(0);
        columnsStart.Should().BeGreaterThan(rowsStart);
        saveStart.Should().BeGreaterThan(columnsStart);
        filterStart.Should().BeGreaterThan(saveStart);

        var rows = probe[rowsStart..columnsStart];
        var columns = probe[columnsStart..saveStart];
        var save = probe[saveStart..filterStart];
        var filter = probe[filterStart..];

        rows.Should().Contain("copy_cell_formula_by_address B13");
        rows.Should().Contain("copy_cell_formula_by_address B15");
        rows.Should().NotContain("copy_cell_formula 1 10");
        rows.Should().NotContain("copy_cell_formula 1 9");
        columns.Should().Contain("copy_cell_formula_by_address L2");
        columns.Should().Contain("copy_cell_formula_by_address M2");
        columns.Should().NotContain("copy_cell_formula 8 1");
        columns.Should().NotContain("copy_cell_formula 7 1");
        save.Should().Contain("copy_cell_formula_by_address B10");
        filter.Should().Contain("copy_cell_formula_by_address B3");
        filter.Should().Contain("copy_cell_formula_by_address B7");
    }
}
