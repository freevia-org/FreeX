using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Enforcement coverage for document protection (Restrict Editing) and Mark as Final on the live
/// <see cref="DocumentView"/> editing surface. These run on an STA thread (<c>[StaFact]</c>, via
/// Xunit.StaFact) because the RichTextBox needs STA + a Dispatcher.
/// </summary>
public sealed class ProtectionEnforcementTests
{
    private static DocumentView Load()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body"));
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void NoChangesProtection_MakesEditorReadOnly_AndStopRestoresEditing()
    {
        var view = Load();
        view.IsReadOnly.Should().BeFalse();

        // Restrict Editing → No changes (Read only) locks the typing surface.
        view.SetProtection(ProtectionMode.ReadOnly);
        view.IsProtected.Should().BeTrue();
        view.IsReadOnly.Should().BeTrue();

        // Stop Protection (None) restores editing.
        view.SetProtection(ProtectionMode.None);
        view.IsProtected.Should().BeFalse();
        view.IsReadOnly.Should().BeFalse();
    }

    [StaFact]
    public void TrackChangesProtection_LeavesEditable_ButForcesTrackChangesOn()
    {
        var view = Load();
        view.TrackChangesEnabled.Should().BeFalse();

        view.SetProtection(ProtectionMode.TrackChangesOnly);

        // Tracked-changes protection keeps the surface editable but forces tracking on.
        view.IsReadOnly.Should().BeFalse();
        view.TrackChangesEnabled.Should().BeTrue();
    }

    [StaFact]
    public void CommentsAndFormsProtection_LockTypingSurface()
    {
        var view = Load();

        view.SetProtection(ProtectionMode.CommentsOnly);
        view.IsReadOnly.Should().BeTrue();

        view.SetProtection(ProtectionMode.FillingForms);
        view.IsReadOnly.Should().BeTrue();
    }

    [StaFact]
    public void MarkAsFinal_LocksEditing_AndEditAnywayRestoresIt()
    {
        var view = Load();
        view.IsMarkedAsFinal.Should().BeFalse();
        view.IsReadOnly.Should().BeFalse();

        view.SetMarkedAsFinal(true);
        view.IsMarkedAsFinal.Should().BeTrue();
        view.IsReadOnly.Should().BeTrue();

        // "Edit Anyway" clears the flag and restores editing.
        view.SetMarkedAsFinal(false);
        view.IsMarkedAsFinal.Should().BeFalse();
        view.IsReadOnly.Should().BeFalse();
    }

    [StaFact]
    public void ProtectionStateChanged_Fires_OnProtectionAndFinalChanges()
    {
        var view = Load();
        var fired = 0;
        view.ProtectionStateChanged += (_, _) => fired++;

        view.SetProtection(ProtectionMode.ReadOnly);
        view.SetMarkedAsFinal(true);

        fired.Should().BeGreaterThanOrEqualTo(2);
    }
}
