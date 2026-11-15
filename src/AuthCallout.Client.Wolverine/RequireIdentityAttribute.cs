// -----------------------------------------------------------------------
// <copyright file="RequireIdentityAttribute.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Exempts one message type from <see cref="CalloutIdentityMiddleware"/> (ADR 0022 open question
/// 2 / packet P20 task 3's <c>RequireIdentity</c> knob): apply to a message type a listener
/// accepts anonymously (e.g. a registration/discovery endpoint) to opt it out of the
/// policy-with-opt-out default every other NATS-listener handler chain gets.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequireIdentityAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="RequireIdentityAttribute"/> class.</summary>
    /// <param name="required">Whether this message type still requires a verified identity; defaults to false (the attribute's purpose is opting out).</param>
    public RequireIdentityAttribute(bool required = false)
    {
        Required = required;
    }

    /// <summary>Gets a value indicating whether this message type requires a verified <c>Callout-Identity</c> header.</summary>
    /// <value><see langword="false"/> to exempt this message type from <see cref="CalloutIdentityMiddleware"/>.</value>
    public bool Required { get; }
}
