using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r471: under Restrict Editing / read-only, NO public mutator may change the document.
///
/// <para>Existing coverage asserted this operation by operation - typing, formatting, comments,
/// undo/redo - and every one of those passed. A census over the whole public surface found six that
/// did not: <c>InsertTableOfContents</c>, <c>UpdateTableOfContents</c>, <c>InsertBibliography</c>,
/// <c>RefreshBibliography</c>, <c>InsertTableOfAuthorities</c> and <c>RefreshTableOfAuthorities</c>
/// all rewrote a protected document. Word disables exactly these under "Restrict Editing - No
/// changes (Read only)".</para>
///
/// <para>This is written as a census rather than six tests on purpose. The defect was not that
/// someone forgot one method; it is that the guard is opt-in, so every method added later can
/// forget it too. A census fails for a NEW unguarded mutator without anyone remembering to extend
/// it. The other 86 methods respected protection under identical setup, which is what makes the six
/// a finding rather than a broken harness.</para>
/// </summary>
public class R471_ReadOnlyProtectionBlocksEveryMutatorTests
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

    private static List<MethodInfo> ZeroArgumentMutators() =>
        typeof(DocumentView)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Length == 0
                        && m.ReturnType == typeof(void)
                        && !m.IsSpecialName
                        // Protection control itself must stay callable while protected, otherwise
                        // the document could never be unlocked again.
                        && !m.Name.Contains("Protection", StringComparison.Ordinal)
                        && !m.Name.Contains("MarkedAsFinal", StringComparison.Ordinal))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public async Task NoPublicMutatorChangesAReadOnlyProtectedDocument()
    {
        var mutated = new List<string>();
        var attempted = 0;

        var ran = await OnUiThread(() =>
        {
            foreach (var method in ZeroArgumentMutators())
            {
                var view = BuildView();
                view.SetProtection(ProtectionMode.ReadOnly);
                var before = Fingerprint(view);
                attempted++;

                try
                {
                    method.Invoke(view, null);
                }
                catch
                {
                    // A method that refuses by throwing has still refused.
                    continue;
                }

                if (!string.Equals(before, Fingerprint(view), StringComparison.Ordinal))
                    mutated.Add(method.Name);
            }
        });

        ran.Should().BeTrue();

        // Non-vacuity: if the reflection filter ever stops matching, the census would pass while
        // examining nothing at all.
        attempted.Should().BeGreaterThan(50, "the census must actually be exercising the surface");

        mutated.Should().BeEmpty(
            "a read-only document must not be rewritten by any command, and Word blocks each of these");
    }

    [Fact]
    public async Task TheCensusWouldNoticeAnUnguardedMutator()
    {
        // Proves the instrument: with protection cleared, the same methods DO change the document,
        // so an empty result above reflects the guards working rather than the census being inert.
        var mutatedWhenUnprotected = 0;

        var ran = await OnUiThread(() =>
        {
            foreach (var method in ZeroArgumentMutators())
            {
                var view = BuildView();
                var before = Fingerprint(view);

                try
                {
                    method.Invoke(view, null);
                }
                catch
                {
                    continue;
                }

                if (!string.Equals(before, Fingerprint(view), StringComparison.Ordinal))
                    mutatedWhenUnprotected++;
            }
        });

        ran.Should().BeTrue();
        mutatedWhenUnprotected.Should().BeGreaterThan(0,
            "without protection these commands are supposed to work; if none mutate, the census " +
            "is measuring nothing and its clean result above is meaningless");
    }
}
