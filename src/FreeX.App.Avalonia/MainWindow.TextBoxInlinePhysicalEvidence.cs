using System.Text.Json;

using Avalonia.Automation;
using Avalonia.Controls;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const string TextBoxInlinePhysicalEvidenceEnvironmentVariable =
        "FREEX_TEXTBOX_INLINE_PHYSICAL_RESULT";

    private readonly string? _textBoxInlinePhysicalEvidencePath =
        Environment.GetEnvironmentVariable(TextBoxInlinePhysicalEvidenceEnvironmentVariable);

    private readonly List<TextBoxInlinePhysicalEvidenceEvent> _textBoxInlinePhysicalEvidenceEvents = [];
    private bool _textBoxInlinePhysicalLayoutObservationPending;

    private void RequestTextBoxInlinePhysicalLayoutObservation()
    {
        if (!string.IsNullOrWhiteSpace(_textBoxInlinePhysicalEvidencePath))
            _textBoxInlinePhysicalLayoutObservationPending = true;
    }

    private void TextBoxInlineEditor_LayoutUpdated(object? sender, EventArgs args)
    {
        if (!_textBoxInlinePhysicalLayoutObservationPending ||
            _textBoxInlineEditingId is not { } textBoxId ||
            _textBoxInlineEditor is not { IsVisible: true, IsFocused: true } editor ||
            editor.Bounds.Width <= 0 ||
            editor.Bounds.Height <= 0)
        {
            return;
        }

        // Clear first so writing evidence cannot re-enter through another layout pass.
        _textBoxInlinePhysicalLayoutObservationPending = false;
        RecordTextBoxInlinePhysicalEvidence("editing", textBoxId);
    }

    private void RecordTextBoxInlinePhysicalEvidence(string phase, Guid textBoxId)
    {
        if (string.IsNullOrWhiteSpace(_textBoxInlinePhysicalEvidencePath))
            return;

        var editor = _textBoxInlineEditor;
        if (editor is null || editor.Bounds.Width <= 0 || editor.Bounds.Height <= 0)
            return;

        var focusedElement = FocusManager?.GetFocusedElement();
        var focusedControl = focusedElement as Control;
        var textBox = GetCurrentSheetTextBox(textBoxId);
        var eventRecord = new TextBoxInlinePhysicalEvidenceEvent(
            phase,
            textBoxId.ToString("D"),
            editor.IsVisible,
            editor.IsFocused,
            true,
            editor.Bounds.Width,
            editor.Bounds.Height,
            AutomationProperties.GetAutomationId(editor),
            focusedControl is null ? null : AutomationProperties.GetAutomationId(focusedControl),
            editor.Text ?? string.Empty,
            textBox?.Text ?? string.Empty,
            DateTimeOffset.UtcNow);
        _textBoxInlinePhysicalEvidenceEvents.Add(eventRecord);

        try
        {
            var path = Path.GetFullPath(_textBoxInlinePhysicalEvidencePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var document = new TextBoxInlinePhysicalEvidenceDocument(
                1,
                "freex-linux-textbox-inline-edit-physical",
                "linux",
                "avalonia",
                "FreeX",
                _textBoxInlinePhysicalEvidenceEvents.ToArray());
            var temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // Physical evidence must never change the production editor's behavior.
        }
    }

    private sealed record TextBoxInlinePhysicalEvidenceDocument(
        int SchemaVersion,
        string Suite,
        string Platform,
        string Shell,
        string App,
        IReadOnlyList<TextBoxInlinePhysicalEvidenceEvent> Events);

    private sealed record TextBoxInlinePhysicalEvidenceEvent(
        string Phase,
        string TextBoxId,
        bool EditorVisible,
        bool EditorFocused,
        bool NonZeroBounds,
        double EditorWidth,
        double EditorHeight,
        string? EditorAutomationId,
        string? FocusedAutomationId,
        string EditorText,
        string ModelText,
        DateTimeOffset CapturedAtUtc);
}
