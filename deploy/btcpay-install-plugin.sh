#!/usr/bin/env bash
# Installs (or updates) the Fiat on-ramp plugin on a BTCPay Server docker deployment.
#
# Two ways to give it the plugin:
#   1. a release URL:   btcpay-install-plugin.sh https://github.com/<owner>/<repo>/releases/download/v0.2.1/BTCPayServer.Plugins.FiatRamp.btcpay
#   2. a tar on stdin:  tar c BTCPayServer.Plugins.FiatRamp.btcpay BTCPayServer.Plugins.FiatRamp.btcpay.json | btcpay-install-plugin.sh
#      (this is what GitHub Actions does over SSH, so the server never needs access to GitHub and the repo can be private)
#
# As an SSH forced command, the URL (if any) comes from $SSH_ORIGINAL_COMMAND; with no URL, stdin is read.
#
# What it does, which is exactly what BTCPay's own "Upload plugin" does:
#   copy <Name>.btcpay and <Name>.json into the plugins folder inside the btcpayserver container,
#   append "install:<Name>" to the folder's "commands" file, restart the container, and wait for
#   the log line "Running plugin <Name> - <version>". Fails loudly otherwise.
#
# Overrides (defaults match a standard btcpay-setup.sh deployment):
#   BTCPAY_CONTAINER (generated_btcpayserver_1)  BTCPAY_PLUGINDIR (/datadir/plugins)  PLUGIN (BTCPayServer.Plugins.FiatRamp)
set -euo pipefail

PLUGIN="${PLUGIN:-BTCPayServer.Plugins.FiatRamp}"
BTCPAY_CONTAINER="${BTCPAY_CONTAINER:-generated_btcpayserver_1}"
BTCPAY_PLUGINDIR="${BTCPAY_PLUGINDIR:-/datadir/plugins}"

log() { printf '[install-plugin] %s\n' "$*" >&2; }
die() { log "ERROR: $*"; exit 1; }

command -v docker >/dev/null || die "docker not found in PATH"
docker inspect "$BTCPAY_CONTAINER" >/dev/null 2>&1 || die "container $BTCPAY_CONTAINER not found (set BTCPAY_CONTAINER)"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

URL="${1:-${SSH_ORIGINAL_COMMAND:-}}"
case "$URL" in install|"") URL="";; esac   # "install" = the files come on stdin (what GitHub Actions sends)
if [ -n "$URL" ]; then
  case "$URL" in
    https://github.com/*/releases/download/*/"$PLUGIN.btcpay"|https://github.com/*/releases/latest/download/"$PLUGIN.btcpay") ;;
    *) die "expected a GitHub release URL ending in /$PLUGIN.btcpay, got: $URL" ;;
  esac
  log "downloading $URL"
  curl -fsSL --retry 3 -o "$TMP/$PLUGIN.btcpay" "$URL"
  curl -fsSL --retry 3 -o "$TMP/$PLUGIN.json" "$URL.json" || die "manifest $URL.json not found"
else
  [ ! -t 0 ] || die "usage: $0 <release-url>   or   tar c <plugin>.btcpay <plugin>.btcpay.json | $0"
  log "reading plugin files from stdin"
  tar x -C "$TMP" -f - 2>/dev/null || die "stdin is not a tar archive"
  [ -f "$TMP/$PLUGIN.btcpay" ] || die "tar did not contain $PLUGIN.btcpay"
  if [ -f "$TMP/$PLUGIN.btcpay.json" ]; then mv "$TMP/$PLUGIN.btcpay.json" "$TMP/$PLUGIN.json"; fi
  [ -f "$TMP/$PLUGIN.json" ] || die "tar did not contain $PLUGIN.btcpay.json"
fi

# sanity: a zip with the plugin assembly, and a manifest naming this plugin
if command -v unzip >/dev/null; then
  unzip -l "$TMP/$PLUGIN.btcpay" | grep -q " $PLUGIN.dll\$" || die "archive does not contain $PLUGIN.dll"
fi
grep -q "\"$PLUGIN\"" "$TMP/$PLUGIN.json" || die "manifest does not name $PLUGIN"
VERSION="$(sed -n 's/.*"[Vv]ersion"[^"]*"\([^"]*\)".*/\1/p' "$TMP/$PLUGIN.json" | head -1)"
log "plugin version: ${VERSION:-unknown}"

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
