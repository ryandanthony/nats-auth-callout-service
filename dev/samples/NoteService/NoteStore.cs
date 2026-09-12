// -----------------------------------------------------------------------
// <copyright file="NoteStore.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>
/// The "notes" application's in-memory store (no database — the request's "use in memory
/// store"). A process-lifetime <see cref="ConcurrentDictionary{TKey,TValue}"/> of notes keyed
/// by id, registered as a singleton and injected into the Wolverine handlers.
/// </summary>
public sealed class NoteStore
{
    private readonly ConcurrentDictionary<Guid, Note> _notes = new();

    /// <summary>Adds or replaces a note.</summary>
    /// <param name="note">The note to store.</param>
    public void Save(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _notes[note.Id] = note;
    }

    /// <summary>Gets a note by id, or null when absent.</summary>
    /// <param name="id">The note id.</param>
    /// <returns>The note, or null.</returns>
    public Note? Find(Guid id) => _notes.TryGetValue(id, out var note) ? note : null;

    /// <summary>Gets every stored note, ordered by title for stable output.</summary>
    /// <returns>The current notes.</returns>
    public IReadOnlyList<Note> All() => [.. _notes.Values.OrderBy(static n => n.Title, StringComparer.Ordinal)];
}
