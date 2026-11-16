#!/usr/bin/env bash
# NACS callout probe for one NATS instance (apps-test | apps). Proves the callout (whichever
# instance answers) authorizes from the expected Mise app config. Needs no user token, so it
# runs identically against the NAS and the cluster NACS and is safe to repeat at any time.
#
#   1. Anonymous connect is granted (mise contributed an anonymousProfile).
#   2. Anonymous publish inside the anonymous grant is allowed (mise.discovery.queries).
#   3. Anonymous publish outside it is denied (mise.house.X.commands).
#   4. Anonymous subscribe to the shared mise._INBOX.> is allowed.
#   5. Anonymous subscribe to the broad _INBOX.> is denied (ADR 0004 hardening).
#   6. A malformed bearer token is denied at connect (fail closed, never downgraded).
#
# Usage: deploy/flux/probe.sh apps-test
set -uo pipefail
inst="${1:?usage: probe.sh apps-test|apps}"
url="tls://${inst}.ants.zone:4222"
pass=0 fail=0

check() { # name, expect(ok|deny), command...
  local name="$1" expect="$2"; shift 2
  local out rc
  out="$("$@" 2>&1)"; rc=$?
  local denied=0
  if [ $rc -ne 0 ] || grep -qiE 'violation|denied|not authorized' <<<"$out"; then denied=1; fi
  if { [ "$expect" = ok ] && [ $denied -eq 0 ]; } || { [ "$expect" = deny ] && [ $denied -eq 1 ]; }; then
    printf 'PASS  %-55s\n' "$name"; pass=$((pass + 1))
  else
    printf 'FAIL  %-55s (rc=%s) %s\n' "$name" "$rc" "$(tr '\n' ' ' <<<"$out" | cut -c1-160)"; fail=$((fail + 1))
  fi
}

n() { nats --server "$url" --tlsfirst --timeout 5s "$@"; }

# Subscribe tests: a permitted subscribe stays open until --count; a denied one gets a
# permissions violation back from the server within the wait window.
sub() { timeout 4 nats --server "$url" --tlsfirst sub "$1" --count 1 2>&1; local rc=$?; [ $rc -eq 124 ] && return 0; return $rc; }

check "anonymous connect granted"                         ok   n server check connection --connect-warn 5s --connect-critical 10s
check "anon pub mise.discovery.queries allowed"            ok   n pub mise.discovery.queries probe
check "anon pub mise.house.ProbeX.commands denied"         deny n pub mise.house.ProbeX.commands probe --wait-for-violation 2s
check "anon sub mise._INBOX.> allowed"                     ok   sub 'mise._INBOX.>'
check "anon sub _INBOX.> denied"                           deny sub '_INBOX.>'
check "malformed bearer denied at connect"                 deny nats --server "$url" --tlsfirst --timeout 5s --token not-a-jwt pub x y

echo "${inst}: ${pass} passed, ${fail} failed"
[ $fail -eq 0 ]
