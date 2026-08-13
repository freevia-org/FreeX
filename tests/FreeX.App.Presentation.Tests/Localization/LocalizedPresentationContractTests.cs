using FluentAssertions;
using Free.Shared.Localization;
using FreeX.App.Presentation;
using FreeX.App.Presentation.Options;

namespace FreeX.App.Presentation.Tests.Localization;

public sealed class LocalizedPresentationContractTests
{
    public enum FocusTarget
    {
        Name,
    }

    [Fact]
    public void Shared_contract_preserves_localized_text_and_validation_behavior()
    {
        var text = LocalizedTextDescriptor.Resource("Greeting", "Ada");
        var resolver = new ResourceKeyTextResolver(
            key => $"get:{key}",
            (key, arguments) => $"format:{key}:{string.Join(",", arguments)}");
        var validation = new ValidationPresentationDescriptor<FocusTarget>(text, FocusTarget.Name);

        text.Resolve(resolver).Should().Be("format:Greeting:Ada");
        LocalizedTextDescriptor.Literal("Ready").Resolve(resolver).Should().Be("Ready");
        validation.FocusTarget.Should().Be(FocusTarget.Name);
        validation.Message.Should().BeSameAs(text);
    }

    [Fact]
    public void Planner_text_resources_adapt_resource_keys_and_preserve_localized_blank_text()
    {
        var resources = new FreeXPlannerTextResources(
            key => key == "AutoFilter_BlankDisplayText" ? "(vide)" : $"get:{key}",
            (key, arguments) => $"format:{key}:{string.Join(",", arguments)}");

        resources.AutoFilter.BlankDisplayText.Should().Be("(vide)");
        resources.AutoFilter.Get("AutoFilter_Search").Should().Be("get:AutoFilter_Search");
        resources.AutoFilter.Format("AutoFilter_ColumnHeader", "A")
            .Should().Be("format:AutoFilter_ColumnHeader:A");
        resources.Text.Get("Backstage_Info_NotSavedYet")
            .Should().Be("get:Backstage_Info_NotSavedYet");
    }

    [Fact]
    public void FreeX_public_planner_signatures_use_only_shared_localization_contracts()
    {
        var presentationAssembly = typeof(OptionsValidationPresentationPlanner).Assembly;
        var sharedAssembly = typeof(LocalizedTextDescriptor).Assembly;
        var localizationContractNames = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(LocalizedTextDescriptor),
            nameof(ResourceKeyTextResolver),
            "ValidationPresentationDescriptor`1",
        };

        var localizationSignatureTypes = presentationAssembly
            .GetExportedTypes()
            .SelectMany(type => type
                .GetMethods()
                .SelectMany(method => new[] { method.ReturnType }
                    .Concat(method.GetParameters().Select(parameter => parameter.ParameterType))))
            .SelectMany(Flatten)
            .Where(type => localizationContractNames.Contains(type.Name))
            .Distinct()
            .ToList();

        localizationSignatureTypes.Should().NotBeEmpty();
        localizationSignatureTypes.Should().OnlyContain(type => type.Assembly == sharedAssembly);
    }

    [Fact]
    public void FreeX_presentation_source_contains_no_localization_facade_namespace()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var presentationRoot = Path.Combine(repoRoot, "src", "FreeX.App.Presentation");
        var staleSources = Directory
            .EnumerateFiles(presentationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "FreeX.App.Presentation.Localization",
                StringComparison.Ordinal))
            .ToList();

        staleSources.Should().BeEmpty();
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (!type.IsGenericType)
            yield break;

        foreach (var argument in type.GetGenericArguments())
            foreach (var nested in Flatten(argument))
                yield return nested;
    }
}
