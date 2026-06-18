using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Behavior QA over the real editing surface: inserting an object should leave the editor in the state
/// that drives the matching contextual ribbon tab (Word puts the caret in a freshly inserted table so
/// "Table Tools" appears immediately). Runs on STA because it builds the real WPF <see cref="DocumentView"/>.
/// </summary>
public sealed class InsertContextBehaviorTests
{
    private static DocumentView EmptyView()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        return view;
    }

    [StaFact]
    public void InsertTable_PlacesCaretInTable_SoTableContextActivates()
    {
        var view = EmptyView();
        Assert.False(view.IsCaretInTable());

        view.InsertTable(2, 2);

        // Word behaviour: the caret lands in the new table so the Table Design contextual tab shows at once.
        Assert.True(view.IsCaretInTable());
    }
}
