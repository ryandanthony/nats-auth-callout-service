// -----------------------------------------------------------------------
// <copyright file="BootstrapLoading.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Validation;
using AuthCallout.Host.Config;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Loads and validates the bootstrap file, failing closed (doc 08 task 1), factored out of
/// <c>Program.cs</c> purely to keep that entry point's own class-coupling count under the
/// repo's analyzer limit.
/// </summary>
public static class BootstrapLoading
{
    /// <summary>
    /// Loads the bootstrap file from <see cref="BootstrapConfigLoader.ResolvePath"/> and runs
    /// <see cref="ConfigValidator.ValidateBootstrap"/> over it. On any failure, writes a
    /// deterministic <c>FATAL:</c> line to standard error and returns null — the caller's
    /// contract is "null means exit non-zero, the reason is already on stderr".
    /// </summary>
    /// <returns>The validated bootstrap config, or null on any load/validation failure.</returns>
    public static async Task<BootstrapConfig?> LoadAndValidateAsync()
    {
        var bootstrapPath = BootstrapConfigLoader.ResolvePath();
        var mapResult = BootstrapConfigLoader.Load(bootstrapPath);
        if (!mapResult.Succeeded)
        {
            await Console.Error.WriteLineAsync($"FATAL: bootstrap file '{bootstrapPath}' failed to load: {mapResult.Error}").ConfigureAwait(false);
            return null;
        }

        var bootstrap = mapResult.Bootstrap!;
        var validation = ConfigValidator.ValidateBootstrap(bootstrap);
        if (validation.IsValid)
        {
            return bootstrap;
        }

        var reason = string.Join("; ", validation.Errors.Select(static e => $"{e.Code}: {e.Message}"));
        await Console.Error.WriteLineAsync($"FATAL: bootstrap file '{bootstrapPath}' is invalid: {reason}").ConfigureAwait(false);
        return null;
    }
}
