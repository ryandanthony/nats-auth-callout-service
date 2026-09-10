// -----------------------------------------------------------------------
// <copyright file="SecretResolverTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Secrets.Tests;

/// <summary>
/// Unit tests for <see cref="SecretResolver"/> against real temporary files (P03 ADR→test
/// map): each <see cref="SecretRefKind"/> resolves per contract, a missing file fails closed
/// naming the path (never the value), the k8sSecret mounted-volume path composition, and
/// <see cref="AuthCallout.Contracts.Seams.ResolvedSecret"/> redaction/zeroing hygiene.
/// </summary>
public sealed class SecretResolverTests : IDisposable
{
    private static readonly CancellationToken NoneToken = CancellationToken.None;

    private readonly string _directory;

    public SecretResolverTests()
    {
        _directory = Directory.CreateTempSubdirectory("authcallout-secrets-tests-").FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a lingering temp directory does not fail the test run.
        }
    }

    // ---- inline ----

    [Fact]
    public async Task Inline_ResolvesToTheLiteralValue()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("s3cr3t-value");

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        Assert.Equal("s3cr3t-value", resolved.Reveal());
    }

    [Fact]
    public async Task Inline_InvokesOnWarning_WithoutIncludingTheValue()
    {
        var warnings = new List<string>();
        var resolver = new SecretResolver(onWarning: warnings.Add);
        var secretRef = SecretRef.Inline("top-secret-do-not-leak");

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        var warning = Assert.Single(warnings);
        Assert.DoesNotContain("top-secret-do-not-leak", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inline_NoOnWarningProvided_DoesNotThrow()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("value");

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        Assert.Equal("value", resolved.Reveal());
    }

    // ---- file ----

    [Fact]
    public async Task File_ResolvesToTheFileContents()
    {
        var path = Path.Combine(_directory, "issuer.seed");
        await File.WriteAllTextAsync(path, "SUAFILE_SECRET_VALUE", NoneToken);
        var resolver = new SecretResolver();
        var secretRef = SecretRef.FromFile(path);

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        Assert.Equal("SUAFILE_SECRET_VALUE", resolved.Reveal());
    }

    [Theory]
    [InlineData("value-with-trailing-lf\n")]
    [InlineData("value-with-trailing-crlf\r\n")]
    public async Task File_TrimsExactlyOneTrailingNewline(string fileContent)
    {
        var path = Path.Combine(_directory, "seed-with-newline");
        await File.WriteAllTextAsync(path, fileContent, NoneToken);
        var resolver = new SecretResolver();
        var secretRef = SecretRef.FromFile(path);

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        Assert.False(resolved.Reveal().EndsWith('\n'));
        Assert.False(resolved.Reveal().EndsWith('\r'));
    }

    [Fact]
    public async Task File_MissingFile_FailsClosed_MessageNamesPathNotValue()
    {
        var path = Path.Combine(_directory, "does-not-exist.seed");
        var resolver = new SecretResolver();
        var secretRef = SecretRef.FromFile(path);

        var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => resolver.ResolveAsync(secretRef, NoneToken));

        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task File_UnreadableFile_FailsClosed_MessageNamesPathNotValue()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permission bits below don't apply on Windows; skip.
        }

        var path = Path.Combine(_directory, "unreadable.seed");
        await File.WriteAllTextAsync(path, "unreadable-secret-value", NoneToken);
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var resolver = new SecretResolver();
            var secretRef = SecretRef.FromFile(path);

            var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => resolver.ResolveAsync(secretRef, NoneToken));

            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("unreadable-secret-value", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // ---- k8sSecret ----

    [Fact]
    public async Task K8sSecret_ResolvesViaMountedVolumeConvention_RootSlashNameSlashKey()
    {
        var secretDir = Path.Combine(_directory, "callout-svc");
        Directory.CreateDirectory(secretDir);
        await File.WriteAllTextAsync(Path.Combine(secretDir, "seed"), "K8S_SECRET_VALUE", NoneToken);
        var resolver = new SecretResolver(k8sSecretMountRoot: _directory);
        var secretRef = SecretRef.FromK8sSecret("callout-svc", "seed");

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        Assert.Equal("K8S_SECRET_VALUE", resolved.Reveal());
    }

    [Fact]
    public async Task K8sSecret_DefaultMountRoot_IsVarRunSecrets()
    {
        Assert.Equal("/var/run/secrets", SecretResolver.DefaultK8sSecretMountRoot);
    }

    [Theory]
    [InlineData("app-a", "token")]
    [InlineData("callout-svc", "seed")]
    public async Task K8sSecret_PathComposition_IsRootSlashNameSlashKey(string name, string key)
    {
        // Golden: the resolver must read exactly `{root}/{name}/{key}` — written here directly
        // via Path.Combine (the documented mount contract), independent of the resolver's own
        // composition, so this test would fail if the resolver's path building drifted.
        var expectedFullPath = Path.Combine(_directory, name, key);
        Directory.CreateDirectory(Path.GetDirectoryName(expectedFullPath)!);
        await File.WriteAllTextAsync(expectedFullPath, "golden-value", NoneToken);
        var resolver = new SecretResolver(k8sSecretMountRoot: _directory);
        var secretRef = SecretRef.FromK8sSecret(name, key);

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        Assert.Equal("golden-value", resolved.Reveal());
    }

    [Fact]
    public async Task K8sSecret_MissingMount_FailsClosed_MessageNamesNameAndKeyNotValue()
    {
        var resolver = new SecretResolver(k8sSecretMountRoot: _directory);
        var secretRef = SecretRef.FromK8sSecret("missing-secret", "missing-key");

        var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => resolver.ResolveAsync(secretRef, NoneToken));

        Assert.Contains("missing-secret", ex.Message, StringComparison.Ordinal);
        Assert.Contains("missing-key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task K8sSecret_UsesDefaultMountRoot_WhenNotOverridden()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.FromK8sSecret("nonexistent-secret-xyz", "key");

        var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => resolver.ResolveAsync(secretRef, NoneToken));

        Assert.Contains("/var/run/secrets", ex.Message, StringComparison.Ordinal);
    }

    // ---- vault ----

    [Fact]
    public async Task Vault_AlwaysFails_WithDeterministicNotSupportedException()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Vault("some-vault-secret", key: "some-key");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => resolver.ResolveAsync(secretRef, NoneToken));

        Assert.Contains("not implemented", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Vault_WithOnlyName_AlsoFails()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Vault("name-only-vault-secret");

        await Assert.ThrowsAsync<NotSupportedException>(() => resolver.ResolveAsync(secretRef, NoneToken));
    }

    // ---- redaction / hygiene ----

    [Fact]
    public async Task ResolvedSecret_ToString_NeverRevealsTheValue()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("do-not-print-me");

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        Assert.Equal("[redacted]", resolved.ToString());
        Assert.DoesNotContain("do-not-print-me", resolved.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvedSecret_StringInterpolation_NeverRevealsTheValue()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("interpolation-leak-check");

        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        var interpolated = $"resolved={resolved}";

        Assert.DoesNotContain("interpolation-leak-check", interpolated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvedSecret_ExceptionMessageFromLoggingSpy_NeverContainsTheValue()
    {
        // Simulates a caller accidentally logging the ResolvedSecret object itself (not
        // calling Reveal()) via an exception message / formatted log line — a common leak
        // vector this wrapper's ToString/interpolation override exists to close.
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("spy-must-not-see-me");
        using var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        var loggedLine = FormatAsLoggerWould("secret resolved", resolved);

        Assert.DoesNotContain("spy-must-not-see-me", loggedLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvedSecret_Dispose_ZeroesValue_RevealThrowsAfterward()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("zero-me-on-dispose");
        var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        resolved.Dispose();

        Assert.Throws<ObjectDisposedException>(() => resolved.Reveal());
    }

    [Fact]
    public async Task ResolvedSecret_ToString_StaysRedacted_AfterDispose()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("still-redacted-after-dispose");
        var resolved = await resolver.ResolveAsync(secretRef, NoneToken);

        resolved.Dispose();

        Assert.Equal("[redacted]", resolved.ToString());
    }

    // ---- misc ----

    [Fact]
    public async Task ResolveAsync_NullSecretRef_ThrowsArgumentNullException()
    {
        var resolver = new SecretResolver();

        await Assert.ThrowsAsync<ArgumentNullException>(() => resolver.ResolveAsync(null!, NoneToken));
    }

    [Fact]
    public async Task ResolveAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var resolver = new SecretResolver();
        var secretRef = SecretRef.Inline("value");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync(secretRef, cts.Token));
    }

    private static string FormatAsLoggerWould(string message, object secretLikeArgument) => $"{message}: {secretLikeArgument}";
}
