# FABLE review — nats-auth-callout-service

Reviewed 2026-09-11 at `65c5bfc` (main). Scope: design docs + ADRs, then the full `src/` tree, with emphasis on (a) security, (b) manufactured code that isn't needed, (c) code that is more complex than it needs to be.

Findings S1, S2, S3, S4 and the `Verify`-ignores-`exp` behavior were **empirically confirmed by executing the repo's own built assemblies** (a throwaway harness referencing `bin/Debug/net10.0` DLLs); observed outputs are quoted inline. The repo's own test suites were also run and are green — all six unit suites (Contracts 183, Core 181, Identity 64, Nats 132, Config 75, Secrets 23) plus the Testcontainers integration suite (7 scenarios against real NATS + Keycloak) — i.e. the demonstrated behaviors coexist with a fully passing test suite, so each is a genuine coverage gap, not a regression or a misreading.

**Overall:** this is a well-designed, unusually well-documented codebase. The design docs and ADRs are genuinely load-bearing (the code cites them and mostly matches them), validation is strict and fail-closed, and the dangerous JWT defaults (clock skew, HS* confusion, embedded `jwk`/`x5c`/`jku`) are all explicitly handled. The findings below are real, but none of them are "rewrite this" findings.

---

## 1. Security findings

### S1 (HIGH) — Stage-2 validation parameters come from `candidates[0]`, with no cross-app consistency check — **FIXED** (`divergent-issuer-config` aggregate rule: apps sharing an authority must have structurally identical issuer blocks; red-to-green with 6 new validator tests)

`ResolutionPipeline.ResolveTokenAsync`:

```csharp
var issuerConfig = candidates[0].Issuer;
var validation = await _tokenValidator.ValidateAsync(rawToken, issuerConfig, ...);
```

The comment asserts "any candidate's IssuerConfig carries the same validation parameters", but nothing enforces that. `RoutingConfig.IssuerIndex` groups applications only by `Issuer.Authority`; each application carries its own full `IssuerConfig` (`clockSkew`, `allowedAlgorithms`, `validateAudience`, `audience`, `jwksUri`). `RoutingConfigValidator` checks each issuer block individually but has **no rule that apps sharing an authority must have identical validation parameters**.

Consequence: if app A (audience `api-a`, `validateAudience: true`) and app B (`validateAudience: false`) share an authority, whichever happens to be `candidates[0]` decides the validation strictness for **both** — and the ordering is whatever the config source produced. A token that should have failed A's audience check can validate under B's laxer parameters and then route to A at stage 3. `JwksCacheManager.SyncIssuers` has the same first-candidate pick for `jwksUri`/`refreshInterval`.

**Confirmed by execution** against the built `AuthCallout.Contracts.dll`: an aggregate with `app-a` (strict: `validateAudience: true`, full algorithm list) and `app-b` (lax: `validateAudience: false`, `clockSkew: 300s`) on the same authority returns `ConfigValidator.ValidateRouting → IsValid=True, errors=0`, and `IssuerIndex[authority][0]` flips between the strict and lax `IssuerConfig` purely on application ordering:

```text
S1 divergent-issuer aggregate: IsValid=True, errors=0
candidates[0] = app-a, ValidateAudience=True
reversed candidates[0] = app-b, ValidateAudience=False
```

**Fix:** add an aggregate validation rule (`divergent-issuer-config`) that rejects a config where two applications share an `authority` but differ in any validation-relevant `IssuerConfig` field. `IssuerConfig` already implements structural equality, so this is a ~10-line validator addition. That converts the code comment's assumption into an enforced invariant.

### S2 (HIGH, design-level) — `_INBOX.>` subscribe on every authenticated grant allows cross-client inbox eavesdropping — **FIXED in `d49162b`** (scoped `_INBOX.{slug}.{derived-principal}.>` grant + client `InboxPrefix`; all suites incl. integration green)

