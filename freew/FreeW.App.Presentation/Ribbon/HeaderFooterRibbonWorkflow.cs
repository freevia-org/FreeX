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
    IRibbonCommand InsertDocumentInfo);

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

        var stateful = new List<HeaderFooterRibbonCommand>(4);
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
