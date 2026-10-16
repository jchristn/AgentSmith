#!/usr/bin/env bash
# Generates the SSH client keys used by the Hugo SSH testbed into docker/testbed/generated/ssh
# (gitignored). Existing keys are kept, so the script is safe to run repeatedly.
#
#   id_ed25519             ed25519, no passphrase
#   id_ed25519_passphrase  ed25519, passphrase "hugo-testbed-passphrase"
#   id_rsa                 RSA 3072, no passphrase
#
# The public keys are copied into generated/ssh/authorized, which compose.yaml mounts into both
# containers as the authorized keys of the key-only user "hugokey" (and of the password user "hugo").
set -euo pipefail

cd "$(dirname "$0")"
OUT="../generated/ssh"
mkdir -p "$OUT/authorized"

if ! command -v ssh-keygen >/dev/null 2>&1; then
  echo "ssh-keygen was not found. Install an OpenSSH client and run this script again." >&2
  exit 1
fi

generate() {
  local name="$1" type="$2" bits="$3" passphrase="$4"
  if [ ! -f "$OUT/$name" ]; then
    if [ -n "$bits" ]; then
      ssh-keygen -q -t "$type" -b "$bits" -N "$passphrase" -C "hugo-testbed-$name" -f "$OUT/$name"
    else
      ssh-keygen -q -t "$type" -N "$passphrase" -C "hugo-testbed-$name" -f "$OUT/$name"
    fi
    echo "Generated $OUT/$name"
  fi
  cp "$OUT/$name.pub" "$OUT/authorized/$name.pub"
}

generate id_ed25519 ed25519 "" ""
generate id_ed25519_passphrase ed25519 "" "hugo-testbed-passphrase"
generate id_rsa rsa 3072 ""

# Container initialization may run as root; make generated files readable by the checkout owner on Linux.
# The mounted generator script retains the checkout owner's UID/GID, even if Compose created the output as root.
if [ "$(id -u)" = "0" ]; then
  owner="$(stat -c '%u:%g' "$0" 2>/dev/null || stat -f '%u:%g' "$0")"
  chown -R "$owner" "$OUT"
fi
chmod 600 "$OUT"/id_ed25519 "$OUT"/id_ed25519_passphrase "$OUT"/id_rsa
echo "SSH testbed keys are in $(cd "$OUT" && pwd)"
