using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class WorksheetCustomViewStateBinaryContractTests
{
    [Fact]
    public void OriginalThirtyParameterConstructorAndDeconstructRemainCallable()
    {
        Type[] originalTypes =
        [
            typeof(string), typeof(WorksheetViewMode), typeof(uint), typeof(uint), typeof(uint?), typeof(uint?),
            typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool),
            typeof(uint?), typeof(uint?), typeof(uint?), typeof(uint?),
            typeof(IReadOnlyList<uint>), typeof(IReadOnlyList<uint>), typeof(IReadOnlyList<uint>),
            typeof(WorksheetAutoFilterModel), typeof(IReadOnlyList<GridRange>),
            typeof(WorksheetPageOrientation?), typeof(WorksheetPaperSize?), typeof(int?), typeof(WorksheetPageMargins?),
            typeof(double?), typeof(double?), typeof(bool?), typeof(bool?), typeof(WorksheetScaleToFit?), typeof(bool?)
        ];
        var constructor = typeof(WorksheetCustomViewState).GetConstructor(originalTypes);
        constructor.Should().NotBeNull("previously compiled consumers call this exact constructor signature");
        var arguments = constructor!.GetParameters().Select(parameter => parameter.IsOptional ? parameter.DefaultValue : null).ToArray();
        arguments[0] = "Data";
        arguments[1] = WorksheetViewMode.Normal;
        arguments[2] = 3u;
        arguments[3] = 1u;
        var state = (WorksheetCustomViewState)constructor.Invoke(arguments);
        state.SheetName.Should().Be("Data");
        state.FrozenRows.Should().Be(3);

        var deconstruct = typeof(WorksheetCustomViewState).GetMethod("Deconstruct", originalTypes.Select(type => type.MakeByRefType()).ToArray());
        deconstruct.Should().NotBeNull("previously compiled consumers call the original 30-out method");
        var outputs = new object?[originalTypes.Length];
        deconstruct!.Invoke(state, outputs);
        outputs[0].Should().Be("Data");
        outputs[2].Should().Be(3u);
        outputs[3].Should().Be(1u);

        var withPrintSettings = state with { PrintSettings = new WorksheetCustomViewPrintSettings(), OpaquePrintAreaExpression = "OFFSET(Data!A1,0,0,1,1)" };
        withPrintSettings.PrintSettings.Should().NotBeNull();
        withPrintSettings.OpaquePrintAreaExpression.Should().NotBeNull();
        state.PrintSettings.Should().BeNull();
    }
}
