using Free.Shared.Ribbon;
using FreeX.App.Presentation.PageLayout;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Services.Ribbon;

/// <summary>Applies localized tab presentation and portable semantic combo choices to FreeX ribbon definitions.</summary>
public static class FreeXRibbonCompositionPlanner
{
    public static RibbonDefinition Compose(RibbonDefinition definition, Func<string, string?> resourceResolver)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(resourceResolver);

        var tabs = definition.Tabs.Select(tab => tab with
        {
            Header = FreeXRibbonTabPresentationCatalog.Resolve(tab.Id, resourceResolver),
            Groups = tab.Groups.Select(group => group with
            {
                Controls = group.Controls.Select(ApplyChoices).ToArray(),
            }).ToArray(),
        }).ToArray();

        return definition with { Tabs = tabs };
    }

    private static RibbonControl ApplyChoices(RibbonControl control)
    {
        if (control is not RibbonComboBox combo)
            return control;

        IReadOnlyList<RibbonComboBoxChoice>? choices = combo.CommandId.Value switch
        {
            "Number Format" => HomeNumberFormatGalleryPlanner.Choices,
            "Scale Width" or "Scale Height" => PageLayoutInputParser.ScalePageCountChoices
                .Select(choice => new RibbonComboBoxChoice(choice.Value, choice.Label)).ToArray(),
            "Scale Percent" => PageLayoutInputParser.ScalePercentChoices
                .Select(choice => new RibbonComboBoxChoice(choice.Value, choice.Label)).ToArray(),
            _ => null,
        };

        return choices is null ? combo : combo with { Choices = choices, Items = Array.Empty<string>() };
    }
}
