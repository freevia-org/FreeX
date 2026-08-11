using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 134 fix: with the audience screen blanked (B/W), keyboard and mouse input must
/// not silently advance the deck, fire an animation, or follow a hyperlink underneath the
/// blank overlay — only the B/W toggle and Escape may act. Also covers the sibling fix:
/// an explicit hyperlink to a HIDDEN slide must still navigate (revealed, like the H key),
/// even though normal Advance continues to skip hidden slides.
/// </summary>
public sealed class SlideShowBlankScreenGatingTests
{
    /// <summary>
    /// KeyEventArgs requires a non-null PresentationSource even though OnKeyDown never
    /// reads it (only e.Key and Keyboard.Modifiers) — a throwaway detached HwndSource
    /// satisfies the constructor without showing (or attaching to) the window under test.
    /// </summary>
    private static readonly PresentationSource DummyInputSource =
        new HwndSource(new HwndSourceParameters("SlideShowBlankScreenGatingTests") { Width = 0, Height = 0 });

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        for (var i = 1; i < slideCount; i++)
            presentation.Slides.Add(new Slide { Title = $"Slide {i + 1}" });
        return presentation;
    }

    /// <summary>
    /// Invokes the window's private OnKeyDown handler exactly as the real KeyDown routed
    /// event would — this is the same method wired via <c>KeyDown += OnKeyDown;</c> in the
    /// constructor, so the reachability path is identical to a live keypress.
    /// </summary>
    private static bool RaiseKeyDown(SlideShowWindow window, Key key)
    {
        // UIElement itself declares a protected virtual OnKeyDown(KeyEventArgs) — the
        // parameterless GetMethod(name) overload sees both that and our two-parameter
        // event handler and throws AmbiguousMatchException, so the parameter types must
        // be specified explicitly to select our handler.
        var method = typeof(SlideShowWindow).GetMethod(
            "OnKeyDown",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(object), typeof(KeyEventArgs) },
            null)
            ?? throw new InvalidOperationException("SlideShowWindow.OnKeyDown not found via reflection.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, DummyInputSource, 0, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        method.Invoke(window, new object[] { window, args });
        return args.Handled;
    }

    /// <summary>
    /// Invokes the window's private OnMouseLeftButtonDown handler exactly as the real
    /// MouseLeftButtonDown routed event would.
    /// </summary>
    private static bool RaiseMouseLeftButtonDown(SlideShowWindow window)
    {
        // Same ambiguity as OnKeyDown: UIElement declares a protected virtual
        // OnMouseLeftButtonDown(MouseButtonEventArgs) alongside our event handler.
        var method = typeof(SlideShowWindow).GetMethod(
            "OnMouseLeftButtonDown",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(object), typeof(MouseButtonEventArgs) },
            null)
            ?? throw new InvalidOperationException("SlideShowWindow.OnMouseLeftButtonDown not found via reflection.");
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseDownEvent
        };
        method.Invoke(window, new object[] { window, args });
        return args.Handled;
    }

    // ── (a) HIGH: blanked screen must gate navigation/activation input ────────────

    [StaFact]
    public void WpfHost_BlankScreen_ArrowKeyDoesNotAdvanceTheDeck()
    {
        var presentation = MakePresentation(3);
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            window.SetScreenMode(SlideShowScreenMode.Black);

            RaiseKeyDown(window, Key.Right);

            window.Controller.CurrentSlideIndex.Should().Be(0);
            window.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_BlankScreen_ClickDoesNotAdvanceTheDeck()
    {
        var presentation = MakePresentation(3);
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            window.SetScreenMode(SlideShowScreenMode.White);

            var handled = RaiseMouseLeftButtonDown(window);

            handled.Should().BeTrue();
            window.Controller.CurrentSlideIndex.Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_BlankScreen_HKeyDoesNotRevealHiddenSlide()
    {
        var presentation = MakePresentation(3);
        presentation.Slides[1].IsHidden = true;
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            window.SetScreenMode(SlideShowScreenMode.Black);

            RaiseKeyDown(window, Key.H);

            window.RevealedHiddenSlideForTest.Should().BeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_BlankScreen_BKeyStillTogglesScreenModeBackToNormal()
    {
        var presentation = MakePresentation(3);
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            window.SetScreenMode(SlideShowScreenMode.Black);

            var handled = RaiseKeyDown(window, Key.B);

            handled.Should().BeTrue();
            window.ScreenMode.Should().Be(SlideShowScreenMode.Normal);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_BlankScreen_EscapeStillClosesTheShow()
    {
        var presentation = MakePresentation(3);
        var window = new SlideShowWindow(presentation, startIndex: 0);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.SetScreenMode(SlideShowScreenMode.Black);

            var handled = RaiseKeyDown(window, Key.Escape);

            handled.Should().BeTrue();
            closed.Should().BeTrue();
        }
        finally
        {
            if (!closed) window.Close();
        }
    }

    // ── Sibling no-regression: unblanked playback keeps responding ────────────────

    [StaFact]
    public void WpfHost_NormalScreen_ArrowKeyStillAdvances()
    {
        var presentation = MakePresentation(3);
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            RaiseKeyDown(window, Key.Right);

            window.Controller.CurrentSlideIndex.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_NormalScreen_ClickStillAdvances()
    {
        var presentation = MakePresentation(3);
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            RaiseMouseLeftButtonDown(window);

            window.Controller.CurrentSlideIndex.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    // ── (b) MED: hyperlink to a hidden slide must still navigate ──────────────────

    [StaFact]
    public void WpfHost_HyperlinkToHiddenSlide_RevealsItWithoutMovingThePlaybackIndex()
    {
        var presentation = MakePresentation(3);
        presentation.Slides[1].IsHidden = true;
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            var hyperlink = new Hyperlink { TargetSlideId = presentation.Slides[1].Id };

            window.ActivateHyperlink(hyperlink);

            // The playback route excludes the hidden slide (normal Advance must keep
            // skipping it), so the controller's own index stays put — same contract as
            // the H-key reveal — but the hidden slide is now what's actually displayed.
            window.Controller.CurrentSlideIndex.Should().Be(0);
            window.RevealedHiddenSlideForTest.Should().BeSameAs(presentation.Slides[1]);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_HyperlinkToVisibleSlide_StillNavigatesNormally()
    {
        // Sibling no-regression: the ordinary (non-hidden) hyperlink jump path must be untouched.
        var presentation = MakePresentation(3);
        var window = new SlideShowWindow(presentation, startIndex: 0);
        try
        {
            var hyperlink = new Hyperlink { TargetSlideId = presentation.Slides[2].Id };

            window.ActivateHyperlink(hyperlink);

            window.Controller.CurrentSlideIndex.Should().Be(2);
            window.RevealedHiddenSlideForTest.Should().BeNull();
        }
        finally
        {
            window.Close();
        }
    }
}
