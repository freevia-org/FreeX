using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Commands;
using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>A reversible edit to a <see cref="Presentation"/>. Mirrors FreeW's IDocumentCommand shape.</summary>
public interface IPresentationCommand
{
    string Label { get; }
    int EstimatedBytes => 256;
    void Apply(Presentation presentation);
    void Revert(Presentation presentation);

    /// <summary>
    /// Whether executing this command would actually change the presentation. When false, the bus
    /// skips it entirely (no Apply, no undo entry) so no-op edits don't pollute the undo history.
    /// Defaults to true — commands that can be invoked on a target where they'd do nothing
    /// (e.g. splitting an unmerged cell) override this.
    /// </summary>
    bool HasEffect(Presentation presentation) => true;
}

/// <summary>
/// FreeP's undo/redo command bus. As in FreeW, the mechanics — paired stacks, depth/byte budget, redo
/// invalidation — are the shared <see cref="UndoRedoStack{TCommand,TPayload}"/>; this bus only adds the
/// presentation-command apply/revert and a change notification.
/// </summary>
public sealed class PresentationCommandBus
{
    private const int MaxDepth = 200;
    private const int MaxBytes = 50 * 1024 * 1024;

    private readonly UndoRedoStack<IPresentationCommand, object?> _stack = new(MaxDepth, MaxBytes);
    private readonly Presentation _presentation;

    public PresentationCommandBus(Presentation presentation) => _presentation = presentation;

    /// <summary>Raised after any execute/undo/redo so a view can refresh.</summary>
    public event Action? Changed;

    public bool CanUndo => _stack.CanUndo;
    public bool CanRedo => _stack.CanRedo;

    /// <summary>Applies a command and records it for undo (invalidating the redo history).</summary>
    public void Execute(IPresentationCommand command)
    {
        // Skip no-op commands entirely so they don't create an empty undo entry.
        if (!command.HasEffect(_presentation))
            return;
        command.Apply(_presentation);
        _stack.Push(command, command.EstimatedBytes, payload: null, command.Label);
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!_stack.CanUndo)
            return;
        var entry = _stack.PopUndo();
        entry.Command.Revert(_presentation);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!_stack.CanRedo)
            return;
        var entry = _stack.PopRedo();
        entry.Command.Apply(_presentation);
        _stack.PushWithoutClearingRedo(entry);
        Changed?.Invoke();
    }
}

/// <summary>
/// Applies a prepared SmartArt state as one undoable operation. Hosts prepare the state through
/// the shared SmartArt planner (including data-part and drawing-cache regeneration), then this
/// command owns the model transition so Undo/Redo restores the complete payload together.
/// </summary>
public sealed class ReplaceSmartArtCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly SmartArtShape _before;
    private readonly SmartArtShape _after;

    public ReplaceSmartArtCommand(
        int slideIndex,
        uint shapeId,
        SmartArtShape before,
        SmartArtShape after)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _before = SlideCloner.CloneSmartArt(before);
        _after = SlideCloner.CloneSmartArt(after);
    }

    public string Label => "Edit SmartArt";

    public void Apply(Presentation presentation) => CopyState(presentation, _after);

    public void Revert(Presentation presentation) => CopyState(presentation, _before);

    private void CopyState(Presentation presentation, SmartArtShape state)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return;

        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.SmartArt is not null)
            SlideCloner.CopySmartArt(shape.SmartArt, state);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SLIDE COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inserts a slide at <paramref name="index"/> (appends at end if index == Count).
/// Revert removes it by reference.
/// </summary>
public sealed class InsertSlideCommand : IPresentationCommand
{
    private readonly int _index;
    private readonly Slide _slide;
    private List<SectionSnapshot>? _beforeSections;

    public InsertSlideCommand(int index, Slide slide)
    {
        _index = index;
        _slide = slide;
    }

    public string Label => "Insert Slide";

    public void Apply(Presentation p)
    {
        var idx = Math.Clamp(_index, 0, p.Slides.Count);

        if (_beforeSections is null)
        {
            _beforeSections = p.Sections
                .Select(section => new SectionSnapshot(
                    section.Id,
                    section.Name,
                    section.SlideIds.ToArray()))
                .ToList();
        }

        p.Slides.Insert(idx, _slide);
        AddSlideToNeighborSection(p, idx, _slide.Id);
    }

    public void Revert(Presentation p)
    {
        p.Slides.Remove(_slide);
        RestoreSections(p, _beforeSections);
    }

    private static void AddSlideToNeighborSection(
        Presentation p,
        int insertedIndex,
        string insertedSlideId)
    {
        if (p.Slides.Count <= 1)
            return;

        if (insertedIndex > 0)
        {
            var previousSlideId = p.Slides[insertedIndex - 1].Id;
            foreach (var section in p.Sections)
            {
                var neighborIndex = section.SlideIds.FindIndex(id =>
                    string.Equals(id, previousSlideId, StringComparison.Ordinal));
                if (neighborIndex < 0)
                    continue;

                section.SlideIds.Insert(neighborIndex + 1, insertedSlideId);
                return;
            }
        }
        else
        {
            var nextSlideId = p.Slides[1].Id;
            foreach (var section in p.Sections)
            {
                var neighborIndex = section.SlideIds.FindIndex(id =>
                    string.Equals(id, nextSlideId, StringComparison.Ordinal));
                if (neighborIndex < 0)
                    continue;

                section.SlideIds.Insert(neighborIndex, insertedSlideId);
                return;
            }
        }
    }

    private static void RestoreSections(
        Presentation p,
        IReadOnlyList<SectionSnapshot>? snapshots)
    {
        if (snapshots is null)
            return;

        p.Sections.Clear();
        foreach (var snapshot in snapshots)
        {
            var section = new PresentationSection
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
            };
            section.SlideIds.AddRange(snapshot.SlideIds);
            p.Sections.Add(section);
        }
    }

    private sealed record SectionSnapshot(string Id, string Name, IReadOnlyList<string> SlideIds);
}

/// <summary>
/// Appends a new blank slide. Kept for backward compatibility with existing callers.
/// </summary>
public sealed class AddSlideCommand : IPresentationCommand
{
    private readonly Slide _slide;
    private List<SectionSnapshot>? _beforeSections;

    public AddSlideCommand(Slide slide) => _slide = slide;
    public string Label => "Add Slide";

    public void Apply(Presentation p)
    {
        if (_beforeSections is null)
        {
            _beforeSections = p.Sections
                .Select(section => new SectionSnapshot(
                    section.Id,
                    section.Name,
                    section.SlideIds.ToArray()))
                .ToList();
        }

        p.Slides.Add(_slide);
        AddSlideToPreviousSection(p, _slide.Id);
    }

    public void Revert(Presentation p)
    {
        p.Slides.Remove(_slide);
        RestoreSections(p, _beforeSections);
    }

    private static void AddSlideToPreviousSection(Presentation p, string slideId)
    {
        if (p.Slides.Count < 2)
            return;

        var previousSlideId = p.Slides[^2].Id;
        foreach (var section in p.Sections)
        {
            var previousIndex = section.SlideIds.FindIndex(id =>
                string.Equals(id, previousSlideId, StringComparison.Ordinal));
            if (previousIndex < 0)
                continue;

            section.SlideIds.Insert(previousIndex + 1, slideId);
            return;
        }
    }

    private static void RestoreSections(
        Presentation p,
        IReadOnlyList<SectionSnapshot>? snapshots)
    {
        if (snapshots is null)
            return;

        p.Sections.Clear();
        foreach (var snapshot in snapshots)
        {
            var section = new PresentationSection
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
            };
            section.SlideIds.AddRange(snapshot.SlideIds);
            p.Sections.Add(section);
        }
    }

    private sealed record SectionSnapshot(string Id, string Name, IReadOnlyList<string> SlideIds);
}

/// <summary>
/// Deletes the slide at <paramref name="index"/>. Captures the slide instance + its original
/// index for undo.
/// </summary>
public sealed class DeleteSlideCommand : IPresentationCommand
{
    private readonly int _index;
    private Slide? _captured;
    private List<SectionSnapshot>? _beforeSections;
    private List<CustomShowSnapshot>? _beforeCustomShows;

    public DeleteSlideCommand(int index) => _index = index;

    public string Label => "Delete Slide";

    public void Apply(Presentation p)
    {
        if (_index < 0 || _index >= p.Slides.Count)
            return;

        if (_captured is null)
        {
            _captured = p.Slides[_index];
            _beforeSections = p.Sections
                .Select(section => new SectionSnapshot(
                    section.Id,
                    section.Name,
                    section.SlideIds.ToArray()))
                .ToList();
            _beforeCustomShows = p.CustomShows
                .Select(show => new CustomShowSnapshot(
                    show.Id,
                    show.Name,
                    show.SlideIds.ToArray()))
                .ToList();
        }

        p.Slides.RemoveAt(_index);
        RemoveSlideReferences(p, _captured.Id);
    }

    public void Revert(Presentation p)
    {
        if (_captured is null) return;
        var idx = Math.Clamp(_index, 0, p.Slides.Count);
        p.Slides.Insert(idx, _captured);
        RestoreSections(p, _beforeSections);
        RestoreCustomShows(p, _beforeCustomShows);
    }

    private static void RemoveSlideReferences(Presentation p, string slideId)
    {
        foreach (var section in p.Sections)
            section.SlideIds.RemoveAll(id => string.Equals(id, slideId, StringComparison.Ordinal));

        foreach (var customShow in p.CustomShows)
            customShow.SlideIds.RemoveAll(id => string.Equals(id, slideId, StringComparison.Ordinal));
    }

    private static void RestoreSections(
        Presentation p,
        IReadOnlyList<SectionSnapshot>? snapshots)
    {
        if (snapshots is null)
            return;

        p.Sections.Clear();
        foreach (var snapshot in snapshots)
        {
            var section = new PresentationSection
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
            };
            section.SlideIds.AddRange(snapshot.SlideIds);
            p.Sections.Add(section);
        }
    }

    private static void RestoreCustomShows(
        Presentation p,
        IReadOnlyList<CustomShowSnapshot>? snapshots)
    {
        if (snapshots is null)
            return;

        p.CustomShows.Clear();
        foreach (var snapshot in snapshots)
        {
            var customShow = new PresentationCustomShow
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
            };
            customShow.SlideIds.AddRange(snapshot.SlideIds);
            p.CustomShows.Add(customShow);
        }
    }

    private sealed record SectionSnapshot(string Id, string Name, IReadOnlyList<string> SlideIds);

    private sealed record CustomShowSnapshot(uint Id, string Name, IReadOnlyList<string> SlideIds);
}

/// <summary>Replaces the complete named custom-show collection as one undoable edit.</summary>
public sealed class ReplaceCustomShowsCommand : IPresentationCommand
{
    private readonly IReadOnlyList<PresentationCustomShow> _before;
    private readonly IReadOnlyList<PresentationCustomShow> _after;

    public ReplaceCustomShowsCommand(
        IEnumerable<PresentationCustomShow> before,
        IEnumerable<PresentationCustomShow> after)
    {
        _before = CloneShows(before);
        _after = CloneShows(after);
    }

    public string Label => "Edit Custom Show";

    public bool HasEffect(Presentation presentation) =>
        !ShowsEqual(presentation.CustomShows, _after);

    public void Apply(Presentation presentation) => Replace(presentation, _after);

    public void Revert(Presentation presentation) => Replace(presentation, _before);

    private static void Replace(
        Presentation presentation,
        IReadOnlyList<PresentationCustomShow> shows)
    {
        presentation.CustomShows.Clear();
        presentation.CustomShows.AddRange(CloneShows(shows));
    }

    private static List<PresentationCustomShow> CloneShows(
        IEnumerable<PresentationCustomShow> shows) =>
        shows.Select(show =>
        {
            var clone = new PresentationCustomShow { Id = show.Id, Name = show.Name };
            clone.SlideIds.AddRange(show.SlideIds);
            return clone;
        }).ToList();

    private static bool ShowsEqual(
        IReadOnlyList<PresentationCustomShow> left,
        IReadOnlyList<PresentationCustomShow> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.Zip(right).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Name == pair.Second.Name
            && pair.First.SlideIds.SequenceEqual(pair.Second.SlideIds));
    }
}

/// <summary>
/// Deep-clones the slide at <paramref name="sourceIndex"/> and inserts it immediately after.
/// Undo removes the duplicate.
/// </summary>
public sealed class DuplicateSlideCommand : IPresentationCommand
{
    private readonly int _sourceIndex;
    private Slide? _duplicate;
    private List<SectionSnapshot>? _beforeSections;

    public DuplicateSlideCommand(int sourceIndex) => _sourceIndex = sourceIndex;

    public string Label => "Duplicate Slide";

    public void Apply(Presentation p)
    {
        if (_sourceIndex < 0 || _sourceIndex >= p.Slides.Count)
            return;

        var source = p.Slides[_sourceIndex];
        if (_beforeSections is null)
        {
            _beforeSections = p.Sections
                .Select(section => new SectionSnapshot(
                    section.Id,
                    section.Name,
                    section.SlideIds.ToArray()))
                .ToList();
        }

        _duplicate = SlideCloner.CloneSlide(source);
        p.Slides.Insert(_sourceIndex + 1, _duplicate);
        AddDuplicateToSourceSection(p, source.Id, _duplicate.Id);
    }

    public void Revert(Presentation p)
    {
        if (_duplicate is not null)
            p.Slides.Remove(_duplicate);
        RestoreSections(p, _beforeSections);
    }

    private static void AddDuplicateToSourceSection(
        Presentation p,
        string sourceSlideId,
        string duplicateSlideId)
    {
        foreach (var section in p.Sections)
        {
            var sourceIndex = section.SlideIds.FindIndex(id =>
                string.Equals(id, sourceSlideId, StringComparison.Ordinal));
            if (sourceIndex < 0)
                continue;

            section.SlideIds.Insert(sourceIndex + 1, duplicateSlideId);
            return;
        }
    }

    private static void RestoreSections(
        Presentation p,
        IReadOnlyList<SectionSnapshot>? snapshots)
    {
        if (snapshots is null)
            return;

        p.Sections.Clear();
        foreach (var snapshot in snapshots)
        {
            var section = new PresentationSection
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
            };
            section.SlideIds.AddRange(snapshot.SlideIds);
            p.Sections.Add(section);
        }
    }

    private sealed record SectionSnapshot(string Id, string Name, IReadOnlyList<string> SlideIds);
}

/// <summary>
/// Moves the slide at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.
/// Both indices are clamped to valid range. Revert moves it back.
/// </summary>
public sealed class MoveSlideCommand : IPresentationCommand
{
    private readonly int _from;
    private readonly int _to;
    private List<SectionSnapshot>? _beforeSections;

    public MoveSlideCommand(int from, int to)
    {
        _from = from;
        _to   = to;
    }

    public string Label => "Move Slide";

    public void Apply(Presentation p)
    {
        if (_from < 0 || _from >= p.Slides.Count)
            return;

        if (_beforeSections is null)
        {
            _beforeSections = p.Sections
                .Select(section => new SectionSnapshot(
                    section.Id,
                    section.Name,
                    section.SlideIds.ToArray()))
                .ToList();
        }

        MoveInList(p.Slides, _from, _to);
        SynchronizeSectionOrder(p);
    }

    public void Revert(Presentation p)
    {
        MoveInList(p.Slides, _to, _from);
        RestoreSections(p, _beforeSections);
    }

    private static void SynchronizeSectionOrder(Presentation p)
    {
        foreach (var section in p.Sections)
        {
            var remaining = section.SlideIds.ToList();
            var ordered = new List<string>(remaining.Count);

            foreach (var slide in p.Slides)
            {
                var index = remaining.FindIndex(id =>
                    string.Equals(id, slide.Id, StringComparison.Ordinal));
                if (index < 0)
                    continue;

                ordered.Add(remaining[index]);
                remaining.RemoveAt(index);
            }

            ordered.AddRange(remaining);
            section.SlideIds.Clear();
            section.SlideIds.AddRange(ordered);
        }
    }

    private static void RestoreSections(
        Presentation p,
        IReadOnlyList<SectionSnapshot>? snapshots)
    {
        if (snapshots is null)
            return;

        p.Sections.Clear();
        foreach (var snapshot in snapshots)
        {
            var section = new PresentationSection
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
            };
            section.SlideIds.AddRange(snapshot.SlideIds);
            p.Sections.Add(section);
        }
    }

    private static void MoveInList<T>(List<T> list, int from, int to)
    {
        if (from == to || from < 0 || from >= list.Count) return;
        var item = list[from];
        list.RemoveAt(from);
        var dest = Math.Clamp(to, 0, list.Count);
        list.Insert(dest, item);
    }

    private sealed record SectionSnapshot(string Id, string Name, IReadOnlyList<string> SlideIds);
}

