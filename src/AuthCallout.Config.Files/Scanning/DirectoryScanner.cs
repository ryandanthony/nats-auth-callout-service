// -----------------------------------------------------------------------
// <copyright file="DirectoryScanner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Config.Files.Parsing;
using AuthCallout.Config.Files.Yaml;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Contracts.Validation;

namespace AuthCallout.Config.Files.Scanning;

/// <summary>
/// Performs one full scan of a directory of per-application config files: list, parse, map,
/// aggregate, validate (doc 09 §3, ADR 0006). Owns the file-system and mapping side of
/// <see cref="FileRoutingConfigProvider"/>, split out so the provider itself only orchestrates
/// scheduling (doc 08 isolation-of-concerns convention mirrored at the class level).
/// </summary>
internal sealed class DirectoryScanner
{
    private static readonly string[] ConfigFilePatterns = ["*.yaml", "*.yml", "*.json"];

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="DirectoryScanner"/> class.</summary>
    /// <param name="directory">The directory to scan (non-recursive, doc 09 §3).</param>
    /// <param name="timeProvider">The time source for <see cref="RoutingConfigBatch.ObservedAt"/>.</param>
    internal DirectoryScanner(string directory, TimeProvider timeProvider)
    {
        _directory = directory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets the deterministic error from the most recent rejected scan (naming the offending
    /// file(s), ADR 0006), or null when the most recent scan succeeded or none has run yet.
    /// </summary>
    /// <value>The most recent scan's rejection error, or null.</value>
    internal string? LastError { get; private set; }

    /// <summary>
    /// Gets the schema-version-migration warnings from the most recent scan (doc 09 §3),
    /// regardless of whether that scan's batch was ultimately accepted or rejected.
    /// </summary>
    /// <value>The most recent scan's migration warnings.</value>
    internal IReadOnlyList<string> LastWarnings { get; private set; } = [];

    /// <summary>
    /// Scans the configured directory once. Returns the validated batch, or null (with
    /// <see cref="LastError"/> set) when any file failed to parse/map or the aggregate failed
    /// validation (ADR 0006: the whole batch is rejected, this method never returns a
    /// partially-valid result).
    /// </summary>
    /// <returns>The validated batch, or null when the scan was rejected.</returns>
    /// <exception cref="IOException">The directory could not be enumerated.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory could not be accessed.</exception>
    internal RoutingConfigBatch? Scan()
    {
        var files = ListConfigFiles();

        var applications = new List<Application>();
        var failures = new List<string>();
        var warnings = new List<string>();

        foreach (var file in files)
        {
            ScanFile(file, applications, failures, warnings);
        }

        LastWarnings = warnings;

        if (failures.Count > 0)
        {
            LastError = $"Rejected batch: {failures.Count} file(s) invalid (keeping last-good, ADR 0006): {string.Join(" | ", failures)}";
            return null;
        }

        var routing = new RoutingConfig(applications);
        var validation = ConfigValidator.ValidateRouting(routing);

        if (!validation.IsValid)
        {
            LastError = $"Rejected batch: aggregate validation failed (keeping last-good, ADR 0006): {string.Join(" | ", validation.Errors.Select(static e => e.Message))}";
            return null;
        }

        LastError = null;
        return new RoutingConfigBatch(routing, _timeProvider.GetUtcNow());
    }

    private static void ScanFile(string file, List<Application> applications, List<string> failures, List<string> warnings)
    {
        var fileName = Path.GetFileName(file);

        JsonElement root;
        try
        {
            root = ParseFile(file);
        }
        catch (Exception ex) when (ex is JsonException or YamlDotNet.Core.YamlException)
        {
            failures.Add($"{fileName}: failed to parse ({ex.Message})");
            return;
        }

        var result = ApplicationFileMapper.TryMap(root);
        if (result.SchemaVersionDefaulted)
        {
            warnings.Add(
                $"{fileName}: 'schemaVersion' missing, treated as {ApplicationFileMapper.SupportedSchemaVersion} during the migration window (doc 09 §3).");
        }

        if (!result.IsSuccess)
        {
            failures.Add($"{fileName}: {result.Error}");
            return;
        }

        applications.Add(result.Application!);
    }

    private static JsonElement ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        return YamlToJsonConverter.Convert(text);
    }

    private List<string> ListConfigFiles()
    {
        var files = new List<string>();
        foreach (var pattern in ConfigFilePatterns)
        {
            files.AddRange(Directory.EnumerateFiles(_directory, pattern, SearchOption.TopDirectoryOnly));
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
