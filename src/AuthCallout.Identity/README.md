# AuthCallout.Identity

Identity primitives from [nats-auth-callout-service](https://github.com/ryandanthony/nats-auth-callout-service), a NATS auth callout that validates OIDC tokens and mints scoped NATS user JWTs.

A subscriber uses this package to verify the signed `Callout-Identity` header a client attaches to its messages (ADR 0010/0011):

1. Read the assertion from the message header named in the app's discovery descriptor.
2. Read its `kid` header and fetch the public key from the `callout_assertion_keys` KV bucket (cache it per `kid`).
3. Verify with `AssertionSigner.Verify(compactJwt, publicKey)`. It returns `AssertionClaims`, or `null` for a bad signature or malformed token.
4. Reject when `claims.Expiry` is in the past.

```csharp
using var signer = new AssertionSigner([new ResolvedAssertionKey("verify-only", throwawaySeed, Active: true)]);
AssertionClaims? claims = signer.Verify(headerValue, publicKey);
```

`AssertionSigner` needs one active key in its keyset even when you only verify; a throwaway, never-used NKey satisfies it. The repo's `dev/samples/Shared` shows the full client and subscriber flow (discovery, self-assertion fetch, verification, OIDC token resolution).

Also contains the service's OIDC `TokenValidator` and JWKS cache.

Docs: https://ryandanthony.github.io/nats-auth-callout-service/ (see Identity propagation).