// ════════════════════════════════════════════════════════════════════════════════
// SHAPE COMMANDS — helpers
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>Sets whether a slide is skipped during slide-show playback.</summary>
public sealed class SetSlideHiddenCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly bool _newValue;
    private bool _oldValue;

    public SetSlideHiddenCommand(int slideIndex, bool hidden)
    {
        _slideIndex = slideIndex;
        _newValue = hidden;
    }

    public string Label => _newValue ? "Hide Slide" : "Show Slide";

    public bool HasEffect(Presentation p) =>
        _slideIndex >= 0 &&
        _slideIndex < p.Slides.Count &&
        p.Slides[_slideIndex].IsHidden != _newValue;

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return;

        _oldValue = p.Slides[_slideIndex].IsHidden;
        p.Slides[_slideIndex].IsHidden = _newValue;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex >= 0 && _slideIndex < p.Slides.Count)
            p.Slides[_slideIndex].IsHidden = _oldValue;
    }
}

/// <summary>Sets whether slideshow media controls are shown for the presentation.</summary>
public sealed class SetShowMediaControlsCommand : IPresentationCommand
{
    private readonly bool _newValue;
    private readonly bool _oldValue;

    public SetShowMediaControlsCommand(bool oldValue, bool newValue)
    {
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public string Label => _newValue ? "Show Media Controls" : "Hide Media Controls";

    public bool HasEffect(Presentation p) => p.ShowMediaControls != _newValue;

    public void Apply(Presentation p) => p.ShowMediaControls = _newValue;

    public void Revert(Presentation p) => p.ShowMediaControls = _oldValue;
}

/// <summary>Sets the presentation-wide slideshow playback settings as one undoable edit.</summary>
public sealed class SetSlideShowSettingsCommand : IPresentationCommand
{
    private readonly bool _oldUseSlideTimings;
    private readonly bool _oldShowWithAnimation;
    private readonly bool _oldShowWithNarration;
    private readonly bool _oldLoopUntilStopped;
    private readonly PresentationShowType _oldShowType;
    private readonly bool _oldShowBrowseScrollbar;
    private readonly uint? _oldKioskRestartAfterMilliseconds;
    private readonly bool _oldShowMediaControls;
    private readonly bool _oldShowMasterShapes;
    private readonly bool _newUseSlideTimings;
    private readonly bool _newShowWithAnimation;
    private readonly bool _newShowWithNarration;
    private readonly bool _newLoopUntilStopped;
    private readonly PresentationShowType _newShowType;
    private readonly bool _newShowBrowseScrollbar;
    private readonly uint? _newKioskRestartAfterMilliseconds;
    private readonly bool _newShowMediaControls;
    private readonly bool _newShowMasterShapes;

    public SetSlideShowSettingsCommand(
        bool oldUseSlideTimings,
        bool oldShowWithAnimation,
        bool oldLoopUntilStopped,
        PresentationShowType oldShowType,
        bool oldShowBrowseScrollbar,
        uint? oldKioskRestartAfterMilliseconds,
        bool oldShowWithNarration,
        bool newUseSlideTimings,
        bool newShowWithAnimation,
        bool newLoopUntilStopped,
        PresentationShowType newShowType,
        bool newShowBrowseScrollbar,
        uint? newKioskRestartAfterMilliseconds,
        bool newShowWithNarration,
        bool oldShowMediaControls = true,
        bool newShowMediaControls = true,
        bool oldShowMasterShapes = true,
        bool newShowMasterShapes = true)
    {
        _oldUseSlideTimings = oldUseSlideTimings;
        _oldShowWithAnimation = oldShowWithAnimation;
        _oldLoopUntilStopped = oldLoopUntilStopped;
        _oldShowType = oldShowType;
        _oldShowBrowseScrollbar = oldShowBrowseScrollbar;
        _oldKioskRestartAfterMilliseconds = oldKioskRestartAfterMilliseconds;
        _oldShowWithNarration = oldShowWithNarration;
        _oldShowMediaControls = oldShowMediaControls;
        _oldShowMasterShapes = oldShowMasterShapes;
        _newUseSlideTimings = newUseSlideTimings;
        _newShowWithAnimation = newShowWithAnimation;
        _newLoopUntilStopped = newLoopUntilStopped;
        _newShowType = newShowType;
        _newShowBrowseScrollbar = newShowBrowseScrollbar;
        _newKioskRestartAfterMilliseconds = newKioskRestartAfterMilliseconds;
        _newShowWithNarration = newShowWithNarration;
        _newShowMediaControls = newShowMediaControls;
        _newShowMasterShapes = newShowMasterShapes;
    }

    public SetSlideShowSettingsCommand(
        bool oldUseSlideTimings,
        bool oldShowWithAnimation,
        bool oldLoopUntilStopped,
        bool newUseSlideTimings,
        bool newShowWithAnimation,
        bool newLoopUntilStopped)
        : this(
            oldUseSlideTimings,
            oldShowWithAnimation,
            oldLoopUntilStopped,
            PresentationShowType.PresentedBySpeaker,
            true,
            null,
            true,
            newUseSlideTimings,
            newShowWithAnimation,
            newLoopUntilStopped,
            PresentationShowType.PresentedBySpeaker,
            true,
            null,
            true)
    {
    }

    public SetSlideShowSettingsCommand(
        bool oldUseSlideTimings,
        bool oldShowWithAnimation,
        bool oldLoopUntilStopped,
        PresentationShowType oldShowType,
        bool newUseSlideTimings,
        bool newShowWithAnimation,
        bool newLoopUntilStopped,
        PresentationShowType newShowType)
        : this(
            oldUseSlideTimings,
            oldShowWithAnimation,
            oldLoopUntilStopped,
            oldShowType,
            true,
            null,
            true,
            newUseSlideTimings,
            newShowWithAnimation,
            newLoopUntilStopped,
            newShowType,
            true,
            null,
            true)
    {
    }

    public string Label => "Set Slide Show Settings";

    public bool HasEffect(Presentation p) =>
        p.UseSlideTimings != _newUseSlideTimings ||
        p.ShowWithAnimation != _newShowWithAnimation ||
        p.ShowWithNarration != _newShowWithNarration ||
        p.ShowMediaControls != _newShowMediaControls ||
        p.ShowMasterShapes != _newShowMasterShapes ||
        p.LoopUntilStopped != _newLoopUntilStopped ||
        p.ShowType != _newShowType ||
        p.ShowBrowseScrollbar != _newShowBrowseScrollbar ||
        p.KioskRestartAfterMilliseconds != _newKioskRestartAfterMilliseconds;

    public void Apply(Presentation p)
    {
        p.UseSlideTimings = _newUseSlideTimings;
        p.ShowWithAnimation = _newShowWithAnimation;
        p.ShowWithNarration = _newShowWithNarration;
        p.ShowMediaControls = _newShowMediaControls;
        p.ShowMasterShapes = _newShowMasterShapes;
        p.LoopUntilStopped = _newLoopUntilStopped;
        p.ShowType = _newShowType;
        p.ShowBrowseScrollbar = _newShowBrowseScrollbar;
        p.KioskRestartAfterMilliseconds = _newKioskRestartAfterMilliseconds;
    }

    public void Revert(Presentation p)
    {
        p.UseSlideTimings = _oldUseSlideTimings;
        p.ShowWithAnimation = _oldShowWithAnimation;
        p.ShowWithNarration = _oldShowWithNarration;
        p.ShowMediaControls = _oldShowMediaControls;
        p.ShowMasterShapes = _oldShowMasterShapes;
        p.LoopUntilStopped = _oldLoopUntilStopped;
        p.ShowType = _oldShowType;
        p.ShowBrowseScrollbar = _oldShowBrowseScrollbar;
        p.KioskRestartAfterMilliseconds = _oldKioskRestartAfterMilliseconds;
    }
}

/// <summary>Sets whether a slide object, including a grouped child, is hidden in the editing view.</summary>
public sealed class SetShapeHiddenCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly bool _newValue;
    private bool _oldValue;

    public SetShapeHiddenCommand(int slideIndex, uint shapeId, bool hidden)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newValue = hidden;
    }

    public string Label => _newValue ? "Hide Object" : "Show Object";

    public bool HasEffect(Presentation p) =>
        TryGetShape(p, out var shape) && shape.IsHidden != _newValue;

    public void Apply(Presentation p)
    {
        if (!TryGetShape(p, out var shape))
            return;

        _oldValue = shape.IsHidden;
        shape.IsHidden = _newValue;
    }

    public void Revert(Presentation p)
    {
        if (TryGetShape(p, out var shape))
            shape.IsHidden = _oldValue;
    }

    private bool TryGetShape(Presentation p, out SlideShape shape)
    {
        shape = null!;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return false;

        shape = FindShape(p.Slides[_slideIndex].Shapes, _shapeId)!;
        return shape is not null;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}

