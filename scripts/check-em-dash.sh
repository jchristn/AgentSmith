#!/usr/bin/env bash
# Fails when any tracked text file contains an em-dash (U+2014). See WRITING_DOCUMENTS.md.
set -eu
cd "$(dirname "$0")/.."
EM=$(printf '\xe2\x80\x94')
if git ls-files -z | xargs -0 grep -I -l "$EM" 2>/dev/null; then
  echo "Em-dash found in the files above. Rewrite the sentences without em-dashes." >&2
  exit 1
fi
echo "No em-dashes found."
