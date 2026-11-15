# AuthCallout.Client.Wolverine

Wolverine integration for [nats-auth-callout-service](https://github.com/ryandanthony/nats-auth-callout-service) (ADR 0022): a thin adapter binding [`AuthCallout.Client`](https://www.nuget.org/packages/AuthCallout.Client)'s token acquisition, connection, and identity-propagation seams into `WolverineFx.Nats`'s transport and handler pipeline. All behavior lives in `AuthCallout.Client`; this package only wires it into Wolverine.

## What it gives you

1. `opts.UseNatsWithCallout(callout => ...)`: authenticates Wolverine's NATS transport through the callout via an `AuthCallback` that pulls a current token from your `ICalloutTokenProvider` on every (re)connect (ADR 0021 rules 4-5), plus a second, correctly-scoped connection used only for discovery/identity-verification KV reads.
2. Outbound: the connection's cached `Callout-Identity` assertion is attached to every outgoing envelope (no-op when the application's discovery descriptor has identity propagation disabled).
3. Inbound: `CalloutIdentityMiddleware`, applied to every handler chain by default (opt out per message type with `[RequireIdentity(false)]`), verifies the `Callout-Identity` header and injects a `CalloutIdentityContext` parameter, or stops the chain on a missing/forged/expired header.

## Quick start (service, client credentials)

```csharp
builder.UseWolverine(opts =>
{
    opts.UseNatsWithCallout(callout =>
    {
        callout.Url = "nats://localhost:4222";
        callout.ApplicationName = "notes";
        callout.UseClientCredentials(cc =>
        {
            cc.Authority = new Uri("https://idp.example.com/realms/dev");
            cc.ClientId = "notes-worker";
            cc.ClientSecret = SecretRef.Inline("...");
        });
    });

    opts.PublishMessage<CreateNote>().ToNatsSubject("notes.commands");
    opts.ListenToNatsSubject("notes.commands");
});
```

A handler receiving a verified caller just takes the context as a parameter:

```csharp
public static NoteCreated Handle(CreateNote command, CalloutIdentityContext identity)
{
    // identity.Subject, identity.Application are verified, signed by the callout (ADR 0011).
    ...
}
```

Exempt a message type that accepts anonymous traffic (e.g. a registration/discovery request):

```csharp
[RequireIdentity(false)]
public sealed record RegisterDevice(string DeviceId);
```

Docs: https://ryandanthony.github.io/nats-auth-callout-service/ (see the Wolverine guide).
