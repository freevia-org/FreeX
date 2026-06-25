namespace FreeP.App.Host;

/// <summary>
/// FreeP's minimal PowerPoint-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX and FreeW, proving the ribbon library is app-neutral.
///
/// Tabs: Home, Insert (Wave 3), Transitions, Animations, Slide Show (Wave 4C).
/// </summary>
internal static class FreePRibbon
{
    public static RibbonDefinition Build()
    {
        return new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("slides", "Slides", "S", 100, g =>
                {
                    // New Slide is the hero; the rest are compact stubs, mirroring PowerPoint's Slides group.
                    g.Large("freep.new-slide", "New Slide", RibbonCommandIconKind.Insert, "N");
                    g.Medium("freep.duplicate-slide", "Duplicate Slide", RibbonCommandIconKind.Copy, "D");
                    g.Medium("freep.delete-slide", "Delete Slide", RibbonCommandIconKind.Delete, "X");
                    g.Medium("freep.layout", "Layout", RibbonCommandIconKind.Grid, "L");
                });
                tab.Group("clipboard", "Clipboard", "C", 90, g =>
                {
                    g.Large("freep.paste", "Paste", RibbonCommandIconKind.Paste, "V");
                    g.Medium("freep.cut", "Cut", RibbonCommandIconKind.Cut, "T");
                    g.Medium("freep.copy", "Copy", RibbonCommandIconKind.Copy, "C");
                });
                tab.Group("font", "Font", "F", 80, g =>
                {
                    g.ComboBox("freep.font-family", "Font", c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Segoe UI", "Georgia", "Verdana" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 140
                    });
                    g.IconToggle("freep.bold", "Bold", RibbonCommandIconKind.Bold, "1");
                    g.IconToggle("freep.italic", "Italic", RibbonCommandIconKind.Italic, "2");
                    g.IconToggle("freep.underline", "Underline", RibbonCommandIconKind.Underline, "3");
                });
            })
            .Tab("insert", "Insert", "N", tab =>
            {
                tab.Group("text", "Text", "T", 100, g =>
                {
                    g.Large("freep.text-box", "Text Box", RibbonCommandIconKind.TextBox, "X");
                });
                tab.Group("illustrations", "Illustrations", "I", 90, g =>
                {
                    g.Large("freep.picture", "Picture", RibbonCommandIconKind.Picture, "P");
                    g.Medium("freep.shape-rectangle", "Rectangle", RibbonCommandIconKind.Rectangle, "R");
                    g.Medium("freep.shape-ellipse", "Ellipse", RibbonCommandIconKind.Ellipse, "E");
                });
            })
            // ── Wave 4C: Transitions tab ───────────────────────────────────────────────
            .Tab("transitions", "Transitions", "K", tab =>
            {
                // "Transition to This Slide" group — gallery of transition kinds via Medium buttons.
                tab.Group("transition-gallery", "Transition to This Slide", "T", 100, g =>
                {
                    g.Medium("freep.transition.none",     "None",     RibbonCommandIconKind.Clear,   "0");
                    g.Medium("freep.transition.fade",     "Fade",     RibbonCommandIconKind.Effects, "F");
                    g.Medium("freep.transition.push",     "Push",     RibbonCommandIconKind.ArrowRight, "U");
                    g.Medium("freep.transition.wipe",     "Wipe",     RibbonCommandIconKind.ArrowLeft,  "W");
                    g.Medium("freep.transition.split",    "Split",    RibbonCommandIconKind.ArrowLeftRight, "S");
                    g.Medium("freep.transition.cut",      "Cut",      RibbonCommandIconKind.Flash,   "C");
                    g.Medium("freep.transition.cover",    "Cover",    RibbonCommandIconKind.Page,    "V");
                    g.Medium("freep.transition.uncover",  "Uncover",  RibbonCommandIconKind.Expand,  "N");
                    g.Medium("freep.transition.blinds",   "Blinds",   RibbonCommandIconKind.View,    "B");
                    g.Medium("freep.transition.dissolve", "Dissolve", RibbonCommandIconKind.Color,   "D");
                    g.Medium("freep.transition.zoom",     "Zoom",     RibbonCommandIconKind.Zoom,    "Z");
                    g.Medium("freep.transition.wheel",    "Wheel",    RibbonCommandIconKind.Rotate,  "H");
                });

                // Timing group — duration, advance options, apply to all.
                tab.Group("transition-timing", "Timing", "I", 90, g =>
                {
                    g.ComboBox("freep.transition.duration", "Duration", c => c with
                    {
                        Items = new[] { "0.50s", "0.75s", "1.00s", "1.50s", "2.00s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.MediumToggle("freep.transition.advance-on-click", "On Mouse Click",
                        RibbonCommandIconKind.Next, "M");
                    g.ComboBox("freep.transition.advance-after", "After", c => c with
                    {
                        Items = new[] { "(none)", "1s", "2s", "3s", "5s", "10s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.transition.apply-all", "Apply To All",
                        RibbonCommandIconKind.Refresh, "A");
                });

                // Slide Show buttons live here for quick access from the Transitions tab.
                tab.Group("slideshow-from-transitions", "Slide Show", "L", 80, g =>
                {
                    g.Large("freep.slideshow.from-beginning",     "From Beginning",     RibbonCommandIconKind.Next,     "B");
                    g.Large("freep.slideshow.from-current-slide", "From Current Slide", RibbonCommandIconKind.Previous, "C");
                });
            })
            // ── Wave 4C: Animations tab ───────────────────────────────────────────────
            .Tab("animations", "Animations", "A", tab =>
            {
                // "Animation" group — Entrance, Emphasis, Exit effect buttons for selected shape.
                tab.Group("animation-effects", "Animation", "N", 100, g =>
                {
                    // Entrance effects
                    g.Medium("freep.anim.entrance.appear",    "Appear",     RibbonCommandIconKind.Flash,   "A");
                    g.Medium("freep.anim.entrance.fade",      "Fade In",    RibbonCommandIconKind.Effects, "F");
                    g.Medium("freep.anim.entrance.fly-in",    "Fly In",     RibbonCommandIconKind.ArrowUp, "Y");
                    g.Medium("freep.anim.entrance.wipe",      "Wipe",       RibbonCommandIconKind.ArrowRight, "W");
                    g.Medium("freep.anim.entrance.zoom",      "Zoom In",    RibbonCommandIconKind.Zoom,    "Z");
                    g.Medium("freep.anim.entrance.split",     "Split",      RibbonCommandIconKind.ArrowLeftRight, "S");
                    g.Separator();
                    // Emphasis effects
                    g.Medium("freep.anim.emphasis.pulse",      "Pulse",      RibbonCommandIconKind.Flash,   "P");
                    g.Medium("freep.anim.emphasis.spin",       "Spin",       RibbonCommandIconKind.Rotate,  "I");
                    g.Medium("freep.anim.emphasis.grow-shrink","Grow/Shrink", RibbonCommandIconKind.Scale,   "G");
                    g.Separator();
                    // Exit effects
                    g.Medium("freep.anim.exit.disappear",  "Disappear", RibbonCommandIconKind.Delete,    "D");
                    g.Medium("freep.anim.exit.fade-out",   "Fade Out",  RibbonCommandIconKind.Effects,   "O");
                    g.Medium("freep.anim.exit.fly-out",    "Fly Out",   RibbonCommandIconKind.ArrowDown, "X");
                    g.Separator();
                    // Remove all animations from selected shape
                    g.Medium("freep.anim.none", "No Animation", RibbonCommandIconKind.Clear, "E");
                });

                // Timing group — trigger, duration, delay, reorder.
                tab.Group("animation-timing", "Timing", "T", 90, g =>
                {
                    g.ComboBox("freep.anim.trigger", "Start", c => c with
                    {
                        Items = new[] { "On Click", "With Previous", "After Previous" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.Next),
                        Width = 130
                    });
                    g.ComboBox("freep.anim.duration", "Duration", c => c with
                    {
                        Items = new[] { "0.25s", "0.50s", "1.00s", "1.50s", "2.00s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.ComboBox("freep.anim.delay", "Delay", c => c with
                    {
                        Items = new[] { "0s", "0.25s", "0.50s", "1.00s", "2.00s" },
                        Icon  = new RibbonCommandIcon(RibbonCommandIconKind.History),
                        Width = 90
                    });
                    g.Medium("freep.anim.move-earlier", "Move Earlier", RibbonCommandIconKind.Previous, "U");
                    g.Medium("freep.anim.move-later",   "Move Later",   RibbonCommandIconKind.Next,     "L");
                });

                // Animation Pane toggle stub.
                tab.Group("animation-pane", "Advanced Animation", "V", 80, g =>
                {
                    g.MediumToggle("freep.anim.pane", "Animation Pane", RibbonCommandIconKind.List, "P");
                });
            })
            .Build();
    }
}
