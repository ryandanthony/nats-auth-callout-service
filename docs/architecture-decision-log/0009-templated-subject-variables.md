# 0009 — Templated subject variables bound to transformed claims

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

Per-scope templating (ADR 0001) supports a single `{scope}` placeholder bound to one `scopeClaim`. This is too narrow:

- A profile may need to build a subject from **more than one** claim (e.g. a tenant and a user).
- A claim value may not be **subject-safe**. NATS treats `.` as a token separator and `*` / `>` / whitespace as wildcards, so a raw value like an email (`alice@example.com`) would fracture into multiple subject tokens or introduce wildcards. Values often need sanitizing, encoding, lowercasing, or hashing before they can be embedded.

We want any claim usable in a subject, safely, with several claims combinable in one template.

## Decision

A profile declares a set of named **template variables**. Each variable binds a claim to a name and applies an ordered **transform pipeline**. Templates reference variables by name with `{name}` and may use several.

### Variable

- `name` — the placeholder name used in templates (e.g. `tenant`, `user`).
- `claim` — the claim path it reads (nested paths supported, as in `ClaimMatch`).
- `transforms` — an ordered list of transform steps applied to each value.
- `required` — when true, the variable must resolve to at least one value or the connection is denied (generalizing `requireScope`).

`scopeClaim` / `{scope}` become a special case: a single variable named `scope`. The old single-claim form remains expressible, so this is a superset.

### Transform pipeline

Each transform is a small, named, pure operation applied left to right, for example:

- `lowercase` / `uppercase`
- `replace` (from -> to, e.g. `.`/`@` -> `_`) for subject-safety
- `sanitize` (map any non-subject-safe character to a safe one)
- `hash` (e.g. a hex digest) when the raw value must not appear in the subject
- `prefix` / `suffix`
- `require-safe` (assert the result is a single valid subject token; otherwise the value is rejected and the connection denied)

The transform set is fixed and defined by the service, not arbitrary code. Unknown transforms are a config-validation error.

### Templates and expansion

- `publishTemplates` / `subscribeTemplates` are subject patterns containing zero or more `{name}` placeholders.
- A template is expanded by substituting each variable's transformed value(s).
- A variable bound to a **multi-valued** claim expands the template once per value. When a template uses multiple multi-valued variables, it expands over the **cross-product** of their values.
- A template referencing a variable that resolves to no value is **not emitted** (and, if that variable is `required`, the connection is denied before any grant is built).
- After transformation, every emitted subject must be a valid subject; an unsafe result is a denial, never a silently malformed grant.

### Example

```jsonc
{
  "variables": [
    { "name": "tenant", "claim": "household_id" },
    { "name": "user", "claim": "email",
      "transforms": [ "lowercase", { "replace": { "from": ".", "to": "_" } },
                      { "replace": { "from": "@", "to": "_" } }, "require-safe" ] }
  ],
  "publishTemplates": [ "mise.house.{tenant}.user.{user}.commands" ],
  "subscribeTemplates": [ "mise.house.{tenant}.user.{user}.events.>" ]
}
```

A token with `household_id = [H1, H2]` and `email = Alice@Example.com` yields publish subjects `mise.house.H1.user.alice_example_com.commands` and `mise.house.H2.user.alice_example_com.commands`.

## Consequences

- Any claim can drive a subject, and multiple claims can be combined in one template.
- Unsafe claim values (emails, display names) become usable via transforms, and the `require-safe` step turns a bad value into a clean denial rather than a malformed subject.
- Replaces the single `scopeClaim`/`{scope}` field on a profile with `variables` + `publishTemplates`/`subscribeTemplates`; `scope` is just a conventional variable name, so existing configs port directly.
- Config validation must check: every template placeholder resolves to a declared variable, every variable's claim path is valid, and every transform is known.
- Cross-product expansion of multiple multi-valued variables can grow the grant; validation/limits on subject count should be considered.
- Hashing enables privacy-preserving subjects (the raw claim never appears in a subject or in emitted events).

## Related

- Design: [02 — Authorization model](../design/02-authorization-model.md#templated-subject-variables), [03 — Resolution pipeline](../design/03-resolution-pipeline.md).
- ADRs: [0001](./0001-named-permission-profiles.md) (supersedes its single `{scope}` templating), [0007](./0007-internal-configuration-model.md) (profile fields), [0003](./0003-resolution-order-issuer-validate-app-profile.md) (variables resolve from validated claims, post-validation).
