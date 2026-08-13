using FluentAssertions;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class FreeXRibbonKeyTipInputSessionTests
{
    [Fact]
    public void InactiveSession_IgnoresTokensAndEscape()
    {
        var session = new FreeXRibbonKeyTipInputSession();

        session.HandleToken("H").Should().Be(FreeXRibbonKeyTipInputStep.Ignored);
        session.HandleEscape().Should().Be(FreeXRibbonKeyTipInputStep.Ignored);
        session.IsActive.Should().BeFalse();
    }

    [Fact]
    public void GenericInput_AccumulatesAcrossScopesAndCanResetWithoutLeavingMode()
    {
        var session = new FreeXRibbonKeyTipInputSession();
        session.Enter(FreeXRibbonKeyTipInputScope.Catalog);

        session.HandleToken("h").Should().Be(new FreeXRibbonKeyTipInputStep(
            FreeXRibbonKeyTipInputIntent.Route,
            FreeXRibbonKeyTipInputScope.Catalog,
            "H"));
        session.HandleToken("b").Input.Should().Be("HB");

        session.EnterScope(FreeXRibbonKeyTipInputScope.Menu);
        session.Input.Should().BeEmpty();
        session.HandleToken("s").Input.Should().Be("S");
        session.ResetInput();
        session.Scope.Should().Be(FreeXRibbonKeyTipInputScope.Menu);
        session.Input.Should().BeEmpty();
    }

    [Theory]
    [InlineData("D", FreeXRibbonKeyTipInputIntent.EnterLegacyDataFilter, FreeXRibbonLegacyKeyTipSequence.DataFilter)]
    [InlineData("E", FreeXRibbonKeyTipInputIntent.EnterLegacyEditPasteSpecial, FreeXRibbonLegacyKeyTipSequence.EditPasteSpecial)]
    public void LegacyTopLevelTokens_EnterCanonicalCommandSequences(
        string token,
        FreeXRibbonKeyTipInputIntent expectedIntent,
        FreeXRibbonLegacyKeyTipSequence expectedSequence)
    {
        var session = new FreeXRibbonKeyTipInputSession();
        session.Enter(FreeXRibbonKeyTipInputScope.TopLevel);

        var step = session.HandleToken(token);

        step.Intent.Should().Be(expectedIntent);
        session.Scope.Should().Be(FreeXRibbonKeyTipInputScope.Commands);
        session.LegacySequence.Should().Be(expectedSequence);
        session.Input.Should().BeEmpty();
    }

    [Fact]
    public void DataFilterSequence_WaitsForFirstFAndInvokesOnSecondF()
    {
        var session = BeginLegacy("D");

        session.HandleToken("F").Should().Be(new FreeXRibbonKeyTipInputStep(
            FreeXRibbonKeyTipInputIntent.WaitForContinuation,
            FreeXRibbonKeyTipInputScope.Commands,
            "F"));
        session.IsActive.Should().BeTrue();

        session.HandleToken("F").Should().Be(new FreeXRibbonKeyTipInputStep(
            FreeXRibbonKeyTipInputIntent.InvokeLegacyDataFilter,
            FreeXRibbonKeyTipInputScope.Commands,
            "FF"));
        session.IsActive.Should().BeFalse();
    }

    [Fact]
    public void EditPasteSpecialSequence_InvokesOnS()
    {
        var session = BeginLegacy("E");

        session.HandleToken("S").Should().Be(new FreeXRibbonKeyTipInputStep(
            FreeXRibbonKeyTipInputIntent.InvokeLegacyEditPasteSpecial,
            FreeXRibbonKeyTipInputScope.Commands,
            "S"));
        session.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("D", "X")]
    [InlineData("E", "F")]
    public void LegacySequences_ConsumeInvalidContinuationAndCancel(string start, string invalid)
    {
        var session = BeginLegacy(start);

        session.HandleToken(invalid).Intent.Should().Be(FreeXRibbonKeyTipInputIntent.Cancel);
        session.IsActive.Should().BeFalse();
        session.Input.Should().BeEmpty();
    }

    [Fact]
    public void EscapeAndUnsupportedTokens_CancelActiveInput()
    {
        var escape = new FreeXRibbonKeyTipInputSession();
        escape.Enter(FreeXRibbonKeyTipInputScope.TopLevel);
        escape.HandleEscape().Intent.Should().Be(FreeXRibbonKeyTipInputIntent.Cancel);
        escape.IsActive.Should().BeFalse();

        var unsupported = new FreeXRibbonKeyTipInputSession();
        unsupported.Enter(FreeXRibbonKeyTipInputScope.Commands);
        unsupported.HandleToken(null).Intent.Should().Be(FreeXRibbonKeyTipInputIntent.Cancel);
        unsupported.IsActive.Should().BeFalse();
    }

    [Fact]
    public void RendererCanDisableLegacyRecognitionForLiteralCatalogTokens()
    {
        var session = new FreeXRibbonKeyTipInputSession();
        session.Enter(FreeXRibbonKeyTipInputScope.Catalog);

        session.HandleToken("D", recognizeLegacyTopLevel: false).Should().Be(new FreeXRibbonKeyTipInputStep(
            FreeXRibbonKeyTipInputIntent.Route,
            FreeXRibbonKeyTipInputScope.Catalog,
            "D"));
        session.LegacySequence.Should().Be(FreeXRibbonLegacyKeyTipSequence.None);
    }

    [Fact]
    public void NoneScope_IsRejectedByEntryPoints()
    {
        var session = new FreeXRibbonKeyTipInputSession();

        var enter = () => session.Enter(FreeXRibbonKeyTipInputScope.None);
        enter.Should().Throw<ArgumentOutOfRangeException>();

        session.Enter(FreeXRibbonKeyTipInputScope.TopLevel);
        var change = () => session.EnterScope(FreeXRibbonKeyTipInputScope.None);
        change.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static FreeXRibbonKeyTipInputSession BeginLegacy(string token)
    {
        var session = new FreeXRibbonKeyTipInputSession();
        session.Enter(FreeXRibbonKeyTipInputScope.TopLevel);
        session.HandleToken(token);
        return session;
    }
}
