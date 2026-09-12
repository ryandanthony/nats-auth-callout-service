// -----------------------------------------------------------------------
// <copyright file="TaskDomain.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;

namespace AuthCallout.Dev.Samples.TaskService;

// The "tasks" application's domain, expressed as Wolverine messages. A second, independent
// application from notes: its own NATS account (TASKS), its own issuer match (azp: tasks-web),
// its own tasks.> subject space and grant. Running both proves application isolation (ADR
// 0001) — a token for one app never yields the other's subjects. Commands/queries are
// IClientRequest (identity-checked); events are internal cascades.

/// <summary>Command: create a new task owned by the calling user.</summary>
public sealed record CreateTask(Guid TaskId, string Description) : IClientRequest;

/// <summary>Command: mark a task complete.</summary>
public sealed record CompleteTask(Guid TaskId) : IClientRequest;

/// <summary>Query: fetch a single task by id (null when absent).</summary>
public sealed record GetTask(Guid TaskId) : IClientRequest;

/// <summary>Query: list every task that is not yet complete.</summary>
public sealed record GetOpenTasks : IClientRequest;

/// <summary>Event: a task was created (cascaded after <see cref="CreateTask"/>).</summary>
public sealed record TaskCreated(Guid TaskId, string Description, string Owner);

/// <summary>Event: a task was completed (cascaded after <see cref="CompleteTask"/>).</summary>
public sealed record TaskCompleted(Guid TaskId, string CompletedBy);

/// <summary>A task aggregate as stored in the in-memory store.</summary>
public sealed record TaskItem(Guid Id, string Description, string Owner, bool IsComplete);
