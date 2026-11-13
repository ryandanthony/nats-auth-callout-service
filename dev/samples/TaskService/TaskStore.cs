// -----------------------------------------------------------------------
// <copyright file="TaskStore.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace AuthCallout.Dev.Samples.TaskService;

/// <summary>
/// The "tasks" application's in-memory store (no database). A process-lifetime
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of tasks keyed by id, registered as a
/// singleton and injected into the Wolverine handlers.
/// </summary>
public sealed class TaskStore
{
    private readonly ConcurrentDictionary<Guid, TaskItem> _tasks = new();

    /// <summary>Adds or replaces a task.</summary>
    /// <param name="task">The task to store.</param>
    public void Save(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _tasks[task.Id] = task;
    }

    /// <summary>Gets a task by id, or null when absent.</summary>
    /// <param name="id">The task id.</param>
    /// <returns>The task, or null.</returns>
    public TaskItem? Find(Guid id) => _tasks.TryGetValue(id, out var task) ? task : null;

    /// <summary>Gets every task that is not yet complete, ordered by description for stable output.</summary>
    /// <returns>The current open tasks.</returns>
    public IReadOnlyList<TaskItem> OpenTasks() =>
        [.. _tasks.Values.Where(static t => !t.IsComplete).OrderBy(static t => t.Description, StringComparer.Ordinal)];
}
