// -----------------------------------------------------------------------
// <copyright file="DockerCalloutConfig.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace AppHost.Local;

/// <summary>
/// Renders the container flavour of AppHost.Local's callout config (<c>--callout docker</c>)
/// from the one committed source of truth, <c>config/</c>, so the two modes never drift and
/// editing <c>config/apps/*.yaml</c> still hot-reloads in docker mode.
/// </summary>
/// <remarks>
/// Only what a container can't reach changes: <c>natsUrl</c> (host <c>localhost:4222</c> becomes
/// the <c>nats</c> container alias), <c>source.directory</c> (the bind-mounted
/// <c>/config/apps</c>), and each app's issuer gets an explicit <c>jwksUri</c> on Keycloak's
/// in-network alias. <c>authority</c> stays untouched on purpose: it must keep matching the
/// tokens' <c>iss</c> claim, which Keycloak derives from the host URL the samples fetch tokens
/// through (<c>https://localhost:8083</c>). <c>idp.dev.internal</c> is Aspire's per-resource
/// container-network alias, covered by the ASP.NET Core dev cert's <c>*.dev.internal</c> SAN,
/// and Aspire trusts that cert inside containers by default (SSL_CERT_DIR), so the JWKS fetch
/// keeps its unconditional RequireHttps.
/// </remarks>
public sealed partial class DockerCalloutConfig : IDisposable
{
    // Aspire.Hosting.Keycloak's fixed in-container HTTPS port (it retargets the "http"
    // endpoint to 8443 once the dev cert is used for HTTPS termination).
    private const int KeycloakContainerHttpsPort = 8443;
    private const int RegexTimeoutMs = 1000;

    private readonly string _sourceDirectory;
    private readonly string _outputDirectory;
    private readonly string _hostIssuerBase;
    private readonly string _containerIssuerBase;
    private readonly FileSystemWatcher _watcher;
    private readonly Lock _renderLock = new();

