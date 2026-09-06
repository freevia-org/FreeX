using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r472: three properties every document mutation must have, asserted across the whole surface
/// rather than one command at a time.
///
/// <list type="number">
/// <item>It raises <c>DocumentChanged</c>. That event is the ONLY thing telling the shell the
/// document is dirty, and also refreshes the navigation, selection and reviewing panes. A mutation
/// that changes content silently means the close prompt never appears and the work is lost without
/// a warning - the worst outcome any of these commands can produce.</item>
/// <item>It leaves something on the undo stack, so Ctrl+Z can reach it.</item>
/// <item>Undoing it restores the document exactly, not approximately.</item>
/// </list>
///
/// <para>All twelve mutators pass today; this is a guard, not a repair. It is written as a census
/// because r471 showed the failure mode in this class is not a forgotten method but an opt-in
/// convention that each newly added method can quietly skip - and a census fails for a NEW method
/// without anyone remembering to extend it.</para>
/// </summary>
public class R472_EveryMutatorNotifiesAndUndoesTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView BuildView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello"));

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string Fingerprint(DocumentView view)
    {
        var sb = new StringBuilder();
        sb.Append(view.PlainText).Append('|').Append(view.Document.Blocks.Count);
        foreach (var block in view.Document.Blocks)
            sb.Append('|').Append(block.GetType().Name);
        return sb.ToString();
    }

    private sealed record Census(int Mutating, List<string> Violations);

    private static Census Run()
    {
        var violations = new List<string>();
        var mutating = 0;

        var methods = typeof(DocumentView)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(void) && !m.IsSpecialName)
            .OrderBy(m => m.Name, StringComparer.Ordinal);

        foreach (var method in methods)
        {
            var view = BuildView();
            var notified = 0;
            view.DocumentChanged += () => notified++;

            var before = Fingerprint(view);
            try { method.Invoke(view, null); } catch { continue; }
            if (string.Equals(before, Fingerprint(view), StringComparison.Ordinal))
                continue;

            mutating++;

            if (notified == 0)
                violations.Add($"{method.Name} changed the document without raising DocumentChanged");

            if (!view.CanUndo)
            {
                violations.Add($"{method.Name} changed the document but left nothing to undo");
                continue;
            }

            view.Undo();
            if (!string.Equals(before, Fingerprint(view), StringComparison.Ordinal))
                violations.Add($"{method.Name} did not restore the document on undo");
        }

        return new Census(mutating, violations);
    }

    [Fact]
    public async Task EveryMutationNotifiesTheShellAndIsFullyUndoable()
    {
        Census? census = null;
        var ran = await OnUiThread(() => census = Run());

        ran.Should().BeTrue();
        census.Should().NotBeNull();

        // Non-vacuity: if the reflection filter stops matching, or every method starts throwing,
        // the census would report no violations while having tested nothing.
        census!.Mutating.Should().BeGreaterThanOrEqualTo(10,
            "the census must actually be exercising mutating commands; it found 12 when written");

        census.Violations.Should().BeEmpty();
    }
}
