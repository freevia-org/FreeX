using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

public sealed partial class MainWindow
{
    /// <summary>
    /// File &gt; Info &gt; Protect Document &gt; Encrypt with Password: sets, changes or clears the
    /// password the document is encrypted with on save. The password lives only on the in-memory
    /// document for this session.
    /// </summary>
    private void OpenEncryptWithPassword()
    {
        _editor.Focus();
        var decision = EncryptDocumentDialog.Prompt(this, BackstageInfoSafetyPanePlanner.ResolveText(UiText.Get));
        if (decision is null)
            return;

        // The mail-merge preview shows a merged copy; the password belongs to the document that is saved.
        var document = _editor.MailMergeSession?.Template ?? _editor.Model;
        if (EncryptDocumentDialogPlanner.Apply(document, decision))
            MarkDirtyOutsideBodyUndo();
    }
}