/// <summary>Edits the supported native PowerPoint Zoom properties as one undoable operation.</summary>
public sealed class SetZoomObjectPropertiesCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ZoomObjectProperties _newValue;
    private ZoomObjectProperties? _oldValue;
    private string? _oldRawXml;

    public SetZoomObjectPropertiesCommand(
        int slideIndex,
        uint shapeId,
        ZoomObjectProperties properties)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newValue = Validate(properties);
    }

    public string Label => "Format Zoom";

    public bool HasEffect(Presentation p) =>
        TryGetZoom(p, out var shape)
        && shape.PreservedObject is { } info
        && !Equals(info.ZoomProperties, _newValue);

    public void Apply(Presentation p)
    {
        if (!TryGetZoom(p, out var shape) || shape.PreservedObject is not { } info)
            return;

        _oldValue = info.ZoomProperties;
        _oldRawXml = info.RawXml;
        if (TryPatchRawXml(info.RawXml, _newValue, out var rawXml))
            info.RawXml = rawXml;
        info.ZoomProperties = _newValue;
    }

    public void Revert(Presentation p)
    {
        if (!TryGetZoom(p, out var shape) || shape.PreservedObject is not { } info)
            return;

        info.ZoomProperties = _oldValue;
        if (_oldRawXml is not null)
            info.RawXml = _oldRawXml;
    }

    private bool TryGetZoom(Presentation p, out SlideShape shape)
    {
        shape = null!;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return false;

        shape = FindShape(p.Slides[_slideIndex].Shapes, _shapeId)!;
        return shape is { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom };
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static ZoomObjectProperties Validate(ZoomObjectProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.FrameBorderGradient is not null && properties.FrameBorderPattern is not null)
            throw new ArgumentException(
                "A Zoom frame border cannot use both gradient and pattern fills.", nameof(properties));
        if (properties.FrameBorderNoFill == true
            && (properties.FrameBorderColor is not null
                || properties.FrameBorderGradient is not null
                || properties.FrameBorderPattern is not null
                || properties.FrameBorderThemeColor is not null))
            throw new ArgumentException(
                "A Zoom frame border cannot combine no-fill with another fill.", nameof(properties));
        if (properties.FrameBorderThemeColor is not null
            && (properties.FrameBorderColor is not null
                || properties.FrameBorderGradient is not null
                || properties.FrameBorderPattern is not null))
            throw new ArgumentException(
                "A Zoom frame border cannot combine a theme color with another fill.", nameof(properties));
        if (properties.FrameBorderShadowEnabled == false && properties.FrameBorderShadow is not null)
            throw new ArgumentException(
                "A disabled Zoom frame shadow cannot carry shadow values.", nameof(properties));
        if (properties.FrameBorderGlowEnabled == false && properties.FrameBorderGlow is not null)
            throw new ArgumentException(
                "A disabled Zoom frame glow cannot carry glow values.", nameof(properties));
        if (properties.FrameBorderSoftEdgeEnabled == false && properties.FrameBorderSoftEdge is not null)
            throw new ArgumentException(
                "A disabled Zoom frame soft edge cannot carry soft-edge values.", nameof(properties));
        if (properties.FrameBorderReflectionEnabled == false && properties.FrameBorderReflection is not null)
            throw new ArgumentException(
                "A disabled Zoom frame reflection cannot carry reflection values.", nameof(properties));
        if (properties.ImageType is not null
            && !string.Equals(properties.ImageType, "preview", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(properties.ImageType, "cover", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Zoom imageType must be 'preview' or 'cover'.", nameof(properties));

        return properties with
        {
            ImageType = properties.ImageType?.Trim().ToLowerInvariant(),
            TransitionDuration = properties.TransitionDuration?.Trim(),
            CropLeft = ValidateCrop(properties.CropLeft, nameof(properties.CropLeft)),
            CropTop = ValidateCrop(properties.CropTop, nameof(properties.CropTop)),
            CropRight = ValidateCrop(properties.CropRight, nameof(properties.CropRight)),
            CropBottom = ValidateCrop(properties.CropBottom, nameof(properties.CropBottom)),
            FrameBorderColor = ValidateFrameBorderColor(properties.FrameBorderColor),
            FrameBorderWidthEmu = ValidateFrameBorderWidth(properties.FrameBorderWidthEmu),
            FrameBorderDash = ValidateFrameBorderDash(properties.FrameBorderDash),
            FrameGeometry = ValidateFrameGeometry(properties.FrameGeometry),
            FrameBorderGradient = ValidateFrameBorderGradient(properties.FrameBorderGradient),
            FrameBorderPattern = ValidateFrameBorderPattern(properties.FrameBorderPattern),
            FrameBorderNoFill = properties.FrameBorderNoFill == true ? true : null,
            FrameBorderThemeColor = ValidateFrameBorderThemeColor(properties.FrameBorderThemeColor),
            FrameBorderShadow = ValidateFrameBorderShadow(properties.FrameBorderShadow),
            FrameBorderShadowEnabled = properties.FrameBorderShadowEnabled == false
                ? false
                : properties.FrameBorderShadow is not null ? true : null,
            FrameBorderGlow = ValidateFrameBorderGlow(properties.FrameBorderGlow),
            FrameBorderGlowEnabled = properties.FrameBorderGlowEnabled == false
                ? false
                : properties.FrameBorderGlow is not null ? true : null,
            FrameBorderSoftEdge = ValidateFrameBorderSoftEdge(properties.FrameBorderSoftEdge),
            FrameBorderSoftEdgeEnabled = properties.FrameBorderSoftEdgeEnabled == false
                ? false
                : properties.FrameBorderSoftEdge is not null ? true : null,
            FrameBorderReflection = ValidateFrameBorderReflection(properties.FrameBorderReflection),
            FrameBorderReflectionEnabled = properties.FrameBorderReflectionEnabled == false
                ? false
                : properties.FrameBorderReflection is not null ? true : null,
        };
    }

    private static ZoomFrameBorderShadow? ValidateFrameBorderShadow(ZoomFrameBorderShadow? value)
    {
        if (value is null)
            return null;

        var color = value.Color.Trim().TrimStart('#');
        if (color.Length != 6 || !color.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "Zoom frame shadow color must be a six-digit RGB value.", nameof(value));
        if (value.Alpha is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(value), value.Alpha,
                "Zoom frame shadow alpha must be between 0 and 100000.");
        if (value.BlurRadiusEmu < 0 || value.DistanceEmu < 0)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Zoom frame shadow blur and distance cannot be negative.");
        if (value.Direction is < 0 or > 21600000)
            throw new ArgumentOutOfRangeException(nameof(value), value.Direction,
                "Zoom frame shadow direction must be between 0 and 21600000.");

        return value with { Color = color.ToUpperInvariant() };
    }

    private static ZoomFrameBorderGlow? ValidateFrameBorderGlow(ZoomFrameBorderGlow? value)
    {
        if (value is null)
            return null;

        var color = value.Color.Trim().TrimStart('#');
        if (color.Length != 6 || !color.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "Zoom frame glow color must be a six-digit RGB value.", nameof(value));
        if (value.Alpha is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(value), value.Alpha,
                "Zoom frame glow alpha must be between 0 and 100000.");
        if (value.RadiusEmu < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value.RadiusEmu,
                "Zoom frame glow radius cannot be negative.");

        return value with { Color = color.ToUpperInvariant() };
    }

    private static ZoomFrameBorderSoftEdge? ValidateFrameBorderSoftEdge(ZoomFrameBorderSoftEdge? value)
    {
        if (value is null)
            return null;
        if (value.RadiusEmu < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value.RadiusEmu,
                "Zoom frame soft-edge radius cannot be negative.");
        return value;
    }

    private static ZoomFrameBorderReflection? ValidateFrameBorderReflection(ZoomFrameBorderReflection? value)
    {
        if (value is null)
            return null;
        if (value.Alpha is < 0 or > 100000
            || value.BlurRadiusEmu < 0
            || value.DistanceEmu < 0
            || value.Direction is < 0 or > 21600000
            || value.ScaleY is < -100000 or > 100000
            || value.ScaleY == 0
            || value.EndPosition is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Zoom frame reflection values are outside the DrawingML range.");
        return value;
    }

    private static ZoomFrameBorderGradient? ValidateFrameBorderGradient(
        ZoomFrameBorderGradient? value)
    {
        if (value is null)
            return null;

        static string NormalizeColor(string color, string parameterName)
        {
            var normalized = color.Trim().TrimStart('#');
            if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
                throw new ArgumentException(
                    "Zoom frame gradient colors must be six-digit RGB values.", parameterName);
            return normalized.ToUpperInvariant();
        }

        if (value.Angle is < 0 or > 21_600_000)
            throw new ArgumentOutOfRangeException(nameof(value), value.Angle,
                "Zoom frame gradient angle must be between 0 and 360 degrees.");

        return value with
        {
            StartColor = NormalizeColor(value.StartColor, nameof(value.StartColor)),
            EndColor = NormalizeColor(value.EndColor, nameof(value.EndColor)),
        };
    }

    private static ZoomFrameBorderPattern? ValidateFrameBorderPattern(
        ZoomFrameBorderPattern? value)
    {
        if (value is null)
            return null;

        var preset = ZoomFrameBorderPatternCatalog.Normalize(value.Preset)
            ?? throw new ArgumentException(
                "Zoom frame border pattern preset is not supported.", nameof(value));

        static string NormalizeColor(string color, string parameterName)
        {
            var normalized = color.Trim().TrimStart('#');
            if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
                throw new ArgumentException(
                    "Zoom frame pattern colors must be six-digit RGB values.", parameterName);
            return normalized.ToUpperInvariant();
        }

        return value with
        {
            Preset = preset,
            ForegroundColor = NormalizeColor(value.ForegroundColor, nameof(value.ForegroundColor)),
            BackgroundColor = NormalizeColor(value.BackgroundColor, nameof(value.BackgroundColor)),
        };
    }

    private static string? ValidateFrameGeometry(string? value)
    {
        if (value is null)
            return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "rect" => "rect",
            "roundrect" => "roundRect",
            "ellipse" => "ellipse",
            _ => throw new ArgumentException(
                "Zoom frame geometry must be rect, roundRect, or ellipse.", nameof(value)),
        };
    }

    private static int? ValidateCrop(int? value, string parameterName)
    {
        if (value is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(parameterName, value,
                "Zoom crop edges must be between 0 and 100000 (thousandths of a percent).");
        return value;
    }

    private static string? ValidateFrameBorderColor(string? value)
    {
        if (value is null or { Length: 0 })
            return value;

        var normalized = value.Trim().TrimStart('#');
        if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException("Zoom frame border color must be a six-digit RGB value.", nameof(value));
        return normalized.ToUpperInvariant();
    }

    private static int? ValidateFrameBorderWidth(int? value)
    {
        if (value is null)
            return null;
        if (value <= 0 || value > 20116800)
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "Zoom frame border width must be between 1 and 20116800 EMU.");
        return value;
    }

    private static OutlineDash? ValidateFrameBorderDash(OutlineDash? value)
    {
        if (value is null)
            return null;
        if (!Enum.IsDefined(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "Zoom frame border dash is not a supported PowerPoint pattern.");
        return value;
    }

    private static ThemeColorSlot? ValidateFrameBorderThemeColor(ThemeColorSlot? value)
    {
        if (value is null)
            return null;
        if (!Enum.IsDefined(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "Zoom frame border theme color is not a supported theme slot.");
        return value;
    }

    private static bool TryPatchRawXml(
        string rawXml,
        ZoomObjectProperties properties,
        out string patchedXml)
    {
        patchedXml = rawXml;
        if (string.IsNullOrWhiteSpace(rawXml))
            return false;

        XElement root;
        try { root = XElement.Parse(rawXml, LoadOptions.PreserveWhitespace); }
        catch { return false; }

        var zoomProperties = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "zmPr",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (zoomProperties.Length == 0)
            return false;

        foreach (var zoomProperty in zoomProperties)
        {
            SetAttribute(zoomProperty, "returnToParent", properties.ReturnToParent);
            SetAttribute(zoomProperty, "imageType", properties.ImageType);
            SetAttribute(zoomProperty, "transitionDur", properties.TransitionDuration);
            SetAttribute(zoomProperty, "showBg", properties.ShowBackground);
            SetCrop(zoomProperty, properties);
        ZoomFrameBorderXml.Set(zoomProperty, properties.FrameBorderColor,
            properties.FrameBorderWidthEmu, properties.FrameBorderDash,
            properties.FrameBorderGradient,
            properties.FrameBorderPattern,
            properties.FrameBorderNoFill,
            properties.FrameBorderThemeColor,
            properties.FrameBorderShadow,
            properties.FrameBorderShadowEnabled,
            properties.FrameBorderGlow,
            properties.FrameBorderGlowEnabled,
            properties.FrameBorderSoftEdge,
            properties.FrameBorderSoftEdgeEnabled,
            properties.FrameBorderReflection,
            properties.FrameBorderReflectionEnabled);
            ZoomFrameGeometryXml.Set(zoomProperty, properties.FrameGeometry);
        }
        patchedXml = root.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    private static void SetAttribute(XElement element, string name, bool? value)
    {
        if (value is null)
            element.Attribute(name)?.Remove();
        else
            element.SetAttributeValue(name, value.Value ? "1" : "0");
    }

    private static void SetAttribute(XElement element, string name, string? value)
    {
        if (value is null)
            element.Attribute(name)?.Remove();
        else
            element.SetAttributeValue(name, value);
    }

    private static void SetCrop(XElement zoomProperty, ZoomObjectProperties properties)
    {
        XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var blipFill = zoomProperty.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "blipFill",
                StringComparison.OrdinalIgnoreCase));
        if (blipFill is null)
            return;

        var values = new[] { properties.CropLeft, properties.CropTop, properties.CropRight, properties.CropBottom };
        var srcRect = blipFill.Element(drawing + "srcRect");
        if (values.All(value => value is null))
        {
            srcRect?.Remove();
            return;
        }

        srcRect ??= new XElement(drawing + "srcRect");
        SetAttribute(srcRect, "l", properties.CropLeft);
        SetAttribute(srcRect, "t", properties.CropTop);
        SetAttribute(srcRect, "r", properties.CropRight);
        SetAttribute(srcRect, "b", properties.CropBottom);
        if (srcRect.Parent is null)
        {
            var trailingContent = blipFill.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "tile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(element.Name.LocalName, "stretch", StringComparison.OrdinalIgnoreCase));
            if (trailingContent is null)
                blipFill.Add(srcRect);
            else
                trailingContent.AddBeforeSelf(srcRect);
        }
    }

    private static void SetAttribute(XElement element, string name, int? value)
    {
        if (value is null)
            element.Attribute(name)?.Remove();
        else
            element.SetAttributeValue(name, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>Replaces one media object's caption-track collection as one undoable edit.</summary>
public sealed class SetMediaCaptionTracksCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly IReadOnlyList<MediaCaptionTrackInfo> _before;
    private readonly IReadOnlyList<MediaCaptionTrackInfo> _after;

    public SetMediaCaptionTracksCommand(
        int slideIndex,
        uint shapeId,
        IEnumerable<MediaCaptionTrackInfo> before,
        IEnumerable<MediaCaptionTrackInfo> after)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _before = CloneTracks(before);
        _after = CloneTracks(after);
    }

    public string Label => "Edit Media Captions";

    public bool HasEffect(Presentation presentation)
    {
        var media = FindMedia(presentation);
        return media is not null && !TracksEqual(media.CaptionTracks, _after);
    }

    public void Apply(Presentation presentation) => ReplaceTracks(FindMedia(presentation), _after);

    public void Revert(Presentation presentation) => ReplaceTracks(FindMedia(presentation), _before);

    private MediaInfo? FindMedia(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return FindMedia(presentation.Slides[_slideIndex].Shapes);
    }

    private MediaInfo? FindMedia(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == _shapeId && shape.Kind == SlideShapeKind.Media)
                return shape.Media;
            if (shape.Children.Count > 0 && FindMedia(shape.Children) is { } child)
                return child;
        }

        return null;
    }

    private static void ReplaceTracks(MediaInfo? media, IReadOnlyList<MediaCaptionTrackInfo> tracks)
    {
        if (media is null)
            return;

        media.CaptionTracks.Clear();
        media.CaptionTracks.AddRange(CloneTracks(tracks));
    }

    private static List<MediaCaptionTrackInfo> CloneTracks(IEnumerable<MediaCaptionTrackInfo> tracks) =>
        tracks.Select(track => new MediaCaptionTrackInfo
        {
            RelationshipId = track.RelationshipId,
            Source = track.Source,
            Bytes = track.Bytes.ToArray(),
            ContentType = track.ContentType,
            Language = track.Language,
            Label = track.Label,
            IsExternal = track.IsExternal
        }).ToList();

    private static bool TracksEqual(
        IReadOnlyList<MediaCaptionTrackInfo> left,
        IReadOnlyList<MediaCaptionTrackInfo> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.Zip(right).All(pair =>
            pair.First.RelationshipId == pair.Second.RelationshipId
            && pair.First.Source == pair.Second.Source
            && pair.First.ContentType == pair.Second.ContentType
            && pair.First.Language == pair.Second.Language
            && pair.First.Label == pair.Second.Label
            && pair.First.IsExternal == pair.Second.IsExternal
            && pair.First.Bytes.SequenceEqual(pair.Second.Bytes));
    }
}

/// <summary>Changes one media object's authored playback volume as one undoable edit.</summary>
public sealed class SetMediaVolumeCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _before;
    private readonly int _after;

    public SetMediaVolumeCommand(int slideIndex, uint shapeId, int before, int after)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _before = Math.Clamp(before, 0, 100);
        _after = Math.Clamp(after, 0, 100);
    }

    public string Label => "Set Media Volume";

    public bool HasEffect(Presentation presentation)
    {
        var media = FindMedia(presentation);
        return media is not null && media.VolumePercent != _after;
    }

    public void Apply(Presentation presentation) => SetVolume(FindMedia(presentation), _after);

    public void Revert(Presentation presentation) => SetVolume(FindMedia(presentation), _before);

    private MediaInfo? FindMedia(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return FindMedia(presentation.Slides[_slideIndex].Shapes);
    }

    private MediaInfo? FindMedia(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == _shapeId && shape.Kind == SlideShapeKind.Media)
                return shape.Media;
            if (shape.Children.Count > 0 && FindMedia(shape.Children) is { } child)
                return child;
        }

        return null;
    }

    private static void SetVolume(MediaInfo? media, int value)
    {
        if (media is not null)
            media.VolumePercent = Math.Clamp(value, 0, 100);
    }
}

/// <summary>Changes one media object's authored start mode and loop policy as one undoable edit.</summary>
public sealed class SetMediaPlaybackOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly MediaPlaybackStartMode _beforeStartMode;
    private readonly MediaPlaybackStartMode _afterStartMode;
    private readonly bool _beforeLoop;
    private readonly bool _afterLoop;
    private readonly bool _beforeShowWhenStopped;
    private readonly bool _afterShowWhenStopped;
    private readonly bool _beforeRewindAfterPlaying;
    private readonly bool _afterRewindAfterPlaying;
    private readonly bool _beforePlayFullScreen;
    private readonly bool _afterPlayFullScreen;
    private readonly int _beforeStopAfterSlides;
    private readonly int _afterStopAfterSlides;

    public SetMediaPlaybackOptionsCommand(
        int slideIndex,
        uint shapeId,
        MediaPlaybackStartMode beforeStartMode,
        bool beforeLoop,
        MediaPlaybackStartMode afterStartMode,
        bool afterLoop,
        bool beforeShowWhenStopped = true,
        bool afterShowWhenStopped = true,
        bool beforeRewindAfterPlaying = false,
        bool afterRewindAfterPlaying = false,
        bool beforePlayFullScreen = false,
        bool afterPlayFullScreen = false,
        int beforeStopAfterSlides = 1,
        int afterStopAfterSlides = 1)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _beforeStartMode = beforeStartMode;
        _beforeLoop = beforeLoop;
        _afterStartMode = afterStartMode;
        _afterLoop = afterLoop;
        _beforeShowWhenStopped = beforeShowWhenStopped;
        _afterShowWhenStopped = afterShowWhenStopped;
        _beforeRewindAfterPlaying = beforeRewindAfterPlaying;
        _afterRewindAfterPlaying = afterRewindAfterPlaying;
        _beforePlayFullScreen = beforePlayFullScreen;
        _afterPlayFullScreen = afterPlayFullScreen;
        _beforeStopAfterSlides = NormalizeSlideCount(beforeStopAfterSlides);
        _afterStopAfterSlides = NormalizeSlideCount(afterStopAfterSlides);
    }

    public string Label => "Set Media Playback Options";

    public bool HasEffect(Presentation presentation)
    {
        var media = FindMedia(presentation);
        return media is not null
            && (media.PlaybackStartMode != _afterStartMode
                || media.Loop != _afterLoop
                || media.ShowWhenStopped != _afterShowWhenStopped
                || media.RewindAfterPlaying != _afterRewindAfterPlaying
                || media.PlayFullScreen != _afterPlayFullScreen
                || media.StopAfterSlides != _afterStopAfterSlides);
    }

    public void Apply(Presentation presentation) => SetOptions(
        FindMedia(presentation), _afterStartMode, _afterLoop, _afterShowWhenStopped, _afterRewindAfterPlaying, _afterPlayFullScreen, _afterStopAfterSlides);

    public void Revert(Presentation presentation) => SetOptions(
        FindMedia(presentation), _beforeStartMode, _beforeLoop, _beforeShowWhenStopped, _beforeRewindAfterPlaying, _beforePlayFullScreen, _beforeStopAfterSlides);

    private MediaInfo? FindMedia(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return FindMedia(presentation.Slides[_slideIndex].Shapes);
    }

    private MediaInfo? FindMedia(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == _shapeId && shape.Kind == SlideShapeKind.Media)
                return shape.Media;
            if (shape.Children.Count > 0 && FindMedia(shape.Children) is { } child)
                return child;
        }

        return null;
    }

    private static void SetOptions(
        MediaInfo? media,
        MediaPlaybackStartMode startMode,
        bool loop,
        bool showWhenStopped,
        bool rewindAfterPlaying,
        bool playFullScreen,
        int stopAfterSlides)
    {
        if (media is null)
            return;

        media.PlaybackStartMode = startMode;
        media.Loop = loop;
        media.ShowWhenStopped = showWhenStopped;
        media.RewindAfterPlaying = rewindAfterPlaying;
        media.PlayFullScreen = playFullScreen && media.IsVideo;
        media.StopAfterSlides = NormalizeSlideCount(stopAfterSlides);
    }

    private static int NormalizeSlideCount(int value) => Math.Max(1, value);
}

/// <summary>Changes one media object's trim and fade timings as one undoable edit.</summary>
public sealed class SetMediaTimingCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly double[] _before;
    private readonly double[] _after;

    public SetMediaTimingCommand(
        int slideIndex,
        uint shapeId,
        double beforeTrimStart,
        double beforeTrimEnd,
        double beforeFadeIn,
        double beforeFadeOut,
        double afterTrimStart,
        double afterTrimEnd,
        double afterFadeIn,
        double afterFadeOut)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _before = Normalize(beforeTrimStart, beforeTrimEnd, beforeFadeIn, beforeFadeOut);
        _after = Normalize(afterTrimStart, afterTrimEnd, afterFadeIn, afterFadeOut);
    }

    public string Label => "Set Media Timing";

    public bool HasEffect(Presentation presentation)
    {
        var media = FindMedia(presentation);
        return media is not null && !Matches(media, _after);
    }

    public void Apply(Presentation presentation) => SetTiming(FindMedia(presentation), _after);

    public void Revert(Presentation presentation) => SetTiming(FindMedia(presentation), _before);

    private MediaInfo? FindMedia(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;
        return FindMedia(presentation.Slides[_slideIndex].Shapes);
    }

    private MediaInfo? FindMedia(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == _shapeId && shape.Kind == SlideShapeKind.Media)
                return shape.Media;
            if (shape.Children.Count > 0 && FindMedia(shape.Children) is { } child)
                return child;
        }
        return null;
    }

    private static double[] Normalize(params double[] values) =>
        values.Select(value => double.IsFinite(value) ? Math.Max(0, value) : 0).ToArray();

    private static bool Matches(MediaInfo media, IReadOnlyList<double> values) =>
        media.TrimStartMilliseconds == values[0]
        && media.TrimEndMilliseconds == values[1]
        && media.FadeInMilliseconds == values[2]
        && media.FadeOutMilliseconds == values[3];

    private static void SetTiming(MediaInfo? media, IReadOnlyList<double> values)
    {
        if (media is null)
            return;
        media.TrimStartMilliseconds = values[0];
        media.TrimEndMilliseconds = values[1];
        media.FadeInMilliseconds = values[2];
        media.FadeOutMilliseconds = values[3];
    }
}

