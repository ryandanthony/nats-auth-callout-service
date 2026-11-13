# AuthCallout.Contracts

Shared contracts for [nats-auth-callout-service](https://github.com/ryandanthony/nats-auth-callout-service), a NATS auth callout that validates OIDC tokens and mints scoped NATS user JWTs.

Reference this package from an application that connects through the callout or subscribes to messages from callout-authenticated clients. It is the single source of truth for the naming rules both sides must agree on:

- `AppSlug`: application name to slug (ADR 0013), and from it every KV bucket name (`DiscoveryBucket`, `AssertionsBucket`, `OidcTokensBucket`, `AssertionKeysBucket`), the discovery key, KV key derivation (`SanitizeKeySegment`, `DeriveKeySegment`), and the scoped client inbox prefix (`ClientInboxPrefix`) the callout's grant requires.
- `AssertionClaims`: the verified claims of a `Callout-Identity` assertion (`id`, `sub`, `app`, `iss`, `exp`).
- The config model (`Application`, `Profile`, `Grant`, ...) and decision types the service itself uses.

```csharp
var slug = AppSlug.From("notes");
var opts = new NatsOpts
{
    AuthOpts = new NatsAuthOpts { Token = oidcToken },
    InboxPrefix = slug.ClientInboxPrefix(subjectFromToken),
};
```

Docs: https://ryandanthony.github.io/nats-auth-callout-service/ (see Identity propagation).
