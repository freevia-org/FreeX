using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void TextBoxInlineEditing_RoutesLifecycleAndPolicyThroughSharedSession()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.TextBoxInlineEditing.cs");

        source.Should().Contain("TextBoxInlineEditSession _textBoxInlineEditSession = new();");
        source.Should().Contain("_textBoxInlineEditSession.Begin(textBox)");
        source.Should().Contain("_textBoxInlineEditSession.CreateCommitPlan(");
        source.Should().Contain("_textBoxInlineEditSession.CreateCancelPlan()");
        source.Should().Contain("_textBoxInlineEditSession.ShouldCommitLostFocus(");
        source.Should().Contain("TextBoxInlineEditSession.CommitCommandTitle");
        source.Should().NotContain("TextBoxInlineEditPlanner.CreateCommitPlan(");
        source.Should().NotContain("TextBoxInlineEditPlanner.PlanKeyDown(");
        source.Should().NotContain("TextBoxInlineEditPlanner.ShouldCommitLostFocus(");
        source.Should().NotContain("new SetTextBoxTextCommand(");
        source.Should().NotContain("e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None");
        source.Should().NotContain("e.Key is Key.Enter or Key.Return && Keyboard.Modifiers == ModifierKeys.None");
        source.Should().NotContain("_textBoxInlineEditingId");
        source.Should().NotContain("_textBoxInlineOriginalText");
    }

    [Fact]
    public void SelectionPaneDialog_RoutesKeyboardPolicyThroughSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SelectionPaneDialog.State.cs");

        source.Should().Contain("SelectionPanePlanner.PlanKeyboardAction(");
        source.Should().Contain("SelectionPaneKeyboardAction.MoveUp");
        source.Should().Contain("SelectionPaneKeyboardAction.FocusRename");
        source.Should().Contain("SelectionPaneKeyboardAction.ToggleVisibility");
        source.Should().NotContain("if (e.Key == Key.F2)");
        source.Should().NotContain("if (e.Key == Key.Space)");
        source.Should().NotContain("if (e.Key == Key.Up)");
    }

    [Fact]
    public void DrawShapeArrowheads_PassesNoFlipToLineEndpoints_BecauseDrawingContextAlreadyFlips()
    {
        // Regression guard for the double-flip arrowhead bug:
        // PushDrawingObjectTransform pushes a ScaleTransform onto the DrawingContext, which
        // already mirrors everything drawn into it (including arrowheads). LineEndpoints must
        // therefore receive flipHorizontal: false / flipVertical: false so the outer dc transform
        // is the ONLY flip applied. If these are ever changed back to the local flipHorizontal /
        // flipVertical variables, arrowheads will land at the wrong corners and point the wrong
        // way on flipped connectors.
        var source = DialogSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.cs");

        // The call in DrawShapeArrowheads must pass literal false for both flip arguments.
        source.Should().Contain("flipHorizontal: false, flipVertical: false, shape.Kind)",
            because: "DrawShapeArrowheads must not pre-flip endpoints — PushDrawingObjectTransform's ScaleTransform already handles the flip");

        // The outer transform push that makes passing false correct must still exist.
        source.Should().Contain("PushDrawingObjectTransform(",
            because: "the dc-level flip transform that makes passing false correct must remain in place");
    }
}
