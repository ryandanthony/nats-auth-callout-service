// -----------------------------------------------------------------------
// <copyright file="IJwksProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.Tokens;

namespace NatsAuthCallout.Jwt;

/// <summary>
/// Supplies the signing keys used to validate incoming bearer JWTs.
/// </summary>
public interface IJwksProvider
{
    /// <summary>
    /// Gets the current set of signing keys, refreshing from the source as needed.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The signing keys.</returns>
    Task<IList<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default);
}
