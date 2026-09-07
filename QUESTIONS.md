# QUESTIONS — before writing `/implementation-plan/*.md`

Questions to resolve before authoring the implementation plan. The plan will be
structured for **parallel execution by multiple Sonnet-class agents via jcode
swarm**, so several questions are specifically about how to partition the work,
what the shared contracts are, and what each work packet's definition-of-done
looks like. The design docs (`docs/design/01–09`) and ADRs 0001–0017 are
internally consistent and thorough; nothing below blocks understanding the
*design* — these are about *executing the build*.

Answer inline (edit this file) or dictate answers and I'll fold them in.

---

## A. Scope and sequencing of the plan itself

1. **MVP cut vs. full design.** Should the plan target the complete design in
   one pass, or define an MVP milestone first? A natural MVP: files config
   source + resolution pipeline + minting + health, deferring the K8s CRD
   source, identity propagation (ADRs 0010–0012), key rotation (0014), and
   Aspire AppHost.K8s to later milestones. Full-parallel swarm execution
   actually favors "everything planned, milestones only for integration
   ordering" — which do you prefer?
   - [ ] MVP milestone first, later milestones sketched
   - [x] Plan everything now, milestone boundaries only gate integration

2. **Database config source.** Docs mark it "future". Confirm it is fully out
   of scope for this plan (no stub project, just the provider interface shaped
   so it can be added)?
   Answer: Include it, postgresql + Dapper ORM

3. **Deployment scope.** Is writing the Helm chart / kustomize under `deploy/`
   and the NAS/homelab deployment part of this plan, or is the deliverable
   "container image + CRD + RBAC manifests" with deployment planned separately?
   Answer: HELM chart is included.

4. **Docs deliverables.** Should the plan include end-user/operator docs
   (README, config reference, "how a subscriber verifies identity" guide), or
   code + schemas + tests only?

   Answer: Include user docs. https://ryandanthony.github.io/ is already set up. https://ryandanthony.github.io/dottie/ is an example

## B. Repo, solution, and CI bootstrap (the serialization point)

5. **Who creates the skeleton?** Parallel agents need a committed solution
   skeleton (sln, empty projects per doc 08 layout, `Directory.Build.props`,
   nullable/analyzers, xUnit test projects, CI workflow) before fan-out.
   Confirm the plan should have a single **Phase 0 bootstrap packet** that one
   agent (or you) lands first, and everything else depends on it.
   Answer: 
   
6. **C# conventions.** Should the plan pull conventions from
   `festina-lente-io/code-standards`? Anything mandatory (analyzers,
   `dotnet format`, EditorConfig, license headers)?
   Answer: use `festina-lente-io/code-standards` nuget package. Use these as starting points:
   
   - https://github.com/ryandanthony/mise/blob/main/.editorconfig
   - https://github.com/ryandanthony/mise/blob/main/global.json
   - https://github.com/ryandanthony/mise/blob/main/Directory.Build.props
   - https://github.com/ryandanthony/mise/blob/main/Directory.Build.targets
   - https://github.com/ryandanthony/mise/blob/main/Directory.Packages.props (modify to not be marten + wolverine + avalonia centric)
   - https://github.com/ryandanthony/mise/blob/main/stylecop.json
   - https://github.com/ryandanthony/mcp-private-library/blob/main/GitVersion.yml
   
7. **CI from day one?** GitHub Actions with build + unit tests required on PR
   from Phase 0? Are Testcontainers-based integration tests feasible in your
   CI runners (Docker-in-Docker availability), or should integration tests be
   a separate non-required workflow initially?

   Answer: CI from day one. github actions should be able to run tests. look at Alba potentially. Tests containers as well. And possibly Aspire?
   
