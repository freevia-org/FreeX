namespace FreeW.Core.Model;

public sealed partial class DocumentCommandBus
{
    private CommandScope? _commandScope;
    private int _commandScopeCallbackDepth;

    /// <summary>
    /// Collects an already-applied gesture without publishing intermediate changes. This does not
    /// open an undo group: existing group ownership and refusal policies retain their meaning.
    /// The optional callback refreshes caller-owned model caches after each source command and rollback.
    /// It may read this bus, but may not execute commands, alter groups or scopes, or change history.
    /// </summary>
    public CommandScope BeginCommandScope(Action? commandApplied = null)
    {
        EnsureOutsideCommandScopeCallback();
        return new(this, commandApplied);
    }

    private void EnsureOutsideCommandScopeCallback()
    {
        if (_commandScopeCallbackDepth > 0)
            throw new InvalidOperationException("Command-scope cache callbacks cannot mutate command history.");
    }

    private void EnsureHistoryTransitionIsAllowed()
    {
        EnsureOutsideCommandScopeCallback();
        if (_commandScope is not null)
            throw new InvalidOperationException("Undo and redo cannot run inside an active command scope.");
    }

    private void InvokeCommandScopeCallback(Action? callback)
    {
        if (callback is null)
            return;
        _commandScopeCallbackDepth++;
        try { callback(); }
        finally { _commandScopeCallbackDepth--; }
    }

    public sealed class CommandScope : IDisposable
    {
        private readonly DocumentCommandBus _owner;
        private readonly List<IDocumentCommand> _commands = [];
        private readonly List<IDocumentCommand>? _parentBatch;
        private readonly int _parentBatchStart;
        private readonly bool _parentBatchNotify;
        private bool _abortedGroup;

        internal CommandScope(DocumentCommandBus owner, Action? commandApplied)
        {
            _owner = owner;
            ParentScope = owner._commandScope;
            CommandApplied = commandApplied;
            _parentBatch = owner._batch;
            _parentBatchStart = _parentBatch?.Count ?? 0;
            _parentBatchNotify = owner._notifyOnEachBatchedExecute;
            owner._commandScope = this;
        }

        internal CommandScope? ParentScope { get; }
        internal Action? CommandApplied { get; }
        public bool IsCompleted { get; private set; }
        public bool IsCommitted { get; private set; }
        public bool HasCommands => Gather(requireBalancedGroup: false).Count > 0;

        internal void Add(IDocumentCommand command) => _commands.Add(command);

        internal void PreserveAbortedGroup(IReadOnlyList<IDocumentCommand> commands)
        {
            // An abort inside a scope cannot claim commands preceding its savepoint.
            // Rollback reinstates the genuine outer batch with those earlier edits intact.
            var owned = ReferenceEquals(commands, _parentBatch)
                ? commands.Skip(_parentBatchStart).ToArray()
                : commands.ToArray();
            if (owned.Length > 0)
                _commands.Add(new CompositeDocumentCommand("Aborted edit", owned));
            _abortedGroup = true;
        }

        /// <summary>
        /// Decorates the completed source commands, then records one history entry or one child
        /// of the genuine outer group. Decorators must not reapply these commands. A failing
        /// decorator rolls the gesture back; observer failures after commit leave it committed.
        /// </summary>
        public bool Commit(string label, Func<IDocumentCommand, IDocumentCommand>? decorate = null)
        {
            _owner.EnsureOutsideCommandScopeCallback();
            EnsureCurrent();
            IDocumentCommand command;
            try
            {
                var commands = Gather(requireBalancedGroup: true);
                if (_abortedGroup)
                    throw new InvalidOperationException("An aborted edit cannot be committed.");
                if (commands.Count == 0)
                {
                    Complete(committed: false);
                    return false;
                }
                command = new CompositeDocumentCommand(label, commands);
                if (decorate is not null)
                    command = decorate(command) ?? throw new InvalidOperationException("The command decorator returned null.");
            }
            catch (Exception exception)
            {
                var failures = Rollback();
                if (failures.Count > 0)
                    throw new AggregateException(new[] { exception }.Concat(failures));
                throw;
            }

            RemoveParentTail();
            Complete(committed: true);
            _owner.RecordAppliedCommand(command);
            return true;
        }

        /// <summary>Reverts only this scope's commands, retaining preceding outer-group edits.</summary>
        public IReadOnlyList<Exception> Rollback()
        {
            _owner.EnsureOutsideCommandScopeCallback();
            EnsureCurrent();
            var commands = Gather(requireBalancedGroup: false);
            RemoveParentTail();
            _owner._batch = _parentBatch;
            Complete(committed: false);
            var failures = new List<Exception>();
            for (var index = commands.Count - 1; index >= 0; index--)
            {
                try { commands[index].Revert(_owner._context); }
                catch (Exception exception) { failures.Add(exception); }
            }
            if (commands.Count > 0)
            {
                for (CommandScope? scope = this; scope is not null; scope = scope.ParentScope)
                {
                    try { _owner.InvokeCommandScopeCallback(scope.CommandApplied); }
                    catch (Exception exception) { failures.Add(exception); }
                }
            }
            if (failures.Count > 0)
                _owner.NotifyChangedBestEffort(failures);
            return failures;
        }

        public void Dispose()
        {
            if (!IsCompleted && Rollback() is { Count: > 0 } failures)
                throw new AggregateException(failures);
        }

        private List<IDocumentCommand> Gather(bool requireBalancedGroup)
        {
            EnsureCurrent();
            var commands = new List<IDocumentCommand>(_commands);
            if (ReferenceEquals(_owner._batch, _parentBatch))
            {
                if (_parentBatch is not null)
                    commands.AddRange(_parentBatch.Skip(_parentBatchStart));
            }
            else
            {
                if (requireBalancedGroup)
                    throw new InvalidOperationException("An undo group is still open inside the command scope.");
                if (_owner._batch is not null)
                    commands.AddRange(_owner._batch);
            }
            return commands;
        }

        private void RemoveParentTail()
        {
            if (_parentBatch is not null && _parentBatch.Count > _parentBatchStart)
                _parentBatch.RemoveRange(_parentBatchStart, _parentBatch.Count - _parentBatchStart);
        }

        private void Complete(bool committed)
        {
            _owner._commandScope = ParentScope;
            _owner._notifyOnEachBatchedExecute = _parentBatchNotify;
            IsCompleted = true;
            IsCommitted = committed;
        }

        private void EnsureCurrent()
        {
            if (IsCompleted || !ReferenceEquals(_owner._commandScope, this))
                throw new InvalidOperationException("Command scopes must finish once, in nesting order.");
        }
    }
}
