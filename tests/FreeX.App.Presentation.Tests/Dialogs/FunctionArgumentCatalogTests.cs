using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class FunctionArgumentCatalogTests
{
    [Fact]
    public void GetArgumentSpecs_Sum_HasRequiredAndOptional()
    {
        var args = FunctionArgumentCatalog.GetArgumentSpecs("SUM");

        args.Should().HaveCount(2);
        args[0].Should().BeEquivalentTo(new { Name = "Number1", Optional = false });
        args[1].Optional.Should().BeTrue();
    }

    [Fact]
    public void GetArgumentSpecs_IsCaseInsensitive()
    {
        FunctionArgumentCatalog.GetArgumentSpecs("sum")
            .Should().BeEquivalentTo(FunctionArgumentCatalog.GetArgumentSpecs("SUM"));
    }

    [Fact]
    public void GetArgumentSpecs_If_HasTwoRequiredOneOptional()
    {
        var args = FunctionArgumentCatalog.GetArgumentSpecs("IF");

        args.Should().HaveCount(3);
        args.Take(2).Should().OnlyContain(spec => !spec.Optional);
        args[2].Optional.Should().BeTrue();
        FunctionArgumentCatalog.RequiredArgumentCount("IF").Should().Be(2);
    }

    [Fact]
    public void GetArgumentSpecs_Vlookup_RangeLookupOptional()
    {
        var args = FunctionArgumentCatalog.GetArgumentSpecs("VLOOKUP");

        args.Should().HaveCount(4);
        args[^1].Name.Should().Be("Range_lookup");
        args[^1].Optional.Should().BeTrue();
        FunctionArgumentCatalog.RequiredArgumentCount("VLOOKUP").Should().Be(3);
    }

    [Fact]
    public void GetArgumentSpecs_NoArgFunction_IsEmpty()
    {
        FunctionArgumentCatalog.GetArgumentSpecs("TODAY").Should().BeEmpty();
        FunctionArgumentCatalog.RequiredArgumentCount("TODAY").Should().Be(0);
        FunctionArgumentCatalog.HasArgumentSpecs("TODAY").Should().BeTrue();
    }

    [Fact]
    public void GetArgumentSpecs_UnknownFunction_FallsBackToGenericArgument()
    {
        FunctionArgumentCatalog.HasArgumentSpecs("ZZZNOTAFUNC").Should().BeFalse();

        var args = FunctionArgumentCatalog.GetArgumentSpecs("ZZZNOTAFUNC");

        args.Should().HaveCount(1);
        args[0].Optional.Should().BeFalse();
    }

    [Fact]
    public void EveryCatalogedFunction_HasNonEmptyArgumentDescriptions()
    {
        foreach (var name in new[] { "SUM", "IF", "XLOOKUP", "FILTER", "DATE", "SUBSTITUTE" })
        {
            FunctionArgumentCatalog.GetArgumentSpecs(name)
                .Should().OnlyContain(spec => !string.IsNullOrWhiteSpace(spec.Description), $"{name} args need help text");
        }
    }

    // ----- Preview builder -----

    [Fact]
    public void BuildFormula_TwoArguments_RendersCallWithoutEqualsPrefix()
    {
        FunctionArgumentCatalog.BuildFormula("SUM", ["A1", "A2"]).Should().Be("SUM(A1, A2)");
    }

    [Fact]
    public void BuildFormula_TrimsTrailingBlankArguments()
    {
        FunctionArgumentCatalog.BuildFormula("IF", ["A1>0", "1", "   "]).Should().Be("IF(A1>0, 1)");
    }

    [Fact]
    public void BuildPreview_TwoArguments_RendersCommaSeparated()
    {
        FunctionArgumentCatalog.BuildPreview("SUM", ["A1", "A2"]).Should().Be("=SUM(A1, A2)");
    }

    [Fact]
    public void BuildPreview_NormalizesFunctionNameToUpper()
    {
        FunctionArgumentCatalog.BuildPreview("sum", ["A1"]).Should().Be("=SUM(A1)");
    }

    [Fact]
    public void BuildPreview_TrimsTrailingBlankArguments()
    {
        FunctionArgumentCatalog.BuildPreview("IF", ["A1>0", "1", "   "]).Should().Be("=IF(A1>0, 1)");
    }

    [Fact]
    public void BuildPreview_PreservesInteriorBlankSlots()
    {
        FunctionArgumentCatalog.BuildPreview("VLOOKUP", ["A1", "", "2"]).Should().Be("=VLOOKUP(A1, , 2)");
    }

    [Fact]
    public void BuildPreview_NoArguments_RendersEmptyParens()
    {
        FunctionArgumentCatalog.BuildPreview("TODAY", []).Should().Be("=TODAY()");
    }

    [Fact]
    public void BuildPreview_AllBlankArguments_RendersEmptyParens()
    {
        FunctionArgumentCatalog.BuildPreview("SUM", ["", "  "]).Should().Be("=SUM()");
    }

    [Fact]
    public void BuildPreview_TrimsIndividualArgumentWhitespace()
    {
        FunctionArgumentCatalog.BuildPreview("SUM", ["  A1  ", " A2 "]).Should().Be("=SUM(A1, A2)");
    }
}