8. **Branch/merge model for the swarm.** jcode agents can share a working tree.
   Preference:
   
   - [x] All agents commit to `main` directly, small commits, coordinator
         resolves conflicts (fastest, matches jcode's native multi-agent model)
   - [ ] Feature branch per work packet + PR merge by coordinator
   - [ ] One integration branch for the whole build, PR to `main` per milestone
   
9. **NuGet pinning.** `NATS.Jwt` / `NATS.NKeys` are 1.0.x/preview. Should
   Phase 0 pin exact versions in a `Directory.Packages.props` (central package
   management) so all parallel packets compile against identical deps?
   Answer: Pin....

## C. Contracts-first questions (what parallel agents code against)

The single biggest parallelization risk is agents inventing incompatible
interfaces. The plan will front-load a **contracts packet**
(`AuthCallout.Contracts` + core interfaces + JSON Schemas) that everything else
codes against.

10. **Contract freeze authority.** Once the contracts packet lands, changes to
    `AuthCallout.Contracts` require coordinator approval (a "contract change"
    is a cross-packet event). OK to encode that rule in the plan?
    Answer: Yes
    
11. **Interface granularity.** Beyond the canonical config model, I intend to
    pin these seams as interfaces in Phase 0 so packets are independently
    buildable — confirm or trim:
    - `IRoutingConfigProvider` (files/k8s/db) + `ActiveConfig` snapshot + change notification
    - `ISecretResolver`
    - `ITokenValidator` (claims out, deny reasons out)
    - `IResolutionPipeline` (request in, `Grant | Deny` decision out)
    - `IUserJwtMinter`
    - `IIdentityStoreWriter` (token/assertion/keys/discovery KV writes)
    - `IEventEmitter`
    - `IAssertionSigner` / keyset abstraction
    - Health-check state surfaces (`IServiceStateProvider` for Serving/Idle)

12. **Deny-reason enum.** Doc 09's event `reason` codes (`invalid_token`,
    `no_issuer_match`, ...) look like the canonical internal `Deny` reason set
    too. Confirm one shared enum in Contracts drives both the pipeline result
    and the event payload (no second mapping layer).
    Answer: 2 and do a mapping
    
13. **JSON Schemas as fixtures.** Should the four JSON Schemas
    (`schemas/events/auth-outcome/v1.json`, `discovery/descriptor/v1.json`,
    `config/application/v1.json`, `config/bootstrap/v1.json`) be authored in
    the contracts packet (hand-written, versioned) so parallel packets can
    contract-test against them immediately? Any preference for schema authoring
    style (draft 2020-12?)?
    Answer: Yes

## D. Design-level clarifications (small gaps the plan must not guess)

14. **Callout xkey encryption.** Doc 08 mentions xkey ("if enabled") only in
    the HA section. Is encrypted callout (`auth_callout.xkey`) in scope v1 —
    i.e., should the bootstrap schema get an optional `xkeySeed: SecretRef`
    and the responder decrypt/encrypt — or explicitly deferred?
    Answer: Implment it, but also create an ADR to support the decision. 

15. **Anonymous connections: enabled how?** The design describes the anonymous
    union grant, but not whether anonymous handling is unconditional. If **no**
    application contributes an `anonymousProfile`, does an anonymous CONNECT
    get denied, or granted an empty-permission JWT in the anonymous account? Is
    there a global "anonymous disabled" switch, or is "no contributions =
    deny" the rule?
    Answer: No contributions = deny

16. **Bucket provisioning.** Who creates the KV buckets and when? Options:
    callout creates/updates buckets (`CreateStoreAsync`) for each enabled app
    at config-apply time (implies bucket-admin perms for the service user), vs.
    pre-provisioned externally. Doc 08's snippet implies the callout creates
    them — confirm, and confirm bucket deletion policy when an app is removed
    from config (leave orphaned? delete?).
    Answer: Call out service creates them.

17. **Bucket TTL mechanics.** Per-message TTL on 2.11+: confirm the token
    record TTL = `exp - now` of the OIDC token exactly, and what the bucket
    `MaxAge` backstop should be (e.g., 24h?). What happens for a token whose
    remaining lifetime exceeds the backstop — clamp or write anyway?
    Answer: no back stop. long running services may have indefinate connections...

18. **Minted JWT extras.** Should minted user JWTs carry anything beyond
    account/name/exp/pub/sub allow-lists — e.g., `allowed_connection_types`,
    response permissions (`allow_responses`), src network limits, or bearer
    flag? Or strictly the minimum per the design?
    Answer: No

19. **`nameClaim` fallback.** If the profile's `nameClaim` is absent from the
    token, is that a deny, a fallback to `sub`, or an empty name?
    Answer: Fall back to `sub`

20. **Assertion JWT shape.** ADR 0011 presumably pins claims (`id`, `sub`,
    `app`, `iss`, `exp`, `kid` header). Should the plan treat the assertion
    format as a fifth versioned schema artifact (it's an interop surface for
    subscribers in other languages), with its own doc under `schemas/`? 

    Answer: No