/// <summary>Replaces one media object's named bookmarks as one undoable edit.</summary>
public sealed class SetMediaBookmarksCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly List<MediaBookmarkInfo> _before;
    private readonly List<MediaBookmarkInfo> _after;

    public SetMediaBookmarksCommand(
        int slideIndex,
        uint shapeId,
        IEnumerable<MediaBookmarkInfo> before,
        IEnumerable<MediaBookmarkInfo> after)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _before = Clone(before);
        _after = Clone(after);
    }

    public string Label => "Set Media Bookmarks";

    public bool HasEffect(Presentation presentation)
    {
        var media = FindMedia(presentation);
        return media is not null && !Equal(media.Bookmarks, _after);
    }

    public void Apply(Presentation presentation) => SetBookmarks(FindMedia(presentation), _after);

    public void Revert(Presentation presentation) => SetBookmarks(FindMedia(presentation), _before);

    private MediaInfo? FindMedia(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;
        return FindMedia(presentation.Slides[_slideIndex].Shapes);
    }

    private MediaInfo? FindMedia(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == _shapeId && shape.Kind == SlideShapeKind.Media)
                return shape.Media;
            if (shape.Children.Count > 0 && FindMedia(shape.Children) is { } child)
                return child;
        }
        return null;
    }

    private static List<MediaBookmarkInfo> Clone(IEnumerable<MediaBookmarkInfo> bookmarks) =>
        bookmarks.Select(bookmark => new MediaBookmarkInfo
        {
            Name = bookmark.Name,
            TimeMilliseconds = double.IsFinite(bookmark.TimeMilliseconds)
                ? Math.Max(0, bookmark.TimeMilliseconds)
                : 0
        }).ToList();

    private static bool Equal(IReadOnlyList<MediaBookmarkInfo> left, IReadOnlyList<MediaBookmarkInfo> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.Name == pair.Second.Name
            && pair.First.TimeMilliseconds == pair.Second.TimeMilliseconds);

    private static void SetBookmarks(MediaInfo? media, IReadOnlyList<MediaBookmarkInfo> bookmarks)
    {
        if (media is null)
            return;
        media.Bookmarks.Clear();
        media.Bookmarks.AddRange(Clone(bookmarks));
    }
}

/// <summary>Edits one native Summary Zoom tile's supported format properties.</summary>
public sealed class SetSummaryZoomTilePropertiesCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _sectionId;
    private readonly ZoomObjectProperties _newValue;
    private string? _oldRawXml;

    public SetSummaryZoomTilePropertiesCommand(
        int slideIndex,
        uint shapeId,
        string sectionId,
        ZoomObjectProperties properties)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _sectionId = string.IsNullOrWhiteSpace(sectionId)
            ? throw new ArgumentException("A Summary Zoom tile section id is required.", nameof(sectionId))
            : sectionId.Trim();
        _newValue = Validate(properties);
    }

    public string Label => "Format Summary Zoom Tile";

    public bool HasEffect(Presentation presentation)
    {
        if (!TryGetTarget(presentation, out var info))
            return false;

        return TryPatchRawXml(info.RawXml, out var patched)
            && !string.Equals(info.RawXml, patched, StringComparison.Ordinal);
    }

    public void Apply(Presentation presentation)
    {
        if (!TryGetTarget(presentation, out var info)
            || !TryPatchRawXml(info.RawXml, out var patched))
            return;

        _oldRawXml ??= info.RawXml;
        info.RawXml = patched;
    }

    public void Revert(Presentation presentation)
    {
        if (_oldRawXml is null || !TryGetTarget(presentation, out var info))
            return;

        info.RawXml = _oldRawXml;
    }

    private bool TryGetTarget(Presentation presentation, out PreservedObjectInfo info)
    {
        info = null!;
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return false;

        var shape = FindShape(presentation.Slides[_slideIndex].Shapes, _shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom }
            || shape.PreservedObject is not { } preserved
            || preserved.SummaryZoomTargets.All(target =>
                !string.Equals(target.SectionId, _sectionId, StringComparison.OrdinalIgnoreCase)))
            return false;

        info = preserved;
        return true;
    }

    private bool TryPatchRawXml(string rawXml, out string patchedXml)
    {
        patchedXml = rawXml;
        if (string.IsNullOrWhiteSpace(rawXml))
            return false;

        XDocument document;
        try { document = XDocument.Parse(rawXml, LoadOptions.PreserveWhitespace); }
        catch (XmlException) { return false; }

        var target = document.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "summaryZmObj", StringComparison.OrdinalIgnoreCase)
            && string.Equals(element.Attribute("sectionId")?.Value, _sectionId,
                StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return false;

        var properties = target.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "zmPr", StringComparison.OrdinalIgnoreCase));
        if (properties is null)
        {
            properties = new XElement(target.Name.Namespace + "zmPr");
            target.Add(properties);
        }

        SetAttribute(properties, "returnToParent", _newValue.ReturnToParent);
        SetAttribute(properties, "imageType", _newValue.ImageType);
        SetAttribute(properties, "transitionDur", _newValue.TransitionDuration);
        SetAttribute(properties, "showBg", _newValue.ShowBackground);
        SetCrop(properties, _newValue);
        ZoomFrameBorderXml.Set(properties, _newValue.FrameBorderColor,
            _newValue.FrameBorderWidthEmu, _newValue.FrameBorderDash,
            _newValue.FrameBorderGradient,
            _newValue.FrameBorderPattern,
            _newValue.FrameBorderNoFill,
            _newValue.FrameBorderThemeColor,
            _newValue.FrameBorderShadow,
            _newValue.FrameBorderShadowEnabled,
            _newValue.FrameBorderGlow,
            _newValue.FrameBorderGlowEnabled,
            _newValue.FrameBorderSoftEdge,
            _newValue.FrameBorderSoftEdgeEnabled,
            _newValue.FrameBorderReflection,
            _newValue.FrameBorderReflectionEnabled);
        ZoomFrameGeometryXml.Set(properties, _newValue.FrameGeometry);
        patchedXml = document.Root!.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    private static ZoomObjectProperties Validate(ZoomObjectProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.FrameBorderGradient is not null && properties.FrameBorderPattern is not null)
            throw new ArgumentException(
                "A Zoom frame border cannot use both gradient and pattern fills.", nameof(properties));
        if (properties.FrameBorderNoFill == true
            && (properties.FrameBorderColor is not null
                || properties.FrameBorderGradient is not null
                || properties.FrameBorderPattern is not null
                || properties.FrameBorderThemeColor is not null))
            throw new ArgumentException(
                "A Zoom frame border cannot combine no-fill with another fill.", nameof(properties));
        if (properties.FrameBorderThemeColor is not null
            && (properties.FrameBorderColor is not null
                || properties.FrameBorderGradient is not null
                || properties.FrameBorderPattern is not null))
            throw new ArgumentException(
                "A Zoom frame border cannot combine a theme color with another fill.", nameof(properties));
        if (properties.ImageType is not null
            && !string.Equals(properties.ImageType, "preview", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(properties.ImageType, "cover", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Zoom imageType must be 'preview' or 'cover'.", nameof(properties));

        return properties with
        {
            ImageType = properties.ImageType?.Trim().ToLowerInvariant(),
            TransitionDuration = properties.TransitionDuration?.Trim(),
            FrameBorderColor = ValidateFrameBorderColor(properties.FrameBorderColor),
            FrameBorderWidthEmu = ValidateFrameBorderWidth(properties.FrameBorderWidthEmu),
            FrameBorderDash = ValidateFrameBorderDash(properties.FrameBorderDash),
            FrameGeometry = ValidateFrameGeometry(properties.FrameGeometry),
            FrameBorderGradient = ValidateFrameBorderGradient(properties.FrameBorderGradient),
            FrameBorderPattern = ValidateFrameBorderPattern(properties.FrameBorderPattern),
            FrameBorderNoFill = properties.FrameBorderNoFill == true ? true : null,
            FrameBorderThemeColor = ValidateFrameBorderThemeColor(properties.FrameBorderThemeColor),
        };
    }

    private static ZoomFrameBorderGradient? ValidateFrameBorderGradient(
        ZoomFrameBorderGradient? value)
    {
        if (value is null)
            return null;

        static string NormalizeColor(string color, string parameterName)
        {
            var normalized = color.Trim().TrimStart('#');
            if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
                throw new ArgumentException(
                    "Zoom frame gradient colors must be six-digit RGB values.", parameterName);
            return normalized.ToUpperInvariant();
        }

        if (value.Angle is < 0 or > 21_600_000)
            throw new ArgumentOutOfRangeException(nameof(value), value.Angle,
                "Zoom frame gradient angle must be between 0 and 360 degrees.");

        return value with
        {
            StartColor = NormalizeColor(value.StartColor, nameof(value.StartColor)),
            EndColor = NormalizeColor(value.EndColor, nameof(value.EndColor)),
        };
    }

    private static ZoomFrameBorderPattern? ValidateFrameBorderPattern(
        ZoomFrameBorderPattern? value)
    {
        if (value is null)
            return null;

        var preset = ZoomFrameBorderPatternCatalog.Normalize(value.Preset)
            ?? throw new ArgumentException(
                "Zoom frame border pattern preset is not supported.", nameof(value));

        static string NormalizeColor(string color, string parameterName)
        {
            var normalized = color.Trim().TrimStart('#');
            if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
                throw new ArgumentException(
                    "Zoom frame pattern colors must be six-digit RGB values.", parameterName);
            return normalized.ToUpperInvariant();
        }

        return value with
        {
            Preset = preset,
            ForegroundColor = NormalizeColor(value.ForegroundColor, nameof(value.ForegroundColor)),
            BackgroundColor = NormalizeColor(value.BackgroundColor, nameof(value.BackgroundColor)),
        };
    }

    private static string? ValidateFrameGeometry(string? value)
    {
        if (value is null)
            return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "rect" => "rect",
            "roundrect" => "roundRect",
            "ellipse" => "ellipse",
            _ => throw new ArgumentException(
                "Zoom frame geometry must be rect, roundRect, or ellipse.", nameof(value)),
        };
    }

    private static string? ValidateFrameBorderColor(string? value)
    {
        if (value is null or { Length: 0 })
            return value;

        var normalized = value.Trim().TrimStart('#');
        if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException("Zoom frame border color must be a six-digit RGB value.", nameof(value));
        return normalized.ToUpperInvariant();
    }

    private static int? ValidateFrameBorderWidth(int? value)
    {
        if (value is null)
            return null;
        if (value <= 0 || value > 20116800)
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "Zoom frame border width must be between 1 and 20116800 EMU.");
        return value;
    }

    private static OutlineDash? ValidateFrameBorderDash(OutlineDash? value)
    {
        if (value is null)
            return null;
        if (!Enum.IsDefined(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "Zoom frame border dash is not a supported PowerPoint pattern.");
        return value;
    }

    private static ThemeColorSlot? ValidateFrameBorderThemeColor(ThemeColorSlot? value)
    {
        if (value is null)
            return null;
        if (!Enum.IsDefined(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "Zoom frame border theme color is not a supported theme slot.");
        return value;
    }

    private static void SetAttribute(XElement element, string name, bool? value)
    {
        if (value is null) element.Attribute(name)?.Remove();
        else element.SetAttributeValue(name, value.Value ? "1" : "0");
    }

    private static void SetAttribute(XElement element, string name, string? value)
    {
        if (value is null) element.Attribute(name)?.Remove();
        else element.SetAttributeValue(name, value);
    }

    private static void SetAttribute(XElement element, string name, int? value)
    {
        if (value is null) element.Attribute(name)?.Remove();
        else element.SetAttributeValue(name, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void SetCrop(XElement properties, ZoomObjectProperties value)
    {
        XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var blipFill = properties.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "blipFill", StringComparison.OrdinalIgnoreCase));
        if (blipFill is null)
            return;

        var values = new[] { value.CropLeft, value.CropTop, value.CropRight, value.CropBottom };
        var srcRect = blipFill.Element(drawing + "srcRect");
        if (values.All(item => item is null))
        {
            srcRect?.Remove();
            return;
        }

        srcRect ??= new XElement(drawing + "srcRect");
        SetAttribute(srcRect, "l", value.CropLeft);
        SetAttribute(srcRect, "t", value.CropTop);
        SetAttribute(srcRect, "r", value.CropRight);
        SetAttribute(srcRect, "b", value.CropBottom);
        if (srcRect.Parent is null)
            blipFill.AddFirst(srcRect);
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}

/// <summary>Edits one native Summary Zoom tile's position and scale as one undoable operation.</summary>
public sealed class SetSummaryZoomTileLayoutCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _sectionId;
    private readonly int _offsetFactorX;
    private readonly int _offsetFactorY;
    private readonly int _scaleFactorX;
    private readonly int _scaleFactorY;
    private SummaryZoomTarget? _oldTarget;
    private string? _oldRawXml;

    public SetSummaryZoomTileLayoutCommand(
        int slideIndex,
        uint shapeId,
        string sectionId,
        int offsetFactorX,
        int offsetFactorY,
        int scaleFactorX,
        int scaleFactorY)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _sectionId = string.IsNullOrWhiteSpace(sectionId)
            ? throw new ArgumentException("A Summary Zoom tile section id is required.", nameof(sectionId))
            : sectionId.Trim();
        _offsetFactorX = ValidateOffset(offsetFactorX, nameof(offsetFactorX));
        _offsetFactorY = ValidateOffset(offsetFactorY, nameof(offsetFactorY));
        _scaleFactorX = ValidateScale(scaleFactorX, nameof(scaleFactorX));
        _scaleFactorY = ValidateScale(scaleFactorY, nameof(scaleFactorY));
    }

    public string Label => "Format Summary Zoom Tile";

    public bool HasEffect(Presentation presentation) =>
        TryGetTarget(presentation, out _, out _, out var target)
        && (target.OffsetFactorX != _offsetFactorX
            || target.OffsetFactorY != _offsetFactorY
            || target.ScaleFactorX != _scaleFactorX
            || target.ScaleFactorY != _scaleFactorY);

    public void Apply(Presentation presentation)
    {
        if (!TryGetTarget(presentation, out var info, out var index, out var target))
            return;

        _oldTarget ??= target;
        _oldRawXml ??= info.RawXml;
        info.SummaryZoomTargets[index] = target with
        {
            OffsetFactorX = _offsetFactorX,
            OffsetFactorY = _offsetFactorY,
            ScaleFactorX = _scaleFactorX,
            ScaleFactorY = _scaleFactorY,
        };
        if (TryPatchRawXml(info.RawXml, out var rawXml))
            info.RawXml = rawXml;
    }

    public void Revert(Presentation presentation)
    {
        if (!TryGetTarget(presentation, out var info, out var index, out _)
            || _oldTarget is null)
            return;

        info.SummaryZoomTargets[index] = _oldTarget;
        if (_oldRawXml is not null)
            info.RawXml = _oldRawXml;
    }

    private bool TryGetTarget(
        Presentation presentation,
        out PreservedObjectInfo info,
        out int index,
        out SummaryZoomTarget target)
    {
        info = null!;
        index = -1;
        target = null!;
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return false;

        var shape = FindShape(presentation.Slides[_slideIndex].Shapes, _shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom }
            || shape.PreservedObject is not { } preserved)
            return false;

        index = preserved.SummaryZoomTargets.FindIndex(candidate =>
            string.Equals(candidate.SectionId, _sectionId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        info = preserved;
        target = preserved.SummaryZoomTargets[index];
        return true;
    }

    private bool TryPatchRawXml(string rawXml, out string patchedXml)
    {
        patchedXml = rawXml;
        if (string.IsNullOrWhiteSpace(rawXml))
            return false;

        XDocument document;
        try { document = XDocument.Parse(rawXml, LoadOptions.PreserveWhitespace); }
        catch { return false; }

        var target = document.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "summaryZmObj", StringComparison.OrdinalIgnoreCase)
            && string.Equals(element.Attribute("sectionId")?.Value, _sectionId,
                StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return false;

        target.SetAttributeValue("offsetFactorX", _offsetFactorX);
        target.SetAttributeValue("offsetFactorY", _offsetFactorY);
        target.SetAttributeValue("scaleFactorX", _scaleFactorX);
        target.SetAttributeValue("scaleFactorY", _scaleFactorY);
        patchedXml = document.Root!.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    private static int ValidateOffset(int value, string parameterName)
    {
        if (value is < -100000 or > 100000)
            throw new ArgumentOutOfRangeException(parameterName, value,
                "Summary Zoom offsets must be between -100000 and 100000.");
        return value;
    }

    private static int ValidateScale(int value, string parameterName)
    {
        if (value is < 1 or > 400000)
            throw new ArgumentOutOfRangeException(parameterName, value,
                "Summary Zoom scales must be between 1 and 400000.");
        return value;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}

/// <summary>Replaces the native cover image on a Zoom object or Summary Zoom tile.</summary>
public sealed class SetZoomCoverImageCommand : IPresentationCommand
{
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly byte[] _imageBytes;
    private readonly string _contentType;
    private readonly string? _summarySectionId;
    private readonly bool _useCoverImage;
    private string? _oldRawXml;
    private ZoomObjectProperties? _oldProperties;
    private ImagePart? _oldPicture;
    private Dictionary<string, byte[]>? _oldParts;
    private Dictionary<string, string>? _oldPartContentTypes;
    private Dictionary<string, (string RelType, string TargetPath)>? _oldSlideRels;

    public SetZoomCoverImageCommand(
        int slideIndex,
        uint shapeId,
        byte[] imageBytes,
        string contentType,
        string? summarySectionId = null,
        bool useCoverImage = true)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _imageBytes = imageBytes is { Length: > 0 }
            ? imageBytes.ToArray()
            : throw new ArgumentException("The cover image cannot be empty.", nameof(imageBytes));
        _contentType = NormalizeContentType(contentType);
        _summarySectionId = string.IsNullOrWhiteSpace(summarySectionId)
            ? null
            : summarySectionId.Trim();
        _useCoverImage = useCoverImage;
    }

    public string Label => _useCoverImage
        ? "Set Zoom Cover Image"
        : "Restore Zoom Preview";

    public bool HasEffect(Presentation presentation)
    {
        if (!TryGetZoom(presentation, out _, out var info)
            || !TryParseZoomProperties(info.RawXml, _summarySectionId, out _, out var properties, out _))
            return false;

        var desiredImageType = _useCoverImage ? "cover" : "preview";
        var currentImageType = properties.Attribute("imageType")?.Value;
        return !string.Equals(currentImageType, desiredImageType, StringComparison.OrdinalIgnoreCase)
            || !TryGetCurrentImage(info, properties, out var current)
            || current is null
            || !current.SequenceEqual(_imageBytes);
    }

    public void Apply(Presentation presentation)
    {
        if (!TryGetZoom(presentation, out var shape, out var info)
            || !TryParseZoomProperties(info.RawXml, _summarySectionId, out var document, out var properties, out var blip))
            return;

        _oldRawXml = info.RawXml;
        _oldProperties = info.ZoomProperties;
        _oldPicture = shape.Picture is null
            ? null
            : new ImagePart
            {
                Bytes = shape.Picture.Bytes.ToArray(),
                ContentType = shape.Picture.ContentType,
            };
        _oldParts = CloneBytes(info.Parts);
        _oldPartContentTypes = new Dictionary<string, string>(info.PartContentTypes, StringComparer.OrdinalIgnoreCase);
        _oldSlideRels = new Dictionary<string, (string RelType, string TargetPath)>(info.SlideRels, StringComparer.Ordinal);

        var relId = blip.Attribute(RelationshipAttribute("embed"))?.Value;
        if (string.IsNullOrWhiteSpace(relId))
        {
            relId = NextRelationshipId(info);
            blip.SetAttributeValue(RelationshipAttribute("embed"), relId);
        }

        var mediaPath = BuildMediaPath(shape.Id, _summarySectionId, _contentType);
        if (info.SlideRels.TryGetValue(relId, out var oldRelation)
            && !string.Equals(oldRelation.TargetPath, mediaPath, StringComparison.OrdinalIgnoreCase))
        {
            info.SlideRels[relId] = (ImageRelationshipType, mediaPath);
            RemoveUnreferencedPart(info, oldRelation.TargetPath);
        }
        else
            info.SlideRels[relId] = (ImageRelationshipType, mediaPath);
        info.Parts[mediaPath] = _imageBytes.ToArray();
        info.PartContentTypes[mediaPath] = _contentType;
        properties.SetAttributeValue("imageType", _useCoverImage ? "cover" : "preview");
        if (_summarySectionId is null)
            info.ZoomProperties = (info.ZoomProperties ?? new ZoomObjectProperties()) with
            {
                ImageType = _useCoverImage ? "cover" : "preview",
            };
        info.RawXml = document.Root!.ToString(SaveOptions.DisableFormatting);
        if (_summarySectionId is null)
        {
            shape.Picture = new ImagePart
            {
                Bytes = _imageBytes.ToArray(),
                ContentType = _contentType,
            };
        }
    }

    public void Revert(Presentation presentation)
    {
        if (!TryGetZoom(presentation, out var shape, out var info)
            || _oldRawXml is null
            || _oldParts is null
            || _oldPartContentTypes is null
            || _oldSlideRels is null)
            return;

        info.RawXml = _oldRawXml;
        info.ZoomProperties = _oldProperties;
        shape.Picture = _oldPicture is null
            ? null
            : new ImagePart
            {
                Bytes = _oldPicture.Bytes.ToArray(),
                ContentType = _oldPicture.ContentType,
            };
        Restore(info.Parts, _oldParts);
        Restore(info.PartContentTypes, _oldPartContentTypes);
        Restore(info.SlideRels, _oldSlideRels);
    }

    private bool TryGetZoom(
        Presentation presentation,
        out SlideShape shape,
        out PreservedObjectInfo info)
    {
        shape = null!;
        info = null!;
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return false;

        shape = FindShape(presentation.Slides[_slideIndex].Shapes, _shapeId)!;
        if (shape is not { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom }
            || shape.PreservedObject is not { } preserved)
            return false;

        info = preserved;
        return _summarySectionId is null
            ? info.SummaryZoomTargets.Count == 0
            : info.SummaryZoomTargets.Any(target =>
                string.Equals(target.SectionId, _summarySectionId, StringComparison.OrdinalIgnoreCase));
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static bool TryParseZoomProperties(
        string rawXml,
        string? summarySectionId,
        out XDocument document,
        out XElement properties,
        out XElement blip)
    {
        document = null!;
        properties = null!;
        blip = null!;
        if (string.IsNullOrWhiteSpace(rawXml))
            return false;

        try { document = XDocument.Parse(rawXml, LoadOptions.PreserveWhitespace); }
        catch { return false; }

        XElement? target = null;
        if (summarySectionId is not null)
        {
            target = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "summaryZmObj", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(element.Attribute("sectionId")?.Value, summarySectionId,
                        StringComparison.OrdinalIgnoreCase));
        }

        properties = (target ?? document.Root)?.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "zmPr", StringComparison.OrdinalIgnoreCase))!;
        if (properties is null)
            return false;

        XNamespace p166 = "http://schemas.microsoft.com/office/powerpoint/2016/6/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var blipFill = properties.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "blipFill");
        if (blipFill is null)
        {
            blipFill = new XElement(p166 + "blipFill",
                new XElement(a + "stretch", new XElement(a + "fillRect")));
            properties.Add(blipFill);
        }

        blip = blipFill.Descendants().FirstOrDefault(element => element.Name.LocalName == "blip")!;
        if (blip is null)
        {
            blip = new XElement(a + "blip");
            blipFill.AddFirst(blip);
        }

        return true;
    }

    private static bool TryGetCurrentImage(PreservedObjectInfo info, XElement properties, out byte[]? bytes)
    {
        bytes = null;
        var relId = properties.Descendants()
            .SelectMany(element => element.Attributes())
            .FirstOrDefault(attribute => attribute.Name.LocalName == "embed")?.Value;
        if (string.IsNullOrWhiteSpace(relId) || !info.SlideRels.TryGetValue(relId, out var imageRelation))
            return false;
        if (string.IsNullOrWhiteSpace(imageRelation.TargetPath))
            return false;

        return info.Parts.TryGetValue(imageRelation.TargetPath, out bytes);
    }

    private static void RemoveUnreferencedPart(PreservedObjectInfo info, string targetPath)
    {
        if (info.SlideRels.Values.Any(relation =>
                string.Equals(relation.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase)))
            return;

        info.Parts.Remove(targetPath);
        info.PartContentTypes.Remove(targetPath);
    }

    private static string NextRelationshipId(PreservedObjectInfo info)
    {
        var suffix = 1;
        var id = $"rIdFreePZoomCover{suffix}";
        while (info.SlideRels.ContainsKey(id))
            id = $"rIdFreePZoomCover{++suffix}";
        return id;
    }

    private static XName RelationshipAttribute(string localName) =>
        XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + localName;

    private static string NormalizeContentType(string contentType)
    {
        var normalized = contentType?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.StartsWith("image/", StringComparison.Ordinal))
            throw new ArgumentException("Zoom cover images must use an image content type.", nameof(contentType));
        return normalized;
    }

    private static string BuildMediaPath(uint shapeId, string? summarySectionId, string contentType)
    {
        var targetKey = summarySectionId is null
            ? "single"
            : new string(summarySectionId
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray())
                .Trim('-');
        if (targetKey.Length == 0)
            targetKey = "tile";
        if (targetKey.Length > 48)
            targetKey = targetKey[..48];
        return $"ppt/media/freep-zoom-cover-{shapeId}-{targetKey}{ExtensionFor(contentType)}";
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        "image/webp" => ".webp",
        _ => ".png",
    };

    private static Dictionary<string, byte[]> CloneBytes(Dictionary<string, byte[]> source) =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    private static void Restore<T>(Dictionary<string, T> destination, Dictionary<string, T> source)
    {
        destination.Clear();
        foreach (var pair in source)
            destination[pair.Key] = pair.Value;
    }
}

