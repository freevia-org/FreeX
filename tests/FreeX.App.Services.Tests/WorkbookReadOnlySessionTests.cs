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
    [InlineData(
        WorkbookReadOnlyRecommendationChoice.OpenReadOnly,
        WorkbookReadOnlyOpenOutcomeKind.ReadOnlyRecommendedAccepted,
        true)]
    [InlineData(
        WorkbookReadOnlyRecommendationChoice.OpenEditable,
        WorkbookReadOnlyOpenOutcomeKind.ReadOnlyRecommendedDeclined,
        false)]
    public void RunOpen_ReadOnlyRecommendation_OwnsPromptDecisionAndStateApplication(
        WorkbookReadOnlyRecommendationChoice choice,
        WorkbookReadOnlyOpenOutcomeKind expectedKind,
        bool expectedReadOnly)
    {
        var workbook = CreateWorkbook(readOnlyRecommended: true);
        var port = new RecordingPromptPort { RecommendationChoice = choice };
        var session = new WorkbookReadOnlySession();

        var outcome = session.RunOpen(workbook, port);

        outcome.Plan.Should().Be(new WorkbookReadOnlyOpenPlan(
            WorkbookReadOnlyPromptKind.ReadOnlyRecommended,
            "Budget.xlsx"));
        outcome.Kind.Should().Be(expectedKind);
        outcome.IsReadOnly.Should().Be(expectedReadOnly);
        session.IsReadOnly.Should().Be(expectedReadOnly);
        port.Calls.Should().Equal("recommendation:Budget.xlsx");
    }

    public static TheoryData<
        WorkbookReservationPasswordResponse,
        WorkbookReadOnlyOpenOutcomeKind,
        bool,
        string[]> ReservationPasswordOutcomes => new()
    {
        {
            WorkbookReservationPasswordResponse.Accepted("secret"),
            WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordAccepted,
            false,
            ["password:Budget.xlsx"]
        },
        {
            WorkbookReservationPasswordResponse.Accepted("wrong"),
            WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordRejected,
            true,
            ["password:Budget.xlsx", "incorrect:Budget.xlsx"]
        },
        {
            WorkbookReservationPasswordResponse.Accepted(string.Empty),
            WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordRejected,
            true,
            ["password:Budget.xlsx", "incorrect:Budget.xlsx"]
        },
        {
            WorkbookReservationPasswordResponse.Cancelled,
            WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordCancelled,
            true,
            ["password:Budget.xlsx"]
        },
    };

    [Theory]
    [MemberData(nameof(ReservationPasswordOutcomes))]
    public void RunOpen_ReservationPassword_OwnsAcceptanceCancellationNoticeOrderAndState(
        WorkbookReservationPasswordResponse response,
        WorkbookReadOnlyOpenOutcomeKind expectedKind,
        bool expectedReadOnly,
        string[] expectedCalls)
    {
        var workbook = CreateWorkbook(reservationPassword: "secret");
        var port = new RecordingPromptPort { PasswordResponse = response };
        var session = new WorkbookReadOnlySession();
        port.ReadOnlyState = () => session.IsReadOnly;

        var outcome = session.RunOpen(workbook, port);

        outcome.Plan.PromptKind.Should().Be(WorkbookReadOnlyPromptKind.ReservationPassword);
        outcome.Kind.Should().Be(expectedKind);
        outcome.IsReadOnly.Should().Be(expectedReadOnly);
        session.IsReadOnly.Should().Be(expectedReadOnly);
        port.Calls.Should().Equal(expectedCalls);
        port.ReadOnlyWhenIncorrectNoticeShown.Should().Be(
            expectedCalls.Contains("incorrect:Budget.xlsx") ? true : null,
            "the shared transition must apply the read-only fallback before asking the renderer to show the warning");
    }

    [Fact]
    public void RunOpen_ReservationPasswordTakesPrecedenceOverRecommendation()
    {
        var workbook = CreateWorkbook(readOnlyRecommended: true, reservationPassword: "secret");
        var port = new RecordingPromptPort
        {
            RecommendationChoice = WorkbookReadOnlyRecommendationChoice.OpenReadOnly,
            PasswordResponse = WorkbookReservationPasswordResponse.Accepted("secret"),
        };

        var outcome = new WorkbookReadOnlySession().RunOpen(workbook, port);

        outcome.Kind.Should().Be(WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordAccepted);
        port.Calls.Should().Equal("password:Budget.xlsx");
    }

    [Fact]
    public void RunOpen_NormalWorkbook_ResetsStateWithoutInvokingPromptPort()
    {
        var session = new WorkbookReadOnlySession();
        var port = new RecordingPromptPort
        {
            RecommendationChoice = WorkbookReadOnlyRecommendationChoice.OpenReadOnly,
        };
        session.RunOpen(CreateWorkbook(readOnlyRecommended: true), port).IsReadOnly.Should().BeTrue();
        port.Calls.Clear();

        var outcome = session.RunOpen(CreateWorkbook(), port);

        outcome.Kind.Should().Be(WorkbookReadOnlyOpenOutcomeKind.Editable);
        outcome.IsReadOnly.Should().BeFalse();
        session.IsReadOnly.Should().BeFalse();
        port.Calls.Should().BeEmpty();
    }

    [Fact]
    public void RunOpen_NullPromptPort_IsRejectedBeforeSessionStateChanges()
    {
        var session = new WorkbookReadOnlySession();
        session.ApplyPromptDecision(openReadOnly: true);

        var action = () => session.RunOpen(CreateWorkbook(), null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("promptPort");
        session.IsReadOnly.Should().BeTrue();
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

    [Fact]
    public void ReservationPasswordResponse_AcceptedRejectsNullPassword()
    {
        var action = () => WorkbookReservationPasswordResponse.Accepted(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("password");
    }

    [Theory]
    [InlineData(null, false, "")]
    [InlineData("", true, "")]
    [InlineData("secret", true, "secret")]
    public void ReservationPasswordResponse_FromPromptResult_PreservesCancellationAndAcceptance(
        string? password,
        bool expectedAccepted,
        string expectedPassword)
    {
        var response = WorkbookReservationPasswordResponse.FromPromptResult(password);

        response.IsAccepted.Should().Be(expectedAccepted);
        response.Password.Should().Be(expectedPassword);
    }

    private static Workbook CreateWorkbook(
        bool readOnlyRecommended = false,
        string? reservationPassword = null) =>
        new("Budget.xlsx")
        {
            FileSharing = readOnlyRecommended || reservationPassword is not null
                ? new WorkbookFileSharingModel
                {
                    ReadOnlyRecommended = readOnlyRecommended,
                    ReservationPassword = reservationPassword,
                }
                : null,
        };

    private sealed class RecordingPromptPort : IWorkbookReadOnlyOpenPromptPort
    {
        public WorkbookReadOnlyRecommendationChoice RecommendationChoice { get; init; }

        public WorkbookReservationPasswordResponse PasswordResponse { get; init; } =
            WorkbookReservationPasswordResponse.Cancelled;

        public List<string> Calls { get; } = [];

        public Func<bool>? ReadOnlyState { get; set; }

        public bool? ReadOnlyWhenIncorrectNoticeShown { get; private set; }

        public WorkbookReadOnlyRecommendationChoice PromptReadOnlyRecommended(WorkbookReadOnlyOpenPlan plan)
        {
            Calls.Add($"recommendation:{plan.WorkbookName}");
            return RecommendationChoice;
        }

        public WorkbookReservationPasswordResponse PromptReservationPassword(WorkbookReadOnlyOpenPlan plan)
        {
            Calls.Add($"password:{plan.WorkbookName}");
            return PasswordResponse;
        }

        public void ShowIncorrectReservationPasswordNotice(WorkbookReadOnlyOpenPlan plan)
        {
            ReadOnlyWhenIncorrectNoticeShown = ReadOnlyState?.Invoke();
            Calls.Add($"incorrect:{plan.WorkbookName}");
        }
    }
}
