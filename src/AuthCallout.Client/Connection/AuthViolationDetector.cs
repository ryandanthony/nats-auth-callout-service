// -----------------------------------------------------------------------
// <copyright file="AuthViolationDetector.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Connection;

/// <summary>
/// Detects an "Authorization Violation" server error by walking an exception's inner-exception
/// chain, since a connect failure can arrive wrapped (e.g. inside an
/// <see cref="AggregateException"/> or a transport wrapper) rather than as the bare server
/// exception (ADR 0021 rule 2: "callout denied the token, detected by walking inner exceptions
/// for the server error").
/// </summary>
public static class AuthViolationDetector
{
    private const string AuthorizationViolationMarker = "Authorization Violation";

    /// <summary>
    /// Walks <paramref name="exception"/> and its inner exceptions (including every branch of
    /// an <see cref="AggregateException"/>) for one whose message contains
    /// <c>"Authorization Violation"</c> (case-insensitive, matching NATS server error text).
    /// </summary>
    /// <param name="exception">The exception a connect attempt raised, or null.</param>
    /// <returns><see langword="true"/> if an authorization-violation server error was found anywhere in the chain.</returns>
#pragma warning disable CA1062 // exception is explicitly typed Exception? and null is a valid, handled input (Flatten yields nothing for null); there is nothing to validate.
    public static bool IsAuthorizationViolation(Exception? exception) =>
        Flatten(exception).Any(candidate => candidate.Message.Contains(AuthorizationViolationMarker, StringComparison.OrdinalIgnoreCase));
#pragma warning restore CA1062

    private static IEnumerable<Exception> Flatten(Exception? exception)
    {
        if (exception is null)
        {
            yield break;
        }

        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                foreach (var flattened in Flatten(inner))
                {
                    yield return flattened;
                }
            }

            yield break;
        }

        if (exception.InnerException is not null)
        {
            foreach (var flattened in Flatten(exception.InnerException))
            {
                yield return flattened;
            }
        }
    }
}