/// <summary>Renames a slide object, including a grouped child, as one undoable edit.</summary>
public sealed class SetShapeNameCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _newName;
    private string _oldName = string.Empty;

    public SetShapeNameCommand(int slideIndex, uint shapeId, string newName)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newName = newName.Trim();
    }

    public string Label => "Rename Object";

    public bool HasEffect(Presentation p) =>
        TryGetShape(p, out var shape) &&
        _newName.Length > 0 &&
        !string.Equals(shape.Name, _newName, StringComparison.Ordinal);

    public void Apply(Presentation p)
    {
        if (!TryGetShape(p, out var shape))
            return;

        _oldName = shape.Name;
        shape.Name = _newName;
    }

    public void Revert(Presentation p)
    {
        if (TryGetShape(p, out var shape))
            shape.Name = _oldName;
    }

    private bool TryGetShape(Presentation p, out SlideShape shape)
    {
        shape = null!;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return false;

        shape = FindShape(p.Slides[_slideIndex].Shapes, _shapeId)!;
        return shape is not null;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}

/// <summary>
/// Sets the title metadata for a slide. Revert restores the previous title.
/// </summary>
public sealed class SetSlideTitleCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly string _newTitle;
    private string? _oldTitle;

    public SetSlideTitleCommand(int slideIndex, string title)
    {
        _slideIndex = slideIndex;
        _newTitle = title;
    }

    public string Label => "Set Slide Title";

    public bool HasEffect(Presentation p) =>
        _slideIndex >= 0 &&
        _slideIndex < p.Slides.Count &&
        !StringComparer.Ordinal.Equals(p.Slides[_slideIndex].Title, _newTitle);

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        var slide = p.Slides[_slideIndex];
        _oldTitle = slide.Title;
        slide.Title = _newTitle;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        p.Slides[_slideIndex].Title = _oldTitle ?? string.Empty;
    }
}

/// <summary>
/// Assigns a slide to an existing presentation layout. Revert restores the prior layout id.
/// </summary>
public sealed class SetSlideLayoutCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly string _newLayoutId;
    private string? _oldLayoutId;
    private bool _initialized;
    private readonly List<PlaceholderGeometryState> _updatedPlaceholders = new();
    private readonly List<SlideShape> _addedPlaceholders = new();

    public SetSlideLayoutCommand(int slideIndex, string layoutId)
    {
        _slideIndex = slideIndex;
        _newLayoutId = layoutId;
    }

    public string Label => "Set Slide Layout";

    public bool HasEffect(Presentation p) =>
        _slideIndex >= 0 &&
        _slideIndex < p.Slides.Count &&
        p.Layouts.Any(layout => StringComparer.Ordinal.Equals(layout.Id, _newLayoutId)) &&
        !StringComparer.Ordinal.Equals(p.Slides[_slideIndex].LayoutId, _newLayoutId);

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        if (!p.Layouts.Any(layout => StringComparer.Ordinal.Equals(layout.Id, _newLayoutId)))
        {
            return;
        }

        var slide = p.Slides[_slideIndex];
        var layout = p.Layouts.First(layout =>
            StringComparer.Ordinal.Equals(layout.Id, _newLayoutId));

        if (_initialized)
        {
            slide.LayoutId = _newLayoutId;
            foreach (var state in _updatedPlaceholders)
                state.ApplyTargetGeometry();

            foreach (var placeholder in _addedPlaceholders)
            {
                if (!slide.Shapes.Contains(placeholder))
                    slide.Shapes.Add(placeholder);
            }

            return;
        }

        _oldLayoutId = slide.LayoutId;
        slide.LayoutId = _newLayoutId;

        foreach (var shape in slide.Shapes.ToList())
        {
            var target = FindMatchingPlaceholder(layout, shape.Placeholder);
            if (target is null || !HasGeometry(target))
                continue;

            var state = new PlaceholderGeometryState(shape, target);
            _updatedPlaceholders.Add(state);
            state.ApplyTargetGeometry();
        }

        var nextShapeId = NextShapeId(slide);
        foreach (var target in layout.Placeholders)
        {
            if (!HasGeometry(target) || target.Placeholder is null ||
                slide.Shapes.Any(shape => MatchesPlaceholder(target.Placeholder, shape.Placeholder)))
            {
                continue;
            }

            var placeholder = SlideCloner.CloneShape(target);
            placeholder.Id = nextShapeId++;
            placeholder.TextBody = null;
            placeholder.Name = string.IsNullOrWhiteSpace(target.Name)
                ? $"Placeholder {placeholder.Id}"
                : target.Name;
            slide.Shapes.Add(placeholder);
            _addedPlaceholders.Add(placeholder);
        }

        _initialized = true;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        var slide = p.Slides[_slideIndex];
        slide.LayoutId = _oldLayoutId;

        foreach (var state in _updatedPlaceholders)
            state.RestoreOriginalGeometry();

        foreach (var placeholder in _addedPlaceholders)
            slide.Shapes.Remove(placeholder);
    }

    private static SlideShape? FindMatchingPlaceholder(
        SlideLayout layout,
        Placeholder? target) =>
        target is null
            ? null
            : layout.Placeholders.FirstOrDefault(candidate =>
                MatchesPlaceholder(candidate.Placeholder, target));

    private static bool MatchesPlaceholder(Placeholder? candidate, Placeholder? target)
    {
        if (candidate is null || target is null || candidate.Idx != target.Idx)
            return false;

        if (candidate.Type == target.Type)
            return true;

        var title = candidate.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle &&
                    target.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle;
        if (title)
            return true;

        return IsContentPlaceholder(candidate.Type) && IsContentPlaceholder(target.Type);
    }

    private static bool IsContentPlaceholder(PlaceholderType type) => type is
        PlaceholderType.Body or PlaceholderType.Object or PlaceholderType.Chart or
        PlaceholderType.Table or PlaceholderType.ClipArt or PlaceholderType.Diagram or
        PlaceholderType.Media or PlaceholderType.Picture;

    private static bool HasGeometry(SlideShape shape) =>
        shape.ExtentCxEmu > 0 || shape.ExtentCyEmu > 0 || shape.HasExplicitZeroExtentTransform;

    private static uint NextShapeId(Slide slide)
    {
        var max = slide.Shapes
            .SelectMany(EnumerateShapes)
            .Select(shape => shape.Id)
            .DefaultIfEmpty(0u)
            .Max();
        return max == uint.MaxValue ? 1 : max + 1;
    }

    private static IEnumerable<SlideShape> EnumerateShapes(SlideShape shape)
    {
        yield return shape;
        foreach (var child in shape.Children)
        foreach (var descendant in EnumerateShapes(child))
            yield return descendant;
    }

    private sealed class PlaceholderGeometryState
    {
        private readonly SlideShape _shape;
        private readonly SlideShape _target;
        private readonly long _offsetX;
        private readonly long _offsetY;
        private readonly long _extentCx;
        private readonly long _extentCy;
        private readonly double _rotation;
        private readonly bool _flipH;
        private readonly bool _flipV;
        private readonly bool _explicitZero;

        public PlaceholderGeometryState(SlideShape shape, SlideShape target)
        {
            _shape = shape;
            _target = target;
            _offsetX = shape.OffsetXEmu;
            _offsetY = shape.OffsetYEmu;
            _extentCx = shape.ExtentCxEmu;
            _extentCy = shape.ExtentCyEmu;
            _rotation = shape.RotationDeg;
            _flipH = shape.FlipH;
            _flipV = shape.FlipV;
            _explicitZero = shape.HasExplicitZeroExtentTransform;
        }

        public void ApplyTargetGeometry()
        {
            _shape.OffsetXEmu = _target.OffsetXEmu;
            _shape.OffsetYEmu = _target.OffsetYEmu;
            _shape.ExtentCxEmu = _target.ExtentCxEmu;
            _shape.ExtentCyEmu = _target.ExtentCyEmu;
            _shape.RotationDeg = _target.RotationDeg;
            _shape.FlipH = _target.FlipH;
            _shape.FlipV = _target.FlipV;
            _shape.HasExplicitZeroExtentTransform = _target.HasExplicitZeroExtentTransform;
        }

        public void RestoreOriginalGeometry()
        {
            _shape.OffsetXEmu = _offsetX;
            _shape.OffsetYEmu = _offsetY;
            _shape.ExtentCxEmu = _extentCx;
            _shape.ExtentCyEmu = _extentCy;
            _shape.RotationDeg = _rotation;
            _shape.FlipH = _flipH;
            _shape.FlipV = _flipV;
            _shape.HasExplicitZeroExtentTransform = _explicitZero;
        }
    }
}