    private DockerCalloutConfig(string sourceDirectory, string outputDirectory, int keycloakHostPort, string keycloakResourceName)
    {
        _sourceDirectory = sourceDirectory;
        _outputDirectory = outputDirectory;
        _hostIssuerBase = $"https://localhost:{keycloakHostPort}";
        _containerIssuerBase = $"https://{keycloakResourceName}.dev.internal:{KeycloakContainerHttpsPort}";

        RenderBootstrap();
        RenderApps();

        _watcher = new FileSystemWatcher(Path.Combine(_sourceDirectory, "apps"))
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += (_, _) => RenderAppsSafely();
        _watcher.Created += (_, _) => RenderAppsSafely();
        _watcher.Deleted += (_, _) => RenderAppsSafely();
        _watcher.Renamed += (_, _) => RenderAppsSafely();
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Gets the rendered directory to bind-mount at <c>/config</c> in the callout container.</summary>
    /// <value>The absolute path of the rendered <c>config-docker/</c> directory.</value>
    public string OutputDirectory => _outputDirectory;

    /// <summary>Renders the docker config once and keeps <c>apps/</c> in sync until disposed.</summary>
    /// <param name="sourceDirectory">The committed local-mode config directory (<c>config/</c>).</param>
    /// <param name="outputDirectory">Where to render the container flavour (git-ignored).</param>
    /// <param name="keycloakHostPort">Keycloak's host port, as used in each app's <c>issuer.authority</c>.</param>
    /// <param name="keycloakResourceName">Keycloak's Aspire resource name (its container-network alias prefix).</param>
    /// <returns>The live renderer.</returns>
    public static DockerCalloutConfig Start(string sourceDirectory, string outputDirectory, int keycloakHostPort, string keycloakResourceName) =>
        new(sourceDirectory, outputDirectory, keycloakHostPort, keycloakResourceName);

    /// <inheritdoc/>
    public void Dispose() => _watcher.Dispose();

    [GeneratedRegex(@"^natsUrl:.*$", RegexOptions.Multiline, RegexTimeoutMs)]
    private static partial Regex NatsUrlLine();

    [GeneratedRegex(@"^(?<indent>[ \t]+)directory:.*$", RegexOptions.Multiline, RegexTimeoutMs)]
    private static partial Regex SourceDirectoryLine();

    [GeneratedRegex(@"^(?<indent>[ \t]+)authority:[ \t]*(?<value>\S+)[ \t]*$", RegexOptions.Multiline, RegexTimeoutMs)]
    private static partial Regex AuthorityLine();

    [GeneratedRegex(@"^[ \t]+jwksUri:", RegexOptions.Multiline, RegexTimeoutMs)]
    private static partial Regex JwksUriLine();

    private static string ReplaceExactlyOnce(string text, Regex pattern, MatchEvaluator replacement, string what)
    {
        var count = pattern.Count(text);
        if (count != 1)
        {
            throw new InvalidOperationException($"Docker callout config: expected exactly one {what} line in config/bootstrap.yaml, found {count}.");
        }

        return pattern.Replace(text, replacement);
    }

    private void RenderBootstrap()
    {
        var text = File.ReadAllText(Path.Combine(_sourceDirectory, "bootstrap.yaml"));
        text = ReplaceExactlyOnce(text, NatsUrlLine(), _ => "natsUrl: nats://nats:4222", "natsUrl");
        text = ReplaceExactlyOnce(text, SourceDirectoryLine(), m => $"{m.Groups["indent"].Value}directory: /config/apps", "source.directory");
        WriteIfChanged(Path.Combine(_outputDirectory, "bootstrap.yaml"), text);
    }

    private void RenderAppsSafely()
    {
        try
        {
            RenderApps();
        }
        catch (IOException ex)
        {
            // Editors often write in several steps; the next event re-renders.
            Console.Error.WriteLine($"Docker callout config: re-render skipped ({ex.Message}).");
        }
    }

    private void RenderApps()
    {
        lock (_renderLock)
        {
            var sourceApps = Path.Combine(_sourceDirectory, "apps");
            var outputApps = Path.Combine(_outputDirectory, "apps");
            Directory.CreateDirectory(outputApps);

            var rendered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in Directory.EnumerateFiles(sourceApps).Where(IsConfigFile))
            {
                var name = Path.GetFileName(source);
                rendered.Add(name);
                WriteIfChanged(Path.Combine(outputApps, name), RenderApp(File.ReadAllText(source)));
            }

            foreach (var stale in Directory.EnumerateFiles(outputApps).Where(f => !rendered.Contains(Path.GetFileName(f))))
            {
                File.Delete(stale);
            }
        }
    }

    private string RenderApp(string text)
    {
        // An explicit jwksUri in the source wins; only the host-loopback Keycloak authority is
        // rewritten, any other issuer passes through untouched.
        if (JwksUriLine().IsMatch(text))
        {
            return text;
        }

        return AuthorityLine().Replace(text, m =>
        {
            var authority = m.Groups["value"].Value;
            if (!authority.StartsWith(_hostIssuerBase + "/", StringComparison.OrdinalIgnoreCase))
            {
                return m.Value;
            }

            var jwksUri = $"{_containerIssuerBase}{authority[_hostIssuerBase.Length..]}/protocol/openid-connect/certs";
            return $"{m.Value}\n{m.Groups["indent"].Value}jwksUri: {jwksUri}";
        });
    }

    private static bool IsConfigFile(string path) =>
        path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    // Write-then-rename from a sibling temp dir so the callout's own watcher (inside the
    // container, on the bind mount) never observes a half-written file, and skip no-op writes
    // so an unrelated editor touch doesn't trigger a spurious config reload.
    private void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        var tempDirectory = Path.Combine(_outputDirectory, ".tmp");
        Directory.CreateDirectory(tempDirectory);
        var temp = Path.Combine(tempDirectory, Path.GetFileName(path));
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
