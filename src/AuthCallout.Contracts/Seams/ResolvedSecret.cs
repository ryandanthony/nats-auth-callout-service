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
/// <para>
/// The plaintext is held in a mutable <see cref="char"/> buffer rather than an immutable
/// <see cref="string"/> so it can be best-effort zeroed via <see cref="Dispose"/> once the
/// consumer no longer needs it (P03 §Hygiene "supports zeroing"). Note that a value obtained
/// through <see cref="Reveal"/> materializes an immutable string copy the GC controls; zeroing
/// only clears this wrapper's own buffer, so callers should minimize the lifetime of revealed
/// copies.
/// </para>
/// </summary>
public sealed class ResolvedSecret : IDisposable
{
    private readonly char[] _value;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ResolvedSecret"/> class, wrapping a resolved plaintext value.</summary>
    /// <param name="value">The resolved plaintext secret value.</param>
    public ResolvedSecret(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value.ToCharArray();
    }

    /// <summary>Returns the plaintext secret value. The only way to obtain it — never implicit.</summary>
    /// <returns>The plaintext secret value.</returns>
    /// <exception cref="ObjectDisposedException">The secret has been zeroed via <see cref="Dispose"/>.</exception>
    public string Reveal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new string(_value);
    }

    /// <inheritdoc />
    public override string ToString() => "[redacted]";

    /// <summary>Best-effort zeroes the wrapped plaintext buffer so the value no longer resides in this instance's memory.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Array.Clear(_value);
        _disposed = true;
    }
}
