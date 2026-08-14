using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record InsertEditingRibbonPorts(
    IRibbonCommand Hyperlink,
    IRibbonCommand EditHyperlink,
    IRibbonCommand RemoveHyperlink,
    IRibbonCommand HyperlinkTooltip,
    IRibbonCommand Bookmark,
    IRibbonCommand LinkBookmark,
    IRibbonCommand BookmarkManager,
    Action PrepareContentControlInsertion,
    Action InsertPlainTextControl,
    Action InsertRichTextControl,
    Action InsertCheckBoxControl,
    Action InsertDatePickerControl,
    Action InsertDropDownListControl,
    Action InsertComboBoxControl,
    Action UpdateFields,
    Action ToggleFieldCodes);

/// <summary>
/// Owns the portable command identity and mutation ordering for Insert links/bookmarks,
/// Developer content controls, and field maintenance. Renderers retain only native dialogs
/// and editor-effect adapters.
/// </summary>
public static class InsertEditingRibbonWorkflow
{
    public static void Register(
        IRibbonCommandRegistry registry,
        InsertEditingRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);

        registry.Bind(FreeWRibbonCommandAction.Hyperlink, ports.Hyperlink);
        registry.Register("freew.insert-hyperlink", ports.Hyperlink);
        registry.Bind(FreeWRibbonCommandAction.EditHyperlink, ports.EditHyperlink);
        registry.Bind(FreeWRibbonCommandAction.RemoveHyperlink, ports.RemoveHyperlink);
        registry.Bind(FreeWRibbonCommandAction.HyperlinkTooltip, ports.HyperlinkTooltip);

        registry.Bind(FreeWRibbonCommandAction.Bookmark, ports.Bookmark);
        registry.Register("freew.insert-bookmark", ports.Bookmark);
        registry.Bind(FreeWRibbonCommandAction.LinkBookmark, ports.LinkBookmark);
        registry.Bind(FreeWRibbonCommandAction.BookmarkManager, ports.BookmarkManager);

        BindPrepared(FreeWRibbonCommandAction.CcText, ports.InsertPlainTextControl);
        BindPrepared(FreeWRibbonCommandAction.CcRichtext, ports.InsertRichTextControl);
        BindPrepared(FreeWRibbonCommandAction.CcCheckbox, ports.InsertCheckBoxControl);
        BindPrepared(FreeWRibbonCommandAction.CcDate, ports.InsertDatePickerControl);
        BindPrepared(FreeWRibbonCommandAction.CcDropdown, ports.InsertDropDownListControl);
        BindPrepared(FreeWRibbonCommandAction.CcCombo, ports.InsertComboBoxControl);

        registry.Bind(FreeWRibbonCommandAction.UpdateFields, new ActionRibbonCommand(ports.UpdateFields));
        registry.Bind(FreeWRibbonCommandAction.ToggleFieldCodes, new ActionRibbonCommand(ports.ToggleFieldCodes));

        void BindPrepared(FreeWRibbonCommandAction action, Action execute) =>
            registry.Bind(
                action,
                new PreparedActionCommand(ports.PrepareContentControlInsertion, execute));
    }

    private sealed class PreparedActionCommand(Action prepare, Action execute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            prepare();
            execute();
        }
    }
}
