using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class WorkbookWindowRegistryCoreTests
{
    [Fact]
    public void Register_KeepsStrongReferencesInOrderAndIgnoresDuplicateRegistration()
    {
        var core = CreateCore();
        var first = new TestWindow();
        var second = new TestWindow();

        core.Register(first).Should().BeTrue();
        core.Register(second).Should().BeTrue();
        core.Register(first).Should().BeFalse();

        core.HasWindows.Should().BeTrue();
        core.Count.Should().Be(2);
        core.Windows.Should().Equal(first, second);
        core.IndexOf(second).Should().Be(1);
    }

    [Fact]
    public void RegistrationAndRemoval_NumberWindowsWithinEachDocument()
    {
        var core = CreateCore();
        var documentA = NewDocumentId();
        var firstA = new TestWindow(documentA);
        var onlyB = new TestWindow(NewDocumentId());
        var secondA = new TestWindow(documentA);

        core.Register(firstA);
        core.Register(onlyB);
        core.Register(secondA);

        firstA.Suffix.Should().Be(":1");
        secondA.Suffix.Should().Be(":2");
        onlyB.Suffix.Should().BeEmpty();

        core.Unregister(firstA).Should().BeTrue();
        core.Unregister(firstA).Should().BeFalse();
        secondA.Suffix.Should().BeEmpty();
        core.Windows.Should().Equal(onlyB, secondA);
    }

    [Fact]
    public void RefreshWindowNumbering_UsesCurrentDocumentIdentityAfterAWindowChangesDocument()
    {
        var core = CreateCore();
        var document = NewDocumentId();
        var first = new TestWindow(document);
        var second = new TestWindow(document);
        core.Register(first);
        core.Register(second);

        second.DocumentId = NewDocumentId();
        core.RefreshWindowNumbering();

        first.Suffix.Should().BeEmpty();
        second.Suffix.Should().BeEmpty();
    }

    [Fact]
    public void VisibleWindowsAndCycling_UseCurrentVisibilityAndWrapBothDirections()
    {
        var core = CreateCore();
        var first = new TestWindow();
        var hidden = new TestWindow { IsVisible = false };
        var third = new TestWindow();
        core.Register(first);
        core.Register(hidden);
        core.Register(third);

        core.VisibleWindows.Should().Equal(first, third);
        core.NextWindowTarget(first, WorkbookWindowCycleDirection.Forward).Should().BeSameAs(third);
        core.NextWindowTarget(third, WorkbookWindowCycleDirection.Forward).Should().BeSameAs(first);
        core.NextWindowTarget(first, WorkbookWindowCycleDirection.Backward).Should().BeSameAs(third);
        core.NextWindowTarget(hidden, WorkbookWindowCycleDirection.Forward).Should().BeNull();

        third.IsVisible = false;
        core.NextWindowTarget(first, WorkbookWindowCycleDirection.Forward).Should().BeNull();
    }

    [Fact]
    public void PlanVisibleArrangement_LeavesHiddenWindowsUntouchedAndPreservesRegistrationOrder()
    {
        var core = CreateCore();
        var first = new TestWindow();
        var hidden = new TestWindow { IsVisible = false };
        var third = new TestWindow();
        core.Register(first);
        core.Register(hidden);
        core.Register(third);

        var plan = core.PlanVisibleArrangement(
            ShellWindowArrangement.Horizontal,
            workAreaWidth: 900,
            workAreaHeight: 600);

        plan.Select(target => target.Window).Should().Equal(first, third);
        plan.Select(target => target.Bounds).Should().Equal(
            new ShellRect(0, 0, 900, 300),
            new ShellRect(0, 300, 900, 300));
        hidden.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void PlanVisibleArrangement_AppliesOptionalDocumentScopeAfterVisibilityFiltering()
    {
        var core = CreateCore();
        var document = NewDocumentId();
        var first = new TestWindow(document);
        var hidden = new TestWindow(document) { IsVisible = false };
        var otherDocument = new TestWindow(NewDocumentId());
        var second = new TestWindow(document);
        core.Register(first);
        core.Register(hidden);
        core.Register(otherDocument);
        core.Register(second);

        var plan = core.PlanVisibleArrangement(
            ShellWindowArrangement.Vertical,
            workAreaWidth: 800,
            workAreaHeight: 600,
            window => window.DocumentId == document);

        plan.Select(target => target.Window).Should().Equal(first, second);
        plan.Select(target => target.Bounds).Should().Equal(
            new ShellRect(0, 0, 400, 600),
            new ShellRect(400, 0, 400, 600));
    }

    [Fact]
    public void SwitchToWindow_InvokesOnlyTheNativeActivationCallbackForTheTarget()
    {
        var core = CreateCore();
        var first = new TestWindow();
        var second = new TestWindow();
        core.Register(first);
        core.Register(second);

        core.SwitchToWindow(
                first,
                WorkbookWindowCycleDirection.Forward,
                target => target.ActivationCount++)
            .Should().BeTrue();

        first.ActivationCount.Should().Be(0);
        second.ActivationCount.Should().Be(1);
    }

    [Fact]
    public void NotificationTargets_ApplyAllThreeAudiencePoliciesInRegistrationOrder()
    {
        var core = CreateCore();
        var documentA = NewDocumentId();
        var origin = new TestWindow(documentA);
        var sibling = new TestWindow(documentA);
        var otherDocument = new TestWindow(NewDocumentId());
        core.Register(origin);
        core.Register(otherDocument);
        core.Register(sibling);

        core.NotificationTargets(origin, WorkbookWindowNotificationAudience.SameDocument)
            .Should().Equal(origin, sibling);
        core.NotificationTargets(origin, WorkbookWindowNotificationAudience.SameDocumentExceptOrigin)
            .Should().Equal(sibling);
        core.NotificationTargets(origin, WorkbookWindowNotificationAudience.AllExceptOrigin)
            .Should().Equal(otherDocument, sibling);
    }

    [Fact]
    public void Notify_SnapshotsTargetsBeforeCallingRendererCode()
    {
        var core = CreateCore();
        var document = NewDocumentId();
        var origin = new TestWindow(document);
        var firstTarget = new TestWindow(document);
        var secondTarget = new TestWindow(document);
        core.Register(origin);
        core.Register(firstTarget);
        core.Register(secondTarget);
        var notified = new List<TestWindow>();

        core.Notify(origin, WorkbookWindowNotificationAudience.SameDocumentExceptOrigin, target =>
        {
            notified.Add(target);
            core.Unregister(target);
        });

        notified.Should().Equal(firstTarget, secondTarget);
        core.Windows.Should().Equal(origin);
    }

    [Fact]
    public void DocumentQueries_TrackRegisteredSiblingsAndDocumentPresence()
    {
        var core = CreateCore();
        var document = NewDocumentId();
        var first = new TestWindow(document);
        var second = new TestWindow(document);
        var other = new TestWindow(NewDocumentId());
        core.Register(first);
        core.Register(other);

        core.HasOtherWindowForDocument(first).Should().BeFalse();
        core.HasWindowForDocument(document).Should().BeTrue();

        core.Register(second);
        core.HasOtherWindowForDocument(first).Should().BeTrue();
        core.HasOtherWindowForDocument(second).Should().BeTrue();
        core.HasOtherWindowForDocument(other).Should().BeFalse();
    }

    [Fact]
    public void UnregisteredOrigin_NotifiesEveryRegisteredWindowForItsDocument()
    {
        var core = CreateCore();
        var document = NewDocumentId();
        var unregisteredOrigin = new TestWindow(document);
        var first = new TestWindow(document);
        var second = new TestWindow(document);
        core.Register(first);
        core.Register(second);

        core.NotificationTargets(
                unregisteredOrigin,
                WorkbookWindowNotificationAudience.SameDocumentExceptOrigin)
            .Should().Equal(first, second);
    }

    private static WorkbookWindowRegistryCore<TestWindow> CreateCore() =>
        new(
            static window => window.DocumentId,
            static window => window.IsVisible,
            static (window, suffix) => window.Suffix = suffix);

    private static WorkbookId NewDocumentId() => new(Guid.NewGuid());

    private sealed class TestWindow
    {
        public TestWindow()
            : this(NewDocumentId())
        {
        }

        public TestWindow(WorkbookId documentId)
        {
            DocumentId = documentId;
        }

        public WorkbookId DocumentId { get; set; }

        public bool IsVisible { get; set; } = true;

        public string Suffix { get; set; } = string.Empty;

        public int ActivationCount { get; set; }
    }
}