`GrantBuilder.BuildSubscribe` adds `AppSlug.ClientInboxSubscribeSubject` (`_INBOX.>`) unconditionally to every authenticated grant (keys + discovery reads are unconditional, so the inbox grant always fires). ADR 0013 acknowledges this as "the minimal grant that still works", but it is worth stating plainly what it costs: **every authenticated client can subscribe to every other client's request-reply inboxes in the same target account.** Any request-reply traffic between services in that account (including other clients' KV direct-get responses, which carry stored OIDC tokens when `autoAddSessionAccess` is in play) is readable by any authenticated principal, including one holding the least-privileged profile. (The repo's own tests pin this: `GrantBuilderTests` asserts `_INBOX.>` is present in every authenticated grant's subscribe list.)

**Confirmed by live experiment** against a real `nats-server` 2.14: two users in one account, the attacker's permissions an exact mirror of a minted least-privilege grant (static subjects + per-stream `$JS.API` publishes + `$KV` reads + `_INBOX.>` subscribe). While a victim user ran an ordinary request-reply, the attacker's `_INBOX.>` subscription captured the victim's reply payload verbatim:

```text
# attacker (sub '_INBOX.>'):
[#1] Received on "_INBOX.hzpsb0O0C4vxesFPfa6FTC.plK5b4rP"
SECRET-ORDER-DATA-42 for customer-777
```

Note: `_INBOX` is **not** special-cased by the server — the NATS docs state the inbox prefix is only a client-side naming convention, and permissions are pure subject-pattern matching with no ownership concept. The per-request NUID suffix is the sole protection, and a `>` wildcard defeats it. NATS's `allow_responses` mechanism covers only the *publish* (responder) side; there is no subscribe-side equivalent.

This partially undermines the care taken elsewhere to scope `$JS.API` publish subjects per backing stream — the reply leg is world-readable.

**Fix options:** NATS supports custom inbox prefixes (`NatsOpts.InboxPrefix` in NATS.NET, `custom_inbox_prefix` in other clients). Grant `_INBOX_{connection-id}.>` (or `_INBOX.{safe-name}.>`) and document that propagation-consuming clients must set the matching inbox prefix. **The mitigation was itself validated live** on the same server: with each user granted only its own scoped prefix (e.g. `_INBOX_v.>`) and the client using `--inbox-prefix _INBOX_v`, request-reply worked normally (`Received with rtt 593µs`), while the attacker's subscriptions to both `_INBOX.>` and the victim's `_INBOX_v.>` were rejected with `Permissions Violation for Subscription`. If that's deemed too much client burden for v1, at minimum promote this from an ADR footnote to a loud, threat-model-level warning in the operations docs: "one account per application, and every authenticated user in an account can read every inbox in it."

### S3 (MEDIUM) — Self-lookup key sanitization can collide across principals, enabling assertion takeover/impersonation — **FIXED in `d49162b`** (`AppSlug.DeriveKeySegment`: sanitized + 16-hex SHA-256 suffix, applied to grant subject, store writer, and client sample)

`AppSlug.SanitizeKeySegment` lowercases and collapses every run of characters outside `[a-z0-9-]` to a single `_`. The self-lookup key is `jti` with a `sub` fallback (`GrantBuilder.ResolveSelfLookupKey`), and `KvIdentityStoreWriter.CreateOrReplaceAsync` deliberately **purges and replaces** on key collision.

Distinct principals can sanitize to the same key: `User.A` vs `user_a` vs `USER A` all become `user_a`. When that happens:

- Client 1's granted subject `$KV...assertions.user_a` now reads **client 2's** signed assertion (the replace path), and vice-versa.
- The assertion is a bearer artifact — whoever holds the compact JWT can attach it as the `Callout-Identity` header. A colliding principal can therefore present a callout-signed assertion for a *different* `sub` and pass subscriber-side verification (doc 06 steps 1–4).

With UUID `jti`s this is unlikely by accident, but the `sub` fallback is attacker-influenced at some IdPs (self-service usernames), and the transform is many-to-one by construction. ADR 0013 already flags "`{jti}` sanitization is mandatory: … colliding key" as a known hazard but the current transform invites exactly that.

