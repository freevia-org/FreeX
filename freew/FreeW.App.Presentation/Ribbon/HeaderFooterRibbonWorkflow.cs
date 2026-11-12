using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record HeaderFooterRibbonBindings(
    IRibbonCommand Header,
    IRibbonCommand Footer,
    IRibbonCommand PageNumber,
    IRibbonCommand PageNumberTop,
    IRibbonCommand PageNumberBottom,
    IRibbonCommand PageNumberCurrent,
    IRibbonCommand PageNumberFormat,
    IRibbonCommand DateTime,
    Func<HeaderFooterSlotKind, IRibbonCommand> CreateEditSlotCommand,
    IRibbonStatefulCommand DifferentFirstPage,
    IRibbonStatefulCommand DifferentOddEvenPages,
    IRibbonStatefulCommand HeaderFromTop,
    IRibbonStatefulCommand FooterFromBottom,
    Func<HeaderFooterSlotKind, IRibbonCommand> CreateNavigationCommand,
    IRibbonCommand Close,
    IRibbonCommand InsertHeaderPageNumber,
    IRibbonCommand InsertFooterPageNumber,
    IRibbonCommand InsertDateTime,
    IRibbonCommand InsertDocumentInfo,
    IRibbonStatefulCommand PreviousSection,
    IRibbonStatefulCommand NextSection,
    IRibbonStatefulCommand LinkToPrevious);

public sealed record HeaderFooterRibbonCommand(
    RibbonCommandId Id,
    IRibbonStatefulCommand Command);

public sealed record HeaderFooterRibbonCommands(
    IReadOnlyList<HeaderFooterRibbonCommand> StatefulCommands);

public sealed record HeaderFooterPageSettingsPorts(
    Func<PageSettings> GetPageSettings,
    Action<Action<PageSettings>> ApplyPageSettings,
    Func<bool> IsEnabled,
    Func<RibbonCommandContext, string?>? ResolveSelectedValue = null);

/// <summary>
/// Renderer adapters behind the Header &amp; Footer Navigation group's per-section commands.
/// <paramref name="GetActiveSlot"/> reports the section + slot the editing surface targets (null when the
/// renderer has none); <paramref name="MoveToAdjacentSection"/> is null when the renderer cannot move its
/// editing surface between sections, which disables Previous/Next rather than faking them.
/// </summary>
public sealed record HeaderFooterSectionNavigationPorts(
    Func<HeaderFooterSectionSlotView?> GetActiveSlot,
    Action<int>? MoveToAdjacentSection,
    Action? ToggleLinkToPrevious,
    Func<bool>? IsEnabled = null);

public sealed record HeaderFooterSectionNavigationCommands(
    IRibbonStatefulCommand PreviousSection,
    IRibbonStatefulCommand NextSection,
    IRibbonStatefulCommand LinkToPrevious);

public sealed record HeaderFooterPageSettingCommands(
    IRibbonStatefulCommand DifferentFirstPage,
    IRibbonStatefulCommand DifferentOddEvenPages,
    IRibbonStatefulCommand HeaderFromTop,
    IRibbonStatefulCommand FooterFromBottom);

