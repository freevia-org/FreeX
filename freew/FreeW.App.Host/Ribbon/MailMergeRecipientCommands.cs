using System;
using System.IO;
using System.Windows;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO.RecipientSources;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Mailings &gt; Select Recipients &gt; Use an Existing List: pick an Excel workbook, a delimited text
/// file or a Word/RTF document, choose the list inside it, and load it into the mail-merge session.
/// </summary>
internal sealed class UseExistingRecipientListCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context)
    {
        var owner = Window.GetWindow(editor);
        var picker = WpfFileDialogService.ShowOpenDialog(
            owner,
            MailMergeRecipientSourcePlanner.ToDialogFilter(MailMergeRecipientSourcePlanner.OpenFilters),
            title: MailMergeRecipientSourceText.SelectTitle);
        if (!picker.Chosen)
            return;

        RecipientSourceImportSession import;
        try
        {
            import = RecipientSourceImportSession.Open(picker.FileName!);
        }
        catch (Exception exception) when (MailMergeRecipientCommandSupport.IsSourceFailure(exception))
        {
            DialogMessageHelper.ShowWarning(
                owner,
                MailMergeRecipientSourcePlanner.DescribeFailure(exception),
                MailMergeRecipientSourceText.SelectTitle);
            return;
        }

        if (RecipientSourceImportDialog.Ask(owner, import) is not { } data)
            return;

        MailMergeRecipientCommandSupport.LoadRecipients(editor, session, data);
    }
}

/// <summary>
/// Mailings &gt; Select Recipients &gt; Type a New List: type the recipients into an address-list grid,
/// save the list to a file (as Word does), and load it into the mail-merge session.
/// </summary>
internal sealed class TypeNewRecipientListCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context)
    {
        var owner = Window.GetWindow(editor);
        var data = RecipientListEditorDialog.Ask(owner, new RecipientListEditorSession(), SaveList);
        if (data is null)
            return;

        MailMergeRecipientCommandSupport.LoadRecipients(editor, session, data);
    }

    // Returns false when the user cancels the save or it fails, which keeps the list dialog open.
    private static bool SaveList(Window owner, MergeData data)
    {
        var save = WpfFileDialogService.ShowSaveDialog(
            owner,
            MailMergeRecipientSourcePlanner.ToDialogFilter(MailMergeRecipientSourcePlanner.SaveFilters),
            MailMergeRecipientSourceText.DefaultFileName,
            ".csv",
            1,
            MailMergeRecipientSourceText.SaveTitle);
        if (!save.Chosen)
            return false;

        try
        {
            RecipientListFile.Save(save.FileName!, data);
            return true;
        }
        catch (Exception exception) when (MailMergeRecipientCommandSupport.IsSourceFailure(exception))
        {
            DialogMessageHelper.ShowWarning(
                owner,
                MailMergeRecipientSourceText.SaveError(
                    exception is RecipientSourceException
                        ? MailMergeRecipientSourcePlanner.DescribeFailure(exception)
                        : exception.Message),
                MailMergeRecipientSourceText.SaveTitle);
            return false;
        }
    }
}

/// <summary>
/// Mailings &gt; Edit Recipient List: include or exclude recipients with check boxes, sort and filter
/// the list. Works on the list as loaded, so excluded recipients can be brought back.
/// </summary>
internal sealed class EditRecipientListCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context)
    {
        var owner = Window.GetWindow(editor);
        if (session.SourceData is not { } source)
        {
            DialogMessageHelper.ShowInfo(
                owner,
                MailMergeRecipientSourceText.EditListNoRecipients,
                MailMergeDialogMetadata.MailMergeTitle);
            return;
        }

        var workflow = new MailMergeSessionWorkflow(session);
        var selection = RecipientListSelectionDialog.Ask(
            owner,
            new RecipientListSelectionSession(source, workflow.ActiveSourceIndexes()));
        if (selection is null)
            return;

        var transition = workflow.ApplyRecipientSelection(selection);
        MailMergeRecipientCommandSupport.Realize(editor, transition);
        DialogMessageHelper.ShowInfo(owner, transition.Message, MailMergeDialogMetadata.MailMergeTitle);
        editor.Focus();
    }
}

internal static class MailMergeRecipientCommandSupport
{
    /// <summary>
    /// The failures reading or writing a recipient file can legitimately produce. Anything else is a
    /// defect and is left to the application's unhandled-exception reporting.
    /// </summary>
    public static bool IsSourceFailure(Exception exception) =>
        exception is RecipientSourceException or IOException or UnauthorizedAccessException;

    public static void LoadRecipients(DocumentView editor, MailMergeSession session, MergeData data)
    {
        var transition = new MailMergeSessionWorkflow(session).LoadRecipients(data);
        Realize(editor, transition);
        DialogMessageHelper.ShowInfo(
            Window.GetWindow(editor),
            transition.Message,
            MailMergeDialogMetadata.MailMergeTitle);
        editor.Focus();
    }

    // Leaving a merge preview hands back the template document to show again.
    public static void Realize(DocumentView editor, MailMergeSessionTransition transition)
    {
        if (transition.DocumentToLoad is { } document)
            editor.LoadModel(document);
    }
}
