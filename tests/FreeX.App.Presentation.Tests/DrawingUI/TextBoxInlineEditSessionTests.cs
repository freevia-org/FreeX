using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class TextBoxInlineEditSessionTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void Begin_CapturesIdentityAndRollbackText()
    {
        var textBox = new TextBoxModel { Text = "Original" };
        var session = new TextBoxInlineEditSession();

        var plan = session.Begin(textBox);

        plan.Should().Be(new TextBoxInlineEditStartPlan(textBox.Id, "Original"));
        session.IsActive.Should().BeTrue();
        session.IsEditing(textBox.Id).Should().BeTrue();
        session.EditingTextBoxId.Should().Be(textBox.Id);
        session.CreateCancelPlan().Should().Be(new TextBoxInlineEditCancelPlan(textBox.Id, "Original"));
    }

    [Fact]
    public void CreateCommitPlan_UsesCapturedTextAndActiveIdentity()
    {
        var textBox = new TextBoxModel { Text = "Before" };
        var sheetId = SheetId.New();
        var session = new TextBoxInlineEditSession();
        session.Begin(textBox);

        var changed = session.CreateCommitPlan(sheetId, "After");
        changed.Should().NotBeNull();
        changed!.TextBoxId.Should().Be(textBox.Id);
        changed.Text.Should().Be("After");
        changed.TextChanged.Should().BeTrue();
        changed.Command.Should().BeOfType<SetTextBoxTextCommand>();

        var unchanged = session.CreateCommitPlan(sheetId, "Before");
        unchanged.Should().NotBeNull();
        unchanged!.TextChanged.Should().BeFalse();
        unchanged.Command.Should().BeNull();
    }

    [Fact]
    public void BeginAgain_ReplacesThePreviousEditingSnapshot()
    {
        var first = new TextBoxModel { Text = "First" };
        var second = new TextBoxModel { Text = "Second" };
        var session = new TextBoxInlineEditSession();
        session.Begin(first);

        session.Begin(second);

        session.IsEditing(first.Id).Should().BeFalse();
        var commit = session.CreateCommitPlan(SheetId.New(), "Changed");
        commit.Should().NotBeNull();
        commit!.TextBoxId.Should().Be(second.Id);
        commit.Text.Should().Be("Changed");
        commit.TextChanged.Should().BeTrue();
        commit.Command.Should().BeOfType<SetTextBoxTextCommand>();
        session.CreateCancelPlan().Should().Be(
            new TextBoxInlineEditCancelPlan(second.Id, "Second"));
    }

    [Fact]
    public void CreateCommitPlan_BuildsCommandForCapturedTextBox()
    {
        var workbook = new Workbook("Text boxes");
        var sheet = workbook.AddSheet("Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 3),
            Text = "Before"
        };
        sheet.TextBoxes.Add(textBox);
        var session = new TextBoxInlineEditSession();
        session.Begin(textBox);

        var plan = session.CreateCommitPlan(sheet.Id, "After");
        var outcome = plan!.Command!.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        textBox.Text.Should().Be("After");
    }

    [Fact]
    public void Complete_ClearsIdentityAndTransitionPlans()
    {
        var session = new TextBoxInlineEditSession();
        session.Begin(new TextBoxModel { Text = "Text" });

        session.Complete();

        session.IsActive.Should().BeFalse();
        session.EditingTextBoxId.Should().BeNull();
        session.CreateCommitPlan(SheetId.New(), "Changed").Should().BeNull();
        session.CreateCancelPlan().Should().BeNull();
    }

    [Fact]
    public void LostFocusPolicy_RequiresAnActiveSession()
    {
        var session = new TextBoxInlineEditSession();
        session.ShouldCommitLostFocus(true, false, false).Should().BeFalse();

        session.Begin(new TextBoxModel());

        session.ShouldCommitLostFocus(true, false, false).Should().BeTrue();
        session.ShouldCommitLostFocus(true, true, false).Should().BeFalse();
    }
}
