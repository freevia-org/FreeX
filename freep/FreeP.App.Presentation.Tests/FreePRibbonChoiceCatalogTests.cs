using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePRibbonChoiceCatalogTests
{
    [Fact]
    public void TextChoiceParsersConvergeForStableTokensAndVisibleLabels()
    {
        foreach (var choice in FreePRibbonChoiceCatalog.TextAutoFitChoices)
        {
            TextAutoFitOptionParser.TryParse(choice.Token, out var fromToken).Should().BeTrue();
            TextAutoFitOptionParser.TryParse(choice.Label, out var fromLabel).Should().BeTrue();
            fromToken.Should().Be(choice.Descriptor);
            fromLabel.Should().Be(choice.Descriptor);
        }

        foreach (var choice in FreePRibbonChoiceCatalog.TextVerticalTypeChoices)
        {
            TextVerticalTypeOptionParser.TryParse(choice.Token, out var fromToken).Should().BeTrue();
            TextVerticalTypeOptionParser.TryParse(choice.Label, out var fromLabel).Should().BeTrue();
            fromToken.Should().Be(choice.Descriptor);
            fromLabel.Should().Be(choice.Descriptor);
        }

        foreach (var choice in FreePRibbonChoiceCatalog.TextColumnCountChoices)
        {
            TextColumnCountOptionParser.TryParse(choice.Token, out var fromToken).Should().BeTrue();
            TextColumnCountOptionParser.TryParse(choice.Label, out var fromLabel).Should().BeTrue();
            fromToken.Should().Be(choice.Descriptor);
            fromLabel.Should().Be(choice.Descriptor);
        }

        foreach (var choice in FreePRibbonChoiceCatalog.TextColumnSpacingChoices)
        {
            TextColumnSpacingOptionParser.TryParse(choice.Token, out var fromToken).Should().BeTrue();
            TextColumnSpacingOptionParser.TryParse(choice.Label, out var fromLabel).Should().BeTrue();
            fromToken.Should().Be(choice.Descriptor);
            fromLabel.Should().Be(choice.Descriptor);
        }
    }

    [Fact]
    public void TableChoiceParsersConvergeForStableTokensAndVisibleLabels()
    {
        foreach (var choice in FreePRibbonChoiceCatalog.TableCellBorderChoices)
        {
            TableCellBorderOptionParser.TryParse(choice.Token, out var tokenSide, out var tokenOutline)
                .Should().BeTrue();
            TableCellBorderOptionParser.TryParse(choice.Label, out var labelSide, out var labelOutline)
                .Should().BeTrue();

            tokenSide.Should().Be(choice.Descriptor.Side);
            labelSide.Should().Be(choice.Descriptor.Side);
            tokenOutline.Should().BeEquivalentTo(choice.Descriptor.Outline);
            labelOutline.Should().BeEquivalentTo(choice.Descriptor.Outline);
        }

        foreach (var choice in FreePRibbonChoiceCatalog.TableCellInsetChoices)
        {
            TableCellInsetOptionParser.TryParse(choice.Token, out var tokenSide, out var tokenInset)
                .Should().BeTrue();
            TableCellInsetOptionParser.TryParse(choice.Label, out var labelSide, out var labelInset)
                .Should().BeTrue();

            tokenSide.Should().Be(choice.Descriptor.Side);
            labelSide.Should().Be(choice.Descriptor.Side);
            tokenInset.Should().Be(choice.Descriptor.InsetPt);
            labelInset.Should().Be(choice.Descriptor.InsetPt);
        }

        foreach (var choice in FreePRibbonChoiceCatalog.TableRowHeightChoices)
        {
            TableRowHeightOptionParser.TryParse(choice.Token, out var fromToken).Should().BeTrue();
            TableRowHeightOptionParser.TryParse(choice.Label, out var fromLabel).Should().BeTrue();
            fromToken.Should().Be(choice.Descriptor);
            fromLabel.Should().Be(choice.Descriptor);
        }
    }

    [Fact]
    public void ParsersAcceptTypedDescriptorsWithoutStringProtocol()
    {
        TextAutoFitOptionParser.TryParse(TextAutoFitKind.Shape, out var autoFit).Should().BeTrue();
        autoFit.Should().Be(TextAutoFitKind.Shape);

        TextVerticalTypeOptionParser.TryParse(TextVerticalType.Vertical270, out var verticalType)
            .Should().BeTrue();
        verticalType.Should().Be(TextVerticalType.Vertical270);

        var border = new FreePRibbonTableCellBorderChoiceDescriptor(
            TableCellBorderSide.Top,
            ShapeOutline.None.Instance);
        TableCellBorderOptionParser.TryParse(border, out var borderSide, out var outline).Should().BeTrue();
        borderSide.Should().Be(TableCellBorderSide.Top);
        outline.Should().BeSameAs(ShapeOutline.None.Instance);

        var inset = new FreePRibbonTableCellInsetChoiceDescriptor(TableCellInsetSide.Bottom, 5.5);
        TableCellInsetOptionParser.TryParse(inset, out var insetSide, out var insetPt).Should().BeTrue();
        insetSide.Should().Be(TableCellInsetSide.Bottom);
        insetPt.Should().Be(5.5);
    }

    [Fact]
    public void TimingChoiceParsersConvergeForStableTokensAndLegacyLabels()
    {
        foreach (var choice in FreePRibbonChoiceCatalog.TransitionDurationChoices)
        {
            PresentationTransitionCommandPlanner.TryParseSeconds(choice.Label, false, out var fromLabel)
                .Should().BeTrue();
            FreePRibbonChoiceCatalog.TryResolve(
                    choice.Token,
                    FreePRibbonChoiceCatalog.TransitionDurationChoices,
                    out var fromToken)
                .Should().BeTrue();
            fromLabel.Should().Be(choice.Descriptor);
            fromToken.Should().Be(choice.Descriptor);
        }

        foreach (var choice in FreePRibbonChoiceCatalog.AnimationTriggerChoices)
        {
            PresentationAnimationCommandPlanner.TryParseTrigger(choice.Token, out var fromToken).Should().BeTrue();
            PresentationAnimationCommandPlanner.TryParseTrigger(choice.Label, out var fromLabel).Should().BeTrue();
            fromToken.Should().Be(choice.Descriptor);
            fromLabel.Should().Be(choice.Descriptor);
        }
    }
}
