// -----------------------------------------------------------------------
// <copyright file="TaskHandlers.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using Microsoft.Extensions.Logging;
using Wolverine.Attributes;

namespace AuthCallout.Dev.Samples.TaskService;

/// <summary>
/// Wolverine handlers for the tasks domain. Like the notes handlers, every client-facing
/// handler takes a <see cref="CalloutIdentityContext"/> that
/// <see cref="CalloutIdentityMiddleware"/> only supplies after verifying the inbound
/// <c>Callout-Identity</c> assertion, so the service authenticates every caller per message
/// and records ownership from the signed <c>sub</c>, not from the message body.
/// </summary>
/// <remarks>
/// Marked <c>[WolverineHandler]</c> because the class name ends in "Handlers" (plural), which
/// Wolverine's naming convention (types ending in "Handler"/"Consumer") does not match; the
/// attribute is also the required discovery mechanism for a <c>static</c> handler class.
/// </remarks>
[WolverineHandler]
public static class TaskHandlers
{
    /// <summary>Handles <see cref="CreateTask"/>: stores the task owned by the verified caller and cascades <see cref="TaskCreated"/>.</summary>
    /// <param name="command">The create command.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory task store.</param>
    /// <param name="logger">The handler logger.</param>
    /// <returns>The <see cref="TaskCreated"/> event to cascade onto <c>tasks.events</c>.</returns>
    public static TaskCreated Handle(CreateTask command, CalloutIdentityContext identity, TaskStore store, ILogger<CreateTask> logger)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);

        store.Save(new TaskItem(command.TaskId, command.Description, identity.Subject, IsComplete: false));
        logger.LogInformation("[tasks] {Owner} created task '{Description}' ({TaskId}).", identity.Subject, command.Description, command.TaskId);
        return new TaskCreated(command.TaskId, command.Description, identity.Subject);
    }

    /// <summary>Handles <see cref="CompleteTask"/>: marks a task complete and cascades <see cref="TaskCompleted"/> (or nothing when absent).</summary>
    /// <param name="command">The complete command.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory task store.</param>
    /// <param name="logger">The handler logger.</param>
    /// <returns>The <see cref="TaskCompleted"/> event to cascade, or null when the task does not exist.</returns>
    public static TaskCompleted? Handle(CompleteTask command, CalloutIdentityContext identity, TaskStore store, ILogger<CompleteTask> logger)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);

        var existing = store.Find(command.TaskId);
        if (existing is null)
        {
            logger.LogWarning("[tasks] {Owner} tried to complete missing task {TaskId}.", identity.Subject, command.TaskId);
            return null;
        }

        store.Save(existing with { IsComplete = true });
        logger.LogInformation("[tasks] {Owner} completed task {TaskId} ('{Description}').", identity.Subject, command.TaskId, existing.Description);
        return new TaskCompleted(command.TaskId, identity.Subject);
    }

    /// <summary>Handles <see cref="GetTask"/> as a request/response query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory task store.</param>
    /// <returns>The requested task, or null when absent.</returns>
    public static TaskItem? Handle(GetTask query, CalloutIdentityContext identity, TaskStore store)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);
        return store.Find(query.TaskId);
    }

    /// <summary>Handles <see cref="GetOpenTasks"/> as a request/response query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="identity">The verified caller identity (from the middleware).</param>
    /// <param name="store">The in-memory task store.</param>
    /// <returns>Every open task.</returns>
    public static IReadOnlyList<TaskItem> Handle(GetOpenTasks query, CalloutIdentityContext identity, TaskStore store)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);
        return store.OpenTasks();
    }

    /// <summary>Handles the cascaded <see cref="TaskCreated"/> event.</summary>
    /// <param name="event">The event.</param>
    /// <param name="logger">The handler logger.</param>
    public static void Handle(TaskCreated @event, ILogger<TaskCreated> logger)
    {
        ArgumentNullException.ThrowIfNull(@event);
        logger.LogInformation("[tasks] event TaskCreated: '{Description}' by {Owner}.", @event.Description, @event.Owner);
    }

    /// <summary>Handles the cascaded <see cref="TaskCompleted"/> event.</summary>
    /// <param name="event">The event.</param>
    /// <param name="logger">The handler logger.</param>
    public static void Handle(TaskCompleted @event, ILogger<TaskCompleted> logger)
    {
        ArgumentNullException.ThrowIfNull(@event);
        logger.LogInformation("[tasks] event TaskCompleted: {TaskId} by {CompletedBy}.", @event.TaskId, @event.CompletedBy);
    }
}
