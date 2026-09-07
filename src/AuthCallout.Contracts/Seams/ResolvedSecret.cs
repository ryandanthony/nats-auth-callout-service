// -----------------------------------------------------------------------
// <copyright file="ResolvedSecret.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// A resolved secret value that redacts itself from accidental exposure (P03 §Hygiene). Its
/// <see cref="ToString"/> and default string-interpolation/formatting never reveal
/// <see cref="Reveal"/>'s contents, so a resolved secret accidentally passed to a logger,
/// exception message, or string interpolation cannot leak the value; callers must call
/// <see cref="Reveal"/> explicitly to obtain the plaintext.
/// </summary>
public sealed class ResolvedSecret
{
    private readonly string _value;

    /// <summary>Initializes a new instance of the <see cref="ResolvedSecret"/> class, wrapping a resolved plaintext value.</summary>
    /// <param name="value">The resolved plaintext secret value.</param>
    public ResolvedSecret(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>Returns the plaintext secret value. The only way to obtain it — never implicit.</summary>
    /// <returns>The plaintext secret value.</returns>
    public string Reveal() => _value;

    /// <inheritdoc />
    public override string ToString() => "[redacted]";
}