/// <summary>
/// Owns Insert and Header &amp; Footer Design command identity over renderer-provided editor, pane,
/// prompt, and dialog adapters. Slot-to-action mapping remains canonical across both renderers.
/// </summary>
public static class HeaderFooterRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.Header,
        FreeWRibbonCommandAction.Footer,
        FreeWRibbonCommandAction.PageNumber,
        FreeWRibbonCommandAction.PageNumberTop,
        FreeWRibbonCommandAction.PageNumberBottom,
        FreeWRibbonCommandAction.PageNumberCurrent,
        FreeWRibbonCommandAction.PageNumberFormat,
        FreeWRibbonCommandAction.Datetime,
        .. FreeWRibbonSemanticCatalog.HeaderFooterEditSlots.Select(binding => binding.Action),
        FreeWRibbonCommandAction.HfDifferentFirstPage,
        FreeWRibbonCommandAction.HfDifferentOddEven,
        FreeWRibbonCommandAction.HfHeaderFromTop,
        FreeWRibbonCommandAction.HfFooterFromBottom,
        .. FreeWRibbonSemanticCatalog.HeaderFooterNavigationSlots.Select(binding => binding.Action),
        FreeWRibbonCommandAction.HfClose,
        FreeWRibbonCommandAction.HfInsertPageNumber,
        FreeWRibbonCommandAction.HfInsertPageNumberFooter,
        FreeWRibbonCommandAction.HfInsertDatetime,
        FreeWRibbonCommandAction.HfInsertField,
        FreeWRibbonCommandAction.HfPreviousSection,
        FreeWRibbonCommandAction.HfNextSection,
        FreeWRibbonCommandAction.HfLinkToPrevious,
    ];

    public static HeaderFooterRibbonCommands Register(
        FreeWRibbonEditorCommandFamilyBuilder builder,
        HeaderFooterRibbonBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(bindings.CreateEditSlotCommand);
        ArgumentNullException.ThrowIfNull(bindings.CreateNavigationCommand);

        Bind(FreeWRibbonCommandAction.Header, bindings.Header);
        Bind(FreeWRibbonCommandAction.Footer, bindings.Footer);
        Bind(FreeWRibbonCommandAction.PageNumber, bindings.PageNumber);
        Bind(FreeWRibbonCommandAction.PageNumberTop, bindings.PageNumberTop);
        Bind(FreeWRibbonCommandAction.PageNumberBottom, bindings.PageNumberBottom);
        Bind(FreeWRibbonCommandAction.PageNumberCurrent, bindings.PageNumberCurrent);
        Bind(FreeWRibbonCommandAction.PageNumberFormat, bindings.PageNumberFormat);
        Bind(FreeWRibbonCommandAction.Datetime, bindings.DateTime);

        foreach (var binding in FreeWRibbonSemanticCatalog.HeaderFooterEditSlots)
            Bind(binding.Action, bindings.CreateEditSlotCommand(binding.Slot));

        var stateful = new List<HeaderFooterRibbonCommand>(7);
        BindStateful(FreeWRibbonCommandAction.HfDifferentFirstPage, bindings.DifferentFirstPage);
        BindStateful(FreeWRibbonCommandAction.HfDifferentOddEven, bindings.DifferentOddEvenPages);
        BindStateful(FreeWRibbonCommandAction.HfHeaderFromTop, bindings.HeaderFromTop);
        BindStateful(FreeWRibbonCommandAction.HfFooterFromBottom, bindings.FooterFromBottom);

        foreach (var binding in FreeWRibbonSemanticCatalog.HeaderFooterNavigationSlots)
            Bind(binding.Action, bindings.CreateNavigationCommand(binding.Slot));

        Bind(FreeWRibbonCommandAction.HfClose, bindings.Close);
        Bind(FreeWRibbonCommandAction.HfInsertPageNumber, bindings.InsertHeaderPageNumber);
        Bind(FreeWRibbonCommandAction.HfInsertPageNumberFooter, bindings.InsertFooterPageNumber);
        Bind(FreeWRibbonCommandAction.HfInsertDatetime, bindings.InsertDateTime);
        Bind(FreeWRibbonCommandAction.HfInsertField, bindings.InsertDocumentInfo);
        BindStateful(FreeWRibbonCommandAction.HfPreviousSection, bindings.PreviousSection);
        BindStateful(FreeWRibbonCommandAction.HfNextSection, bindings.NextSection);
        BindStateful(FreeWRibbonCommandAction.HfLinkToPrevious, bindings.LinkToPrevious);

        return new HeaderFooterRibbonCommands(stateful);

        void Bind(FreeWRibbonCommandAction action, IRibbonCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            builder.Bind(action, command);
        }

        void BindStateful(FreeWRibbonCommandAction action, IRibbonStatefulCommand command)
        {
            Bind(action, command);
            stateful.Add(new HeaderFooterRibbonCommand(
                FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action),
                command));
        }
    }

    public static HeaderFooterPageSettingCommands CreatePageSettingCommands(
        HeaderFooterPageSettingsPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.GetPageSettings);
        ArgumentNullException.ThrowIfNull(ports.ApplyPageSettings);
        ArgumentNullException.ThrowIfNull(ports.IsEnabled);

        return new HeaderFooterPageSettingCommands(
            DifferentFirstPage: Toggle(
                page => page.DifferentFirstPage = !page.DifferentFirstPage,
                page => page.DifferentFirstPage),
            DifferentOddEvenPages: Toggle(
                page => page.DifferentOddEvenPages = !page.DifferentOddEvenPages,
                page => page.DifferentOddEvenPages),
            HeaderFromTop: Distance(
                page => page.HeaderDistancePt,
                (page, points) => page.HeaderDistancePt = points),
            FooterFromBottom: Distance(
                page => page.FooterDistancePt,
                (page, points) => page.FooterDistancePt = points));

        IRibbonStatefulCommand Toggle(
            Action<PageSettings> apply,
            Func<PageSettings, bool> isChecked) =>
            new PageSettingToggleCommand(ports, apply, isChecked);

        IRibbonStatefulCommand Distance(
            Func<PageSettings, double> getDistance,
            Action<PageSettings, double> setDistance) =>
            new PageSettingDistanceCommand(ports, getDistance, setDistance);
    }

    /// <summary>
    /// Creates Word's per-section Navigation commands: Previous / Next move the editing surface to the
    /// neighbouring section's header/footer (disabled at the document ends), and Link to Previous is a
    /// toggle that is checked while the section inherits the slot and unavailable in the first section.
    /// </summary>
    public static HeaderFooterSectionNavigationCommands CreateSectionNavigationCommands(
        HeaderFooterSectionNavigationPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.GetActiveSlot);

        return new HeaderFooterSectionNavigationCommands(
            PreviousSection: new SectionMoveCommand(ports, direction: -1),
            NextSection: new SectionMoveCommand(ports, direction: +1),
            LinkToPrevious: new LinkToPreviousCommand(ports));
    }

    private sealed class SectionMoveCommand(
        HeaderFooterSectionNavigationPorts ports,
        int direction) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                ports.MoveToAdjacentSection!(direction);
        }

        public RibbonCommandState GetState()
        {
            var canMove = ports.MoveToAdjacentSection is not null
                && ports.IsEnabled?.Invoke() != false
                && ports.GetActiveSlot() is { } view
                && (direction < 0 ? view.HasPreviousSection : view.HasNextSection);
            return new RibbonCommandState(IsEnabled: canMove);
        }
    }

    private sealed class LinkToPreviousCommand(HeaderFooterSectionNavigationPorts ports) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (GetState().IsEnabled)
                ports.ToggleLinkToPrevious!();
        }

        public RibbonCommandState GetState()
        {
            var view = ports.GetActiveSlot();
            var enabled = ports.ToggleLinkToPrevious is not null
                && ports.IsEnabled?.Invoke() != false
                && view is { CanLinkToPrevious: true };
            return new RibbonCommandState(IsEnabled: enabled, IsChecked: view?.IsLinkedToPrevious == true);
        }
    }

    private sealed class PageSettingToggleCommand(
        HeaderFooterPageSettingsPorts ports,
        Action<PageSettings> apply,
        Func<PageSettings, bool> isChecked) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (ports.IsEnabled())
                ports.ApplyPageSettings(apply);
        }

        public RibbonCommandState GetState() => new(
            IsEnabled: ports.IsEnabled(),
            IsChecked: isChecked(ports.GetPageSettings()));
    }

    private sealed class PageSettingDistanceCommand(
        HeaderFooterPageSettingsPorts ports,
        Func<PageSettings, double> getDistance,
        Action<PageSettings, double> setDistance) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var selectedValue = ports.ResolveSelectedValue?.Invoke(context) ?? context.SelectedValue;
            if (!ports.IsEnabled()
                || !HeaderFooterDialogPlanner.TryParseDistance(selectedValue, out var points))
            {
                return;
            }

            ports.ApplyPageSettings(page => setDistance(page, points));
        }

        public RibbonCommandState GetState() => new(
            IsEnabled: ports.IsEnabled(),
            Value: HeaderFooterDialogPlanner.FormatDistance(getDistance(ports.GetPageSettings())));
    }
}
