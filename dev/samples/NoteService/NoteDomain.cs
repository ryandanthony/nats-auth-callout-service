// -----------------------------------------------------------------------
// <copyright file="NoteDomain.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;

namespace AuthCallout.Dev.Samples.NoteService;

// The "notes" application's domain, expressed as Wolverine messages. Commands mutate state,
// queries read it, and events announce what changed — all flowing over NATS through the
// callout-authorized connection (subjects notes.commands / notes.queries / notes.events),
// so every message is subject to both the connection's minted grant and the per-message
// Callout-Identity verification in CalloutIdentityMiddleware. Commands and queries are
// IClientRequest (client-originated, identity-checked); events are internal cascades.

/// <summary>Command: create a new note owned by the calling user.</summary>
public sealed record CreateNote(Guid NoteId, string Title, string Body) : IClientRequest;

/// <summary>Command: rename an existing note.</summary>
public sealed record RenameNote(Guid NoteId, string NewTitle) : IClientRequest;

/// <summary>Query: fetch a single note by id (null when absent).</summary>
public sealed record GetNote(Guid NoteId) : IClientRequest;

/// <summary>Query: list every note the store currently holds.</summary>
public sealed record ListNotes : IClientRequest;

/// <summary>Event: a note was created (cascaded after <see cref="CreateNote"/>).</summary>
public sealed record NoteCreated(Guid NoteId, string Title, string Owner);

/// <summary>Event: a note was renamed (cascaded after <see cref="RenameNote"/>).</summary>
public sealed record NoteRenamed(Guid NoteId, string OldTitle, string NewTitle);

/// <summary>A note aggregate as stored in the in-memory store.</summary>
public sealed record Note(Guid Id, string Title, string Body, string Owner);
