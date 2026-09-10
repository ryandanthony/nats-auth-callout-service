// -----------------------------------------------------------------------
// <copyright file="BootstrapConfigLoader.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.Files.Yaml;

namespace AuthCallout.Host.Config;

/// <summary>
/// Loads and maps the bootstrap file (doc 08 §Process composition: "BootstrapConfigLoader,
/// Options + secrets") from a fixed path (env var <see cref="BootstrapPathEnvVar"/>, default
/// <see cref="DefaultPath"/>), accepting either YAML or JSON by extension (doc 09 shows the
/// bootstrap file as YAML; JSON is accepted for parity with the per-app files convention).
/// The bootstrap layer is loaded exactly once at startup and never hot-reloaded (doc 04
/// §Two layers), so this loader has no watch surface, unlike
/// <see cref="AuthCallout.Contracts.Seams.IRoutingConfigProvider"/>.
/// </summary>
public static class BootstrapConfigLoader
{
    /// <summary>Gets the environment variable naming the bootstrap file path.</summary>
    /// <value>The environment variable name, <c>AUTHCALLOUT_BOOTSTRAP_PATH</c>.</value>
    public static string BootstrapPathEnvVar => "AUTHCALLOUT_BOOTSTRAP_PATH";

    /// <summary>Gets the default bootstrap file path used when <see cref="BootstrapPathEnvVar"/> is unset.</summary>
    /// <value>The default path, <c>/config/bootstrap.yaml</c> (P17's Helm chart mounts the bootstrap ConfigMap here).</value>
    public static string DefaultPath => "/config/bootstrap.yaml";

    /// <summary>Resolves the effective bootstrap file path from <see cref="BootstrapPathEnvVar"/> or <see cref="DefaultPath"/>.</summary>
    /// <returns>The effective bootstrap file path.</returns>
    public static string ResolvePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable(BootstrapPathEnvVar);
        return string.IsNullOrWhiteSpace(fromEnv) ? DefaultPath : fromEnv;
    }

    /// <summary>
    /// Loads and maps the bootstrap file at <paramref name="path"/>. Fail-closed: any I/O,
    /// parse, or mapping problem is returned as a deterministic
    /// <see cref="BootstrapMapResult.Error"/> rather than throwing, mirroring the routing
    /// providers' own "never yield a partially-valid result" discipline (ADR 0006), even
    /// though this specific load only ever runs once, at startup.
    /// </summary>
    /// <param name="path">The bootstrap file path (YAML or JSON, by extension).</param>
    /// <returns>The mapping result.</returns>
    public static BootstrapMapResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BootstrapMapResult.Failure($"Bootstrap file '{path}' could not be read: {ex.Message}");
        }

        JsonElement root;
        try
        {
            root = IsJson(path) ? JsonSerializer.Deserialize<JsonElement>(text) : YamlToJsonConverter.Convert(text);
        }
        catch (Exception ex) when (ex is JsonException or YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return BootstrapMapResult.Failure($"Bootstrap file '{path}' is not well-formed: {ex.Message}");
        }

        return BootstrapFileMapper.TryMap(root);
    }

    private static bool IsJson(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
