# P15 — Aspire dev loop + sample services

**Wave:** 3
**Depends on:** P13 (runnable host), P11 (CRD manifests for AppHost.K8s)
**Blocks:** M3
**Estimated size:** 1.5 agent-days

## Scope

The `dev/` tree from doc 08 §Local development: two Aspire AppHosts and the
two sample services (`NoteService`, `TaskService`) that double as the manual
end-to-end check.

Grounding: doc 08 §Local development (both AppHost sketches, sample-service
descriptions), doc 06 (the sequence the samples must demonstrate).

## Files owned

```
dev/AppHost.Local/**       (+ dev/AppHost.Local/config/apps/{Note,Task}.yaml)
dev/AppHost.K8s/**
dev/samples/NoteService/**
dev/samples/TaskService/**
```

(If sln changes are needed: coordinator-mediated.)

## Tasks

1. **Sample services** (thin workers):
   - `NoteService`: connects with an OIDC token (Keycloak dev realm),
     pub/sub on `notes.>`, propagation enabled — after connect it fetches
     discovery + its own assertion, attaches `Callout-Identity` on publishes,
     and logs the verdict. Also runs a subscriber loop that verifies incoming
     assertions (P08 verification helper) and resolves the caller's OIDC
     token (`autoAddSessionAccess` profile).
   - `TaskService`: second independent app on `tasks.>`, different `match`,
     service profile with `autoAddSessionAccess` — proves isolation
     (ADR 0001) and contributes an `anonymousProfile` (ADR 0004).
2. **`AppHost.Local`** (files source): NATS w/ JetStream external on 4222 +
   Keycloak (`Aspire.Hosting.Keycloak`) with a committed dev realm import
   (clients/users/roles for both samples) + callout project (files source
   pointed at `config/apps/`) + both samples. Editing `Note.yaml` hot-reloads
   live (doc 08's inner loop). Callout NATS server config for auth callout
   (dev NKeys, committed **dev-only** seeds clearly marked) generated or
   documented.
3. **`AppHost.K8s`** (CRD source): CommunityToolkit `AddK3sCluster`,
   apply CRD + RBAC + sample CRs (from P11 `deploy/crd/`), callout with
   `Source__Kind=k8s` + injected kubeconfig, same NATS + samples — proves
   file/CRD convergence end to end (doc 08).
4. **Dev README** (`dev/README.md`): prerequisites, how to run each loop,
   what "working" looks like (which logs/dashboard traces to check), how to
   mint a token by hand against dev Keycloak (`curl` snippets).

## Definition of done

- [ ] `dotnet run --project dev/AppHost.Local`: all resources healthy, both
      samples authorized, Note→Task identity propagation visible in logs,
      OTel dashboard shows auth spans.
- [ ] `dotnet run --project dev/AppHost.K8s`: same behavior via CRDs.
- [ ] Editing `Note.yaml` (Local) / patching the CR (K8s) changes routing
      live — documented check.
- [ ] No real secrets: all seeds/passwords are generated-or-dev-marked.
