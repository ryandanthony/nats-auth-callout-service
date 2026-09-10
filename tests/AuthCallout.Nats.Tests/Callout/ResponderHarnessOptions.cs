// -----------------------------------------------------------------------
// <copyright file="ResponderHarnessOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Nats.Callout;
using NATS.Client.Core;

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>Collaborator overrides for <see cref="ResponderHarness"/>, grouped to stay within the parameter-count analyzer limit.</summary>
internal sealed class ResponderHarnessOptions
{
    public IResolutionPipeline? Pipeline { get; init; }

    public ActiveConfig? ActiveConfig { get; init; }

    public bool UseDefaultActiveConfig { get; init; } = true;

    public IUserJwtMinter? Minter { get; init; }

    public ICalloutCrypto? Crypto { get; init; }

    public IIdentityStoreWriter? IdentityStoreWriter { get; init; }

    public IEventEmitter? EventEmitter { get; init; }

    public CalloutResponderOptions? ResponderOptions { get; init; }

    public INatsConnection? Connection { get; init; }
}
