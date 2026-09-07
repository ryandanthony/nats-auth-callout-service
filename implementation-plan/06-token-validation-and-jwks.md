# P06 — Token validation + JWKS cache manager

**Wave:** 1
**Depends on:** P02 (M0 gate)
**Blocks:** P05 (interface consumers can substitute), P13
**Estimated size:** 1.5–2 agent-days

## Scope

`AuthCallout.Identity`: `ITokenValidator` (strict, fail-closed OIDC/JWT
validation) and `IJwksCacheManager` (prewarm, proactive refresh, single-flight
on-demand fetch). This packet keeps the IdP off the hot path.

Grounding: `docs/design/08-technical-architecture-csharp.md` §Token
validation + §JWKS cache manager, ADR 0016 (validation parameters), doc 04
issuer model (discovery, `jwksUri` override, per-issuer `refreshInterval`).

## Files owned

```
src/AuthCallout.Identity/Validation/**   (TokenValidator)
src/AuthCallout.Identity/Jwks/**         (JwksCacheManager, per-issuer state)
tests/AuthCallout.Identity.Tests/**
```

Do **not** touch `src/AuthCallout.Identity/Assertions/**` (P08).

## Tasks

1. **`TokenValidator`** on `Microsoft.IdentityModel`, building
   `TokenValidationParameters` per issuer exactly per doc 08's snippet /
   ADR 0016:
   - `ValidateIssuer/ValidateLifetime/RequireSignedTokens/RequireExpirationTime`
     = true, non-configurable; `ValidIssuer` pinned to the discovery doc's
     `issuer`.
   - `ClockSkew` from issuer config (default 60s; the ≤300s cap is enforced
     at config load by P02's validator).
   - `ValidAlgorithms` = issuer allow-list (asymmetric-only guaranteed by
     P02 validation).
   - `ValidateAudience` + `ValidAudiences` per issuer config.
   - Keys **only** from the cache manager by `kid`; embedded `jwk`/`x5c`/`jku`
     ignored (`TryAllIssuerSigningKeys=false`, resolver delegates to cache).
   - Unknown `kid` → trigger single-flight refresh, retry once, else deny.
   - Output: validated `IClaimsAccessor`-compatible claim set, or a mapped
     `DenyReason` (`SignatureInvalid`, `Expired`, `AudienceMismatch`,
     `AlgorithmRejected`, ...). Never throws for invalid tokens.
2. **`JwksCacheManager`** (hosted-service-shaped, but registered by P13):
   - One `ConfigurationManager<OpenIdConnectConfiguration>` per issuer;
     `jwksUri` override respected (bypass discovery when set).
   - **Prewarm**: `PrewarmAllAsync` fetches discovery+JWKS for every
     configured issuer; completion is what P13 gates `Serving` on
     (`prewarmRequired`). Per-fetch `fetchTimeout`; Polly retry/backoff.
   - **Proactive refresh** loop per issuer on `jwks.refreshInterval` (or
     per-issuer override) via `RequestRefresh()`.
   - **Config tracking**: subscribes to `IActiveConfigSource`; add issuer →
     prewarm it; last app for an issuer removed → drop state. Issuers
     de-duplicated across applications.
   - **Single-flight**: concurrent unknown-`kid` fetches per issuer collapse
     to one IdP call.
   - Health surface: per-issuer freshness (Healthy/Degraded stale-but-usable/
     Unhealthy no-usable-keys) exposed for P13's `jwks` health check; metrics
     (fetch counts proactive vs on-demand, failures, cache age, single-flight
     collapses).
3. **Test IdP**: an in-proc stub OIDC issuer (Kestrel or `WireMock.Net`-free
   hand-rolled `HttpListener`/TestServer) serving discovery + JWKS with
   rotatable keys, used to test rotation, unknown-kid, timeout, and
   discovery-vs-override paths. Also produces signed test tokens — offer it
   to P14 as a shared fixture (coordinate location: `tests/` shared project,
   coordinator-approved).

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| HS256 token rejected even if allow-listed upstream is impossible; alg-confusion (ADR 0016) | craft HS token, assert `AlgorithmRejected` |
| Expired / not-yet-valid within skew (ADR 0016) | boundary tests at ±skew |
| Audience on/off, multi-audience (ADR 0016) | table |
| Embedded jwk/x5c/jku header keys ignored (doc 08) | crafted token, deny |
| Unknown kid → single-flight refresh → success and failure paths | stub IdP counts fetches |
| Prewarm blocks until all issuers fetched; failure keeps NotReady (doc 08) | test |
| Proactive refresh picks up rotated key before first client presents it | rotate stub, assert no on-demand fetch |
| jwksUri override bypasses discovery (doc 04) | stub |
| Issuer add/remove tracking from config changes | test |

## Definition of done

- [ ] All tests green, no real network in unit tests.
- [ ] Validator never throws on bad input (property-style fuzz over malformed
      tokens returns `Denied`).
- [ ] Health/metrics surfaces consumable by P13 (interface in Contracts).
- [ ] Stub IdP fixture advertised to P14.
