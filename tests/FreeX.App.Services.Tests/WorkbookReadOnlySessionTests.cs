using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookReadOnlySessionTests
{
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "reserve")]
    public void PlanOpen_RestrictedWorkbookRequestsPrompt(bool readOnlyRecommended, string? reservationPassword)
    {
        var workbook = new Workbook("Budget.xlsx")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                ReadOnlyRecommended = readOnlyRecommended,
                ReservationPassword = reservationPassword,
            },
        };

        var plan = new WorkbookReadOnlySession().PlanOpen(workbook);

        plan.ShouldPrompt.Should().BeTrue();
        plan.WorkbookName.Should().Be("Budget.xlsx");
        plan.PromptKind.Should().Be(reservationPassword is null
            ? WorkbookReadOnlyPromptKind.ReadOnlyRecommended
            : WorkbookReadOnlyPromptKind.ReservationPassword);
    }

    [Theory]
    [InlineData("secret", false, false)]
    [InlineData("wrong", true, true)]
    [InlineData(null, true, false)]
    public void ApplyReservationPassword_OwnsVerificationAndReadOnlyFallback(
        string? providedPassword,
        bool expectedReadOnly,
        bool expectedIncorrectNotice)
    {
        var workbook = new Workbook("Budget.xlsx")
        {
            FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" },
        };
        var session = new WorkbookReadOnlySession();
        session.PlanOpen(workbook).PromptKind.Should().Be(WorkbookReadOnlyPromptKind.ReservationPassword);

        var decision = session.ApplyReservationPassword(providedPassword);

        decision.IsReadOnly.Should().Be(expectedReadOnly);
        decision.ShouldShowIncorrectPasswordNotice.Should().Be(expectedIncorrectNotice);
        session.IsReadOnly.Should().Be(expectedReadOnly);
    }

    [Fact]
    public void PlanOpen_NormalWorkbookResetsPreviousReadOnlyDecision()
    {
        var session = new WorkbookReadOnlySession();
        session.ApplyPromptDecision(openReadOnly: true);

        var plan = session.PlanOpen(new Workbook("Book1.xlsx"));

        plan.ShouldPrompt.Should().BeFalse();
        session.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void ResolveExistingSaveTarget_ReadOnlySessionWithholdsTargetWithoutResolvingIt()
    {
        var session = new WorkbookReadOnlySession();
        session.ApplyPromptDecision(openReadOnly: true);
        var resolverCalls = 0;

        var target = session.ResolveExistingSaveTarget(() =>
        {
            resolverCalls++;
            return new FileSaveTarget("Budget.fxl", new TestFileAdapter(extension: ".fxl"));
        });

        target.Should().BeNull();
        resolverCalls.Should().Be(0);
    }

    [Fact]
    public void ResolveExistingSaveTarget_EditableSessionReturnsResolvedTarget()
    {
        var session = new WorkbookReadOnlySession();
        var expected = new FileSaveTarget("Budget.fxl", new TestFileAdapter(extension: ".fxl"));

        session.ResolveExistingSaveTarget(() => expected).Should().BeSameAs(expected);
    }
}
