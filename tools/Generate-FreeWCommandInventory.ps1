param(
    [string]$JsonPath = "docs\parity\freew-command-inventory.json",
    [string]$MarkdownPath = "docs\parity\freew-command-inventory.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
$definitionsProject = ConvertTo-ToolXmlAttribute (Resolve-ToolRepoPath -Path "freew\FreeW.Ribbon.Definitions\FreeW.Ribbon.Definitions.csproj" -RepoRoot $repoRoot)

$programSource = @'
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Free.Shared.Ribbon;
using FreeW.Ribbon.Definitions;

if (args.Length != 3)
{
    throw new ArgumentException("Expected repository root, JSON output path, and Markdown output path.");
}

var inventory = FreeWCommandInventory.Build(args[0]);
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

File.WriteAllText(args[1], JsonSerializer.Serialize(inventory, options) + Environment.NewLine, Encoding.UTF8);
File.WriteAllText(args[2], FreeWCommandInventoryMarkdown.Build(inventory), Encoding.UTF8);

internal static class FreeWCommandInventory
{
    private const string WpfProfile = "WPF";
    private const string AvaloniaProfile = "Avalonia";

    private static readonly SourceLiteralFile[] SourceFiles =
    [
        new("canonicalDefinitionSource", "Canonical shared definition source", "freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.cs"),
        new("canonicalOrdinaryDefinitionSource", "Canonical ordinary-tab definition source", "freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.Ordinary.cs"),
        new("canonicalContextualDefinitionSource", "Canonical contextual-tab definition source", "freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.Contextual.cs"),
        new("wpfDefinitionSource", "WPF definition source", "freew/FreeW.Ribbon.Definitions/FreeWRibbon.cs"),
        new("avaloniaDefinitionSource", "Avalonia definition source", "freew/FreeW.Ribbon.Definitions/FreeWAvaloniaRibbonDefinition.cs"),
        new("wpfRegistrySource", "WPF registry source", "freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs"),
        new("avaloniaRegistrySource", "Avalonia registry source", "freew/FreeW.App.Avalonia/Ribbon/FreeWAvaloniaRibbonCommands.cs"),
        new("quickPartWorkflowSource", "Shared Quick Parts registry source", "freew/FreeW.App.Presentation/Ribbon/QuickPartRibbonWorkflow.cs"),
        new("tableInsertionWorkflowSource", "Shared Table insertion registry source", "freew/FreeW.App.Presentation/Ribbon/TableInsertionRibbonWorkflow.cs"),
        new("paragraphEditingWorkflowSource", "Shared Paragraph editing registry source", "freew/FreeW.App.Presentation/Ribbon/ParagraphEditingRibbonWorkflow.cs"),
    ];

    private static readonly ClassificationRule[] GapClassificationRules =
    [
        new("shared-profile", "Command is present in both compiled FreeW ribbon profiles."),
        new("command-id-alias", "Equivalent or near-equivalent command is exposed by the other profile under a different generated command id."),
        new("platform-only", "Command belongs to a host, shell, or desktop-only surface and should not be counted as cross-platform behavior debt."),
        new("profile-shape-only", "One-sided row is generated only by menu, dropdown, combo, gallery, or palette shape; any direct control requires paired behavior evidence."),
        new("deferred", "Command belongs to a known richer Word slice and any direct control carries paired behavior evidence for the intentional variance."),
        new("actionable-gap", "Shared Word behavior appears in one compiled profile only and needs product follow-up."),
    ];

    private static readonly Dictionary<string, CommandBehaviorEvidence> BehaviorEvidenceCatalog = new(StringComparer.Ordinal)
    {
        ["freew.delete-comment"] = ReviewCommentEvidence(
            "Deletes the comment thread at the caret, clears anchor/reference runs, and keeps the operation undoable in both shells.",
            "ThreadedCommentCommandTests.DeleteCommentAtCaret_RemovesThreadRangeAndReference",
            "DocumentViewReviewTests.DeleteCommentAtCaret_removes_the_comment"),
        ["freew.previous-comment"] = ReviewCommentEvidence(
            "Moves to the previous comment thread in document order, including wraparound behavior in both shells.",
            "ThreadedCommentCommandTests.CommentNavigation_WrapsAndNoOpsWithoutComments",
            "DocumentViewCommentTests.NextPreviousComment_moves_caret_in_document_order_and_wraps"),
        ["freew.next-comment"] = ReviewCommentEvidence(
            "Moves to the next comment thread in document order, including wraparound and table-cell comment anchors in both shells.",
            "ThreadedCommentCommandTests.CommentNavigation_MovesBetweenThreadsInDocumentOrder",
            "DocumentViewCommentTests.NextPreviousComment_moves_caret_in_document_order_and_wraps"),
        ["freew.resolve-comment"] = ReviewCommentEvidence(
            "Toggles the resolved state for the comment thread at the caret through WPF editor behavior and Avalonia registry execution.",
            "ThreadedCommentCommandTests.ToggleResolveCommentAtCaret_TogglesResolved",
            "DocumentViewReviewTests.ResolveComment_registry_command_toggles_the_comment_at_the_caret"),
        ["freew.track-changes"] = ReviewTrackingEvidence(
            "Toggles the WPF Track Changes state through the ribbon command and proves Avalonia records typed edits as tracked insertions while Track Changes is enabled.",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewTrackEditTests.cs",
            "DocumentViewTrackEditTests.Typing_with_TrackChanges_on_records_a_tracked_insertion"),
        ["freew.reviewing-pane"] = ReviewPaneEvidence(
            "Surfaces tracked revisions in the Reviewing Pane data path and resolves pane entries through the shared revision list behavior.",
            "ReviewingPaneTests.ListRevisions_SurfacesEveryTrackedChangeInReadingOrder",
            "ReviewingPaneTests.AcceptEntry_Insertion_Keeps_Run_As_Ordinary_Text"),
        ["freew.display-for-review"] = ReviewDisplayEvidence(
            "Keeps tracked revision data intact while returning the display policy to All Markup in both shells.",
            "TrackingDisplayControlTests.DisplayForReview_TogglingBackToAllMarkup_RestoresNormalRenderingAndLosesNothing",
            "DocumentViewReviewTests.DisplayForReview_AllMarkup_shows_and_styles_insertions_and_deletions"),
        ["freew.display-for-review-all-markup"] = ReviewDisplayEvidence(
            "Applies All Markup without mutating the document model while rendering tracked insertions and deletions in both shells.",
            "TrackingDisplayControlTests.DisplayForReview_SetToAllMarkup_DoesNotAffectModel",
            "DocumentViewReviewTests.DisplayForReview_AllMarkup_shows_and_styles_insertions_and_deletions"),
        ["freew.display-for-review-simple-markup"] = ReviewDisplayEvidence(
            "Uses Simple Markup/final inline text while preserving tracked revision metadata across the display-mode round trip.",
            "TrackingDisplayControlTests.DisplayForReview_SimpleMarkup_InsertedRunSurvivesCommitWithKindAndText",
            "DocumentViewReviewTests.DisplayForReview_SimpleMarkup_uses_final_inline_text_and_change_bar"),
        ["freew.display-for-review-no-markup"] = ReviewDisplayEvidence(
            "Hides deleted text in No Markup while preserving deleted-run revision data in both shells.",
            "TrackingDisplayControlTests.DisplayForReview_NoMarkup_DeletedRunSurvivesCommitWithKindAndText",
            "DocumentViewReviewTests.DisplayForReview_NoMarkup_hides_deleted_text_without_losing_revision_data"),
        ["freew.display-for-review-original"] = ReviewDisplayEvidence(
            "Hides inserted text in Original view while preserving inserted-run revision data in both shells.",
            "TrackingDisplayControlTests.DisplayForReview_Original_InsertedRunSurvivesCommitWithKindAndText",
            "DocumentViewReviewTests.DisplayForReview_Original_hides_inserted_text_without_losing_revision_data"),
        ["freew.show-markup-insertions-deletions"] = ReviewShowMarkupEvidence(
            "Toggles Insertions and Deletions visual chrome without dropping tracked revision markers from the model.",
            "TrackingDisplayControlTests.ShowMarkupInsertionsDeletions_WhenToggedOff_RevisionMarkerSurvivesCommit"),
        ["freew.show-markup-comments"] = ReviewShowMarkupEvidence(
            "Toggles comment markup visibility without dropping comment anchors from the model.",
            "TrackingDisplayControlTests.ShowMarkupComments_WhenToggedOff_CommentIdSurvivesCommit"),
        ["freew.show-markup-formatting"] = ReviewShowMarkupEvidence(
            "Toggles tracked-formatting chrome while preserving FormatRevision metadata through commit.",
            "TrackingDisplayControlTests.ShowMarkupFormatting_WhenOff_FormatRevisionSurvivesCommit"),
        ["freew.show-markup-balloons"] = ReviewBalloonsEvidence(
            "Renders comment and tracked-change balloons with shared card metadata and anchored revision/comment data in both shells."),
        ["freew.accept-this"] = ReviewChangesEvidence(
            "Accepts the selected/current insertion as ordinary text while leaving unrelated tracked changes pending.",
            "ReviewingPaneTests.AcceptRevision_ResolvesOnlyTheSelectedChange",
            "DocumentViewReviewTests.AcceptCurrent_clears_insertion_mark_keeping_text"),
        ["freew.reject-this"] = ReviewChangesEvidence(
            "Rejects the selected/current insertion by removing inserted text while leaving unrelated tracked changes pending.",
            "ReviewingPaneTests.RejectRevision_ResolvesOnlyTheSelectedChange_AndRemovesInsertedText",
            "DocumentViewReviewTests.RejectCurrent_removes_inserted_text"),
        ["freew.accept-all"] = ReviewBulkChangesEvidence(
            "Accepts every tracked insertion/deletion through the shared model and clears the document's revision list.",
            "TrackChangesTests.AcceptAll_NormalizesInsertions_AndRemovesDeletions",
            "DocumentViewReviewTests.AcceptAll_clears_every_revision"),
        ["freew.reject-all"] = ReviewBulkChangesEvidence(
            "Rejects every tracked insertion/deletion through the shared model and clears the document's revision list.",
            "TrackChangesTests.RejectAll_RemovesInsertions_AndNormalizesDeletions",
            "DocumentViewReviewTests.RejectAll_clears_every_revision_and_drops_insertions"),
        ["freew.previous-change"] = ReviewChangeNavigationEvidence(
            "Routes the Previous Change command to the host-owned Reviewing Pane navigation callback in both shells."),
        ["freew.next-change"] = ReviewChangeNavigationEvidence(
            "Routes the Next Change command to the host-owned Reviewing Pane navigation callback in both shells."),
        ["freew.outline-view"] = OutlineViewEvidence(
            "Shows a dedicated outline surface with shared-model rows, level filtering, first-line mode, caret navigation, undoable heading actions, and mutually exclusive production view-mode transitions in both shells."),
        ["freew.new-comment"] = ProtectionHistoryEvidence(
            "Creates a comment under comments-only protection and keeps the classified comment-history entry undoable and redoable in both shells."),
        ["freew.reply-comment"] = ProtectionHistoryEvidence(
            "Replies to an existing comment under comments-only protection and keeps the classified comment-history entry undoable and redoable in both shells."),
        ["freew.restrict-editing"] = ProtectionHistoryEvidence(
            "Applies comments-only Restrict Editing policy through shared enforcement so comment history remains available while body history is blocked in both shells."),
        ["freew.undo"] = ProtectionHistoryEvidence(
            "Allows Undo for classified comment mutations under comments-only protection while blocking body-history Undo in both shells."),
        ["freew.redo"] = ProtectionHistoryEvidence(
            "Allows Redo for classified comment mutations under comments-only protection while continuing to block body-history history replay in both shells."),
        ["freew.image-crop"] = HostParityEvidence(
            "Picture crop uses the shared dialog planner and SetImageCropCommand in both hosts, including selected-image enablement and undo restoration.",
            "ImageAndTableConversionParityTests.ImageCropHostRoute_MutatesSelectedImageAndUndoRestoresIt",
            "CommandParityCropTableToTextTests.ImageCropRegistryRoute_MatchesSelectionEnablementMutationAndUndo"),
        ["freew.image-alt-text"] = HostParityEvidence(
            "Picture Alt Text uses the current description as its prompt seed and SetImageAltTextCommand in both hosts, including selected-image enablement, cancel no-op, normalization, and undo restoration.",
            "ImageAndTableConversionParityTests.PictureCoreHostRoutes_MutateSelectedImageAndUndoRestoreIt",
            "PictureCoreCommandParityTests.ImageAltTextRegistryRoute_MatchesSelectionMutationCancelAndUndo",
            "freew/FreeW.App.Avalonia.Tests/PictureCoreCommandParityTests.cs"),
        ["freew.image-border"] = HostParityEvidence(
            "Picture Border uses the shared ImageBorderDialogPlanner and SetImageBorderCommand in both hosts, including selected-image enablement, current-value defaults, cancel no-op, and undo restoration.",
            "ImageAndTableConversionParityTests.PictureCoreHostRoutes_MutateSelectedImageAndUndoRestoreIt",
            "PictureCoreCommandParityTests.ImageBorderRegistryRoute_MatchesSelectionMutationCancelAndUndo",
            "freew/FreeW.App.Avalonia.Tests/PictureCoreCommandParityTests.cs"),
        ["freew.image-reset"] = HostParityEvidence(
            "Reset Picture uses one shared natural-size policy and ResetImageSizeCommand in both hosts, including selected-image enablement and complete undo restoration.",
            "ImageAndTableConversionParityTests.PictureCoreHostRoutes_MutateSelectedImageAndUndoRestoreIt",
            "PictureCoreCommandParityTests.ImageResetRegistryRoute_MatchesNaturalSizeMutationAndUndo",
            "freew/FreeW.App.Avalonia.Tests/PictureCoreCommandParityTests.cs"),
        ["freew.image-size"] = HostParityEvidence(
            "Picture Size uses the shared ImageSizeDialogPlanner and SetImageSizeCommand in both hosts, including selected-image enablement, current-value defaults, cancel no-op, and undo restoration.",
            "ImageAndTableConversionParityTests.PictureCoreHostRoutes_MutateSelectedImageAndUndoRestoreIt",
            "PictureCoreCommandParityTests.ImageSizeRegistryRoute_MatchesSelectionMutationCancelAndUndo",
            "freew/FreeW.App.Avalonia.Tests/PictureCoreCommandParityTests.cs"),
        ["freew.field"] = FinalFiveEvidence(
            "The shared field catalog drives both pickers and both editors insert a structural complex-field run at the caret through undoable document mutation.",
            "InsertTextCommands_UseSharedQuickPartAndFieldBehavior"),
        ["freew.save-quickpart"] = FinalFiveEvidence(
            "Both shells capture selected text through the shared Quick Part planner and persist paragraph lines through the same cross-platform library.",
            "InsertTextCommands_UseSharedQuickPartAndFieldBehavior"),
        ["freew.building-blocks-organizer"] = FinalFiveEvidence(
            "Both shells use the shared Quick Part library and insert the selected building block through their undoable text-edit path.",
            "InsertTextCommands_UseSharedQuickPartAndFieldBehavior"),
        ["freew.insert-table"] = TableInsertionEvidence(
            "The shared insertion workflow maps the legacy Table route to the canonical 3 × 3 insertion command for both renderers."),
        ["freew.table-2x2"] = TableInsertionEvidence(
            "The shared insertion workflow maps the 2 × 2 menu choice and primary Table face to one command for both renderers."),
        ["freew.table-3x3"] = TableInsertionEvidence(
            "The shared insertion workflow maps the 3 × 3 menu choice and legacy Table route to one command for both renderers."),
        ["freew.table-4x4"] = TableInsertionEvidence(
            "The shared insertion workflow inserts a 4 × 4 table through the same renderer port in both hosts."),
        ["freew.table-5x2"] = TableInsertionEvidence(
            "The shared insertion workflow inserts a 5 × 2 table through the same renderer port in both hosts."),
        ["freew.bullets"] = ParagraphEditingEvidence("Both renderers route bullet-list toggling through the shared paragraph command family."),
        ["freew.numbering"] = ParagraphEditingEvidence("Both renderers route numbered-list toggling through the shared paragraph command family."),
        ["freew.align-left"] = ParagraphEditingEvidence("Both renderers map left paragraph alignment through the shared command family."),
        ["freew.align-center"] = ParagraphEditingEvidence("Both renderers map centered paragraph alignment through the shared command family."),
        ["freew.align-right"] = ParagraphEditingEvidence("Both renderers map right paragraph alignment through the shared command family."),
        ["freew.align-justify"] = ParagraphEditingEvidence("Both renderers map justified paragraph alignment through the shared command family."),
        ["freew.indent-increase"] = ParagraphEditingEvidence("Both renderers increase paragraph indentation through one prepared shared action."),
        ["freew.indent-decrease"] = ParagraphEditingEvidence("Both renderers decrease paragraph indentation through one prepared shared action."),
        ["freew.space-before-toggle"] = ParagraphEditingEvidence("Both renderers toggle paragraph space-before through one prepared shared action."),
        ["freew.space-after-toggle"] = ParagraphEditingEvidence("Both renderers toggle paragraph space-after through one prepared shared action."),
        ["freew.keep-with-next"] = ParagraphEditingEvidence("Both renderers toggle keep-with-next through one prepared shared action."),
        ["freew.keep-lines"] = ParagraphEditingEvidence("Both renderers toggle keep-lines-together through one prepared shared action."),
        ["freew.widow-control"] = ParagraphEditingEvidence("Both renderers toggle widow/orphan control through one prepared shared action."),
        ["freew.para-border"] = ParagraphEditingEvidence("Both renderers toggle paragraph borders through one prepared shared action."),
        ["freew.sort"] = ParagraphEditingEvidence("Both renderers preserve native sort adapters behind one shared semantic route."),
        ["freew.multilevel-list"] = MultilevelListWorkflowEvidence(
            "The shared workflow applies the canonical decimal multilevel definition in both renderers."),
        ["freew.multilevel-demote"] = MultilevelListWorkflowEvidence(
            "The shared workflow routes Increase List Level as a +1 level delta in both renderers."),
        ["freew.multilevel-promote"] = MultilevelListWorkflowEvidence(
            "The shared workflow routes Decrease List Level as a -1 level delta in both renderers."),
        ["freew.multilevel-preset-0"] = MultilevelListWorkflowEvidence(
            "The shared workflow applies the canonical decimal outline preset in both renderers."),
        ["freew.multilevel-preset-1"] = MultilevelListWorkflowEvidence(
            "The shared workflow applies the decimal/letter/Roman outline preset in both renderers."),
        ["freew.multilevel-preset-2"] = MultilevelListWorkflowEvidence(
            "The shared workflow applies the heading-linked outline preset in both renderers."),
        ["freew.multilevel-define"] = MultilevelListWorkflowEvidence(
            "The shared workflow fails closed without a renderer definition-dialog endpoint instead of applying defaults.",
            "MultilevelListRibbonWorkflowTests.MissingDefineDialogFailsClosedInsteadOfApplyingDefaults"),
        ["freew.draw-table"] = FinalFiveEvidence(
            "Both shells normalize the dimension dialog through one planner and insert the resulting table through the undoable block command path.",
            "TableDrawingCommands_MutateAndUndo"),
        ["freew.eraser"] = FinalFiveEvidence(
            "Both shells use the shared eraser plan to remove the caret cell's right border by an undoable horizontal merge, while preserving explicit selection merges.",
            "TableDrawingCommands_MutateAndUndo"),
        ["freew.image-style-1"] = PictureStyleEvidence("Simple Frame, White"),
        ["freew.image-style-2"] = PictureStyleEvidence("Simple Frame, Black"),
        ["freew.image-style-3"] = PictureStyleEvidence("Thick Matte, Black"),
        ["freew.image-style-4"] = PictureStyleEvidence("Double Frame, Black"),
        ["freew.image-style-5"] = PictureStyleEvidence("Soft Edge Rectangle"),
        ["freew.image-style-6"] = PictureStyleEvidence("Soft Edge Oval"),
        ["freew.image-style-7"] = PictureStyleEvidence("Drop Shadow Rectangle"),
        ["freew.image-style-8"] = PictureStyleEvidence("Drop Shadow White"),
        ["freew.image-style-9"] = PictureStyleEvidence("Perspective Shadow"),
        ["freew.image-style-10"] = PictureStyleEvidence("Reflected Rounded Rectangle"),
        ["freew.image-style-11"] = PictureStyleEvidence("Reflected Bevel, White"),
        ["freew.image-style-12"] = PictureStyleEvidence("Metal Rounded Rectangle"),
        ["freew.table-to-text"] = HostParityEvidence(
            "Table to Text uses the shared delimiter choices and TextTableConvert model route in both hosts, preserving contextual enablement and undo restoration.",
            "ImageAndTableConversionParityTests.TableToTextHostRoute_UsesSharedConverterAndUndoRestoresTable",
            "CommandParityCropTableToTextTests.TableToTextRegistryRoute_MatchesCaretEnablementMutationSelectionAndUndo"),
        ["freew.chart-type-bar"] = ChartCommandEvidence(
            "Changes the selected chart from column to bar through the WPF and Avalonia ribbon command registries and keeps the change undoable in both shells.",
            "FreeWRibbonParityTests.ChartDesign_ChangeTypeRibbonCommandMutatesSelectedChartAndUndoRestoresIt",
            "ChartSmartArtContextualTabTests.SetChartType_command_changes_chart_kind_and_reverts_on_undo"),
        ["freew.chart-style-5"] = ChartCommandEvidence(
            "Applies the selected chart style gallery item through the WPF and Avalonia ribbon command registries and keeps the style change undoable in both shells.",
            "FreeWRibbonParityTests.ChartDesign_StyleGalleryCommandMutatesSelectedChartAndUndoRestoresIt",
            "ChartSmartArtContextualTabTests.SetChartStyle_command_changes_style_id_and_reverts_on_undo"),
        ["freew.chart-quick-layout-1"] = ChartQuickLayoutEvidence("Layout 1"),
        ["freew.chart-quick-layout-2"] = ChartQuickLayoutEvidence("Layout 2"),
        ["freew.chart-quick-layout-3"] = ChartQuickLayoutEvidence("Layout 3"),
        ["freew.chart-quick-layout-4"] = ChartQuickLayoutEvidence("Layout 4"),
        ["freew.chart-quick-layout-5"] = ChartQuickLayoutEvidence("Layout 5"),
        ["freew.chart-quick-layout-6"] = ChartQuickLayoutEvidence("Layout 6"),
        ["freew.chart-quick-layout-7"] = ChartQuickLayoutEvidence("Layout 7"),
        ["freew.chart-quick-layout-8"] = ChartQuickLayoutEvidence("Layout 8"),
        ["freew.chart-quick-layout-9"] = ChartQuickLayoutEvidence("Layout 9"),
        ["freew.chart-color-mono-blue"] = ChartCommandEvidence(
            "Applies the monochromatic blue chart color palette through the WPF chart-color command and the Avalonia chart-colors alias, keeping the change undoable in both shells.",
            "FreeWRibbonParityTests.ChartDesign_ColorSchemeRibbonCommandMutatesSelectedChartAndUndoRestoresIt",
            "ChartSmartArtContextualTabTests.SetChartColorScheme_command_changes_scheme_and_reverts_on_undo"),
        ["freew.chart-colors-mono-blue"] = ChartCommandEvidence(
            "Applies the monochromatic blue chart color palette through the WPF chart-color command and the Avalonia chart-colors alias, keeping the change undoable in both shells.",
            "FreeWRibbonParityTests.ChartDesign_ColorSchemeRibbonCommandMutatesSelectedChartAndUndoRestoresIt",
            "ChartSmartArtContextualTabTests.SetChartColorScheme_command_changes_scheme_and_reverts_on_undo"),
        ["freew.chart-toggle-legend"] = ChartCommandEvidence(
            "Toggles the selected chart legend through the WPF and Avalonia ribbon command registries, clears layout overrides where applicable, and keeps the change undoable.",
            "FreeWRibbonParityTests.ChartDesign_ToggleLegendRibbonCommandMutatesSelectedChartAndUndoRestoresIt",
            "ChartSmartArtContextualTabTests.ToggleChartLegend_command_clears_layout_override_and_reverts_on_undo"),
        ["freew.chart-title"] = ChartCommandEvidence(
            "Sets or clears the selected chart title through the shared chart edit command, clears quick-layout overrides, and keeps the change undoable in both shells.",
            "FreeWRibbonParityTests.ChartDesign_TitleSetterMutatesSelectedChartAndUndoRestoresIt",
            "ChartSmartArtContextualTabTests.ToggleChartTitle_command_sets_default_title_and_reverts_on_undo"),
        ["freew.chart-axis-titles"] = ChartCommandEvidence(
            "Sets or clears selected chart axis titles through the shared chart edit command, clears quick-layout overrides, and keeps the change undoable in both shells.",
            "FreeWRibbonParityTests.ChartDesign_AxisTitlesSetterMutatesSelectedChartAndUndoRestoresIt",
            "ChartSmartArtContextualTabTests.ToggleChartAxisTitles_command_sets_default_titles_and_reverts_on_undo"),
        ["freew.smartart-add-shape"] = SmartArtStructureEvidence("adds a shape"),
        ["freew.smartart-remove-shape"] = SmartArtStructureEvidence("removes the final shape while retaining the one-node invariant"),
        ["freew.smartart-promote"] = SmartArtStructureEvidence("promotes the final child into the top-level hierarchy"),
        ["freew.smartart-demote"] = SmartArtStructureEvidence("demotes the final top-level shape beneath its previous sibling"),
        ["freew.smartart-move-up"] = SmartArtStructureEvidence("moves the final top-level shape upward"),
        ["freew.smartart-move-down"] = SmartArtStructureEvidence("moves the first top-level shape downward"),
        ["freew.smartart-edit-text"] = SmartArtCommandEvidence(
            "Edits SmartArt node text through the seeded host dialog or selected-value route, preserving layout, color, style, size, placement, selection, and undo.",
            "FreeWRibbonParityTests.SmartArtDesignContextualTab_AllEightCommandsMatchSelectionMutationAndUndoBehavior",
            "ChartSmartArtContextualTabTests.SmartArt_edit_and_style_commands_mutate_preserve_metadata_and_support_undo"),
        ["freew.smartart-change-style"] = SmartArtCommandEvidence(
            "Applies a shared SmartArtStyle catalog entry in both shells while preserving node structure and supporting undo.",
            "FreeWRibbonParityTests.SmartArtDesignContextualTab_AllEightCommandsMatchSelectionMutationAndUndoBehavior",
            "ChartSmartArtContextualTabTests.SmartArt_edit_and_style_commands_mutate_preserve_metadata_and_support_undo"),
        ["freew.chart-edit-data"] = ChartCommandEvidence(
            "Replaces the selected chart's editable category and series data through the shared chart model route and keeps the change undoable in Avalonia.",
            "FreeWRibbonParityTests.ChartDesign_ReplaceChartDataMutatesModel",
            "ChartSmartArtContextualTabTests.EditChartData_command_replaces_chart_data_and_reverts_on_undo"),
        ["freew.chart-size"] = ChartCommandEvidence(
            "Resizes the selected chart through the shared floating-object size route and keeps the size change undoable in Avalonia.",
            "FreeWRibbonParityTests.ChartDesign_SetSizeMutatesWidthAndHeight",
            "ChartSmartArtContextualTabTests.ChartSize_command_resizes_selected_chart_and_reverts_on_undo"),
        ["freew.chart-size-dialog"] = FinalProfileRouteEvidence(
            "Routes More Size Options to the existing owner-modal chart size behavior in WPF and the selected-chart dialog callback in Avalonia.",
            "FreeWRibbonParityTests.FinalCommandProfileAsymmetries_RouteToBackedWpfCommands",
            "freew/FreeW.App.Avalonia.Tests/ChartSmartArtContextualTabTests.cs",
            "ChartSmartArtContextualTabTests.Chart_size_primary_and_dialog_alias_route_selected_chart_to_owner_modal_callback"),
        ["freew.citation"] = ReferencesEvidence(
            "Inserts tagged Word-like CITATION complex-field runs and proves Update Fields renumbers source-order numeric citations in both shells.",
            "freew/FreeW.App.Host.Tests/CitationEditorTests.cs",
            "CitationEditorTests.InsertCitation_TaggedSourceWithQuotedFieldArgument_RenumbersOnUpdateFields",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.InsertCitation_tagged_source_with_quoted_field_argument_renumbers_on_update_fields"),
        ["freew.manage-sources"] = ReferencesEvidence(
            "Replaces the current document source list through backed source-management flow and keeps replacement undoable in both shells.",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.ReferencesCitations_ExposesBackedWordStyleManageSources",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.ReplaceSources_replaces_source_list_and_undo_reverts"),
        ["freew.bibliography"] = ReferencesEvidence(
            "Builds generated bibliography/reference-list paragraphs from document sources and keeps insertion undoable in both shells.",
            "freew/FreeW.App.Host.Tests/CitationEditorTests.cs",
            "CitationEditorTests.InsertBibliography_BuildsBlockFromSourcesAndUndoReverts",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.InsertBibliography_builds_block_from_sources_and_undo_reverts"),
        ["freew.mark-citation"] = ReferencesEvidence(
            "Creates durable hidden Word-style TA citation marks with category/short-citation data in both shells.",
            "freew/FreeW.App.Host.Tests/MarkCitationEditorTests.cs",
            "MarkCitationEditorTests.MarkCitation_DropsAHiddenCitationMarkThatSurvivesCommit",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.MarkCitation_accepts_full_citation_dialog_result"),
        ["freew.table-of-authorities"] = ReferencesEvidence(
            "Builds a generated Table of Authorities from body citation marks and preserves grouped legal-authority output in both shells.",
            "freew/FreeW.App.Host.Tests/MarkCitationEditorTests.cs",
            "MarkCitationEditorTests.InsertTableOfAuthorities_BuildsAGroupedTableFromTheMarks",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.MarkCitation_body_mark_builds_table_and_survives_docx_roundtrip"),
        ["freew.table-of-authorities-refresh"] = ReferencesEvidence(
            "Refreshes an existing Table of Authorities region through shared region planning without duplicating stale generated paragraphs in both shells.",
            "freew/FreeW.App.Host.Tests/MarkCitationEditorTests.cs",
            "MarkCitationEditorTests.RefreshTableOfAuthorities_ReplacesThePriorRegionInPlaceWithoutDuplicating",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.UpdateFields_refreshes_existing_table_of_authorities_with_explicit_break_page_references"),
        ["freew.update-fields"] = ReferencesEvidence(
            "Refreshes stale generated references in one field-update pass, including TOC, bibliography, citation fields, and Table of Authorities coverage.",
            "freew/FreeW.App.Host.Tests/NumericCitationEditorTests.cs",
            "NumericCitationEditorTests.UpdateFields_CitationFieldAndBibliographyRefresh_DoNotOverwriteCitationFromStaleView",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.UpdateFields_refreshes_toc_and_bibliography_in_same_pass"),
        ["freew.statistics"] = StatisticsEvidence(
            "Computes Review > Proofing statistics from the shared document model, including paragraphs nested inside table cells, and routes the Avalonia review command to the host statistics callback."),
        ["freew.spellcheck-toggle"] = ProofingEvidence(
            "Toggles spellcheck diagnostics through the shared proofing planner and the Avalonia review command registry without mutating document content.",
            "freew/FreeW.Core.Model.Tests/ProofingDiagnosticPlannerTests.cs",
            "ProofingDiagnosticPlannerTests.Build_suppresses_diagnostics_when_spellcheck_disabled"),
        ["freew.add-to-dictionary"] = ProofingEvidence(
            "Adds the current proofing word to the custom dictionary so the shared proofing planner suppresses that diagnostic on subsequent passes.",
            "freew/FreeW.Core.Model.Tests/CustomDictionaryTests.cs",
            "CustomDictionaryTests.Add_ThenContains_FindsWord"),
        ["freew.thesaurus"] = ThesaurusEvidence(
            "Loads bundled thesaurus senses for known words and lets the Avalonia review surface replace the current proofing word with the chosen synonym."),
        ["freew.set-proofing-language"] = ProofingLanguageEvidence(
            "Applies proofing language metadata to selected text ranges in both shells while keeping multi-paragraph language changes reversible."),
        ["freew.merge-next-record"] = MailingsEvidence(
            "Inserts the Word-style NEXT RECORD mail-merge rule field through both ribbon registries so record advancement survives as a model placeholder."),
        ["freew.merge-record-number"] = MailingsEvidence(
            "Inserts the Word-style MERGE RECORD # mail-merge rule field through both ribbon registries so record numbering survives as a model placeholder."),
        ["freew.merge-sequence-number"] = MailingsEvidence(
            "Inserts the Word-style MERGE SEQUENCE # mail-merge rule field through both ribbon registries so merge sequence numbering survives as a model placeholder."),
        ["freew.merge-find-recipient"] = FinalProfileRouteEvidence(
            "Routes Find Recipient through the shared wraparound recipient planner in WPF and the owner-modal Avalonia mailings host route.",
            "FreeWRibbonParityTests.MailingsFindRecipientAndCheckErrors_UseSharedPlannersThroughWpfCommands",
            "freew/FreeW.App.Avalonia.Tests/MailMergeDialogSurfaceTests.cs",
            "MailMergeDialogSurfaceTests.MailingsCommandHost_RoutesFindAndErrorChecksThroughDialogsAndSharedPlanners"),
        ["freew.merge-check-errors"] = FinalProfileRouteEvidence(
            "Routes Check for Errors through the shared Word-order mode planner in WPF and the owner-modal Avalonia mailings host route.",
            "FreeWRibbonParityTests.MailingsFindRecipientAndCheckErrors_UseSharedPlannersThroughWpfCommands",
            "freew/FreeW.App.Avalonia.Tests/MailMergeDialogSurfaceTests.cs",
            "MailMergeDialogSurfaceTests.MailingsCommandHost_RoutesFindAndErrorChecksThroughDialogsAndSharedPlanners"),
        ["freew.print-layout"] = PrintFamilyViewEvidence(
            "Routes the shared Print Layout command through WPF stateful view-mode commands and the Avalonia host callback so the Word-style page surface can be restored from print-family view changes."),
        ["freew.print-preview"] = BackstagePrintEvidence(
            "Routes the shared Print Preview command to host-backed WPF and Avalonia preview callbacks while the Backstage evidence contract retains paired fixed-layout renderer rows."),
        ["freew.arrange-all"] = WindowShellEvidence(
            "WPF exposes Arrange All for desktop multi-window tiling; Avalonia keeps the portable View > Window profile to New Window and Split only."),
        ["freew.check-updates"] = WpfHelpShellEvidence(
            "WPF exposes Check for Updates on the desktop Help/Product tab; Avalonia intentionally omits update orchestration from its compact portable shell profile."),
        ["freew.copy-diagnostics"] = WpfHelpShellEvidence(
            "WPF exposes Copy Diagnostics on the desktop Help tab; Avalonia intentionally omits that desktop diagnostics shortcut from its compact portable shell profile."),
        ["freew.feedback"] = WpfHelpShellEvidence(
            "WPF exposes Feedback on the desktop Help tab; Avalonia intentionally omits that desktop support shortcut from its compact portable shell profile."),
        ["freew.help-online"] = WpfHelpShellEvidence(
            "WPF exposes Help Online on the desktop Help tab; Avalonia intentionally omits that desktop Help tab from its compact portable shell profile."),
        ["freew.backstage"] = AvaloniaFileShellEvidence(
            "Avalonia exposes a compact File entry for the portable shell; WPF routes file lifecycle through its Backstage/File surface instead of this generated command id."),
        ["freew.import-pdf-text"] = AvaloniaPdfImportEvidence(
            "Avalonia exposes PDF text import as an explicit portable File command; WPF carries PDF/import support through file workflow and document-persistence evidence rather than this generated command id."),
        ["freew.new"] = AvaloniaFileShellEvidence(
            "Avalonia exposes New as a compact File command; WPF routes the same lifecycle through its Backstage/File workflow rather than this generated command id."),
        ["freew.open"] = AvaloniaFileShellEvidence(
            "Avalonia exposes Open as a compact File command; WPF routes the same lifecycle through its Backstage/File workflow rather than this generated command id."),
        ["freew.save"] = AvaloniaFileShellEvidence(
            "Avalonia exposes Save as a compact File command; WPF routes the same lifecycle through its Backstage/File workflow rather than this generated command id."),
    };

    private static readonly Dictionary<string, string> PlatformOnlyNotes = new(StringComparer.Ordinal)
    {
        ["freew.arrange-all"] = "Accepted host variance: WPF desktop multi-window tiling command; Avalonia portable shell intentionally exposes New Window and Split without Arrange All tiling.",
        ["freew.backstage"] = "Accepted host variance: Avalonia compact File entry opens its portable shell file surface; WPF uses the Backstage/File surface instead of this generated command id.",
        ["freew.check-updates"] = "Accepted host variance: WPF desktop Help/Product update command; Avalonia compact shell omits update orchestration from the ribbon profile.",
        ["freew.copy-diagnostics"] = "Accepted host variance: WPF desktop diagnostics shortcut on Help; Avalonia compact shell omits the desktop Help tab.",
        ["freew.feedback"] = "Accepted host variance: WPF desktop support shortcut on Help; Avalonia compact shell omits the desktop Help tab.",
        ["freew.help-online"] = "Accepted host variance: WPF desktop Help shortcut; Avalonia compact shell omits the desktop Help tab.",
        ["freew.import-pdf-text"] = "Accepted host variance: Avalonia compact File command makes PDF text import explicit; WPF covers PDF/import through file workflow and document-persistence evidence.",
        ["freew.new"] = "Accepted host variance: Avalonia compact File command; WPF routes New through Backstage/File lifecycle workflow instead of this generated command id.",
        ["freew.open"] = "Accepted host variance: Avalonia compact File command; WPF routes Open through Backstage/File lifecycle workflow instead of this generated command id.",
        ["freew.save"] = "Accepted host variance: Avalonia compact File command; WPF routes Save through Backstage/File lifecycle workflow instead of this generated command id.",
    };

    public static InventoryDocument Build(string repoRoot)
    {
        var wpf = Collect(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf), WpfProfile);
        var avalonia = Collect(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia), AvaloniaProfile);
        var commandIds = wpf.Keys.Concat(avalonia.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourceTexts = SourceFiles.ToDictionary(
            file => file.Id,
            file => ReadRepositoryFile(repoRoot, file.RelativePath),
            StringComparer.Ordinal);
        var canonicalDefinitionSource = sourceTexts["canonicalDefinitionSource"]
            + sourceTexts["canonicalOrdinaryDefinitionSource"]
            + sourceTexts["canonicalContextualDefinitionSource"];

        var commands = commandIds.Select(commandId =>
        {
            wpf.TryGetValue(commandId, out var wpfLocations);
            avalonia.TryGetValue(commandId, out var avaloniaLocations);
            var wpfPresent = wpfLocations is { Count: > 0 };
            var avaloniaPresent = avaloniaLocations is { Count: > 0 };
            var sourceLiteralEvidence = new SourceLiteralEvidence(
                WpfDefinitionSource: ContainsCommandLiteral(canonicalDefinitionSource, commandId) ||
                    ContainsCommandLiteral(sourceTexts["wpfDefinitionSource"], commandId),
                AvaloniaDefinitionSource: ContainsCommandLiteral(canonicalDefinitionSource, commandId) ||
                    ContainsCommandLiteral(sourceTexts["avaloniaDefinitionSource"], commandId),
                WpfRegistrySource: ContainsCommandLiteral(sourceTexts["wpfRegistrySource"], commandId) ||
                    ContainsCommandLiteral(sourceTexts["quickPartWorkflowSource"], commandId) ||
                    ContainsCommandLiteral(sourceTexts["tableInsertionWorkflowSource"], commandId) ||
                    ContainsCommandLiteral(sourceTexts["paragraphEditingWorkflowSource"], commandId),
                AvaloniaRegistrySource: ContainsCommandLiteral(sourceTexts["avaloniaRegistrySource"], commandId) ||
                    ContainsCommandLiteral(sourceTexts["quickPartWorkflowSource"], commandId) ||
                    ContainsCommandLiteral(sourceTexts["tableInsertionWorkflowSource"], commandId) ||
                    ContainsCommandLiteral(sourceTexts["paragraphEditingWorkflowSource"], commandId));
            var behaviorEvidence = BehaviorEvidenceCatalog.GetValueOrDefault(commandId);
            var profileClassification = ClassifyProfile(wpfPresent, avaloniaPresent);
            var gapClassification = ClassifyGap(
                commandId,
                wpfPresent,
                avaloniaPresent,
                wpfLocations ?? Array.Empty<CommandLocation>(),
                avaloniaLocations ?? Array.Empty<CommandLocation>(),
                commandIds,
                behaviorEvidence is not null);
            return new CommandEntry(
                CommandId: commandId,
                Label: (wpfLocations ?? avaloniaLocations ?? throw new InvalidOperationException()).First().Label,
                WpfPresent: wpfPresent,
                AvaloniaPresent: avaloniaPresent,
                ProfileSurface: Surface(wpfPresent, avaloniaPresent),
                MissingProfile: MissingProfile(wpfPresent, avaloniaPresent),
                Classification: profileClassification.Name,
                GapClassification: gapClassification.Name,
                GapClassificationRule: gapClassification.Rule,
                Notes: gapClassification.Notes,
                WpfLocations: wpfLocations ?? Array.Empty<CommandLocation>(),
                AvaloniaLocations: avaloniaLocations ?? Array.Empty<CommandLocation>(),
                SourceLiteralEvidence: sourceLiteralEvidence,
                BehaviorEvidence: behaviorEvidence);
        }).ToArray();

        return new InventoryDocument(
            Schema: "freew.command-inventory.v5",
            SchemaVersion: 5,
            GeneratedBy: "tools/Generate-FreeWCommandInventory.ps1",
            TopologySource: "freew/FreeW.Ribbon.Definitions FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf/Avalonia)",
            SourceLiteralEvidenceNote: "Source literal evidence records exact command-id text in source files only; canonical shared definitions contribute to both profile-definition columns. It is not behavior proof and never creates inventory rows.",
            BehaviorEvidenceNote: "Behavior evidence links a bounded command row to focused WPF and Avalonia tests; it strengthens parity confidence but does not create inventory rows or change gap classification.",
            ClassificationNote: "Gap classifications are generated from explicit rule order: shared-profile, command-id-alias, platform-only, profile-shape-only, deferred, then actionable-gap. Direct one-sided controls require paired behavior evidence to avoid actionable-gap.",
            ClassificationRules: GapClassificationRules,
            SourceLiteralFiles: SourceFiles.Select(file => new SourceLiteralFileEntry(file.Id, file.Label, file.RelativePath)).ToArray(),
            Summary: new InventorySummary(
                TotalCommands: commands.Length,
                Both: commands.Count(command => command.ProfileSurface == "both"),
                WpfOnly: commands.Count(command => command.ProfileSurface == "wpf-only"),
                AvaloniaOnly: commands.Count(command => command.ProfileSurface == "avalonia-only"),
                MissingWpf: commands.Count(command => command.MissingProfile == WpfProfile),
                MissingAvalonia: commands.Count(command => command.MissingProfile == AvaloniaProfile),
                ActionableMissingWpf: commands.Count(command => command.MissingProfile == WpfProfile && command.GapClassification == "actionable-gap"),
                ActionableMissingAvalonia: commands.Count(command => command.MissingProfile == AvaloniaProfile && command.GapClassification == "actionable-gap"),
                SharedProfile: commands.Count(command => command.GapClassification == "shared-profile"),
                ProfileShapeOnly: commands.Count(command => command.GapClassification == "profile-shape-only"),
                CommandIdAliases: commands.Count(command => command.GapClassification == "command-id-alias"),
                PlatformOnly: commands.Count(command => command.GapClassification == "platform-only"),
                Deferred: commands.Count(command => command.GapClassification == "deferred"),
                ActionableGaps: commands.Count(command => command.GapClassification == "actionable-gap"),
                BehaviorEvidenceRows: commands.Count(command => command.BehaviorEvidence is not null)),
            Commands: commands);
    }

    private static CommandBehaviorEvidence ReviewCommentEvidence(
        string summary,
        string wpfTest,
        string avaloniaTest) =>
        new(
            EvidenceId: "freew.review-comments.shared-behavior",
            Slice: "Review comments",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/ThreadedCommentCommandTests.cs",
                Test: wpfTest),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: avaloniaTest.StartsWith("DocumentViewCommentTests.", StringComparison.Ordinal)
                    ? "freew/FreeW.App.Avalonia.Tests/DocumentViewCommentTests.cs"
                    : "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
                Test: avaloniaTest));

    private static CommandBehaviorEvidence HostParityEvidence(
        string summary,
        string wpfTest,
        string avaloniaTest,
        string avaloniaPath = "freew/FreeW.App.Avalonia.Tests/CommandParityCropTableToTextTests.cs") =>
        new(
            EvidenceId: "freew.command-host-parity.shared-behavior",
            Slice: "Command host parity",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/ImageAndTableConversionParityTests.cs",
                Test: wpfTest),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: avaloniaPath,
                Test: avaloniaTest));

    private static CommandBehaviorEvidence FinalFiveEvidence(string summary, string test) =>
        new(
            EvidenceId: "freew.final-five-command-parity.shared-behavior",
            Slice: "Final command parity",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FinalFiveCommandParityTests.cs",
                Test: $"FinalFiveCommandParityTests.{test}"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/FinalFiveCommandParityTests.cs",
                Test: $"FinalFiveCommandParityTests.{test}"));

    private static CommandBehaviorEvidence TableInsertionEvidence(string summary) =>
        new(
            EvidenceId: "freew.table-insertion.shared-workflow",
            Slice: "Insert table command behavior",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Presentation.Tests/TableInsertionRibbonWorkflowTests.cs",
                Test: "TableInsertionRibbonWorkflowTests.EditorFamilyBuilderReceivesTheSameCanonicalAndAdapterCommands"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Presentation.Tests/TableInsertionRibbonWorkflowTests.cs",
                Test: "TableInsertionRibbonWorkflowTests.BothRenderersDelegateTableInsertionPolicyToSharedPresentation"));

    private static CommandBehaviorEvidence ParagraphEditingEvidence(string summary) =>
        new(
            EvidenceId: "freew.paragraph-editing.shared-workflow",
            Slice: "Paragraph editing command behavior",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Presentation.Tests/ParagraphEditingRibbonWorkflowTests.cs",
                Test: "ParagraphEditingRibbonWorkflowTests.NativeCommandsPreserveStateAndAllExecutionsPrepareFirst"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Presentation.Tests/ParagraphEditingRibbonWorkflowTests.cs",
                Test: "ParagraphEditingRibbonWorkflowTests.BothRenderersDelegateParagraphPolicyToSharedPresentation"));

    private static CommandBehaviorEvidence MultilevelListWorkflowEvidence(
        string summary,
        string test = "MultilevelListRibbonWorkflowTests.SharedWorkflowOwnsDefaultPresetsLevelsAndDefineDialog") =>
        new(
            EvidenceId: "freew.multilevel-list.shared-workflow",
            Slice: "Multilevel List shared workflow",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Presentation.Tests/MultilevelListRibbonWorkflowTests.cs",
                Test: test),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Presentation.Tests/MultilevelListRibbonWorkflowTests.cs",
                Test: test));

    private static CommandBehaviorEvidence PictureStyleEvidence(string presetName) =>
        HostParityEvidence(
            $"Picture Style '{presetName}' uses the shared PictureStyleCatalog and SetImageStyleCommand in both hosts, including selected-image enablement, complete bundled mutation, selection refresh, and undo restoration.",
            "ImageAndTableConversionParityTests.PictureStyleRegistryRoutes_ApplySharedCatalogPresetAndUndo",
            "PictureStyleCommandParityTests.PictureStyleRegistryRoutes_ApplySharedCatalogPresetAndUndo",
            "freew/FreeW.App.Avalonia.Tests/PictureStyleCommandParityTests.cs");

    private static CommandBehaviorEvidence ProtectionHistoryEvidence(string summary) =>
        new(
            EvidenceId: "freew.comments-only-protection-history.shared-behavior",
            Slice: "Comments-only protection history",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/ProtectionEnforcementTests.cs",
                Test: "ProtectionEnforcementTests.CommentsOnlyProtection_AllowsEachClassifiedCommentHistoryEntry"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/DocumentViewProtectionTests.cs",
                Test: "DocumentViewProtectionTests.CommentsOnly_allows_each_classified_comment_history_entry"));

    private static CommandBehaviorEvidence ReviewTrackingEvidence(
        string summary,
        string avaloniaPath,
        string avaloniaTest) =>
        ReviewEvidence(
            evidenceId: "freew.review-tracking.shared-behavior",
            slice: "Review tracking",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            wpfTest: "FreeWRibbonParityTests.ReviewTrackingAndChanges_CommandRoutesExecuteBackedActions",
            avaloniaPath: avaloniaPath,
            avaloniaTest: avaloniaTest);

    private static CommandBehaviorEvidence ReviewPaneEvidence(
        string summary,
        string wpfTest,
        string avaloniaTest) =>
        ReviewEvidence(
            evidenceId: "freew.reviewing-pane.shared-behavior",
            slice: "Reviewing Pane",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/ReviewingPaneTests.cs",
            wpfTest: wpfTest,
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/ReviewingPaneTests.cs",
            avaloniaTest: avaloniaTest);

    private static CommandBehaviorEvidence ReviewDisplayEvidence(
        string summary,
        string wpfTest,
        string avaloniaTest) =>
        ReviewEvidence(
            evidenceId: "freew.review-display.shared-behavior",
            slice: "Review display modes",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/TrackingDisplayControlTests.cs",
            wpfTest: wpfTest,
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: avaloniaTest);

    private static CommandBehaviorEvidence ReviewShowMarkupEvidence(
        string summary,
        string wpfTest) =>
        ReviewEvidence(
            evidenceId: "freew.review-show-markup.shared-behavior",
            slice: "Review Show Markup",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/TrackingDisplayControlTests.cs",
            wpfTest: wpfTest,
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: "DocumentViewReviewTests.ShowMarkup_toggles_hide_visual_chrome_but_preserve_model_data");

    private static CommandBehaviorEvidence ReviewBalloonsEvidence(string summary) =>
        ReviewEvidence(
            evidenceId: "freew.review-balloons.shared-behavior",
            slice: "Review balloons",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/ThesaurusAndBalloonsTests.cs",
            wpfTest: "ThesaurusAndBalloonsTests.BalloonOverlay_Enable_RendersSharedCardMetadata",
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: "DocumentViewReviewTests.Review_balloons_pane_renders_revisions_and_comments_from_model_data");

    private static CommandBehaviorEvidence ReviewChangesEvidence(
        string summary,
        string wpfTest,
        string avaloniaTest) =>
        ReviewEvidence(
            evidenceId: "freew.review-changes.shared-behavior",
            slice: "Review changes",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/ReviewingPaneTests.cs",
            wpfTest: wpfTest,
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: avaloniaTest);

    private static CommandBehaviorEvidence ReviewBulkChangesEvidence(
        string summary,
        string sharedModelTest,
        string avaloniaTest) =>
        ReviewEvidence(
            evidenceId: "freew.review-bulk-changes.shared-behavior",
            slice: "Review bulk changes",
            summary: summary,
            wpfPath: "freew/FreeW.Core.Model.Tests/TrackChangesTests.cs",
            wpfTest: sharedModelTest,
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: avaloniaTest);

    private static CommandBehaviorEvidence ReviewChangeNavigationEvidence(string summary) =>
        ReviewEvidence(
            evidenceId: "freew.review-change-navigation.shared-behavior",
            slice: "Review change navigation",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            wpfTest: "FreeWRibbonParityTests.ReviewTrackingAndChanges_CommandRoutesExecuteBackedActions",
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/ReviewChangeNavigationTests.cs",
            avaloniaTest: "ReviewChangeNavigationTests.Production_MainWindow_step_opens_hidden_pane_and_navigates");

    private static CommandBehaviorEvidence OutlineViewEvidence(string summary) =>
        ReviewEvidence(
            evidenceId: "freew.outline-view.shared-behavior",
            slice: "Outline view",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/OutlineViewTests.cs",
            wpfTest: "OutlineViewTests.Entering_ShowsHeadingsAndBodyInStructureOrder",
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/OutlineViewParityTests.cs",
            avaloniaTest: "OutlineViewParityTests.Production_outline_callback_swaps_workspace_and_is_mutually_exclusive_with_view_modes");

    private static CommandBehaviorEvidence StatisticsEvidence(string summary) =>
        ReviewEvidence(
            evidenceId: "freew.review-proofing-statistics.shared-behavior",
            slice: "Review proofing statistics",
            summary: summary,
            wpfPath: "freew/FreeW.Core.Model.Tests/WordCountTests.cs",
            wpfTest: "WordCountTests.Of_IncludesTableCellParagraphs",
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: "DocumentViewReviewTests.Review_safety_commands_route_to_host_callbacks");

    private static CommandBehaviorEvidence ProofingEvidence(
        string summary,
        string wpfPath,
        string wpfTest) =>
        ReviewEvidence(
            evidenceId: "freew.review-proofing.shared-behavior",
            slice: "Review proofing",
            summary: summary,
            wpfPath: wpfPath,
            wpfTest: wpfTest,
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: "DocumentViewReviewTests.Proofing_commands_toggle_state_dictionary_thesaurus_and_language");

    private static CommandBehaviorEvidence ThesaurusEvidence(string summary) =>
        ReviewEvidence(
            evidenceId: "freew.review-thesaurus.shared-behavior",
            slice: "Review thesaurus",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/ThesaurusAndBalloonsTests.cs",
            wpfTest: "ThesaurusAndBalloonsTests.ThesaurusLookup_KnownWord_ReturnsSensesWithSynonyms",
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: "DocumentViewReviewTests.Thesaurus_replace_current_proofing_word_replaces_caret_word");

    private static CommandBehaviorEvidence ProofingLanguageEvidence(string summary) =>
        ReviewEvidence(
            evidenceId: "freew.review-proofing-language.shared-behavior",
            slice: "Review proofing language",
            summary: summary,
            wpfPath: "freew/FreeW.App.Host.Tests/CharacterBorderShadingLanguageApplyTests.cs",
            wpfTest: "CharacterBorderShadingLanguageApplyTests.SetProofingLanguage_MultiParagraphSelection_IsReversibleWithSingleUndo",
            avaloniaPath: "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            avaloniaTest: "DocumentViewReviewTests.Proofing_language_applies_only_to_the_selected_range_across_paragraphs");

    private static CommandBehaviorEvidence ReviewEvidence(
        string evidenceId,
        string slice,
        string summary,
        string wpfPath,
        string wpfTest,
        string avaloniaPath,
        string avaloniaTest) =>
        new(
            EvidenceId: evidenceId,
            Slice: slice,
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: wpfPath,
                Test: wpfTest),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: avaloniaPath,
                Test: avaloniaTest));

    private static CommandBehaviorEvidence ChartCommandEvidence(
        string summary,
        string wpfTest,
        string avaloniaTest) =>
        new(
            EvidenceId: "freew.chart.shared-behavior",
            Slice: "Chart command behavior",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: wpfTest),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/ChartSmartArtContextualTabTests.cs",
                Test: avaloniaTest));

    private static CommandBehaviorEvidence FinalProfileRouteEvidence(
        string summary,
        string wpfTest,
        string avaloniaPath,
        string avaloniaTest) =>
        new(
            EvidenceId: "freew.final-command-profile-routing.shared-behavior",
            Slice: "Final command profile routing",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: wpfTest),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: avaloniaPath,
                Test: avaloniaTest));

    private static CommandBehaviorEvidence ChartQuickLayoutEvidence(string layoutName) =>
        ChartCommandEvidence(
            $"Applies {layoutName} from the shared nine-entry chart layout catalog in both shells, preserves chart data and style, follows selection enablement, and supports undo/redo.",
            "FreeWRibbonParityTests.ChartDesign_QuickLayoutCatalogCommandsMatchSelectionMutationAndUndoBehavior",
            "ChartSmartArtContextualTabTests.ChartQuickLayoutCatalog_commands_apply_preserve_selection_and_support_undo_redo");

    private static CommandBehaviorEvidence SmartArtStructureEvidence(string behavior) =>
        SmartArtCommandEvidence(
            $"Uses the shared structural command to {behavior}, follows selection enablement, preserves unrelated SmartArt state, and supports undo/redo in both shells.",
            "FreeWRibbonParityTests.SmartArtDesignContextualTab_AllEightCommandsMatchSelectionMutationAndUndoBehavior",
            "ChartSmartArtContextualTabTests.SmartArt_structure_commands_mutate_preserve_selection_and_support_undo_redo");

    private static CommandBehaviorEvidence SmartArtCommandEvidence(
        string summary,
        string wpfTest,
        string avaloniaTest) =>
        new(
            EvidenceId: "freew.smartart.shared-behavior",
            Slice: "SmartArt command behavior",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: wpfTest),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/ChartSmartArtContextualTabTests.cs",
                Test: avaloniaTest));

    private static CommandBehaviorEvidence BackstagePrintEvidence(string summary) =>
        new(
            EvidenceId: "freew.backstage-print-export.shared-behavior",
            Slice: "Backstage print/export evidence",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: "FreeWRibbonParityTests.PrintPreviewRibbonCommandInvokesHostCallback"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/ViewTabDepthTests.cs",
                Test: "ViewTabDepthTests.Print_preview_command_invokes_host_callback"));

    private static CommandBehaviorEvidence ReferencesEvidence(
        string summary,
        string wpfPath,
        string wpfTest,
        string avaloniaPath,
        string avaloniaTest) =>
        new(
            EvidenceId: "freew.references-fields.shared-behavior",
            Slice: "References fields and generated regions",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: wpfPath,
                Test: wpfTest),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: avaloniaPath,
                Test: avaloniaTest));

    private static CommandBehaviorEvidence MailingsEvidence(string summary) =>
        new(
            EvidenceId: "freew.mailings-rules.shared-behavior",
            Slice: "Mailings merge rule fields",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: "FreeWRibbonParityTests.MailingsRulesSpecialFields_InsertSharedInstructionsThroughRegistry"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/MailingsTabTests.cs",
                Test: "MailingsTabTests.Rules_commands_insert_shared_rule_instructions_via_registry"));

    private static CommandBehaviorEvidence PrintFamilyViewEvidence(string summary) =>
        new(
            EvidenceId: "freew.print-family-view.shared-behavior",
            Slice: "Print-family view behavior",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/WebLayoutDraftCommandTests.cs",
                Test: "WebLayoutDraftCommandTests.ViewToggles_AreMutuallyExclusive_InCheckedState"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/ViewTabDepthTests.cs",
                Test: "ViewTabDepthTests.Print_layout_command_invokes_host_callback"));

    private static CommandBehaviorEvidence WpfHelpShellEvidence(string summary) =>
        new(
            EvidenceId: "freew.platform-only.wpf-help-shell",
            Slice: "WPF Help shell variance",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: "FreeWRibbonParityTests.HelpTab_ExposesOnlyBackedFreeWLocalSupportCommands"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
                Test: "RibbonAndDocumentTests.Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed"));

    private static CommandBehaviorEvidence WindowShellEvidence(string summary) =>
        new(
            EvidenceId: "freew.platform-only.window-shell",
            Slice: "Window-management shell variance",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: "FreeWRibbonParityTests.View_Window_NewWindowAndArrangeAll_AreBacked"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
                Test: "RibbonAndDocumentTests.Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed"));

    private static CommandBehaviorEvidence AvaloniaFileShellEvidence(string summary) =>
        new(
            EvidenceId: "freew.platform-only.avalonia-file-shell",
            Slice: "Avalonia compact File shell variance",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                Test: "FreeWRibbonParityTests.Wpf_profile_uses_backstage_shell_instead_of_avalonia_file_command_strip"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
                Test: "RibbonAndDocumentTests.Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed"));

    private static CommandBehaviorEvidence AvaloniaPdfImportEvidence(string summary) =>
        new(
            EvidenceId: "freew.platform-only.pdf-import-shell",
            Slice: "PDF import shell variance",
            Summary: summary,
            WpfEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Presentation.Tests/DocumentPersistenceWorkflowTests.cs",
                Test: "DocumentPersistenceWorkflowTests.ImportPdfText_UsesExplicitImportAdaptersOutsideNormalOpenSaveCatalog"),
            AvaloniaEvidence: new BehaviorEvidenceLink(
                Path: "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
                Test: "RibbonAndDocumentTests.Import_pdf_ribbon_command_invokes_host_route"));

    private static IReadOnlyDictionary<string, IReadOnlyList<CommandLocation>> Collect(RibbonDefinition definition, string profile)
    {
        var locations = new Dictionary<string, List<CommandLocation>>(StringComparer.Ordinal);
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                    AddControl(locations, tab, group, control, profile);
            }
        }

        return locations.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CommandLocation>)pair.Value
                .OrderBy(location => location.TabId, StringComparer.Ordinal)
                .ThenBy(location => location.GroupId, StringComparer.Ordinal)
                .ThenBy(location => location.Label, StringComparer.Ordinal)
                .ThenBy(location => location.ControlType, StringComparer.Ordinal)
                .ThenBy(location => location.Layout, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static void AddControl(
        Dictionary<string, List<CommandLocation>> locations,
        RibbonTab tab,
        RibbonGroup group,
        RibbonControl control,
        string profile)
    {
        if (!string.IsNullOrEmpty(control.CommandId.Value))
        {
            AddLocation(locations, control.CommandId.Value, new CommandLocation(
                Profile: profile,
                TabId: tab.Id,
                Tab: tab.Header,
                GroupId: group.Id,
                Group: group.Header,
                Label: control.Label,
                ControlType: control.GetType().Name,
                Layout: control.PreferredLayout.ToString()));
        }

        foreach (var menuLocation in MenuLocations(control, tab, group, profile))
            AddLocation(locations, menuLocation.CommandId, menuLocation.Location);
    }

'@ + (Get-ToolCommandInventoryMenuTraversalSource) + @'

    private static void AddLocation(
        Dictionary<string, List<CommandLocation>> locations,
        string commandId,
        CommandLocation location)
    {
        if (!locations.TryGetValue(commandId, out var existing))
        {
            existing = [];
            locations.Add(commandId, existing);
        }

        existing.Add(location);
    }

    private static Classification ClassifyProfile(bool wpfPresent, bool avaloniaPresent) =>
        (wpfPresent, avaloniaPresent) switch
        {
            (true, true) => new Classification("shared-profile", "Command is present in both compiled FreeW ribbon profiles."),
            (true, false) => new Classification("wpf-profile-only", "Command is present only in the compiled WPF FreeW ribbon profile."),
            (false, true) => new Classification("avalonia-profile-only", "Command is present only in the compiled Avalonia FreeW ribbon profile."),
            _ => throw new InvalidOperationException("Command row has no compiled profile location."),
        };

    private static GapClassification ClassifyGap(
        string commandId,
        bool wpfPresent,
        bool avaloniaPresent,
        IReadOnlyList<CommandLocation> wpfLocations,
        IReadOnlyList<CommandLocation> avaloniaLocations,
        IReadOnlyCollection<string> allCommandIds,
        bool hasBehaviorEvidence)
    {
        if (wpfPresent && avaloniaPresent)
            return new GapClassification("shared-profile", "shared-profile", "Command is present in both compiled FreeW ribbon profiles.");

        var locations = wpfPresent ? wpfLocations : avaloniaLocations;

        if (IsCommandIdAlias(commandId, allCommandIds))
            return new GapClassification("command-id-alias", "command-id-alias", "Other profile exposes the same or closest command intent under a different generated command id.");

        if (IsPlatformOnly(commandId, locations))
            return new GapClassification(
                "platform-only",
                "platform-only",
                PlatformOnlyNotes.GetValueOrDefault(commandId) ??
                    "Host, shell, or desktop-only command; track separately from shared Word behavior gaps.");

        if (IsProfileShapeOnly(commandId, locations, hasBehaviorEvidence))
            return new GapClassification("profile-shape-only", "profile-shape-only", "Row is generated only by menu, dropdown, combo, gallery, or palette shape; a direct projection is retained only with paired behavior evidence.");

        if (IsDeferred(commandId, locations, hasBehaviorEvidence))
            return new GapClassification("deferred", "deferred", "Known richer Word slice that is intentionally outside the current generated Avalonia profile surface.");

        return new GapClassification("actionable-gap", "actionable-gap", "Shared Word command is present in only one compiled profile and needs follow-up.");
    }

    private static bool IsProfileShapeOnly(
        string commandId,
        IReadOnlyList<CommandLocation> locations,
        bool hasBehaviorEvidence) =>
        (!HasDirectActionControl(locations) || hasBehaviorEvidence) &&
        (locations.Any(location => location.ControlType is "RibbonMenuItem" or "RibbonComboBox" or "RibbonDropdown" or "RibbonGallery") ||
        locations.Any(location => location.GroupId is
            "chart-colors" or
            "chart-quick-layout" or
            "chart-style" or
            "chart-styles" or
            "document-formatting" or
            "draw-borders" or
            "hf-position" or
            "picture-adjust" or
            "picture-size" or
            "picture-styles" or
            "smartart-colors" or
            "smartart-create-graphic" or
            "smartart-edit" or
            "smartart-layouts" or
            "smartart-styles" or
            "symbols" or
            "table-style") ||
        commandId.StartsWith("freew.chart-color-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.chart-colors-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.chart-quick-layout-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.chart-style-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.cover-page.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.cover-page-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.equation.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.font-color.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.page-color.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.para-spacing.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.quick-parts.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.shape-change-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.shape-effect-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.shape-fill-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.shape-outline-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.smartart-colors-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.smartart-layout-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.symbol.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.table-", StringComparison.Ordinal) && commandId.Contains("x", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.table-borders.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.theme.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.theme-colors.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.theme-fonts.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.watermark.", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.wordart-style-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.wordart-warp-", StringComparison.Ordinal));

    private static bool HasDirectActionControl(IReadOnlyList<CommandLocation> locations) =>
        locations.Any(location => location.ControlType is
            "RibbonButton" or
            "RibbonToggleButton" or
            "RibbonSplitButton" or
            "RibbonCheckBox");

    private static bool IsCommandIdAlias(string commandId, IReadOnlyCollection<string> allCommandIds) =>
        AliasPairs.Any(pair =>
            (string.Equals(pair.Left, commandId, StringComparison.Ordinal) && allCommandIds.Contains(pair.Right)) ||
            (string.Equals(pair.Right, commandId, StringComparison.Ordinal) && allCommandIds.Contains(pair.Left)));

    private static bool IsPlatformOnly(string commandId, IReadOnlyList<CommandLocation> locations) =>
        locations.Any(location => location.TabId == "file") ||
        locations.Any(location => location.TabId == "help") ||
        locations.Any(location => location.TabId == "view" && location.GroupId == "window" && commandId is not "freew.new-window" and not "freew.split");

    private static bool IsDeferred(
        string commandId,
        IReadOnlyList<CommandLocation> locations,
        bool hasBehaviorEvidence) =>
        (!HasDirectActionControl(locations) || hasBehaviorEvidence) &&
        (locations.Any(location => location.TabId is
            "developer" or
            "header-footer-design" or
            "picture-format" or
            "drawing-format" or
            "chart-design" or
            "chart-format" or
            "smartart-design") ||
        commandId.StartsWith("freew.cc-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.hf-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.image-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.smartart-", StringComparison.Ordinal) ||
        commandId.StartsWith("freew.wordart-", StringComparison.Ordinal));

    private static readonly (string Left, string Right)[] AliasPairs =
    [
        ("freew.bookmark", "freew.insert-bookmark"),
        ("freew.caption", "freew.insert-caption"),
        ("freew.citation", "freew.insert-citation"),
        ("freew.cell-borders", "freew.table-borders"),
        ("freew.cell-shading", "freew.table-shading"),
        ("freew.chart-color-colorful1", "freew.chart-colors-colorful1"),
        ("freew.chart-color-colorful2", "freew.chart-colors-colorful2"),
        ("freew.chart-color-colorful3", "freew.chart-colors-colorful3"),
        ("freew.chart-color-colorful4", "freew.chart-colors-colorful4"),
        ("freew.chart-color-mono-blue", "freew.chart-colors-mono-blue"),
        ("freew.chart-color-mono-grey", "freew.chart-colors-mono-grey"),
        ("freew.chart-color-mono-orange", "freew.chart-colors-mono-orange"),
        ("freew.find", "freew.find-replace-dialog"),
        ("freew.formatting-marks", "freew.show-hide-para"),
        ("freew.hyperlink", "freew.insert-hyperlink"),
        ("freew.merge-cells", "freew.table-merge-cells"),
        ("freew.page-border", "freew.page-borders"),
        ("freew.shape-textbox", "freew.text-box"),
        ("freew.shapes", "freew.shape"),
        ("freew.smartart-change-colors", "freew.smartart-colors"),
        ("freew.smartart-change-layout", "freew.smartart-layout"),
        ("freew.split", "freew.split-window"),
        ("freew.split-cell", "freew.table-split-cell"),
        ("freew.table", "freew.insert-table"),
        ("freew.table-insert-col", "freew.table-insert-col-right"),
        ("freew.table-insert-row", "freew.table-insert-below"),
        ("freew.zoom-in", "freew.zoom-dialog"),
        ("freew.zoom-out", "freew.zoom-dialog"),
    ];

    private static string Surface(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "both"
            : wpfPresent
                ? "wpf-only"
                : "avalonia-only";

    private static string MissingProfile(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "none"
            : wpfPresent
                ? AvaloniaProfile
                : WpfProfile;

    private static string ReadRepositoryFile(string repoRoot, string relativePath)
    {
        var path = Path.Combine(new[] { repoRoot }.Concat(relativePath.Split('/')).ToArray());
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "";
    }

    private static bool ContainsCommandLiteral(string source, string commandId) =>
        Regex.IsMatch(
            source,
            $@"(?<![A-Za-z0-9_.-]){Regex.Escape(commandId)}(?![A-Za-z0-9_.-])",
            RegexOptions.CultureInvariant);
}

internal static class FreeWCommandInventoryMarkdown
{
    public static string Build(InventoryDocument inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FreeW WPF/Avalonia Command Inventory");
        builder.AppendLine();
        builder.AppendLine("Generated by `tools/Generate-FreeWCommandInventory.ps1` from compiled `FreeW.Ribbon.Definitions` profiles. Do not edit by hand.");
        builder.AppendLine();
        builder.AppendLine("Rows are created only from `FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf)` and `FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia)`, including menu children. Source literal evidence columns show exact command-id text in source files only; the canonical shared definition contributes to both profile-definition columns. These literals are not behavior proof and never create rows. Behavior evidence links a bounded command row to focused WPF and Avalonia tests; it strengthens parity confidence but does not create rows or change gap classification.");
        builder.AppendLine();
        builder.AppendLine(inventory.ClassificationNote);
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Total | Both profiles | WPF profile only | Avalonia profile only | Missing WPF profile | Missing Avalonia profile | Actionable missing WPF | Actionable missing Avalonia |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
        builder.AppendLine($"| {inventory.Summary.TotalCommands} | {inventory.Summary.Both} | {inventory.Summary.WpfOnly} | {inventory.Summary.AvaloniaOnly} | {inventory.Summary.MissingWpf} | {inventory.Summary.MissingAvalonia} | {inventory.Summary.ActionableMissingWpf} | {inventory.Summary.ActionableMissingAvalonia} |");
        builder.AppendLine();
        builder.AppendLine("## Classification Counts");
        builder.AppendLine();
        builder.AppendLine("| Shared profile | Profile-shape only | Command-id aliases | Platform-only | Deferred | Actionable gaps | Behavior evidence rows |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        builder.AppendLine($"| {inventory.Summary.SharedProfile} | {inventory.Summary.ProfileShapeOnly} | {inventory.Summary.CommandIdAliases} | {inventory.Summary.PlatformOnly} | {inventory.Summary.Deferred} | {inventory.Summary.ActionableGaps} | {inventory.Summary.BehaviorEvidenceRows} |");
        builder.AppendLine();
        builder.AppendLine("## Classification Rules");
        builder.AppendLine();
        builder.AppendLine("| Classification | Rule |");
        builder.AppendLine("|---|---|");
        foreach (var rule in inventory.ClassificationRules)
            builder.AppendLine($"| {Escape(rule.Name)} | {Escape(rule.Description)} |");
        builder.AppendLine();
        builder.AppendLine("## Matrix");
        builder.AppendLine();
        builder.AppendLine("| Command ID | Label | WPF profile | Avalonia profile | Missing profile | Profile classification | Gap classification | Rule | WPF locations | Avalonia locations | Source literal evidence | Behavior evidence | Notes |");
        builder.AppendLine("|---|---|---:|---:|---|---|---|---|---|---|---|---|---|");

        foreach (var command in inventory.Commands)
        {
            builder.AppendLine(
                $"| `{Escape(command.CommandId)}` | {Escape(command.Label)} | {YesNo(command.WpfPresent)} | {YesNo(command.AvaloniaPresent)} | {Escape(command.MissingProfile)} | {Escape(command.Classification)} | {Escape(command.GapClassification)} | {Escape(command.GapClassificationRule)} | {Escape(Locations(command.WpfLocations))} | {Escape(Locations(command.AvaloniaLocations))} | {Escape(SourceEvidence(command.SourceLiteralEvidence))} | {Escape(BehaviorEvidence(command.BehaviorEvidence))} | {Escape(command.Notes)} |");
        }

        return builder.ToString();
    }

    private static string Locations(IReadOnlyList<CommandLocation> locations) =>
        locations.Count == 0
            ? "-"
            : string.Join("<br>", locations.Select(location => $"{location.TabId}/{location.GroupId} ({location.ControlType}; {location.Layout})"));

    private static string SourceEvidence(SourceLiteralEvidence evidence)
    {
        var hits = new List<string>();
        if (evidence.WpfDefinitionSource)
            hits.Add("WPF definition source");
        if (evidence.AvaloniaDefinitionSource)
            hits.Add("Avalonia definition source");
        if (evidence.WpfRegistrySource)
            hits.Add("WPF registry source");
        if (evidence.AvaloniaRegistrySource)
            hits.Add("Avalonia registry source");

        return hits.Count == 0 ? "-" : string.Join("<br>", hits);
    }

    private static string BehaviorEvidence(CommandBehaviorEvidence? evidence) =>
        evidence is null
            ? "-"
            : $"{evidence.Slice}: {evidence.WpfEvidence.Test}<br>{evidence.AvaloniaEvidence.Test}";

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
}

internal sealed record InventoryDocument(
    string Schema,
    int SchemaVersion,
    string GeneratedBy,
    string TopologySource,
    string SourceLiteralEvidenceNote,
    string BehaviorEvidenceNote,
    string ClassificationNote,
    IReadOnlyList<ClassificationRule> ClassificationRules,
    IReadOnlyList<SourceLiteralFileEntry> SourceLiteralFiles,
    InventorySummary Summary,
    IReadOnlyList<CommandEntry> Commands);

internal sealed record SourceLiteralFileEntry(
    string Id,
    string Label,
    string Path);

internal sealed record InventorySummary(
    int TotalCommands,
    int Both,
    int WpfOnly,
    int AvaloniaOnly,
    int MissingWpf,
    int MissingAvalonia,
    int ActionableMissingWpf,
    int ActionableMissingAvalonia,
    int SharedProfile,
    int ProfileShapeOnly,
    int CommandIdAliases,
    int PlatformOnly,
    int Deferred,
    int ActionableGaps,
    int BehaviorEvidenceRows);

internal sealed record CommandEntry(
    string CommandId,
    string Label,
    bool WpfPresent,
    bool AvaloniaPresent,
    string ProfileSurface,
    string MissingProfile,
    string Classification,
    string GapClassification,
    string GapClassificationRule,
    string Notes,
    IReadOnlyList<CommandLocation> WpfLocations,
    IReadOnlyList<CommandLocation> AvaloniaLocations,
    SourceLiteralEvidence SourceLiteralEvidence,
    CommandBehaviorEvidence? BehaviorEvidence);

internal sealed record CommandLocation(
    string Profile,
    string TabId,
    string Tab,
    string GroupId,
    string Group,
    string Label,
    string ControlType,
    string Layout);

internal sealed record SourceLiteralEvidence(
    bool WpfDefinitionSource,
    bool AvaloniaDefinitionSource,
    bool WpfRegistrySource,
    bool AvaloniaRegistrySource);

internal sealed record CommandBehaviorEvidence(
    string EvidenceId,
    string Slice,
    string Summary,
    BehaviorEvidenceLink WpfEvidence,
    BehaviorEvidenceLink AvaloniaEvidence);

internal sealed record BehaviorEvidenceLink(
    string Path,
    string Test);

internal sealed record Classification(string Name, string Notes);

internal sealed record GapClassification(string Name, string Rule, string Notes);

internal sealed record ClassificationRule(string Name, string Description);

internal sealed record SourceLiteralFile(string Id, string Label, string RelativePath);
'@

Invoke-ToolGeneratedProject @{
    Prefix = "freex-freew-command-inventory"
    Name = "FreeW.CommandInventory.Generator"
    Reference = $definitionsProject
    Source = $programSource
    Outputs = [ordered]@{ $resolvedJsonPath = $JsonPath; $resolvedMarkdownPath = $MarkdownPath }
    Arguments = {
        param($outputPaths)
        @($repoRoot, $outputPaths[0].TempPath, $outputPaths[1].TempPath)
    }
    Script = "tools\Generate-FreeWCommandInventory.ps1"
    Failure = "FreeW command inventory generator failed."
    Check = $Check
    CheckMessage = "FreeW command inventory docs are up to date."
    WriteMessage = "Wrote $JsonPath and $MarkdownPath."
    DotNetPath = "dotnet"
}