21. **Health port / endpoint names.** Doc 08 says "e.g. 8080" and
    `/healthz|/readyz|/startupz`. Pin those now (they leak into Helm, probes,
    Aspire)? Any metrics endpoint (Prometheus scrape) needed, or OTLP-only?
    Answer: Pin now

22. **OTel resource naming.** Service name (`nats-auth-callout-service`?),
    metric naming conventions, and whether the OTLP endpoint is assumed
    (collector sidecar / Aspire dashboard only)?
    Answer: service name and otlp endpoint should be configurable at deployment time.

## E. Test strategy and the swarm's feedback loops

Each parallel packet needs a self-contained "done" check. Proposed loop
hierarchy: (a) per-packet unit tests, (b) contract/golden tests against the
JSON Schemas and canonical-model mapping, (c) integration suite (Testcontainers
`nats:2.11 -js`, Keycloak) owned by a dedicated packet, (d) Aspire manual loop.

23. **Real Keycloak in integration tests?** Testcontainers-Keycloak boot is
    slow (~10–20s). Acceptable, or should integration tests use a lightweight
    stub OIDC issuer (static discovery + JWKS + locally-signed tokens) with
    Keycloak reserved for the Aspire manual loop? My lean: stub issuer for CI
    speed **plus** a small number of Keycloak-tagged tests.
    Answer: Acceptable
    
24. **k3s in CI.** The CRD provider's integration tests need a cluster.
    Testcontainers-k3s in CI, or envtest-style fake (kind locally only), or
    CRD provider tested against a mocked watch stream + k3s only in the Aspire
    loop?
    Answer: k3s in CI is acceptable
    
25. **Coverage bar.** Any hard requirement (e.g., decision core ≥ 90%), or
    "table-driven tests for every ADR-specified behavior" as the qualitative
    bar (my preference — the plan would enumerate ADR→test-case mappings per
    packet)?
    Answer: 90%

## F. Swarm execution parameters

26. **Packet sizing.** My draft decomposition is ~12–16 work packets (one per
    plan doc), e.g.: contracts+schemas, secrets, config-files provider,
    config-k8s provider, claim matcher+template engine, resolution pipeline,
    token validator+JWKS manager, minter+responder, identity stores+assertion
    signer, event emitter, health+lifecycle host wiring, integration suite,
    Aspire dev loop, container/CI/supply-chain, deploy manifests, samples.
    Comfortable with packets of roughly 0.5–2 agent-days each, or do you want
    coarser (fewer, bigger) packets with less coordination overhead?
    Answer: that is fine.
    
27. **Concurrency limit.** How many parallel Sonnet workers do you want the
    plan tuned for (affects how finely packets are split and how many
    integration sync points exist)? e.g., 3–4 vs. 6–8.
    Answer: max 6
    
28. **Human review gates.** Where do you want mandatory *your*-review
    checkpoints — end of Phase 0 (contracts), first end-to-end auth
    (integration green), pre-release? Or review-by-exception with the
    coordinator merging autonomously?
    Answer: Phase 0, then review by exception
    
29. **Plan file layout.** Proposal for `/implementation-plan/`:
    `00-overview.md` (dependency graph, swarm protocol, merge rules),
    `01-phase0-bootstrap.md`, then one file per packet with: scope, inputs
    (contracts), outputs, files owned (to avoid merge collisions), test/DoD,
    and explicit "may not touch" list. One file per packet OK, or do you want
    per-phase files instead?
    Answer: one file per packet is ok

---

### My defaults if you don't care

Answer: I have answered all questions above. Ignore the following...

Where you have no opinion, I'll assume: plan everything with milestone gates
(A1), DB source out (A2), image+manifests only (A3), operator docs yes but as a
late packet (A4), Phase-0 bootstrap packet (B5), central package management
with pinned versions (B9), direct-to-main small commits with file-ownership per
packet (B8), shared deny-reason enum (C12), schemas authored in Phase 0 (C13),
xkey deferred with a `SecretRef` slot reserved (D14), callout creates buckets
and leaves orphans with a warning metric (D16), stub OIDC issuer + tagged
Keycloak tests (E23), mocked watch + k3s-in-Aspire only (E24), ~14 packets
tuned for 4–6 workers (F26/27), review gates at contracts-freeze and
first-e2e-green (F28), one file per packet (F29).
