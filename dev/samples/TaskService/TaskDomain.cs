// -----------------------------------------------------------------------
// <copyright file="TaskDomain.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Dev.Samples.TaskService;

// The "tasks" application's domain, expressed as Wolverine messages. A second, independent
// application from notes: its own NATS account (TASKS), its own issuer match (azp: tasks-web),
// its own tasks.> subject space and grant. Running both proves application isolation (ADR
// 0001) — a token for one app never yields the other's subjects. Every message here gets the
// package's default identity verification (CalloutIdentityPolicy); none opts out.

/// <summary>Command: create a new task owned by the calling user.</summary>
public sealed record CreateTask(Guid TaskId, string Description);

/// <summary>Command: mark a task complete.</summary>
public sealed record CompleteTask(Guid TaskId);

/// <summary>Query: fetch a single task by id (null when absent).</summary>
public sealed record GetTask(Guid TaskId);

/// <summary>Query: list every task that is not yet complete.</summary>
#pragma warning disable S2094 // Deliberately empty: a Wolverine query message with no parameters.
public sealed record GetOpenTasks;
#pragma warning restore S2094

/// <summary>Event: a task was created (cascaded after <see cref="CreateTask"/>).</summary>
public sealed record TaskCreated(Guid TaskId, string Description, string Owner);

/// <summary>Event: a task was completed (cascaded after <see cref="CompleteTask"/>).</summary>
public sealed record TaskCompleted(Guid TaskId, string CompletedBy);

/// <summary>A task aggregate as stored in the in-memory store.</summary>
public sealed record TaskItem(Guid Id, string Description, string Owner, bool IsComplete);
