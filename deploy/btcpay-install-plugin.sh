#!/usr/bin/env bash
# Installs (or updates) the Fiat on-ramp plugin on a BTCPay Server docker deployment.
#
# Usage:   btcpay-install-plugin.sh <url-of-.btcpay>
#          btcpay-install-plugin.sh https://github.com/mokokolalala/btcpayserver-plugin-fiatramp/releases/download/v0.2.1/BTCPayServer.Plugins.FiatRamp.btcpay
#
# When run as an SSH forced command (see deploy/DEPLOY.md), the URL comes from $SSH_ORIGINAL_COMMAND
# and must point at a release of the plugin repository; anything else is refused.
#
# What it does, which is exactly what BTCPay's own "Upload plugin" does:
#   1. downloads <Name>.btcpay and <Name>.btcpay.json from the release,
#   2. copies them into the plugins folder inside the btcpayserver container as <Name>.btcpay / <Name>.json,
#   3. appends "install:<Name>" to the plugins folder's "commands" file,
#   4. restarts the container; on startup BTCPay swaps the plugin in,
#   5. waits for the log line that says the new version is running, and fails loudly otherwise.
#
# Environment overrides (defaults match a standard btcpay-setup.sh / docker-compose deployment):
#   BTCPAY_CONTAINER   name of the btcpayserver container        (default: generated_btcpayserver_1)
#   BTCPAY_PLUGINDIR   plugins folder inside the container       (default: /datadir/plugins)
#   PLUGIN             plugin identifier                         (default: BTCPayServer.Plugins.FiatRamp)
#   RELEASES_PREFIX    only URLs starting with this are accepted (default: the plugin repo's releases)
set -euo pipefail

PLUGIN="${PLUGIN:-BTCPayServer.Plugins.FiatRamp}"
BTCPAY_CONTAINER="${BTCPAY_CONTAINER:-generated_btcpayserver_1}"
BTCPAY_PLUGINDIR="${BTCPAY_PLUGINDIR:-/datadir/plugins}"
RELEASES_PREFIX="${RELEASES_PREFIX:-https://github.com/mokokolalala/btcpayserver-plugin-fiatramp/releases/download/}"

URL="${1:-${SSH_ORIGINAL_COMMAND:-}}"
log() { printf '[install-plugin] %s\n' "$*" >&2; }
die() { log "ERROR: $*"; exit 1; }

[ -n "$URL" ] || die "usage: $0 <url-of-.btcpay>"
case "$URL" in
  "$RELEASES_PREFIX"*"/$PLUGIN.btcpay") ;;
  *) die "refusing URL outside ${RELEASES_PREFIX}<tag>/$PLUGIN.btcpay: $URL" ;;
esac
command -v docker >/dev/null || die "docker not found in PATH"
docker inspect "$BTCPAY_CONTAINER" >/dev/null 2>&1 || die "container $BTCPAY_CONTAINER not found (set BTCPAY_CONTAINER)"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

log "downloading $URL"
curl -fsSL --retry 3 -o "$TMP/$PLUGIN.btcpay" "$URL"
curl -fsSL --retry 3 -o "$TMP/$PLUGIN.json" "$URL.json" || die "manifest $URL.json not found"

# sanity: it is a zip that contains the plugin assembly, and the manifest names this plugin
if command -v unzip >/dev/null; then
  unzip -l "$TMP/$PLUGIN.btcpay" | grep -q " $PLUGIN.dll\$" || die "archive does not contain $PLUGIN.dll"
fi
grep -q "\"$PLUGIN\"" "$TMP/$PLUGIN.json" || die "manifest does not name $PLUGIN"
VERSION="$(sed -n 's/.*"[Vv]ersion"[^"]*"\([^"]*\)".*/\1/p' "$TMP/$PLUGIN.json" | head -1)"
log "release version: ${VERSION:-unknown}"

log "copying into $BTCPAY_CONTAINER:$BTCPAY_PLUGINDIR"
docker exec "$BTCPAY_CONTAINER" mkdir -p "$BTCPAY_PLUGINDIR"
docker cp "$TMP/$PLUGIN.btcpay" "$BTCPAY_CONTAINER:$BTCPAY_PLUGINDIR/$PLUGIN.btcpay"
docker cp "$TMP/$PLUGIN.json"   "$BTCPAY_CONTAINER:$BTCPAY_PLUGINDIR/$PLUGIN.json"
docker exec "$BTCPAY_CONTAINER" sh -c "printf 'install:%s\n' '$PLUGIN' >> '$BTCPAY_PLUGINDIR/commands'"

SINCE="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
log "restarting $BTCPAY_CONTAINER"
docker restart "$BTCPAY_CONTAINER" >/dev/null

log "waiting for BTCPay to load the plugin"
for i in $(seq 1 60); do
  sleep 3
  if docker logs --since "$SINCE" "$BTCPAY_CONTAINER" 2>&1 | grep -q "Running plugin $PLUGIN - "; then
    docker logs --since "$SINCE" "$BTCPAY_CONTAINER" 2>&1 | grep "Running plugin $PLUGIN - " | tail -1 >&2
    log "OK"
    exit 0
  fi
  if docker logs --since "$SINCE" "$BTCPAY_CONTAINER" 2>&1 | grep -qiE "$PLUGIN.*(fail|error|exception)|plugin.*$PLUGIN.*disabled"; then
    break
  fi
done
log "plugin did not report as running within the timeout; recent log lines:"
docker logs --since "$SINCE" "$BTCPAY_CONTAINER" 2>&1 | grep -iE "plugin|error|fail" | tail -20 >&2
exit 1
