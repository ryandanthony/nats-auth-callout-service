// -----------------------------------------------------------------------
// <copyright file="HealthEndpointMapper.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthCallout.Host.Health;

/// <summary>
/// Maps the three probe endpoints onto tag-filtered subsets of the registered health checks
/// (doc 08 §Health &amp; readiness §Probe mapping, D21: <c>/healthz</c> <c>live</c>,
/// <c>/readyz</c> <c>ready</c>, <c>/startupz</c> <c>startup</c>). Every endpoint shares
/// <see cref="HealthReportJsonWriter.WriteAsync"/> so all three probes and the plain
/// <c>ASP.NET Core</c> default status-code mapping (Healthy/Degraded → 200, Unhealthy → 503)
/// behave identically apart from which checks they run.
/// </summary>
public static class HealthEndpointMapper
{
    /// <summary>Gets the doc 08/D21-pinned liveness path.</summary>
    /// <value>The liveness endpoint path, <c>/healthz</c>.</value>
    public static string LivenessPath => "/healthz";

    /// <summary>Gets the doc 08/D21-pinned readiness path.</summary>
    /// <value>The readiness endpoint path, <c>/readyz</c>.</value>
    public static string ReadinessPath => "/readyz";

    /// <summary>Gets the doc 08/D21-pinned startup path.</summary>
    /// <value>The startup endpoint path, <c>/startupz</c>.</value>
    public static string StartupPath => "/startupz";

    /// <summary>Maps <see cref="LivenessPath"/>, <see cref="ReadinessPath"/>, and <see cref="StartupPath"/> onto <paramref name="app"/>.</summary>
    /// <param name="app">The application to map the endpoints onto.</param>
    /// <returns><paramref name="app"/>, for chaining.</returns>
    public static IEndpointRouteBuilder MapAuthCalloutHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(LivenessPath, TaggedOptions("live"));
        app.MapHealthChecks(ReadinessPath, TaggedOptions("ready"));
        app.MapHealthChecks(StartupPath, TaggedOptions("startup"));
        return app;
    }

    private static HealthCheckOptions TaggedOptions(string tag) => new()
    {
        Predicate = registration => registration.Tags.Contains(tag),
        ResponseWriter = HealthReportJsonWriter.WriteAsync,
    };
}
