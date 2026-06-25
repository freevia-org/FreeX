using System.Windows;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>
/// Binds FreeP's ribbon command ids (declared in <see cref="FreePRibbon"/>) to behavior, implementing the
/// shared <see cref="IRibbonCommandRegistry"/>.
///
/// Wave 3A: most ids are now real commands routed through <see cref="EditingSession"/>.
///
/// Still stubbed (noted below): freep.paste, freep.cut, freep.copy, freep.layout, freep.font-family.
/// </summary>
internal static class FreePRibbonCommands
{
    public static RibbonCommandRegistry Build(RibbonStateStore stateStore, EditingSession editor)
    {
        var registry = new RibbonCommandRegistry();

        // ── Slide management ─────────────────────────────────────────────────────

        registry.Register("freep.new-slide",
            new ActionCommand(() => editor.InsertSlide()));

        registry.Register("freep.duplicate-slide",
            new ActionCommand(() => editor.DuplicateCurrentSlide()));

        registry.Register("freep.delete-slide",
            new ActionCommand(() => editor.DeleteCurrentSlide()));

        // ── Insert shapes ────────────────────────────────────────────────────────

        registry.Register("freep.text-box",
            new ActionCommand(() => editor.InsertDefaultTextBox()));

        registry.Register("freep.shape-rectangle",
            new ActionCommand(() => editor.InsertDefaultRectangle()));

        registry.Register("freep.shape-ellipse",
            new ActionCommand(() => editor.InsertDefaultEllipse()));

        // Picture: open a file-open dialog and insert. Heavy WPF dialog plumbing
        // is intentionally thin here — 3C can refine the UX.
        registry.Register("freep.picture", new ActionCommand(() =>
        {
            // TODO(3C): replace with a proper picture-insert dialog with preview.
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Insert Picture",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg|All files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var bytes = System.IO.File.ReadAllBytes(dlg.FileName);
                    var ext   = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    var mime  = ext switch
                    {
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif"  => "image/gif",
                        ".bmp"  => "image/bmp",
                        ".svg"  => "image/svg+xml",
                        _       => "image/png"
                    };
                    editor.InsertPicture(bytes, mime);
                }
                catch
                {
                    // Ignore IO errors silently (e.g. access denied).
                }
            }
        }));

        // ── Format toggles (stateful) ────────────────────────────────────────────

        registry.Register("freep.bold",      new EditorToggleCommand(stateStore, "freep.bold",
            () => editor.ToggleBoldOnSelection()));
        registry.Register("freep.italic",    new EditorToggleCommand(stateStore, "freep.italic",
            () => editor.ToggleItalicOnSelection()));
        registry.Register("freep.underline", new EditorToggleCommand(stateStore, "freep.underline",
            () => editor.ToggleUnderlineOnSelection()));

        // ── Clipboard / layout / font-family: STUBBED ────────────────────────────
        // freep.paste / freep.cut / freep.copy  — waiting for clipboard model (Wave 3C).
        // freep.layout                           — layout picker UI (Wave 3B).
        // freep.font-family                      — font-family combo (Wave 3C text editing).
        foreach (var id in new[]
        {
            "freep.paste", "freep.cut", "freep.copy",
            "freep.layout",
            "freep.font-family",
        })
        {
            registry.Register(id, new ActionCommand(() => { /* STUB: wave 3B/3C */ }));
        }

        return registry;
    }

    // ── Inner helpers ─────────────────────────────────────────────────────────────

    /// <summary>A fire-and-forget command over a plain delegate.</summary>
    private sealed class ActionCommand : IRibbonCommand
    {
        private readonly Action _action;
        public ActionCommand(Action action) => _action = action;
        public void Execute(RibbonCommandContext context) => _action();
    }

    /// <summary>
    /// Stateful toggle that routes through the editor and updates the ribbon state store.
    /// The checked state is a local indicator only; in a full implementation the editor would
    /// expose selection-format query methods that 3C can feed back.
    /// </summary>
    private sealed class EditorToggleCommand : IRibbonStatefulCommand
    {
        private readonly RibbonStateStore _stateStore;
        private readonly RibbonCommandId _id;
        private readonly Action _toggle;
        private bool _checked;

        public EditorToggleCommand(RibbonStateStore stateStore, RibbonCommandId id, Action toggle)
        {
            _stateStore = stateStore;
            _id         = id;
            _toggle     = toggle;
        }

        public void Execute(RibbonCommandContext context)
        {
            _toggle();
            _checked = !_checked;
            _stateStore.SetChecked(_id, _checked);
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: _checked);
    }
}
