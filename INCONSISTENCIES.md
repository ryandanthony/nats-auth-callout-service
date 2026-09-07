# Documentation review — inconsistencies

Review of `docs/design/*` and `docs/architecture-decision-log/*` on 2026-09-06.
Ordered by severity. File references use design doc numbers (e.g. "doc 03") and ADR numbers.

**Status: all 17 resolved on 2026-09-07.** Decisions:

1. Discovery read granted unconditionally per-app (doc 03/07, ADR 0012 fixed).
2. Per-profile fallback expiry removed; the global anonymous expiry is the sole
   tokenless expiry source (ADR 0016 makes `exp` mandatory).
3. Keys-bucket read is unconditional on every authenticated grant.
4. ADR 0007 model and doc 02 diagram updated to match ADRs 0014/0016.
5. Discovery scope is per-application (docs 02/05/06 corrected).
6. Doc 05 updated to the keyset model, cites ADR 0014.
7. Doc 07 table now shows logical and physical names; grants use physical subjects.
8. `current` listed as a reserved key in ADR 0013; forbidden as a `kid`.
9. Added `grant_construction_failed`; anonymous grants emit `reason: anonymous`
   (`no_token` removed).
10. `jti` fallback hardcoded to `sub`, "configurable" dropped.
11. `docs/README.md` rewritten; dead links removed (also from the ADR index).
12. `docs/design/README.md` refreshed (doc 07/08 rows, ADRs 0012–0017 listed).
13. Per-issuer override is `issuer.refreshInterval`; all durations unified as
    duration strings (`clockSkew`, `jwksCache`, `warnStaleness`, `maxStaleness`,
    `anonymous.expiry`, ...).
14. `reconnectBackoff` added to the doc 09 bootstrap example.
15. Bootstrap changes are restart-only; ADR 0014 reworded.
16. ADR 0012 descriptor example updated to physical bucket names.
17. ADR 0002 reworded: anonymous only when no token is presented.

Original findings follow for the record.


## High — functional contradictions

### 1. Discovery-read paradox for apps with identity propagation disabled

ADR 0012, doc 05 (§ Identity stores), doc 06 (§ notes), and doc 07 (§ toggle) all say the
discovery descriptor is **still published with `enabled: false`** "so clients and subscribers
can discover this at runtime."

But doc 03 (§ Grant construction) says the discovery-entry read is added **only** "if the
application has identity propagation enabled": "When the application disables identity
propagation, these reads are not added."

If the read grant is only added when enabled, a client of a disabled app can never read the
descriptor that tells it propagation is disabled, defeating the descriptor's stated purpose.
Either the discovery read must be granted unconditionally (per app), or the "discover at
runtime" claim must be dropped for disabled apps. Doc 02 (§ Identity propagation) also says
the descriptor is "readable by any authenticated connection", contradicting doc 03's
conditional grant and doc 07's per-application scoping (see #5).

### 2. `fallbackExpirySeconds` is unreachable under ADR 0016

- ADR 0007 (Profile): `FallbackExpirySeconds` — "used when the token carries no `exp`."
- Doc 03 (§ Grant construction): "Determine expiry: prefer the token's own `exp`, else the
  profile's fallback."
- ADR 0016 / doc 08 (§ Token validation): `RequireExpirationTime = true`, **non-configurable**.

A token with no `exp` now fails validation at stage 2 and never reaches grant construction,
so the fallback branch is dead code for every non-anonymous path. Either ADR 0016 should
note it supersedes the fallback rationale, or `fallbackExpirySeconds` needs a new documented
purpose (e.g. capping long-lived token `exp`) or removal.

### 3. Keys-bucket read: unconditional vs. toggle-dependent (doc 07 contradicts itself)

Doc 07's Assertion-keys bucket row says: read "is auto-added to **every** profile grant"
(unconditional). ADR 0011 (§ Consequences) says the same. But ADR 0012 and doc 07's own
toggle section say a disabled application's grants "do not receive keys-bucket read", and
doc 03 makes the keys read conditional on the toggle. The bucket table in doc 07 — which
declares "this table wins" — states the pre-ADR-0012 unconditional behavior. The table row
should be qualified by the toggle.

## Medium — contract / model drift

### 4. ADR 0007 canonical model was never updated for ADRs 0014 and 0016

ADR 0014 (§ Consequences) explicitly says: "Requires the bootstrap model to carry an
assertion **keyset** (list) instead of a single `assertionKey` (ADR 0007 / 04 updated
accordingly)." Doc 04 was updated (`assertionKeys: AssertionKey[]?`), but ADR 0007 still has
the singular `AssertionKey: SecretRef?`.

Likewise ADR 0016 says its fields "extend the existing per-issuer `issuer` block in the
canonical model (ADR 0007)", but ADR 0007's `IssuerConfig` lacks `clockSkewSeconds`,
`allowedAlgorithms`, and `refreshIntervalSeconds`, and types `Audience: string?` (singular)
while doc 04, doc 09, and ADR 0016 use one-or-more audiences (`audience: [nats]` →
`ValidAudiences`). The doc-02 class diagram (`Issuer`) has the same gaps.

### 5. Discovery bucket reader scope differs across docs

- Doc 02: "readable by **any** authenticated connection."
- Doc 05 / doc 06: "read auto-granted to every authenticated connection, not to anonymous."
- Doc 07 / ADR 0012: readable by "that application's authenticated connections" only
  (`callout.apps.{app-slug}.discovery` added per-app).

