// -----------------------------------------------------------------------
// <copyright file="SecretResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Secrets;

/// <summary>
/// <see cref="ISecretResolver"/> dispatching on <see cref="SecretRef.Kind"/> (doc 04 §Secrets,
/// ADR 0005, P03): <see cref="SecretRefKind.Inline"/> returns the literal value (dev/local
/// only — logs a warning via the optional <c>onWarning</c> callback, never the value itself);
/// <see cref="SecretRefKind.File"/> reads a mounted file's contents; <see cref="SecretRefKind.K8sSecret"/>
/// reads via the <b>mounted-volume convention</b> documented on
/// <see cref="DefaultK8sSecretMountRoot"/> rather than the Kubernetes API, so this type (and
/// this whole assembly) never takes a <c>KubernetesClient</c> dependency and works in any pod
/// without RBAC; <see cref="SecretRefKind.Vault"/> is a registered kind that always fails with
/// a deterministic <see cref="NotSupportedException"/> (doc 08: "File + k8sSecret first; vault
/// behind the same interface").
/// <para>
/// Resolution is a <b>startup-time-only</b> concern (the bootstrap layer is never hot-reloaded,
/// doc 04 §Two layers): this type does no caching, no retry, and no rotation — each call reads
/// fresh from disk and returns.
/// </para>
/// </summary>
public sealed class SecretResolver : ISecretResolver
{
    /// <summary>
    /// The default root directory under which <see cref="SecretRefKind.K8sSecret"/> references
    /// are resolved: <c>{root}/{name}/{key}</c>, mirroring how Kubernetes itself projects a
    /// Secret volume mount (one file per key, named after the key, inside a directory named
    /// after the Secret) — the same layout `kubelet` already uses for the built-in service
    /// account token at <c>/var/run/secrets/kubernetes.io/serviceaccount/</c>. P17 (Helm)
    /// mounts each referenced Kubernetes Secret at <c>{DefaultK8sSecretMountRoot}/{secretName}/</c>
    /// using a standard Secret volume, so no RBAC or API access is required by this process.
    /// </summary>
#pragma warning disable S2339 // Must stay usable as this constructor's default parameter value, which C# requires to be a compile-time constant.
    public const string DefaultK8sSecretMountRoot = "/var/run/secrets";
#pragma warning restore S2339

    private readonly string _k8sSecretMountRoot;
    private readonly Action<string>? _onWarning;

    /// <summary>Initializes a new instance of the <see cref="SecretResolver"/> class.</summary>
    /// <param name="k8sSecretMountRoot">
    /// The root directory <see cref="SecretRefKind.K8sSecret"/> references are resolved
    /// under (see <see cref="DefaultK8sSecretMountRoot"/>). Overridable for tests; production
    /// callers should leave this at the default, matching P17's Helm mount contract.
    /// </param>
    /// <param name="onWarning">
    /// Invoked with a human-readable, value-free warning message when resolution takes a
    /// discouraged path (currently: an <see cref="SecretRefKind.Inline"/> reference, dev/local
    /// use only). Optional; when null, warnings are silently dropped. Never called with the
    /// resolved secret value. Callers (P13/Host) may wire this to their logger.
    /// </param>
    public SecretResolver(string? k8sSecretMountRoot = null, Action<string>? onWarning = null)
    {
        _k8sSecretMountRoot = string.IsNullOrWhiteSpace(k8sSecretMountRoot) ? DefaultK8sSecretMountRoot : k8sSecretMountRoot;
        _onWarning = onWarning;
    }

    /// <inheritdoc />
    public Task<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretRef);
        cancellationToken.ThrowIfCancellationRequested();

        // CA2000 (dispose before scope loss) does not apply: ResolveAsync's contract, per
        // ISecretResolver, is to hand a live ResolvedSecret to the caller, who owns its
        // lifetime and disposal (P03 hygiene: "supports zeroing" is a caller-driven Dispose).
#pragma warning disable CA2000
        return secretRef.Kind switch
        {
            SecretRefKind.Inline => ResolveInline(secretRef),
            SecretRefKind.File => ResolveFileAsync(secretRef, cancellationToken),
            SecretRefKind.K8sSecret => ResolveK8sSecretAsync(secretRef, cancellationToken),
            SecretRefKind.Vault => throw new NotSupportedException(
                "SecretRefKind.Vault is registered but not implemented in v1 (doc 08: \"File + k8sSecret first; vault behind the same interface\"). Use 'file' or 'k8sSecret' instead."),
            _ => throw new NotSupportedException($"Unrecognized SecretRefKind '{secretRef.Kind}'."),
        };
#pragma warning restore CA2000
    }

    private Task<ResolvedSecret> ResolveInline(SecretRef secretRef)
    {
        if (string.IsNullOrEmpty(secretRef.Value))
        {
            throw new SecretResolutionException("Inline secret reference has no 'value'.");
        }

        _onWarning?.Invoke(
            "Resolving an inline secret reference. Inline values are for dev/local use only "
            + "(doc 04 \u00a7Secrets); production deployments should use 'file' or 'k8sSecret'.");
        return Task.FromResult(new ResolvedSecret(secretRef.Value));
    }

    private static async Task<ResolvedSecret> ResolveFileAsync(SecretRef secretRef, CancellationToken cancellationToken)
    {
        var path = secretRef.Path;
        if (string.IsNullOrEmpty(path))
        {
            throw new SecretResolutionException("File secret reference has no 'path'.");
        }

        string raw;
        try
        {
            raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new SecretResolutionException($"Secret file '{path}' could not be read.", ex);
        }

        return new ResolvedSecret(TrimTrailingNewline(raw));
    }

#pragma warning disable S100 // "K8s" matches the repo-wide convention (SecretRefKind.K8sSecret, AuthCallout.Config.K8s); "K8S" would be inconsistent.
    private async Task<ResolvedSecret> ResolveK8sSecretAsync(SecretRef secretRef, CancellationToken cancellationToken)
#pragma warning restore S100
    {
        var name = secretRef.Name;
        var key = secretRef.Key;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(key))
        {
            throw new SecretResolutionException("k8sSecret reference must have both 'name' and 'key'.");
        }

        var path = Path.Combine(_k8sSecretMountRoot, name, key);
        string raw;
        try
        {
            raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new SecretResolutionException(
                $"Kubernetes Secret '{name}' key '{key}' could not be read from the mounted-volume path '{path}'. "
                + $"Ensure the Secret is mounted at '{_k8sSecretMountRoot}/{name}' (P17 Helm mount contract).",
                ex);
        }

        return new ResolvedSecret(TrimTrailingNewline(raw));
    }

    private static string TrimTrailingNewline(string value)
    {
        const string CrLf = "\r\n";
        if (value.EndsWith(CrLf, StringComparison.Ordinal))
        {
            return value[..^CrLf.Length];
        }

        if (value.EndsWith('\n') || value.EndsWith('\r'))
        {
            return value[..^1];
        }

        return value;
    }
}
