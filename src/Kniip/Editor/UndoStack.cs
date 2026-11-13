using System;
using System.Collections.Generic;

namespace Kniip.Editor;

/// <summary>Undo/redo op basis van momentopnamen van het document (annotaties zijn klein; pixels worden gedeeld).</summary>
public sealed class UndoStack
{
    private const int Limit = 100;

    private readonly LinkedList<DocumentState> _undo = new();
    private readonly Stack<DocumentState> _redo = new();

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Push(DocumentState stateBeforeChange)
    {
        _undo.AddLast(stateBeforeChange);
        if (_undo.Count > Limit)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
        Changed?.Invoke();
    }

    public DocumentState? Undo(DocumentState current)
    {
        if (_undo.Last == null)
        {
            return null;
        }

        DocumentState state = _undo.Last.Value;
        _undo.RemoveLast();
        _redo.Push(current);
        Changed?.Invoke();
        return state;
    }

    public DocumentState? Redo(DocumentState current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        DocumentState state = _redo.Pop();
        _undo.AddLast(current);
        Changed?.Invoke();
        return state;
    }
}
