// -----------------------------------------------------------------------
// <copyright file="NoteDomain.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Dev.Samples.NoteService;

// The "notes" application's domain, expressed as Wolverine messages. Commands mutate state,
// queries read it, and events announce what changed — all flowing over NATS through the
// callout-authorized connection (subjects notes.commands / notes.queries / notes.events), so
// every message is subject to both the connection's minted grant and the per-message
// Callout-Identity verification AuthCallout.Client.Wolverine applies to every handler chain
// by default (CalloutIdentityPolicy). Nothing here opts out via [RequireIdentity(false)],
// which is correct: every one of these messages — including the cascaded events, published
// and consumed by this same authenticated connection — carries a verifiable identity.

/// <summary>Command: create a new note owned by the calling user.</summary>
public sealed record CreateNote(Guid NoteId, string Title, string Body);

/// <summary>Command: rename an existing note.</summary>
public sealed record RenameNote(Guid NoteId, string NewTitle);

/// <summary>Query: fetch a single note by id (null when absent).</summary>
public sealed record GetNote(Guid NoteId);

/// <summary>Query: list every note the store currently holds.</summary>
#pragma warning disable S2094 // Deliberately empty: a Wolverine query message with no parameters.
public sealed record ListNotes;
#pragma warning restore S2094

/// <summary>Event: a note was created (cascaded after <see cref="CreateNote"/>).</summary>
public sealed record NoteCreated(Guid NoteId, string Title, string Owner);

/// <summary>Event: a note was renamed (cascaded after <see cref="RenameNote"/>).</summary>
public sealed record NoteRenamed(Guid NoteId, string OldTitle, string NewTitle);

/// <summary>A note aggregate as stored in the in-memory store.</summary>
public sealed record Note(Guid Id, string Title, string Body, string Owner);
