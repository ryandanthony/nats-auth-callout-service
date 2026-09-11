#!/usr/bin/env python3
"""Schema <-> reference drift check (P18 DoD item 3, doc 09 config schemas).

Walks every property name defined anywhere in schemas/config/{application,bootstrap}/v1.json
(including nested objects and $defs) and asserts each one appears at least once, verbatim, as
inline code (`propertyName`) in the corresponding docs-site configuration reference page. This
does not validate types or semantics -- it only catches the drift this packet is required to
prevent: a schema field silently missing from the human-facing reference.

Usage: python3 docs-site/scripts/check-config-reference-drift.py
Exit code 0 = no drift, 1 = drift found (missing properties listed on stderr).
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

SCHEMA_TO_REFERENCE = {
    ROOT / "schemas/config/bootstrap/v1.json": ROOT / "docs-site/docs/configuration/bootstrap-reference.md",
    ROOT / "schemas/config/application/v1.json": ROOT / "docs-site/docs/configuration/application-reference.md",
}

# Property names that are intentionally not documented as standalone reference rows because
# they are structural/internal to JSON Schema itself, not user-facing config fields.
IGNORE = {"$schema", "$id", "$defs", "$ref"}


def collect_properties(schema: dict, defs: dict, seen_defs: set[str] | None = None) -> set[str]:
    """Recursively collects every property name declared in `properties` blocks."""
    seen_defs = seen_defs or set()
    names: set[str] = set()

    def walk(node):
        if not isinstance(node, dict):
            return
        if "$ref" in node:
            ref = node["$ref"]
            def_name = ref.rsplit("/", 1)[-1]
            if def_name not in seen_defs and def_name in defs:
                seen_defs.add(def_name)
                walk(defs[def_name])
            return
        props = node.get("properties")
        if isinstance(props, dict):
            for name, sub in props.items():
                if name not in IGNORE:
                    names.add(name)
                walk(sub)
        items = node.get("items")
        if items is not None:
            walk(items)
        for combinator in ("allOf", "oneOf", "anyOf"):
            for sub in node.get(combinator, []) or []:
                walk(sub)
        if "additionalProperties" in node and isinstance(node["additionalProperties"], dict):
            walk(node["additionalProperties"])

    walk(schema)
    return names


def main() -> int:
    failures: list[str] = []
    for schema_path, reference_path in SCHEMA_TO_REFERENCE.items():
        if not schema_path.exists():
            failures.append(f"missing schema file: {schema_path}")
            continue
        if not reference_path.exists():
            failures.append(f"missing reference page: {reference_path}")
            continue

        schema = json.loads(schema_path.read_text())
        defs = schema.get("$defs", {})
        properties = collect_properties(schema, defs)

        reference_text = reference_path.read_text()
        # Match inline-code occurrences of the exact property name, e.g. `natsUrl` or `kind`.
        missing = sorted(
            name for name in properties
            if not re.search(rf"`{re.escape(name)}`", reference_text)
        )
        if missing:
            failures.append(
                f"{reference_path.relative_to(ROOT)} is missing documentation for "
                f"{schema_path.relative_to(ROOT)} properties: {', '.join(missing)}"
            )

    if failures:
        print("Schema <-> reference drift detected:", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1

    print("OK: every schema property is referenced in the configuration reference pages.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