internal static class ShapeHelper
{
    internal static SlideShape? Find(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return Find(p.Slides[slideIndex].Shapes, shapeId);
    }

    internal static SlideShape? Find(Slide slide, uint shapeId) =>
        Find(slide.Shapes, shapeId);
    private static SlideShape? Find(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && Find(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    internal static List<SlideShape>? Shapes(Presentation p, int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return p.Slides[slideIndex].Shapes;
    }

    internal static IEnumerable<SlideShape> All(Presentation p, int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count)
            yield break;

        foreach (var shape in All(p.Slides[slideIndex].Shapes))
            yield return shape;
    }

    private static IEnumerable<SlideShape> All(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in All(shape.Children))
                yield return child;
        }
    }

    internal static List<SlideShape>? FindContainingList(
        Presentation p,
        int slideIndex,
        uint shapeId)
    {
        var shapes = Shapes(p, slideIndex);
        return shapes is null ? null : FindContainingList(shapes, shapeId);
    }

    private static List<SlideShape>? FindContainingList(
        List<SlideShape> shapes,
        uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
            {
                return shapes;
            }

            if (shape.Children.Count > 0 &&
                FindContainingList(shape.Children, shapeId) is { } childList)
            {
                return childList;
            }
        }

        return null;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SHAPE COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>Adds <paramref name="shape"/> to the slide at <paramref name="slideIndex"/>.</summary>
public sealed class AddShapeCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly SlideShape _shape;

    public AddShapeCommand(int slideIndex, SlideShape shape)
    {
        _slideIndex = slideIndex;
        _shape      = shape;
    }

    public string Label => "Add Shape";
    public void Apply(Presentation p)  => ShapeHelper.Shapes(p, _slideIndex)?.Add(_shape);
    public void Revert(Presentation p) => ShapeHelper.Shapes(p, _slideIndex)?.Remove(_shape);
}

/// <summary>
/// Changes one AutoShape's preset geometry while preserving its authored frame, text, and style.
/// The old preset guides/custom paths are captured so the operation is a single undoable edit.
/// </summary>
public sealed class ChangeAutoShapeKindCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly DrawingShapeKind _newKind;
    private DrawingShapeKind _oldKind;
    private Dictionary<string, double>? _oldAdjustments;
    private List<CustomGeometryPath>? _oldCustomGeometry;
    private List<CustomGeometryConnectionSite>? _oldCustomConnectionSites;

    public ChangeAutoShapeKindCommand(int slideIndex, uint shapeId, DrawingShapeKind newKind)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newKind = newKind;
    }

    public string Label => "Change Shape";

    public bool HasEffect(Presentation presentation) =>
        ShapeHelper.Find(presentation, _slideIndex, _shapeId) is
        { Kind: SlideShapeKind.AutoShape } shape &&
        shape.AutoShapeKind != _newKind;

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is not { Kind: SlideShapeKind.AutoShape })
            return;

        _oldKind = shape.AutoShapeKind;
        _oldAdjustments = new Dictionary<string, double>(shape.PresetGeometryAdjustments,
            StringComparer.OrdinalIgnoreCase);
        _oldCustomGeometry = CloneCustomGeometry(shape.CustomGeometry);
        _oldCustomConnectionSites = CloneCustomConnectionSites(shape.CustomConnectionSites);
        shape.AutoShapeKind = _newKind;
        shape.PresetGeometryAdjustments.Clear();
        shape.CustomGeometry.Clear();
        shape.CustomConnectionSites.Clear();
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is not { Kind: SlideShapeKind.AutoShape })
            return;

        shape.AutoShapeKind = _oldKind;
        shape.PresetGeometryAdjustments.Clear();
        if (_oldAdjustments is not null)
        {
            foreach (var pair in _oldAdjustments)
                shape.PresetGeometryAdjustments[pair.Key] = pair.Value;
        }

        shape.CustomGeometry.Clear();
        if (_oldCustomGeometry is not null)
            shape.CustomGeometry.AddRange(CloneCustomGeometry(_oldCustomGeometry));
        shape.CustomConnectionSites.Clear();
        if (_oldCustomConnectionSites is not null)
            shape.CustomConnectionSites.AddRange(CloneCustomConnectionSites(_oldCustomConnectionSites));
    }

    private static List<CustomGeometryPath> CloneCustomGeometry(IEnumerable<CustomGeometryPath> paths) =>
        paths.Select(path =>
        {
            var copy = new CustomGeometryPath
            {
                PathW = path.PathW,
                PathH = path.PathH,
                Fill = path.Fill,
                Stroke = path.Stroke,
            };
            copy.Segments.AddRange(path.Segments);
            return copy;
        }).ToList();

    private static List<CustomGeometryConnectionSite> CloneCustomConnectionSites(
        IEnumerable<CustomGeometryConnectionSite> sites) =>
        sites.Select(site => new CustomGeometryConnectionSite
        {
            X = site.X,
            Y = site.Y,
            Angle = site.Angle,
        }).ToList();
}

/// <summary>
/// Replaces one SmartArt graphic with ordinary slide shapes at the same z-order position.
/// This is the model-side operation behind PowerPoint's Convert to Shapes command.
/// </summary>
public sealed class ConvertSmartArtToShapesCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _smartArtId;
    private readonly SlideShape _original;
    private readonly List<SlideShape> _converted;
    private int _index = -1;

    public ConvertSmartArtToShapesCommand(
        int slideIndex,
        uint smartArtId,
        SlideShape original,
        IEnumerable<SlideShape> converted)
    {
        _slideIndex = slideIndex;
        _smartArtId = smartArtId;
        _original = SlideCloner.CloneShape(original);
        _converted = converted.Select(SlideCloner.CloneShape).ToList();
    }

    public string Label => "Convert SmartArt to Shapes";

    public bool HasEffect(Presentation presentation) =>
        ShapeHelper.Find(presentation, _slideIndex, _smartArtId) is { Kind: SlideShapeKind.SmartArt } &&
        _converted.Count > 0;

    public void Apply(Presentation presentation)
    {
        var shapes = ShapeHelper.FindContainingList(presentation, _slideIndex, _smartArtId);
        if (shapes is null || _converted.Count == 0)
            return;

        _index = shapes.FindIndex(shape => shape.Id == _smartArtId);
        if (_index < 0)
            return;

        shapes.RemoveAt(_index);
        shapes.InsertRange(_index, _converted);
    }

    public void Revert(Presentation presentation)
    {
        if (_converted.Count == 0 || _index < 0)
            return;

        var firstConverted = _converted[0].Id;
        var currentShapes = ShapeHelper.FindContainingList(presentation, _slideIndex, firstConverted);
        if (currentShapes is null)
            return;

        var currentIndex = currentShapes.FindIndex(shape => shape.Id == firstConverted);
        if (currentIndex < 0)
            return;

        var count = Math.Min(_converted.Count, currentShapes.Count - currentIndex);
        currentShapes.RemoveRange(currentIndex, count);
        currentShapes.Insert(Math.Clamp(currentIndex, 0, currentShapes.Count), _original);
    }
}

/// <summary>
/// Removes the shape identified by <paramref name="shapeId"/> from the slide.
/// Captures the shape + its z-index for undo.
/// </summary>
public sealed class DeleteShapeCommand : IPresentationCommand
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.openxmlformats.org/presentationml/2006/main";

    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private SlideShape? _captured;
    private int         _capturedIndex;
    private List<ShapeAnimation>? _capturedAnimations;
    private string? _capturedBuildListXml;
    private List<(SlideShape Connector, ConnectorAttachment? Start, ConnectorAttachment? End)>?
        _capturedConnectorAttachments;

    public DeleteShapeCommand(int slideIndex, uint shapeId)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
    }

    public string Label => "Delete Shape";

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper.Shapes(p, _slideIndex);
        if (shapes is null) return;
        _capturedIndex = shapes.FindIndex(s => s.Id == _shapeId);
        if (_capturedIndex < 0) return;
        if (!ChartHelper.IsObjectEditable(shapes[_capturedIndex])) return;
        _captured = shapes[_capturedIndex];

        var slide = p.Slides[_slideIndex];
        _capturedAnimations = slide.Animations.ToList();
        _capturedBuildListXml = slide.AnimationBuildListXml;
        _capturedConnectorAttachments = ShapeHelper.All(p, _slideIndex)
            .Where(shape => shape.Kind == SlideShapeKind.Connector &&
                (shape.ConnectionStart?.ShapeId == _shapeId ||
                 shape.ConnectionEnd?.ShapeId == _shapeId))
            .Select(connector =>
                (connector, connector.ConnectionStart, connector.ConnectionEnd))
            .ToList();

        shapes.RemoveAt(_capturedIndex);
        slide.Animations.RemoveAll(animation => animation.ShapeId == _shapeId);
        slide.AnimationBuildListXml = RemoveBuildListEntriesForShape(
            slide.AnimationBuildListXml,
            _shapeId);

        if (_capturedConnectorAttachments is not null)
        {
            foreach (var (connector, _, _) in _capturedConnectorAttachments)
            {
                if (connector.ConnectionStart?.ShapeId == _shapeId)
                    connector.ConnectionStart = null;
                if (connector.ConnectionEnd?.ShapeId == _shapeId)
                    connector.ConnectionEnd = null;
            }
        }
    }

    public void Revert(Presentation p)
    {
        if (_captured is null) return;
        var shapes = ShapeHelper.Shapes(p, _slideIndex);
        if (shapes is null) return;
        var idx = Math.Clamp(_capturedIndex, 0, shapes.Count);
        shapes.Insert(idx, _captured);

        var slide = p.Slides[_slideIndex];
        if (_capturedAnimations is not null)
        {
            slide.Animations.Clear();
            slide.Animations.AddRange(_capturedAnimations);
            slide.AnimationBuildListXml = _capturedBuildListXml;
        }

        if (_capturedConnectorAttachments is not null)
        {
            foreach (var (connector, start, end) in _capturedConnectorAttachments)
            {
                connector.ConnectionStart = start;
                connector.ConnectionEnd = end;
            }
        }
    }

    private static string? RemoveBuildListEntriesForShape(string? rawXml, uint shapeId)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return rawXml;

        try
        {
            var root = XElement.Parse(rawXml, LoadOptions.PreserveWhitespace);
            if (root.Name != PresentationNamespace + "bldLst")
                return rawXml;

            var entries = root.Elements(PresentationNamespace + "bldP")
                .Where(entry => uint.TryParse(
                    entry.Attribute("spid")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var entryShapeId) && entryShapeId == shapeId)
                .ToArray();
            foreach (var entry in entries)
                entry.Remove();

            return root.Elements().Any()
                ? root.ToString(SaveOptions.DisableFormatting)
                : null;
        }
        catch (XmlException)
        {
            // Preserve malformed/unmodeled timing payloads rather than destroying source data.
            return rawXml;
        }
    }
}

/// <summary>
/// Translates a shape by (<paramref name="dxEmu"/>, <paramref name="dyEmu"/>).
/// Revert subtracts the same delta.
/// Also re-routes any connectors whose start/end is attached to the moved shape (Wave 23).
/// </summary>
public sealed class MoveShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly long _dx;
    private readonly long _dy;
    private bool _applied;

    // Captured reroute data: (connectorId, oldX, oldY, oldCx, oldCy, oldRoute, newX, newY, newCx, newCy)
    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

    public MoveShapeCommand(int slideIndex, uint shapeId, long dxEmu, long dyEmu)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _dx         = dxEmu;
        _dy         = dyEmu;
    }

    public string Label => "Move Shape";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null || !ChartHelper.IsObjectEditable(s)) return;
        s.OffsetXEmu += _dx;
        s.OffsetYEmu += _dy;
        _applied = true;

        // Reroute attached connectors after the shape has moved.
        _rerouteCapture = ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.OffsetXEmu -= _dx;
        s.OffsetYEmu -= _dy;

        // Restore connector bounds captured during Apply.
        RevertReroute(p, _slideIndex, _rerouteCapture);
    }

    internal static List<(uint, long, long, long, long, List<(long X, long Y)>?, long, long, long, long)> ApplyReroute(
        Presentation p, int slideIndex, uint movedShapeId)
    {
        var captures = new List<(uint, long, long, long, long, List<(long X, long Y)>?, long, long, long, long)>();
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return captures;

        var slide = p.Slides[slideIndex];
        foreach (var cmd in ConnectorRouter.BuildRerouteCommands(p, slideIndex, movedShapeId))
        {
            // Find the connector and capture old bounds + old route before applying.
            var c = ShapeHelper.Find(p, slideIndex, cmd.ConnectorId);
            if (c is null) continue;
            long ox = c.OffsetXEmu, oy = c.OffsetYEmu, ocx = c.ExtentCxEmu, ocy = c.ExtentCyEmu;
            var oroute = c.ElbowRoute;
            cmd.Apply(p);
            captures.Add((cmd.ConnectorId, ox, oy, ocx, ocy, oroute, cmd.NewX, cmd.NewY, cmd.NewCx, cmd.NewCy));
        }
        return captures;
    }

    internal static void RevertReroute(
        Presentation p, int slideIndex,
        List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>? captures)
    {
        if (captures is null || slideIndex < 0 || slideIndex >= p.Slides.Count) return;
        var slide = p.Slides[slideIndex];
        foreach (var (id, ox, oy, ocx, ocy, oroute, _, _, _, _) in captures)
        {
            var c = ShapeHelper.Find(p, slideIndex, id);
            if (c is null) continue;
            c.OffsetXEmu  = ox;
            c.OffsetYEmu  = oy;
            c.ExtentCxEmu = ocx;
            c.ExtentCyEmu = ocy;
            c.ElbowRoute  = oroute;
        }
    }
}

/// <summary>
/// Sets the absolute position and size of a shape, capturing prior values for undo.
/// Also re-routes any connectors whose start/end is attached to the resized shape (Wave 23).
/// </summary>
public sealed class ResizeShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly long _newOffsetX;
    private readonly long _newOffsetY;
    private readonly long _newCx;
    private readonly long _newCy;
    private long _oldOffsetX, _oldOffsetY, _oldCx, _oldCy;
    private bool _applied;

    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

    public ResizeShapeCommand(int slideIndex, uint shapeId, long newOffsetX, long newOffsetY, long newCx, long newCy)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newOffsetX = newOffsetX;
        _newOffsetY = newOffsetY;
        _newCx      = newCx;
        _newCy      = newCy;
    }

    public string Label => "Resize Shape";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null || !ChartHelper.IsObjectEditable(s)) return;
        _oldOffsetX = s.OffsetXEmu;
        _oldOffsetY = s.OffsetYEmu;
        _oldCx      = s.ExtentCxEmu;
        _oldCy      = s.ExtentCyEmu;
        s.OffsetXEmu  = _newOffsetX;
        s.OffsetYEmu  = _newOffsetY;
        s.ExtentCxEmu = _newCx;
        s.ExtentCyEmu = _newCy;
        _applied = true;

        _rerouteCapture = MoveShapeCommand.ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.OffsetXEmu  = _oldOffsetX;
        s.OffsetYEmu  = _oldOffsetY;
        s.ExtentCxEmu = _oldCx;
        s.ExtentCyEmu = _oldCy;

        MoveShapeCommand.RevertReroute(p, _slideIndex, _rerouteCapture);
    }
}