Global vs. per-application read scope is a real permission difference. Doc 07 is declared
authoritative, so docs 02/05/06 should say per-application. (Related to #1.)

### 6. Doc 05 still describes a single assertion signing key

Doc 05 (§ Signing and connection identity): "a separate **assertion signing key** (bootstrap
secret ref) signs assertions ... it rotates independently of the issuer seed (ADR 0011)."
Post-ADR-0014 this is a keyset (one active, rest verify-only), and rotation semantics live in
ADR 0014, which doc 05 never cites.

### 7. Competing claims of authority over bucket naming, and doc 07 ignores ADR 0013

Doc 07 opens with "Where any other document disagrees about a key pattern ... **this table
wins**", then uses only the dotted *logical* names (`callout.apps.{app-slug}.oidc-tokens.{id}`).
ADR 0013 declares itself "the *authoritative physical* mapping" with underscore bucket names
(`callout_apps_{app-slug}_oidc_tokens`), and doc 09 says the discovery descriptor "is the
authoritative runtime source of these names for consumers." Doc 07 neither references
ADR 0013 nor notes that its dotted paths are logical-only; its grounds list stops at
ADR 0012. A reader of doc 07 alone would grant/consume the wrong subjects
(`callout.apps...` vs `$KV.callout_apps_...`).

### 8. ADR 0013's mapping table omits the `current` key

ADR 0011, ADR 0014, and doc 07 all use a `callout.assertion-keys.current` /
`callout_assertion_keys.current` pointer, but ADR 0013's "authoritative" mapping table lists
only `{kid}` as the keys-bucket key. `current` also collides conceptually with a `kid` named
`current` — the sanctioned key set should be pinned in the mapping table.

### 9. Denial reasons missing for template-variable failures

Doc 03 / ADR 0009: a `required` variable with no value denies; an unsafe transformed value
denies (`require-safe`). ADR 0008 even lists "the scope requirement was unmet" as a failure
event case. But the reason-code enum in doc 05 and doc 09 (`no_token`, `invalid_token`,
`no_issuer_match`, `no_app_match`, `ambiguous_app`, `no_profile_match`, `granted`) has no
code for these grant-construction denials. Also, `no_token` is listed as a *failure*-style
reason code, yet the no-token path is a successful anonymous grant — which
`outcome`/`reason` pair an anonymous grant emits is unspecified.

### 10. `jti`-fallback is "configurable" but no config field exists

ADR 0010 and doc 07 say that when a token has no `jti`, the callout "falls back to another
client-known value (**configurable**, e.g. `sub`)". No such field exists in the canonical
model (ADR 0007), doc 04, or the doc 09 file/CRD schemas.

## Minor — stale text and broken links

### 11. `docs/README.md` is stale and has broken links

- "the distilled decisions (**ADRs 0001-0008**)" — there are 17 ADRs.
- Links to `current-state.md` and `rewrite-input.md`, neither of which exists in the repo.
- The ADR index (`docs/architecture-decision-log/README.md`) also links
  `../rewrite-input.md` (missing).
- The README describes this folder as notes *about* a source repo at
  `~/code/nats-auth-callout-service`, but this repo *is* that path and contains no code —
  "capture how the service works today" points at a document that isn't here.

### 12. `docs/design/README.md` is stale

- Doc 08 row still says "...project layout, and **open gaps to decide**" — doc 08 now says
  every gap is resolved (§ Decisions previously open).
- "Grounding decisions" lists only ADRs 0001–0011; 0012–0017 are missing despite "Every
  element traces to an ADR."
- The doc 07 row omits the discovery bucket ("token store, id lookup, assertion keys").

### 13. JWKS refresh setting naming/units drift

Doc 08's JWKS config table names the per-issuer override `jwks.refreshIntervalSeconds`, but
per doc 04/doc 09 it lives on the application's `issuer` block (`issuer.refreshIntervalSeconds`),
not under the bootstrap `jwks` section. Units also differ: bootstrap `jwks.refreshInterval`
is a duration string (`5m`) while the per-issuer override is integer seconds.

### 14. Bootstrap `source` example omits `reconnectBackoff`

Doc 04 defines watch-health thresholds as `{ warnStalenessSeconds, maxStalenessSeconds,
reconnectBackoff }` (ADR 0017), but the doc 09 bootstrap example shows only the two
staleness fields.

### 15. "Bootstrap reload" vs "not hot-reloaded"

Doc 04 / ADR 0005: the bootstrap layer is "loaded once at startup ... **Not hot-reloaded**."
ADR 0014 says keyset edits are "applied through the normal config path (**bootstrap reload**
/ restart)". If bootstrap only changes via restart, ADR 0014 should say restart-only;
otherwise the bootstrap layer's reload story needs defining.

### 16. ADR 0012 descriptor example uses retired dotted bucket names

ADR 0012's JSON example shows `"oidcTokenBucket": "callout.apps.notes.oidc-tokens"` etc.
Doc 09 acknowledges this ("updated from the dotted illustrative names in ADR 0012"), but
ADR 0012 itself carries no such note, so it reads as current.

### 17. ADR 0002 wording could imply invalid-token → anonymous

ADR 0002: "every connection ... is authorized by a profile (or the anonymous grant when no
**valid** token is presented)." Per ADR 0003 an invalid token is denied, never anonymous;
only a *missing* token is anonymous. "No valid token" invites the wrong reading.