**Confirmed by execution** of the real `AppSlug.SanitizeKeySegment`:

```text
'User.A' -> 'user_a'
'user_a' -> 'user_a'
'USER A' -> 'user_a'
```

**Fix:** make the key collision-resistant instead of merely subject-safe — e.g. `sanitized + "_" + first-16-hex(SHA256(raw))` (the `TransformPipeline.Hash` helper already exists), applied identically in the grant subject and the write path (both already flow through `SanitizeKeySegment`, so it's a one-function change).

### S4 (MEDIUM) — Assertion payload serializes `payloadSchemaVersion`, not the documented `schemaVersion` — **FIXED** (`[JsonPropertyName("schemaVersion")]`; wire-shape test now pins the exact documented field order/names)

`AssertionSigner`:

```csharp
private sealed record AssertionPayload(int PayloadSchemaVersion, string Id, ...);
```

With the camelCase naming policy this goes on the wire as `"payloadSchemaVersion": 1`. **Confirmed by executing** the real `AssertionSigner.Sign` and decoding the produced compact JWT:

```text
payload: {"payloadSchemaVersion":1,"id":"conn-id-123","sub":"user-sub","app":"my-app","iss":"https://idp.example","exp":1789162032}
```

ADR 0011 / doc 09 / the AssertionSigner's own XML doc say the claim is `schemaVersion` ("reuse the ADR 0008 schemaVersion discipline"). External consumers implementing verification from the docs will look for a field that isn't there. This is an interop contract drift, cheap to fix now and breaking to fix after consumers exist. Rename via `[JsonPropertyName("schemaVersion")]` (the C# property name was presumably chosen to dodge a naming collision or analyzer rule — attribute it instead).

Related nit: `Verify` checks `alg` and signature but not `typ`, and does not validate `exp` — **confirmed by execution**: `signer.Verify` on an assertion with `exp` ten minutes in the past returns a non-null `AssertionClaims` (`Verify(expired): True`). Expiry is delegated to consumers per doc 06, which is consistent, but since this class is the de-facto reference verifier (see M2 below) an `exp` check here would prevent consumer copy-paste bugs.

### S5 (MEDIUM, design-accepted but under-signposted) — Raw bearer tokens in a bucket-wide readable KV

`autoAddSessionAccess` grants `$KV.callout_apps_{slug}_oidc_tokens.>` — the **whole bucket**. Any service holding such a profile can read every connected user's raw OIDC access token and replay it against the IdP or any other API accepting that audience. ADR 0010 accepts this (short TTLs, per-app bucket isolation), and the per-app bucket boundary is good — but "this profile can harvest every live user credential for the app" deserves an explicit warning in the authorization-model docs where `autoAddSessionAccess` is described, not just in the KV design doc.

### S6 (LOW) — `ValidIssuer` pinned to `authority`, not the discovered issuer

ADR 0016: "`ValidIssuer` pinned to the issuer discovered from `authority` (`{authority}/.well-known/openid-configuration` → `issuer`)". The code pins `ValidIssuer = issuer.Authority` directly (`TokenValidator.BuildValidationParameters`), and stage 1 also matches the raw `iss` against `authority` via `IssuerIndex`. For providers where the discovery `issuer` differs from the configured authority string (trailing slash, tenant aliasing), everything fails closed — so this is availability drift, not a vulnerability — but the code and ADR disagree and one of them should be corrected.

### Security positives worth keeping

- ADR 0016 is fully realized: 60s skew with a 300s cap enforced at config load, asymmetric-only algorithm allow-list validated at load *and* preflighted per request, keys only from cached JWKS by `kid`, unknown-`kid` single-flight refresh with one retry, never-throws outer guard.
- Generic denial string to clients; reasons only to events/logs.
- xkey envelope fail-closed in both mismatch directions, encryption signaled by header, never content-sniffed (ADR 0018 done right).
- 128-bit CSPRNG connection ids, Base64Url (subject-safe).
- `UnvalidatedIssuerReader` reads exactly one claim pre-validation, BCL-only.
- Postgres `LISTEN` channel identifier-quoted with the CA2100 reasoning written down; migrations content-addressed and transactional.
- Secrets: mounted-volume convention avoids K8s API/RBAC entirely; inline secrets warn; health JSON writer is deliberately reflection-free.
- Identity-store write failure downgrades a grant to a denial (fail closed) rather than replying without propagation.

---

## 2. Manufactured / unnecessary code

### M1 — `IConnectionIdGenerator` is dead code

`src/AuthCallout.Contracts/Primitives/IConnectionIdGenerator.cs` has **zero implementations and zero consumers**. `ConnectionIdGenerator` is a static class that does not implement it, and `KvIdentityStoreWriter` calls the static directly. The interface was manufactured for a seam nobody uses. Delete it (or actually inject it if a test ever needs deterministic ids — none does today).

### M2 — `AssertionSigner.Verify` ships consumer-side logic the service never uses

The service only signs assertions; verification is the *subscriber's* job (doc 06). `Verify` is exercised only by unit tests and one integration test. It's ~70 lines of production surface (plus `KeyPair.FromPublicKey` handling) that exists as a reference implementation. Either move it to a small consumer sample/package where it's the point, or keep it but say so — right now it reads as manufactured API surface.

### M3 — `CustomResourceSpecMapper` is a ~540-line near-duplicate of `ApplicationFileMapper` (~570 lines)

Both maintain the same seven `HashSet<string>` field allow-lists and the same per-object mapping/unknown-field logic, differing only in that the CRD variant drops `schemaVersion`/`name` and prefixes errors differently. That's ~500 lines of duplicated strict-mapping code where drift between the two is exactly the class of bug the "identical model from every source" ADR (0007) exists to prevent — and the duplication has to be caught by the cross-source canonical tests instead of by construction. A shared `ApplicationShapeMapper` with two thin adapters (top-level frame differs, everything from `issuer` down is identical) would halve this.

### M4 — Repeated bespoke result structs

`AppSelectionResult`, `AnonymousComposeResult`, `GrantBuildResult`, `ApplicationMapResult`… are each ~35 lines of identical two-field success-or-DenyReason ceremony with doc comments on every accessor. One internal `Result<T>`/`Outcome<T>` (or just `(T?, DenyReason?)` tuples, matching the `TryXxx` pattern the repo already uses elsewhere) would remove well over a hundred lines. Not wrong, just manufactured weight.

### M5 — Analyzer-suppression essays

The repo carries dozens of `#pragma warning disable` blocks each with a multi-line justification — including suppressing **S2339 for `public const` wire-protocol literals** (`"$SYS.REQ.USER.AUTH"`, `GenericDenialError`) and S4017 six times for the same `IReadOnlyDictionary<string, IReadOnlyList<string>>` shape. When the same rule needs the same suppression essay in six files, the rule is wrong for this codebase: tune `FestinaLente.CodeStandards` (editorconfig severity for S2339/S4017/S100/CA1021) instead of manufacturing per-site prose. This is the single largest source of comment noise in the repo.

---

## 3. More complex than it needs to be

### C1 — Claims round-trip through JSON text on every request

`TokenValidator.BuildClaimsAccessor` does `JsonSerializer.Serialize(dictionary)` → `JsonDocument.Parse` → `Clone()`; the `ClaimsAccessor` ctor then clones again. That's serialize + parse + clone per authenticated request on the hot path, purely to convert `IDictionary<string, object>` into a `JsonElement`. It's *safe*, and it keeps Core free of Microsoft.IdentityModel types (a stated design goal), but `SerializeToUtf8Bytes` + `JsonDocument.Parse` (skipping the intermediate string) and a non-cloning internal path would keep the same isolation at half the allocation. Worth a note, not a rewrite — auth callout traffic is connection-rate, not message-rate.

### C2 — `YamlToJsonConverter` triple representation hop

YAML → `YamlNode` → `JsonNode` → JSON **string** → `JsonElement`. The `ToJsonString()`/`Deserialize<JsonElement>()` hop exists only because the code wants a `JsonElement` at the end. Config-load-only, so harmless, but the string round-trip is one hop more than needed (`JsonNode` → `WriteTo(Utf8JsonWriter)` → parse, or map `YamlNode` directly to a writer).

### C3 — `HealthReportJsonWriter` buffers through a `MemoryStream`

The whole report is written to a `MemoryStream` and copied to the response body. `Utf8JsonWriter` can target the response `BodyWriter` directly. Trivial, and health endpoints are tiny — but it's an example of a pattern that appears several times: an extra buffering/indirection layer justified by a long comment rather than removed.

### C4 — `JwksCacheManager.SyncIssuers` add-then-remove race edges

`SyncIssuers` iterates `_issuers.Keys` while other invocations (config change events can fire concurrently with `PrewarmAllAsync`) may add/remove. `ConcurrentDictionary` makes this safe-but-racy: a lost race can dispose a `TrackedIssuer` another path just handed out, or leave an added issuer without a started refresh loop until the next prewarm. In practice config changes are rare and the code recovers on the next sync, but a simple `lock` around `SyncIssuers` would make the reasoning trivial where it is currently subtle — the class already accepts locks elsewhere (`TrackedIssuer._startLock`).

### C5 — Doc-comment-to-code ratio

Several files are >60% comments (e.g. `EventSubjectResolver` is ~50 lines of XML doc for a 20-line method; `AppSlug.MaxLength` has a paragraph). The citations to ADRs are genuinely valuable — this is the best-grounded generated code I've reviewed — but paragraphs restating what one-line code already says (`/// <summary>Creates a successful selection.</summary>`) are pure weight. Suggested norm: cite the ADR, skip narrating the obvious.

### Complexity verdicts that are fine as-is

- `TemplateExpander`'s cross-product with odometer indices: appropriate, and the zero-value/empty-template semantics are pinned by ADR 0009 and tested.
- `TokenValidator`'s preflight double-parse: justified (precise `AlgorithmRejected` reason vs IDX10511 collapse) and the justification is written where it matters.
- `ServiceStateMachine`: three gates + terminal stop, cleanly done. (`PrewarmRequired` and the `_freshness` read under `Serving` are fine; `SetFreshness` not raising events is intentional per the design.)
- `CalloutResponder`: the decrypt → decode → resolve → store-before-reply → mint → seal → reply → emit-after-reply ordering matches doc 03 exactly, and the fail-closed store downgrade is correct.
- `AppSelector`'s null-match-means-match reduction is subtle but valid because the `ambiguous-null-match` validator rule guarantees the precondition — and the comment says exactly that.

---

## 4. Docs review

- The ADR log is coherent and the code traces to it. Doc 03's decision tree matches `ResolutionPipeline` stage-for-stage, including all deny paths.
- ADR 0013's "wire-level JS-API subjects" section honestly documents the `_INBOX.>` compromise (see S2 — it should graduate to a threat-model note).
- ADR 0016 vs `TokenValidator`: one drift (S6, `ValidIssuer` source).
- ADR 0011/doc 09 vs `AssertionSigner`: one drift (S4, `schemaVersion` field name).
- ADR 0013 says the mapping is implemented once as "`BucketNaming` in AuthCallout.Contracts" — the actual type is `AppSlug`. Cosmetic doc drift.
- Doc 06's consumer verification steps are implementable from the docs alone *except* for the S4 field-name drift.

---

## 5. Suggested priority

1. ~~**S1** — add the divergent-issuer-config validation rule~~ — **done** (red-to-green).
2. ~~**S4** — fix `schemaVersion` field name~~ — **done** (red-to-green, wire shape pinned by test).
3. ~~**S3** — collision-resistant self-lookup keys~~ — **done** (`d49162b`).
4. ~~**S2** — per-connection inbox prefixes~~ — **done** (`d49162b`, scoped per-principal prefixes).
5. **M1** — delete `IConnectionIdGenerator`.
6. **M3/M4/M5** — dedupe mappers, collapse result structs, tune the analyzer config.
7. **S5/S6 + doc drifts** — docs pass.