/// <summary>Sets the source crop rectangle on a picture without changing its frame geometry.</summary>
public sealed class SetPictureCropCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly double _left;
    private readonly double _top;
    private readonly double _right;
    private readonly double _bottom;
    private bool _captured;
    private bool _hadFormat;
    private double _oldLeft;
    private double _oldTop;
    private double _oldRight;
    private double _oldBottom;

    public SetPictureCropCommand(
        int slideIndex,
        uint shapeId,
        double left,
        double top,
        double right,
        double bottom)
    {
        Validate(left, top, right, bottom);
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _left = left;
        _top = top;
        _right = right;
        _bottom = bottom;
    }

    public string Label => "Crop Picture";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return false;

        var format = shape.PictureFormat;
        return (format?.CropLeft ?? 0) != _left ||
               (format?.CropTop ?? 0) != _top ||
               (format?.CropRight ?? 0) != _right ||
               (format?.CropBottom ?? 0) != _bottom;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return;

        if (!_captured)
        {
            _captured = true;
            _hadFormat = shape.PictureFormat is not null;
            _oldLeft = shape.PictureFormat?.CropLeft ?? 0;
            _oldTop = shape.PictureFormat?.CropTop ?? 0;
            _oldRight = shape.PictureFormat?.CropRight ?? 0;
            _oldBottom = shape.PictureFormat?.CropBottom ?? 0;
        }

        if (shape.PictureFormat is null)
        {
            if (_left == 0 && _top == 0 && _right == 0 && _bottom == 0)
                return;
            shape.PictureFormat = new PictureFormat();
        }

        shape.PictureFormat.CropLeft = _left;
        shape.PictureFormat.CropTop = _top;
        shape.PictureFormat.CropRight = _right;
        shape.PictureFormat.CropBottom = _bottom;
        if (_left == 0 && _top == 0 && _right == 0 && _bottom == 0 &&
            !shape.PictureFormat.HasColorEffect)
        {
            shape.PictureFormat = null;
        }
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture || !_captured)
            return;

        if (shape.PictureFormat is null)
        {
            if (!_hadFormat && (_oldLeft != 0 || _oldTop != 0 || _oldRight != 0 || _oldBottom != 0))
                shape.PictureFormat = new PictureFormat();
            else
                return;
        }

        shape.PictureFormat.CropLeft = _oldLeft;
        shape.PictureFormat.CropTop = _oldTop;
        shape.PictureFormat.CropRight = _oldRight;
        shape.PictureFormat.CropBottom = _oldBottom;
        if (!_hadFormat && !shape.PictureFormat.HasColorEffect)
            shape.PictureFormat = null;
    }

    private static void Validate(double left, double top, double right, double bottom)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(right) || double.IsNaN(bottom) ||
            left < 0 || top < 0 || right < 0 || bottom < 0 ||
            left + right >= 1 || top + bottom >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(left), "Picture crop fractions must be non-negative and leave a visible source rectangle.");
        }
    }
}

/// <summary>Sets the authored color effects on a picture without changing its crop or frame.</summary>
public sealed class SetPictureColorEffectsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly PictureColorEffectValues _values;
    private bool _captured;
    private bool _hadFormat;
    private PictureColorEffectValues _oldValues;

    public SetPictureColorEffectsCommand(
        int slideIndex,
        uint shapeId,
        PictureColorEffectValues values)
    {
        Validate(values);
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _values = values;
    }

    public string Label => "Picture Color Effects";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return false;

        return ReadValues(shape.PictureFormat) != _values;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return;

        if (!_captured)
        {
            _captured = true;
            _hadFormat = shape.PictureFormat is not null;
            _oldValues = ReadValues(shape.PictureFormat);
        }

        if (shape.PictureFormat is null)
        {
            if (_values == PictureColorEffectValues.Reset)
                return;
            shape.PictureFormat = new PictureFormat();
        }

        shape.PictureFormat.Grayscale = _values.Grayscale;
        shape.PictureFormat.BiLevelThreshold = _values.BiLevelThreshold;
        shape.PictureFormat.Brightness = _values.Brightness;
        shape.PictureFormat.Contrast = _values.Contrast;
        shape.PictureFormat.AlphaModPct = _values.AlphaModPct;

        if (!shape.PictureFormat.HasCrop && !shape.PictureFormat.HasColorEffect)
            shape.PictureFormat = null;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture || !_captured)
            return;

        if (shape.PictureFormat is null)
        {
            if (!_hadFormat)
                return;
            shape.PictureFormat = new PictureFormat();
        }

        shape.PictureFormat.Grayscale = _oldValues.Grayscale;
        shape.PictureFormat.BiLevelThreshold = _oldValues.BiLevelThreshold;
        shape.PictureFormat.Brightness = _oldValues.Brightness;
        shape.PictureFormat.Contrast = _oldValues.Contrast;
        shape.PictureFormat.AlphaModPct = _oldValues.AlphaModPct;
        if (!shape.PictureFormat.HasCrop && !shape.PictureFormat.HasColorEffect)
            shape.PictureFormat = null;
    }

    private static PictureColorEffectValues ReadValues(PictureFormat? format) => format is null
        ? PictureColorEffectValues.Reset
        : new(
            format.Grayscale,
            format.BiLevelThreshold,
            format.Brightness,
            format.Contrast,
            format.AlphaModPct);

    private static void Validate(PictureColorEffectValues values)
    {
        if (values.BiLevelThreshold is { } threshold &&
            (double.IsNaN(threshold) || threshold < 0 || threshold > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Bi-level threshold must be between 0 and 1.");
        if (values.Brightness is { } brightness &&
            (double.IsNaN(brightness) || brightness < -1 || brightness > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Brightness must be between -1 and 1.");
        if (values.Contrast is { } contrast &&
            (double.IsNaN(contrast) || contrast < -1 || contrast > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Contrast must be between -1 and 1.");
        if (values.AlphaModPct is { } alpha &&
            (double.IsNaN(alpha) || alpha < 0 || alpha > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Alpha must be between 0 and 1.");
    }
}

/// <summary>
/// Sets one DrawingML preset-geometry adjustment on a shape.
/// A missing value removes the authored adjustment and restores the preset default.
/// </summary>
public sealed class SetShapeGeometryAdjustmentCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _name;
    private readonly double? _newValue;
    private bool _hadOldValue;
    private double _oldValue;

    public SetShapeGeometryAdjustmentCommand(int slideIndex, uint shapeId, string name, double? value)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("An adjustment name is required.", nameof(name))
            : name;
        _newValue = value;
    }

    public string Label => "Edit Shape Geometry";

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null)
            return;

        _hadOldValue = shape.PresetGeometryAdjustments.TryGetValue(_name, out _oldValue);
        if (_newValue is { } value)
            shape.PresetGeometryAdjustments[_name] = value;
        else
            shape.PresetGeometryAdjustments.Remove(_name);
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null)
            return;

        if (_hadOldValue)
            shape.PresetGeometryAdjustments[_name] = _oldValue;
        else
            shape.PresetGeometryAdjustments.Remove(_name);
    }
}

/// <summary>Moves one vertex or curve control point in an imported custom geometry path.</summary>
public sealed class SetCustomGeometryPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private readonly double _newX;
    private readonly double _newY;
    private readonly CustomGeometryPointSlot _slot;
    private CustomSegment? _oldSegment;

    public SetCustomGeometryPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double x,
        double y,
        CustomGeometryPointSlot slot = CustomGeometryPointSlot.Endpoint)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
        _newX = x;
        _newY = y;
        _slot = slot;
    }

    public string Label => _slot == CustomGeometryPointSlot.Endpoint ? "Edit Shape Vertex" : "Edit Curve Control Point";

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex < 0 || _segmentIndex >= path.Segments.Count)
            return;

        var segment = path.Segments[_segmentIndex];
        if (!CanMove(segment.Kind, _slot))
            return;

        _oldSegment = segment;
        path.Segments[_segmentIndex] = ApplyPoint(segment, _slot, _newX, _newY);
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _oldSegment is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex >= 0 && _segmentIndex < path.Segments.Count)
            path.Segments[_segmentIndex] = _oldSegment;
    }

    private static bool CanMove(CustomSegmentKind kind, CustomGeometryPointSlot slot) =>
        ((kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo) && slot == CustomGeometryPointSlot.Endpoint) ||
        (kind == CustomSegmentKind.QuadBezTo && (slot is CustomGeometryPointSlot.Control1 or CustomGeometryPointSlot.Endpoint)) ||
        (kind == CustomSegmentKind.CubicBezTo && (slot is CustomGeometryPointSlot.Control1 or CustomGeometryPointSlot.Control2 or CustomGeometryPointSlot.Endpoint));

    private static CustomSegment ApplyPoint(CustomSegment segment, CustomGeometryPointSlot slot, double x, double y) =>
        slot switch
        {
            CustomGeometryPointSlot.Control1 => segment with { X = x, Y = y },
            CustomGeometryPointSlot.Control2 => segment with { X1 = x, Y1 = y },
            _ when segment.Kind is CustomSegmentKind.QuadBezTo => segment with { X1 = x, Y1 = y },
            _ => segment with { X = x, Y = y },
        };
}

/// <summary>Sets one authored ArcTo angle or radius in a custom geometry path.</summary>
public sealed class SetCustomGeometryArcPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private readonly double _newValue;
    private readonly CustomGeometryArcPointSlot _slot;
    private double _oldValue;

    public SetCustomGeometryArcPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double value,
        CustomGeometryArcPointSlot slot)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
        _newValue = value;
        _slot = slot;
    }

    public string Label => _slot switch
    {
        CustomGeometryArcPointSlot.RadiusX or CustomGeometryArcPointSlot.RadiusY => "Edit Arc Radius",
        _ => "Edit Arc Angle",
    };

    public bool HasEffect(Presentation presentation)
    {
        var segment = FindSegment(presentation);
        return segment is { Kind: CustomSegmentKind.ArcTo };
    }

    public void Apply(Presentation presentation)
    {
        var segment = FindSegment(presentation);
        if (segment is not { Kind: CustomSegmentKind.ArcTo })
            return;

        _oldValue = ReadValue(segment);
        ReplaceSegment(presentation, WriteValue(segment, _newValue));
    }

    public void Revert(Presentation presentation)
    {
        if (FindSegment(presentation) is { Kind: CustomSegmentKind.ArcTo })
            ReplaceSegment(presentation, WriteValue(FindSegment(presentation)!, _oldValue));
    }

    private CustomSegment? FindSegment(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return null;

        var path = shape.CustomGeometry[_pathIndex];
        return _segmentIndex >= 0 && _segmentIndex < path.Segments.Count
            ? path.Segments[_segmentIndex]
            : null;
    }

    private void ReplaceSegment(Presentation presentation, CustomSegment replacement)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex >= 0 && _segmentIndex < path.Segments.Count)
            path.Segments[_segmentIndex] = replacement;
    }

    private double ReadValue(CustomSegment segment) => _slot switch
    {
        CustomGeometryArcPointSlot.StartAngle => segment.StAng,
        CustomGeometryArcPointSlot.EndAngle => segment.StAng + segment.SwAng,
        CustomGeometryArcPointSlot.RadiusX => segment.WR,
        CustomGeometryArcPointSlot.RadiusY => segment.HR,
        _ => 0,
    };

    private CustomSegment WriteValue(CustomSegment segment, double value) => _slot switch
    {
        CustomGeometryArcPointSlot.StartAngle => segment with { StAng = value },
        CustomGeometryArcPointSlot.EndAngle => segment with { SwAng = value - segment.StAng },
        CustomGeometryArcPointSlot.RadiusX => segment with { WR = Math.Max(1, value) },
        CustomGeometryArcPointSlot.RadiusY => segment with { HR = Math.Max(1, value) },
        _ => segment,
    };
}

/// <summary>Inserts a straight custom-geometry vertex after a selected endpoint.</summary>
public sealed class InsertCustomGeometryPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private readonly double _x;
    private readonly double _y;
    private int _insertedSegmentIndex = -1;

    public InsertCustomGeometryPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double x,
        double y)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
        _x = x;
        _y = y;
    }

    public string Label => "Add Shape Point";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape is not null &&
            _pathIndex >= 0 && _pathIndex < shape.CustomGeometry.Count &&
            _segmentIndex >= 0 && _segmentIndex < shape.CustomGeometry[_pathIndex].Segments.Count &&
            shape.CustomGeometry[_pathIndex].Segments[_segmentIndex].Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo;
    }

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex < 0 || _segmentIndex >= path.Segments.Count ||
            path.Segments[_segmentIndex].Kind is not (CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo))
            return;

        _insertedSegmentIndex = _segmentIndex + 1;
        path.Segments.Insert(_insertedSegmentIndex, new CustomSegment(
            CustomSegmentKind.LineTo, X: _x, Y: _y));
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_insertedSegmentIndex >= 0 && _insertedSegmentIndex < path.Segments.Count &&
            path.Segments[_insertedSegmentIndex].Kind == CustomSegmentKind.LineTo)
            path.Segments.RemoveAt(_insertedSegmentIndex);
    }
}

/// <summary>Deletes a selected straight custom-geometry vertex while preserving path structure.</summary>
public sealed class DeleteCustomGeometryPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private CustomSegment? _removedSegment;

    public DeleteCustomGeometryPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
    }

    public string Label => "Delete Shape Point";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return false;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex < 0 || _segmentIndex >= path.Segments.Count ||
            path.Segments[_segmentIndex].Kind != CustomSegmentKind.LineTo)
            return false;

        return path.Segments.Count(segment =>
            segment.Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo) > 2;
    }

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (!HasEffect(p) || shape is null)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        _removedSegment = path.Segments[_segmentIndex];
        path.Segments.RemoveAt(_segmentIndex);
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _removedSegment is null ||
            _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        shape.CustomGeometry[_pathIndex].Segments.Insert(_segmentIndex, _removedSegment);
    }
}

/// <summary>
/// Sets the rotation of a shape; captures old rotation for undo.
/// Also re-routes any connectors whose start/end is attached to the rotated shape (Wave 23).
/// </summary>
public sealed class RotateShapeCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly double _newRotationDeg;
    private double          _oldRotationDeg;
    private bool             _applied;

    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

    public RotateShapeCommand(int slideIndex, uint shapeId, double newRotationDeg)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _newRotationDeg = newRotationDeg;
    }

    public string Label => "Rotate Shape";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null || !ChartHelper.IsObjectEditable(s)) return;
        _oldRotationDeg = s.RotationDeg;
        s.RotationDeg   = _newRotationDeg;
        _applied = true;

        _rerouteCapture = MoveShapeCommand.ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.RotationDeg = _oldRotationDeg;

        MoveShapeCommand.RevertReroute(p, _slideIndex, _rerouteCapture);
    }
}

/// <summary>
/// Toggles a shape's horizontal or vertical mirror state and re-routes attached connectors.
/// The flip flags are serialized shape semantics; this command supplies the missing authoring path.
/// </summary>
public sealed class FlipShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly bool _horizontal;
    private bool _oldFlip;
    private bool _applied;

    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

    public FlipShapeCommand(int slideIndex, uint shapeId, bool horizontal)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _horizontal = horizontal;
    }

    public string Label => _horizontal ? "Flip Horizontal" : "Flip Vertical";

    public bool HasEffect(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        return shape is not null && ChartHelper.IsObjectEditable(shape);
    }

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || !ChartHelper.IsObjectEditable(shape)) return;

        _oldFlip = _horizontal ? shape.FlipH : shape.FlipV;
        if (_horizontal)
            shape.FlipH = !_oldFlip;
        else
            shape.FlipV = !_oldFlip;

        _applied = true;
        _rerouteCapture = MoveShapeCommand.ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null) return;

        if (_horizontal)
            shape.FlipH = _oldFlip;
        else
            shape.FlipV = _oldFlip;

        MoveShapeCommand.RevertReroute(p, _slideIndex, _rerouteCapture);
    }
}

/// <summary>Replaces the fill of a shape; captures old fill for undo.</summary>
public sealed class SetShapeFillCommand : IPresentationCommand
{
    private readonly int        _slideIndex;
    private readonly uint       _shapeId;
    private readonly ShapeFill? _newFill;
    private ShapeFill?          _oldFill;

    public SetShapeFillCommand(int slideIndex, uint shapeId, ShapeFill? newFill)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newFill    = newFill;
    }

    public string Label => "Set Fill";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldFill = s.Fill;
        s.Fill   = _newFill;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.Fill = _oldFill;
    }
}

/// <summary>Replaces the outline of a shape; captures old outline for undo.</summary>
public sealed class SetShapeOutlineCommand : IPresentationCommand
{
    private readonly int           _slideIndex;
    private readonly uint          _shapeId;
    private readonly ShapeOutline? _newOutline;
    private ShapeOutline?          _oldOutline;

    public SetShapeOutlineCommand(int slideIndex, uint shapeId, ShapeOutline? newOutline)
    {
        _slideIndex  = slideIndex;
        _shapeId     = shapeId;
        _newOutline  = newOutline;
    }

    public string Label => "Set Outline";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldOutline = s.Outline;
        s.Outline   = _newOutline;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.Outline = _oldOutline;
    }
}

