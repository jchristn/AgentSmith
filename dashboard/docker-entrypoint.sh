#!/bin/sh
# Runs before nginx starts (nginx image /docker-entrypoint.d hook).
# HUGO_API_URL: URL the browser uses for the API; empty means same origin (nginx proxies /v1.0, /mcp, /openapi.json).
# HUGO_API_UPSTREAM: where nginx proxies those paths. Default http://hugo-server:8800.
set -e
UPSTREAM="${HUGO_API_UPSTREAM:-http://hugo-server:8800}"
RESOLVER=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
case "${RESOLVER}" in
  *:*) RESOLVER="[${RESOLVER}]" ;;
esac
cat > /etc/nginx/hugo-upstream.inc <<INC
resolver ${RESOLVER:-127.0.0.11} valid=30s ipv6=off;
set \$hugo_api "${UPSTREAM}";
INC
cat > /usr/share/nginx/html/config.js <<CONFIG
window.HUGO_CONFIG = { apiUrl: "${HUGO_API_URL:-}" };
CONFIG
