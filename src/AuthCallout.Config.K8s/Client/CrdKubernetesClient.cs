// -----------------------------------------------------------------------
// <copyright file="CrdKubernetesClient.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace AuthCallout.Config.K8s.Client;

/// <summary>
/// The real <see cref="ICrdKubernetesClient"/>, backed by KubernetesClient (doc 08 §Kubernetes
/// CRD source). The only type in <c>AuthCallout.Config.K8s</c> that references <c>k8s.*</c>
/// directly, keeping the Kubernetes client dependency isolated to this project (ADR 0005,
/// doc 08 isolation invariant).
/// </summary>
public sealed class CrdKubernetesClient : ICrdKubernetesClient, IDisposable
{
    private readonly Kubernetes _client;
    private readonly string _ownerLabelSelector;
    private readonly bool _ownsClient;

    /// <summary>Initializes a new instance of the <see cref="CrdKubernetesClient"/> class, resolving cluster config automatically.</summary>
    /// <param name="ownerLabelSelector">The label selector this instance uses to claim owned Custom Resources (ADR 0005).</param>
    public CrdKubernetesClient(string ownerLabelSelector)
        : this(ownerLabelSelector, new Kubernetes(ResolveConfig()))
    {
        _ownsClient = true;
    }

    /// <summary>Initializes a new instance of the <see cref="CrdKubernetesClient"/> class over a caller-supplied client (e.g. for k3s Testcontainers in P14).</summary>
    /// <param name="ownerLabelSelector">The label selector this instance uses to claim owned Custom Resources (ADR 0005).</param>
    /// <param name="client">The Kubernetes client to use. Not disposed by this instance.</param>
    public CrdKubernetesClient(string ownerLabelSelector, Kubernetes client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerLabelSelector);
        ArgumentNullException.ThrowIfNull(client);
        _ownerLabelSelector = ownerLabelSelector;
        _client = client;
    }

    /// <summary>Gets the <c>AuthApplication</c> CRD's group (doc 09 §4).</summary>
    /// <value>The CRD group, <c>authcallout.ants.zone</c>.</value>
    public static string Group => "authcallout.ants.zone";

    /// <summary>Gets the <c>AuthApplication</c> CRD's served/storage version (doc 09 §4).</summary>
    /// <value>The CRD version, <c>v1</c>.</value>
    public static string Version => "v1";

    /// <summary>Gets the <c>AuthApplication</c> CRD's plural name, used in the API path.</summary>
    /// <value>The CRD plural name, <c>authapplications</c>.</value>
    public static string Plural => "authapplications";

    /// <inheritdoc />
    public async Task<CrdListResult> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.CustomObjects.ListCustomObjectForAllNamespacesWithHttpMessagesAsync(
            group: Group,
            version: Version,
            resourcePlural: Plural,
            labelSelector: _ownerLabelSelector,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var list = (JsonElement)response.Body;
        var resourceVersion = list.GetProperty("metadata").GetProperty("resourceVersion").GetString() ?? string.Empty;
        var resources = new List<CustomResourceRecord>();
        foreach (var item in list.GetProperty("items").EnumerateArray())
        {
            resources.Add(ToRecord(item));
        }

        return new CrdListResult(resources, resourceVersion);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CrdChangeEvent> WatchAsync(
        string resourceVersion, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = _client.CustomObjects.WatchListCustomObjectForAllNamespacesAsync(
            group: Group,
            version: Version,
            resourcePlural: Plural,
            labelSelector: _ownerLabelSelector,
            resourceVersion: resourceVersion,
            cancellationToken: cancellationToken);

        await using var enumerator = events.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            var next = await TryMoveNextAsync(enumerator).ConfigureAwait(false);
            if (next is null)
            {
                yield break;
            }

            var (eventType, item) = next.Value;
            if (eventType is WatchEventType.Error or WatchEventType.Bookmark)
            {
                // An Error-typed event's body is a Status object, not a CR (the watch stream
                // itself throwing on transport failure is the real signal acted on below); a
                // Bookmark carries no CR payload. Both are skipped rather than mapped as a change.
                continue;
            }

            var kind = ToChangeKind(eventType);
            yield return new CrdChangeEvent(kind, ToRecord((JsonElement)item));
        }
    }

    /// <inheritdoc />
    public async Task PatchStatusAsync(
        CustomResourceRecord resource, long observedGeneration, bool accepted, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var statusBody = new
        {
            status = new
            {
                observedGeneration,
                accepted,
                reason = accepted ? string.Empty : reason,
            },
        };
        var patch = new V1Patch(statusBody, V1Patch.PatchType.MergePatch);

        try
        {
            await _client.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                body: patch,
                group: Group,
                version: Version,
                namespaceParameter: resource.Namespace,
                plural: Plural,
                name: resource.Name,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpOperationException)
        {
            // Status write failures are logged by the caller, never fatal (P11 packet §Status
            // write-back: "Status write failures are logged, never fatal") - the CR may have
            // been deleted or modified concurrently; the next successful list/watch cycle
            // reconciles status again.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    /// <summary>
    /// Resolves cluster config the way doc 08 specifies: in-cluster when running as a pod,
    /// falling back to the local kubeconfig for Aspire/dev (doc 08 §Kubernetes CRD source).
    /// Out-of-cluster uses <see cref="KubernetesClientConfiguration.BuildDefaultConfig"/>, which
    /// honors the <c>KUBECONFIG</c> environment variable before falling back to <c>~/.kube/config</c>;
    /// the argumentless <c>BuildConfigFromConfigFile()</c> silently ignores <c>KUBECONFIG</c> and
    /// always reads the default path, which breaks any dev/CI run pointing KUBECONFIG at a
    /// non-default cluster (e.g. the Aspire K3s AppHost).
    /// </summary>
    /// <returns>The resolved client configuration.</returns>
    private static KubernetesClientConfiguration ResolveConfig() =>
        KubernetesClientConfiguration.IsInCluster()
            ? KubernetesClientConfiguration.InClusterConfig()
            : KubernetesClientConfiguration.BuildDefaultConfig();

    private static CrdChangeKind ToChangeKind(WatchEventType eventType) => eventType switch
    {
        WatchEventType.Added => CrdChangeKind.Added,
        WatchEventType.Deleted => CrdChangeKind.Deleted,
        _ => CrdChangeKind.Modified,
    };

    /// <summary>
    /// Advances the watch enumerator, translating a 410-Gone transport failure into
    /// <see cref="CrdWatchGoneException"/> (doc 08/ADR 0017: relist-and-rewatch on 410).
    /// </summary>
    private static async Task<(WatchEventType eventType, object item)?> TryMoveNextAsync(
        IAsyncEnumerator<(WatchEventType eventType, object item)> enumerator)
    {
        try
        {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                return null;
            }

            return (enumerator.Current.eventType, enumerator.Current.item);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Gone)
        {
            throw new CrdWatchGoneException(
                "The Custom Resource watch's resourceVersion expired (410 Gone); a relist is required.", ex);
        }
    }

    private static CustomResourceRecord ToRecord(JsonElement item)
    {
        var metadata = item.GetProperty("metadata");
        var name = metadata.GetProperty("name").GetString()!;
        var ns = metadata.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() ?? string.Empty : string.Empty;
        var generation = metadata.TryGetProperty("generation", out var genEl) ? genEl.GetInt64() : 0L;
        var spec = item.TryGetProperty("spec", out var specEl) ? specEl : default;
        return new CustomResourceRecord(name, ns, generation, spec);
    }
}