/// <summary>
/// Moves a shape to a specific z-index (position in the Shapes list).
/// The shape list is painter's order (index 0 = back). Captures old index for undo.
/// </summary>
public sealed class ReorderShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _newZIndex;
    private int           _oldZIndex = -1;

    public ReorderShapeCommand(int slideIndex, uint shapeId, int newZIndex)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newZIndex  = newZIndex;
    }

    public string Label => "Reorder Shape";

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper.FindContainingList(p, _slideIndex, _shapeId);
        if (shapes is null) return;
        _oldZIndex = shapes.FindIndex(s => s.Id == _shapeId);
        if (_oldZIndex < 0) return;
        MoveInList(shapes, _oldZIndex, _newZIndex);
    }

    public void Revert(Presentation p)
    {
        if (_oldZIndex < 0) return;
        var shapes = ShapeHelper.FindContainingList(p, _slideIndex, _shapeId);
        if (shapes is null) return;
        MoveInList(shapes, _newZIndex, _oldZIndex);
    }

    private static void MoveInList<T>(List<T> list, int from, int to)
    {
        if (from == to || from < 0 || from >= list.Count) return;
        var item = list[from];
        list.RemoveAt(from);
        var dest = Math.Clamp(to, 0, list.Count);
        list.Insert(dest, item);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// TEXT / RUN-FORMAT COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Replaces the entire <see cref="TextBody"/> of a shape, capturing the old body for undo.
/// Used for whole-body replace (e.g. paste rich text).
/// </summary>
public sealed class SetShapeTextCommand : IPresentationCommand
{
    private readonly int       _slideIndex;
    private readonly uint      _shapeId;
    private readonly TextBody? _newBody;
    private TextBody?          _oldBody;

    public SetShapeTextCommand(int slideIndex, uint shapeId, TextBody? newBody)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newBody    = newBody;
    }

    public string Label => "Set Text";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldBody   = s.TextBody;
        s.TextBody = _newBody;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.TextBody = _oldBody;
    }
}

/// <summary>
/// Changes the DrawingML text-frame autofit mode of one shape while preserving the authored
/// distinction between no autofit, shrink text on overflow, and grow shape to fit text.
/// </summary>
public sealed class SetShapeTextAutoFitCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly TextAutoFitKind _newKind;
    private TextAutoFitKind _oldKind;

    public SetShapeTextAutoFitCommand(int slideIndex, uint shapeId, TextAutoFitKind newKind)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newKind = newKind;
    }

    public string Label => "Set Text Autofit";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape?.TextBody is { } body && body.AutoFitKind != _newKind;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        _oldKind = body.AutoFitKind;
        body.AutoFitKind = _newKind;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        body.AutoFitKind = _oldKind;
    }
}

/// <summary>Changes the DrawingML text-frame text direction of one shape.</summary>
public sealed class SetShapeTextVerticalTypeCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly TextVerticalType _newType;
    private TextVerticalType _oldType;

    public SetShapeTextVerticalTypeCommand(int slideIndex, uint shapeId, TextVerticalType newType)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newType = newType;
    }

    public string Label => "Set Text Direction";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape?.TextBody is { } body && body.VerticalType != _newType;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        _oldType = body.VerticalType;
        body.VerticalType = _newType;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        body.VerticalType = _oldType;
    }
}

/// <summary>Changes the DrawingML text-frame column count of one shape.</summary>
public sealed class SetShapeTextColumnCountCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _newCount;
    private int _oldCount;

    public SetShapeTextColumnCountCommand(int slideIndex, uint shapeId, int newCount)
    {
        if (newCount < 1)
            throw new ArgumentOutOfRangeException(nameof(newCount), "Text column count must be positive.");

        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newCount = newCount;
    }

    public string Label => "Set Text Columns";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape?.TextBody is { } body && body.ColumnCount != _newCount;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        _oldCount = body.ColumnCount;
        body.ColumnCount = _newCount;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        body.ColumnCount = _oldCount;
    }
}

/// <summary>Changes the DrawingML text-frame column spacing of one shape.</summary>
public sealed class SetShapeTextColumnSpacingCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly long _newSpacingEmu;
    private long _oldSpacingEmu;

    public SetShapeTextColumnSpacingCommand(int slideIndex, uint shapeId, long newSpacingEmu)
    {
        if (newSpacingEmu < 0)
            throw new ArgumentOutOfRangeException(nameof(newSpacingEmu), "Text column spacing cannot be negative.");

        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newSpacingEmu = newSpacingEmu;
    }

    public string Label => "Set Text Column Spacing";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape?.TextBody is { } body && body.ColumnSpacingEmu != _newSpacingEmu;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        _oldSpacingEmu = body.ColumnSpacingEmu;
        body.ColumnSpacingEmu = _newSpacingEmu;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        body.ColumnSpacingEmu = _oldSpacingEmu;
    }
}

/// <summary>
/// Base for run-format toggle commands that operate over a single run identified by
/// (slideIndex, shapeId, paragraphIndex, runIndex).
/// Apply/Revert are symmetric (toggle). Captures old value for non-toggle set commands.
/// </summary>
public abstract class RunFormatCommandBase : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _paragraphIndex;
    private readonly int  _runIndex;

    protected RunFormatCommandBase(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
    }

    public abstract string Label { get; }

    public void Apply(Presentation p)   => WithRun(p, ApplyToRun);
    public void Revert(Presentation p)  => WithRun(p, RevertFromRun);

    protected abstract void ApplyToRun(Run run);
    protected abstract void RevertFromRun(Run run);

    private void WithRun(Presentation p, Action<Run> action)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return;
        action(para.Runs[_runIndex]);
    }
}

/// <summary>Toggles bold on a single run.</summary>
/// <remarks>
/// RR1 fix: Apply snapshots the run's prior (Bold, BoldSet) pair so that Revert can restore
/// the exact prior state — including BoldSet=false (inherited) — rather than blindly
/// re-toggling which would bake the run to explicit non-bold after undo.
/// </remarks>
public sealed class ToggleRunBoldCommand : RunFormatCommandBase
{
    private bool _priorBold;
    private bool _priorBoldSet;

    public ToggleRunBoldCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Bold";

    protected override void ApplyToRun(Run r)
    {
        // Snapshot the prior (Bold, BoldSet) pair before mutating — may be inherited (BoldSet=false).
        _priorBold    = r.Bold;
        _priorBoldSet = r.BoldSet;
        // Forward toggle: invert run.Bold and mark as explicit so the choice round-trips.
        r.Bold    = !r.Bold;
        r.BoldSet = true;
    }

    protected override void RevertFromRun(Run r)
    {
        // Restore the exact prior (Bold, BoldSet) pair — including inherited (BoldSet=false).
        r.Bold    = _priorBold;
        r.BoldSet = _priorBoldSet;
    }
}

/// <summary>Toggles italic on a single run.</summary>
/// <remarks>
/// RR1 fix: mirrors the same prior-(Italic,ItalicSet) snapshot+restore pattern as
/// <see cref="ToggleRunBoldCommand"/>.
/// </remarks>
public sealed class ToggleRunItalicCommand : RunFormatCommandBase
{
    private bool _priorItalic;
    private bool _priorItalicSet;

    public ToggleRunItalicCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Italic";

    protected override void ApplyToRun(Run r)
    {
        _priorItalic    = r.Italic;
        _priorItalicSet = r.ItalicSet;
        r.Italic    = !r.Italic;
        r.ItalicSet = true;
    }

    protected override void RevertFromRun(Run r)
    {
        r.Italic    = _priorItalic;
        r.ItalicSet = _priorItalicSet;
    }
}

/// <summary>Toggles underline on a single run.</summary>
public sealed class ToggleRunUnderlineCommand : RunFormatCommandBase
{
    public ToggleRunUnderlineCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Underline";
    protected override void ApplyToRun(Run r)   => r.Underline = !r.Underline;
    protected override void RevertFromRun(Run r) => r.Underline = !r.Underline;
}

/// <summary>Toggles a run's superscript baseline offset.</summary>
public sealed class ToggleRunSuperscriptCommand : RunFormatCommandBase
{
    private int? _priorBaseline;

    public ToggleRunSuperscriptCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Superscript";

    protected override void ApplyToRun(Run r)
    {
        _priorBaseline = r.BaselineOffset;
        r.BaselineOffset = r.BaselineOffset > 0 ? null : 10000;
    }

    protected override void RevertFromRun(Run r) => r.BaselineOffset = _priorBaseline;
}

/// <summary>Toggles a run's subscript baseline offset.</summary>
public sealed class ToggleRunSubscriptCommand : RunFormatCommandBase
{
    private int? _priorBaseline;

    public ToggleRunSubscriptCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Subscript";

    protected override void ApplyToRun(Run r)
    {
        _priorBaseline = r.BaselineOffset;
        r.BaselineOffset = r.BaselineOffset < 0 ? null : -10000;
    }

    protected override void RevertFromRun(Run r) => r.BaselineOffset = _priorBaseline;
}

/// <summary>Sets the font family on a single run; captures old value for undo.</summary>
public sealed class SetRunFontCommand : IPresentationCommand
{
    private readonly int     _slideIndex;
    private readonly uint    _shapeId;
    private readonly int     _paragraphIndex;
    private readonly int     _runIndex;
    private readonly string? _newFont;
    private string?          _oldFont;

    public SetRunFontCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex, string? newFont)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
        _newFont        = newFont;
    }

    public string Label => "Set Font";

    public void Apply(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        _oldFont        = run.FontFamily;
        run.FontFamily  = _newFont;
    }

    public void Revert(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        run.FontFamily = _oldFont;
    }

    private Run? GetRun(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return null;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return null;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return null;
        return para.Runs[_runIndex];
    }
}

/// <summary>Sets the font size on a single run; captures old value for undo.</summary>
public sealed class SetRunFontSizeCommand : IPresentationCommand
{
    private readonly int     _slideIndex;
    private readonly uint    _shapeId;
    private readonly int     _paragraphIndex;
    private readonly int     _runIndex;
    private readonly double? _newSize;
    private double?          _oldSize;

    public SetRunFontSizeCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex, double? newSizePt)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
        _newSize        = newSizePt;
    }

    public string Label => "Set Font Size";

    public void Apply(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        _oldSize       = run.FontSizePt;
        run.FontSizePt = _newSize;
    }

    public void Revert(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        run.FontSizePt = _oldSize;
    }

    private Run? GetRun(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return null;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return null;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return null;
        return para.Runs[_runIndex];
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// TRANSITION + ANIMATION COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Sets or clears the slide transition for the slide at <paramref name="slideIndex"/>.
/// Captures the old transition for undo.
/// </summary>
public sealed class SetSlideTransitionCommand : IPresentationCommand
{
    private readonly int              _slideIndex;
    private readonly SlideTransition? _newTransition;
    private SlideTransition?          _oldTransition;

    public SetSlideTransitionCommand(int slideIndex, SlideTransition? transition)
    {
        _slideIndex    = slideIndex;
        _newTransition = transition;
    }

    public string Label => "Set Transition";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var slide         = p.Slides[_slideIndex];
        _oldTransition    = slide.Transition;
        slide.Transition  = _newTransition;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Transition = _oldTransition;
    }
}

/// <summary>
/// Replaces the raw PowerPoint paragraph-build list on a slide. The build list is
/// intentionally kept as source XML because PowerPoint stores timing metadata that
/// is broader than the current shared model. The command makes authoring changes
/// undoable without discarding unrelated timing entries.
/// </summary>
public sealed class SetSlideAnimationBuildListCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly string? _newBuildListXml;
    private string? _oldBuildListXml;

    public SetSlideAnimationBuildListCommand(int slideIndex, string? buildListXml)
    {
        _slideIndex = slideIndex;
        _newBuildListXml = buildListXml;
    }

    public string Label => "Set Text Build";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return;

        var slide = p.Slides[_slideIndex];
        _oldBuildListXml = slide.AnimationBuildListXml;
        slide.AnimationBuildListXml = _newBuildListXml;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return;

        p.Slides[_slideIndex].AnimationBuildListXml = _oldBuildListXml;
    }
}

/// <summary>
/// Appends a <see cref="ShapeAnimation"/> to the animation list of the slide at
/// <paramref name="slideIndex"/>.
/// </summary>
public sealed class AddShapeAnimationCommand : IPresentationCommand
{
    private readonly int            _slideIndex;
    private readonly ShapeAnimation _animation;

    public AddShapeAnimationCommand(int slideIndex, ShapeAnimation animation)
    {
        _slideIndex = slideIndex;
        _animation  = animation;
    }

    public string Label => "Add Animation";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Animations.Add(_animation);
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Animations.Remove(_animation);
    }
}

/// <summary>
/// Removes the animation at <paramref name="animationIndex"/> from the slide at <paramref name="slideIndex"/>.
/// Captures the entry and its index for undo.
/// </summary>
public sealed class RemoveShapeAnimationCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly int _animationIndex;
    private ShapeAnimation? _captured;

    public RemoveShapeAnimationCommand(int slideIndex, int animationIndex)
    {
        _slideIndex     = slideIndex;
        _animationIndex = animationIndex;
    }

    public string Label => "Remove Animation";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (_animationIndex < 0 || _animationIndex >= anims.Count) return;
        _captured = anims[_animationIndex];
        anims.RemoveAt(_animationIndex);
    }

    public void Revert(Presentation p)
    {
        if (_captured is null) return;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        var idx = Math.Clamp(_animationIndex, 0, anims.Count);
        anims.Insert(idx, _captured);
    }
}

/// <summary>
/// Reorders the animation at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.
/// </summary>
public sealed class ReorderShapeAnimationCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly int _from;
    private readonly int _to;

    public ReorderShapeAnimationCommand(int slideIndex, int fromIndex, int toIndex)
    {
        _slideIndex = slideIndex;
        _from       = fromIndex;
        _to         = toIndex;
    }

    public string Label => "Reorder Animation";

    public void Apply(Presentation p)  => MoveInList(p, _from, _to);
    public void Revert(Presentation p) => MoveInList(p, _to, _from);

    private void MoveInList(Presentation p, int from, int to)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (from == to || from < 0 || from >= anims.Count) return;
        var item = anims[from];
        anims.RemoveAt(from);
        var dest = Math.Clamp(to, 0, anims.Count);
        anims.Insert(dest, item);
    }
}

/// <summary>
/// Replaces the animation entry at <paramref name="animationIndex"/> with a new <see cref="ShapeAnimation"/>.
/// Captures old entry for undo.
/// </summary>
public sealed class SetShapeAnimationCommand : IPresentationCommand
{
    private readonly int            _slideIndex;
    private readonly int            _animationIndex;
    private readonly ShapeAnimation _newAnimation;
    private ShapeAnimation?         _oldAnimation;

    public SetShapeAnimationCommand(int slideIndex, int animationIndex, ShapeAnimation newAnimation)
    {
        _slideIndex      = slideIndex;
        _animationIndex  = animationIndex;
        _newAnimation    = newAnimation;
    }

    public string Label => "Edit Animation";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (_animationIndex < 0 || _animationIndex >= anims.Count) return;
        _oldAnimation         = anims[_animationIndex];
        anims[_animationIndex] = _newAnimation;
    }

    public void Revert(Presentation p)
    {
        if (_oldAnimation is null) return;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (_animationIndex < 0 || _animationIndex >= anims.Count) return;
        anims[_animationIndex] = _oldAnimation;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// NOTES COMMAND
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Replaces the speaker-notes <see cref="TextBody"/> on the slide at <paramref name="slideIndex"/>.
/// Captures the previous value for undo. Pass null to clear notes.
/// </summary>
public sealed class SetSlideNotesCommand : IPresentationCommand
{
    private readonly int       _slideIndex;
    private readonly TextBody? _newNotes;
    private TextBody?          _oldNotes;

    public SetSlideNotesCommand(int slideIndex, TextBody? newNotes)
    {
        _slideIndex = slideIndex;
        _newNotes   = newNotes;
    }

    public string Label => "Set Notes";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var slide  = p.Slides[_slideIndex];
        _oldNotes  = slide.Notes;
        slide.Notes = _newNotes;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Notes = _oldNotes;
    }
}

/// <summary>Sets the color on a single run; captures old value for undo.</summary>
public sealed class SetRunColorCommand : IPresentationCommand
{
    private readonly int              _slideIndex;
    private readonly uint             _shapeId;
    private readonly int              _paragraphIndex;
    private readonly int              _runIndex;
    private readonly ThemeAwareColor? _newColor;
    private ThemeAwareColor?          _oldColor;

    public SetRunColorCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex, ThemeAwareColor? newColor)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
        _newColor       = newColor;
    }

    public string Label => "Set Color";

    public void Apply(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        _oldColor  = run.Color;
        run.Color  = _newColor;
    }

    public void Revert(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        run.Color = _oldColor;
    }

    private Run? GetRun(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return null;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return null;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return null;
        return para.Runs[_runIndex];
    }
}
