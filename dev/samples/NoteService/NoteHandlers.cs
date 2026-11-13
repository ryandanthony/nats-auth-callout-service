// -----------------------------------------------------------------------
// <copyright file="NoteHandlers.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using Microsoft.Extensions.Logging;
using Wolverine.Attributes;

namespace AuthCallout.Dev.Samples.NoteService;

/// <summary>
/// Wolverine handlers for the notes domain. Every handler takes a
/// <see cref="CalloutIdentityContext"/> parameter, which
/// <see cref="CalloutIdentityMiddleware"/> supplies only after it has verified the inbound
/// message's <c>Callout-Identity</c> assertion — so a handler body only ever runs for an
/// authenticated, identified caller (the "service checks the auth of the clients" step). The
/// verified caller's <c>sub</c> becomes the note's owner, taken from the signed assertion
/// rather than anything the client could spoof in the message body.
/// </summary>
/// <remarks>
/// Marked <c>[WolverineHandler]</c> because the class name ends in "Handlers" (plural), which
/// Wolverine's naming convention (types ending in "Handler"/"Consumer") does not match; the
/// attribute is also the required discovery mechanism for a <c>static</c> handler class.
/// </remarks>
[WolverineHandler]
public static class NoteHandlers
{
    /// <summary>Handles <see cref="CreateNote"/>: stores the note owned by the verified caller and cascades <see cref="NoteCreated"/>.</summary>
    /// <param name="command">The create command.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory note store.</param>
    /// <param name="logger">The handler logger.</param>
    /// <returns>The <see cref="NoteCreated"/> event to cascade onto <c>notes.events</c>.</returns>
    public static NoteCreated Handle(CreateNote command, CalloutIdentityContext identity, NoteStore store, ILogger<CreateNote> logger)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);

        store.Save(new Note(command.NoteId, command.Title, command.Body, identity.Subject));
        logger.LogInformation("[notes] {Owner} created note '{Title}' ({NoteId}).", identity.Subject, command.Title, command.NoteId);
        return new NoteCreated(command.NoteId, command.Title, identity.Subject);
    }

    /// <summary>Handles <see cref="RenameNote"/>: renames a note the caller can see and cascades <see cref="NoteRenamed"/> (or nothing when absent).</summary>
    /// <param name="command">The rename command.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory note store.</param>
    /// <param name="logger">The handler logger.</param>
    /// <returns>The <see cref="NoteRenamed"/> event to cascade, or null when the note does not exist.</returns>
    public static NoteRenamed? Handle(RenameNote command, CalloutIdentityContext identity, NoteStore store, ILogger<RenameNote> logger)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);

        var existing = store.Find(command.NoteId);
        if (existing is null)
        {
            logger.LogWarning("[notes] {Owner} tried to rename missing note {NoteId}.", identity.Subject, command.NoteId);
            return null;
        }

        store.Save(existing with { Title = command.NewTitle });
        logger.LogInformation("[notes] {Owner} renamed note {NoteId}: '{Old}' -> '{New}'.", identity.Subject, command.NoteId, existing.Title, command.NewTitle);
        return new NoteRenamed(command.NoteId, existing.Title, command.NewTitle);
    }

    /// <summary>Handles <see cref="GetNote"/> as a request/response query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory note store.</param>
    /// <returns>The requested note, or null when absent.</returns>
    public static Note? Handle(GetNote query, CalloutIdentityContext identity, NoteStore store)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);
        return store.Find(query.NoteId);
    }

    /// <summary>Handles <see cref="ListNotes"/> as a request/response query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory note store.</param>
    /// <returns>Every stored note.</returns>
    public static IReadOnlyList<Note> Handle(ListNotes query, CalloutIdentityContext identity, NoteStore store)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);
        return store.All();
    }

    /// <summary>Handles the cascaded <see cref="NoteCreated"/> event (a subscriber reacting to domain events).</summary>
    /// <param name="event">The event.</param>
    /// <param name="logger">The handler logger.</param>
    public static void Handle(NoteCreated @event, ILogger<NoteCreated> logger)
    {
        ArgumentNullException.ThrowIfNull(@event);
        logger.LogInformation("[notes] event NoteCreated: '{Title}' by {Owner}.", @event.Title, @event.Owner);
    }

    /// <summary>Handles the cascaded <see cref="NoteRenamed"/> event.</summary>
    /// <param name="event">The event.</param>
    /// <param name="logger">The handler logger.</param>
    public static void Handle(NoteRenamed @event, ILogger<NoteRenamed> logger)
    {
        ArgumentNullException.ThrowIfNull(@event);
        logger.LogInformation("[notes] event NoteRenamed: {NoteId} now '{New}'.", @event.NoteId, @event.NewTitle);
    }
}
