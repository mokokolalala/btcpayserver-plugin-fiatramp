#!/usr/bin/env bash
# One-time server setup for automatic installs of the Fiat on-ramp plugin. Run as root (sudo) on the BTCPay host:
#
#   curl -fsSL https://raw.githubusercontent.com/mokokolalala/btcpayserver-plugin-fiatramp/master/deploy/setup-server.sh | sudo bash -s -- "ssh-ed25519 AAAA... deploy key"
#
# It creates a "btcpaydeploy" user that can only run the plugin installer, installs that installer,
# authorises the given SSH public key for it, and does one real install of the latest release
# (BTCPay restarts for a few seconds). Safe to run again; it just refreshes everything.
set -euo pipefail

REPO="${REPO:-mokokolalala/btcpayserver-plugin-fiatramp}"
PLUGIN="${PLUGIN:-BTCPayServer.Plugins.FiatRamp}"
DEPLOY_USER="${DEPLOY_USER:-btcpaydeploy}"
BTCPAY_CONTAINER="${BTCPAY_CONTAINER:-generated_btcpayserver_1}"
INSTALLER=/usr/local/bin/btcpay-install-plugin
PUBKEY="${1:-}"

say() { printf '\n==> %s\n' "$*"; }
die() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" = 0 ] || die "run with sudo"
case "$PUBKEY" in ssh-ed25519\ *|ssh-rsa\ *|ecdsa-sha2-*) ;; *) die "first argument must be the SSH public key line you were sent (starts with ssh-ed25519)";; esac
command -v docker >/dev/null || die "docker not found"
docker inspect "$BTCPAY_CONTAINER" >/dev/null 2>&1 || die "BTCPay container '$BTCPAY_CONTAINER' not found. Find it with: docker ps   then rerun with BTCPAY_CONTAINER=<name> in front of the command"
command -v curl >/dev/null || die "curl not found (apt install -y curl)"
command -v unzip >/dev/null || { say "installing unzip"; apt-get install -y -q unzip >/dev/null 2>&1 || true; }

say "deploy user: $DEPLOY_USER"
id "$DEPLOY_USER" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$DEPLOY_USER"
usermod -aG docker "$DEPLOY_USER"
HOME_DIR="$(getent passwd "$DEPLOY_USER" | cut -d: -f6)"
if [ "$BTCPAY_CONTAINER" != "generated_btcpayserver_1" ]; then
  # non-standard container name: remember it for the installer
  printf 'BTCPAY_CONTAINER=%s\n' "$BTCPAY_CONTAINER" > /etc/btcpay-install-plugin.env
fi

say "installer: $INSTALLER"
curl -fsSL "https://raw.githubusercontent.com/$REPO/master/deploy/btcpay-install-plugin.sh" -o "$INSTALLER.tmp"
head -1 "$INSTALLER.tmp" | grep -q '^#!' || die "could not download the installer from GitHub"
mv "$INSTALLER.tmp" "$INSTALLER"
chmod 755 "$INSTALLER"
# wrapper that loads the optional env file, used as the forced command
cat > /usr/local/bin/btcpay-install-plugin-ssh <<'EOF'
#!/usr/bin/env bash
[ -f /etc/btcpay-install-plugin.env ] && set -a && . /etc/btcpay-install-plugin.env && set +a
exec /usr/local/bin/btcpay-install-plugin
EOF
chmod 755 /usr/local/bin/btcpay-install-plugin-ssh

say "SSH key, locked to the installer"
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$HOME_DIR/.ssh"
AK="$HOME_DIR/.ssh/authorized_keys"
touch "$AK"
grep -v 'btcpay-install-plugin' "$AK" > "$AK.tmp" || true
printf 'command="/usr/local/bin/btcpay-install-plugin-ssh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty %s\n' "$PUBKEY" >> "$AK.tmp"
mv "$AK.tmp" "$AK"
chown "$DEPLOY_USER:$DEPLOY_USER" "$AK"
chmod 600 "$AK"
if grep -qE '^\s*(AllowUsers|AllowGroups)' /etc/ssh/sshd_config 2>/dev/null; then
  echo "NOTE: /etc/ssh/sshd_config restricts logins (AllowUsers/AllowGroups). Add '$DEPLOY_USER' there and reload sshd, or the deploys will be refused."
fi

say "test install of the latest release (BTCPay restarts now)"
sudo -u "$DEPLOY_USER" env BTCPAY_CONTAINER="$BTCPAY_CONTAINER" "$INSTALLER" "https://github.com/$REPO/releases/latest/download/$PLUGIN.btcpay"

PORT="$( (sshd -T 2>/dev/null || true) | awk '$1=="port"{print $2; exit}')"
IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
say "DONE. Send this back:"
echo "   host: ${IP:-<public hostname or IP of this server>}   (use the public address if this one is private)"
echo "   ssh port: ${PORT:-22}"
echo "   user: $DEPLOY_USER"
echo "   setup OK"
