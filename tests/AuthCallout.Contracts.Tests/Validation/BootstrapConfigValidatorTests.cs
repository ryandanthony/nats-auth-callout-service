// -----------------------------------------------------------------------
// <copyright file="BootstrapConfigValidatorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Contracts.Validation;

namespace AuthCallout.Contracts.Tests.Validation;

/// <summary>
/// Table tests for every doc 04 §Validation rule enforced by
/// <see cref="ConfigValidator.ValidateBootstrap"/>, plus the cross-layer assertion-keyset
/// rule enforced by <see cref="ConfigValidator.ValidateActiveConfig"/> (ADR 0011, ADR 0014).
/// </summary>
public sealed class BootstrapConfigValidatorTests
{
    [Fact]
    public void ValidBootstrap_Passes()
    {
        var bootstrap = TestBuilders.ValidBootstrap();

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyNatsUrl_Rejected()
    {
        var bootstrap = TestBuilders.ValidBootstrap() with { NatsUrl = string.Empty };

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "nats-url-required", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedIssuerSeed_Rejected()
    {
        // Constructed via the record primary constructor (bypassing SecretRef's own
        // factories) to exercise SecretRefShape's defense-in-depth check.
        var malformed = new SecretRef(SecretRefKind.File, Value: null, Path: null, Name: null, Key: null);
        var bootstrap = TestBuilders.ValidBootstrap() with { IssuerSeed = malformed };

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "malformed-secret-ref", StringComparison.Ordinal));
    }

    [Fact]
    public void AssertionKeyset_ZeroActive_Rejected()
    {
        var keys = new[] { TestBuilders.ValidAssertionKey("a", active: false), TestBuilders.ValidAssertionKey("b", active: false) };
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: keys);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "assertion-keyset-active-count", StringComparison.Ordinal));
    }

    [Fact]
    public void AssertionKeyset_MultipleActive_Rejected()
    {
        var keys = new[] { TestBuilders.ValidAssertionKey("a", active: true), TestBuilders.ValidAssertionKey("b", active: true) };
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: keys);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "assertion-keyset-active-count", StringComparison.Ordinal));
    }

    [Fact]
    public void AssertionKeyset_ExactlyOneActive_IsValid()
    {
        var keys = new[] { TestBuilders.ValidAssertionKey("a", active: true), TestBuilders.ValidAssertionKey("b", active: false) };
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: keys);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AssertionKeyset_ReservedKid_Rejected()
    {
        var keys = new[] { TestBuilders.ValidAssertionKey(AssertionKey.ReservedCurrentKid, active: true) };
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: keys);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "reserved-kid", StringComparison.Ordinal));
    }

    [Fact]
    public void AssertionKeyset_DuplicateKid_Rejected()
    {
        var keys = new[] { TestBuilders.ValidAssertionKey("dup", active: true), TestBuilders.ValidAssertionKey("dup", active: false) };
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: keys);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "duplicate-kid", StringComparison.Ordinal));
    }

    [Fact]
    public void FilesSource_MissingDirectory_Rejected()
    {
        var source = SourceConfig.Files(
            new FilesSourceSettings(string.Empty),
            DurationString.Parse("60s"),
            DurationString.Parse("15m"),
            DurationString.Parse("5s"));
        var bootstrap = TestBuilders.ValidBootstrap(source: source);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "files-directory-required", StringComparison.Ordinal));
    }

    [Fact]
    public void K8sSource_MissingSelector_Rejected()
    {
        var source = SourceConfig.K8s(
            new K8sSourceSettings(string.Empty),
            DurationString.Parse("60s"),
            DurationString.Parse("15m"),
            DurationString.Parse("5s"));
        var bootstrap = TestBuilders.ValidBootstrap(source: source);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "k8s-selector-required", StringComparison.Ordinal));
    }

    [Fact]
    public void PostgresSource_MissingConnectionString_Rejected()
    {
        var malformedConnectionString = new SecretRef(SecretRefKind.Inline, Value: null, Path: null, Name: null, Key: null);
        var source = SourceConfig.Postgres(
            new PostgresSourceSettings(malformedConnectionString, DurationString.Parse("30s")),
            DurationString.Parse("60s"),
            DurationString.Parse("15m"),
            DurationString.Parse("5s"));
        var bootstrap = TestBuilders.ValidBootstrap(source: source);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "malformed-secret-ref", StringComparison.Ordinal));
    }

    [Fact]
    public void PostgresSource_DefaultNotifyChannel_IsValid()
    {
        var source = SourceConfig.Postgres(
            new PostgresSourceSettings(SecretRef.Inline("postgres://localhost/db"), DurationString.Parse("30s")),
            DurationString.Parse("60s"),
            DurationString.Parse("15m"),
            DurationString.Parse("5s"));
        var bootstrap = TestBuilders.ValidBootstrap(source: source);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.True(result.IsValid);
        Assert.Equal(PostgresSourceSettings.DefaultNotifyChannel, source.PostgresSettings!.NotifyChannel);
    }

    [Fact]
    public void MaxStalenessNotGreaterThanWarnStaleness_Rejected()
    {
        var source = SourceConfig.Files(
            new FilesSourceSettings("/config/apps"),
            DurationString.Parse("60s"),
            DurationString.Parse("60s"),
            DurationString.Parse("5s"));
        var bootstrap = TestBuilders.ValidBootstrap(source: source);

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "max-staleness-exceeds-warn", StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroMaxDegreeOfParallelism_Rejected()
    {
        var bootstrap = TestBuilders.ValidBootstrap() with
        {
            Concurrency = new ConcurrencySettings(MaxDegreeOfParallelism: 0, PerRequestTimeout: DurationString.Parse("1s"), QueueCapacity: null),
        };

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "max-degree-of-parallelism-positive", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyAnonymousTargetAccount_Rejected()
    {
        var bootstrap = TestBuilders.ValidBootstrap() with
        {
            Anonymous = new AnonymousGlobalConfig(TargetAccount: string.Empty, Expiry: DurationString.Parse("5m"), Events: null),
        };

        var result = ConfigValidator.ValidateBootstrap(bootstrap);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "anonymous-target-account-required", StringComparison.Ordinal));
    }

    [Fact]
    public void ActiveConfig_PropagationEnabled_WithNoAssertionKeys_Rejected()
    {
        var app = TestBuilders.ValidApplication(identityPropagation: new IdentityPropagationConfig(Enabled: true));
        var routing = new RoutingConfig([app]);
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: null);

        var result = ConfigValidator.ValidateActiveConfig(bootstrap, routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "assertion-keyset-required", StringComparison.Ordinal));
    }

    [Fact]
    public void ActiveConfig_PropagationDisabledEverywhere_WithNoAssertionKeys_IsValid()
    {
        var app = TestBuilders.ValidApplication(identityPropagation: new IdentityPropagationConfig(Enabled: false));
        var routing = new RoutingConfig([app]);
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: null);

        var result = ConfigValidator.ValidateActiveConfig(bootstrap, routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ActiveConfig_PropagationEnabled_WithAssertionKeys_IsValid()
    {
        var app = TestBuilders.ValidApplication(identityPropagation: new IdentityPropagationConfig(Enabled: true));
        var routing = new RoutingConfig([app]);
        var bootstrap = TestBuilders.ValidBootstrap(assertionKeys: [TestBuilders.ValidAssertionKey()]);

        var result = ConfigValidator.ValidateActiveConfig(bootstrap, routing);

        Assert.True(result.IsValid);
    }
}
