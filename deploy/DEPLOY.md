# Deploying the Fiat on-ramp plugin to the live BTCPay Server

Instructions for the system administrator. After the one-time setup below, every tagged release of this
repository installs itself on the server from GitHub Actions. Nothing on the server needs to change again.

## How it works

1. A `v*` tag (for example `v0.2.2`) makes GitHub Actions build, test and pack the plugin and publish a
   GitHub release with `BTCPayServer.Plugins.FiatRamp.btcpay` and its `.json` manifest.
2. A second job SSHes into the server as a dedicated user whose key is locked to one command:
   `deploy/btcpay-install-plugin.sh`. The only thing the job sends is the release URL.
3. The script does what BTCPay's own *Upload plugin* button does: it copies the `.btcpay` and manifest into
   the plugins folder inside the `btcpayserver` container, queues `install:BTCPayServer.Plugins.FiatRamp`,
   restarts the container (a few seconds of downtime) and waits for the log line
   `Running plugin BTCPayServer.Plugins.FiatRamp - <version>`. If that line does not appear, the job fails
   and prints the relevant log lines.

The deploy key cannot run anything else on the server, and the script refuses any URL that is not a release
of this repository. Store settings (providers, steps, texts) live in BTCPay's database and survive updates.

## Requirements

- BTCPay Server **2.4.5 or newer** on the standard docker deployment (`btcpay-setup.sh`). The container is
  named `generated_btcpayserver_1` and the plugins folder inside it is `/datadir/plugins`. Other layouts work
  with the `BTCPAY_CONTAINER` / `BTCPAY_PLUGINDIR` overrides, see the script header.
- SSH access to the host from the internet on some port (GitHub's runners connect from changing IPs; if the
  firewall allowlists SSH sources, allow GitHub Actions' published IP ranges or use the pull model, see the end).
- `curl` and `unzip` on the host (`apt install curl unzip`).

## One-time setup on the server

Run as a user with `sudo`.

### 1. Create the deploy user

```bash
sudo useradd --create-home --shell /bin/bash btcpaydeploy
```

```bash
sudo usermod -aG docker btcpaydeploy
```

Membership of the `docker` group is what lets the script copy files into the container and restart it.
No sudo rights are given to this user.

### 2. Install the script

```bash
sudo curl -fsSL -o /usr/local/bin/btcpay-install-plugin https://raw.githubusercontent.com/mokokolalala/btcpayserver-plugin-fiatramp/master/deploy/btcpay-install-plugin.sh
```

```bash
sudo chmod 755 /usr/local/bin/btcpay-install-plugin
```

Read it before trusting it; it is about 80 lines.

### 3. Create the deploy key and lock it to the script

Generate a key pair on the server (or anywhere; only the private half must reach GitHub):

```bash
ssh-keygen -t ed25519 -N "" -C "github-actions fiatramp deploy" -f /tmp/fiatramp_deploy
```

Install the public half for the deploy user with a forced command and no other SSH features:

```bash
sudo -u btcpaydeploy mkdir -p /home/btcpaydeploy/.ssh && sudo -u btcpaydeploy chmod 700 /home/btcpaydeploy/.ssh
```

```bash
echo "command=\"/usr/local/bin/btcpay-install-plugin\",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty $(cat /tmp/fiatramp_deploy.pub)" | sudo -u btcpaydeploy tee -a /home/btcpaydeploy/.ssh/authorized_keys >/dev/null
```

```bash
sudo -u btcpaydeploy chmod 600 /home/btcpaydeploy/.ssh/authorized_keys
```

The forced command means: whatever the client asks to run is ignored and handed to the script as
`$SSH_ORIGINAL_COMMAND`, which the script only accepts if it is a release URL of this repository.

If `sshd_config` restricts logins with `AllowUsers` or `AllowGroups`, add `btcpaydeploy` there and reload sshd.

### 4. Test it from the server itself

This performs a real install of the current release, including a restart of BTCPay:

```bash
ssh -i /tmp/fiatramp_deploy -o StrictHostKeyChecking=no btcpaydeploy@localhost "https://github.com/mokokolalala/btcpayserver-plugin-fiatramp/releases/download/v0.2.1/BTCPayServer.Plugins.FiatRamp.btcpay"
```

Expected ending: `Running plugin BTCPayServer.Plugins.FiatRamp - 0.2.1.0` and `[install-plugin] OK`.
Afterwards, in BTCPay, Server Settings → Plugins lists *Fiat on-ramp 0.2.1*.

### 5. Hand the private key to GitHub, then delete it from the server

Print it, paste it into GitHub as described in the next section, then remove it:

```bash
cat /tmp/fiatramp_deploy
```

```bash
shred -u /tmp/fiatramp_deploy /tmp/fiatramp_deploy.pub
```

## One-time setup in GitHub

In the repository `mokokolalala/btcpayserver-plugin-fiatramp`, Settings → Secrets and variables → Actions:

| Kind | Name | Value |
|---|---|---|
| Secret | `DEPLOY_SSH_KEY` | the private key, the whole file including the BEGIN/END lines |
| Variable | `DEPLOY_HOST` | the server's hostname or IP |
| Variable | `DEPLOY_PORT` | the SSH port, only if it is not 22 |
| Variable | `DEPLOY_USER` | only if the user is not `btcpaydeploy` |

The deploy job runs in a GitHub *environment* named `production`, created automatically on first use.
Optional hardening in Settings → Environments → production: require a reviewer, so a tag builds and
publishes a release but the install waits for a click.

Automatic deploys are off until `DEPLOY_HOST` exists; the build workflow skips the deploy job without it.

## Day to day

- **Release and deploy**: push a tag. `git tag v0.2.2 && git push origin v0.2.2`. Watch the run in the
  Actions tab; the deploy job's log ends with the `Running plugin … - 0.2.2.0` line from the server.
- **Redeploy or roll back**: Actions → *Deploy plugin to server* → *Run workflow*, enter any existing tag,
  for example `v0.2.1`. Same script, older release.
- **Verify on the server**: `docker logs --since 10m generated_btcpayserver_1 | grep "Running plugin BTCPayServer.Plugins.FiatRamp"`.
- **Verify in BTCPay**: Server Settings → Plugins shows the version; a store's Plugins → Fiat on-ramp page opens;
  an invoice link with `#credit-card` appended shows the walkthrough.

## If a deploy fails

- The job prints the server's recent log lines. The usual causes: the release assets are not there yet
  (rerun the job), the container name differs (set `BTCPAY_CONTAINER` in the deploy user's environment or
  edit the script's default), or the host rejected the key (check `/var/log/auth.log`).
- BTCPay disables a plugin that throws during startup so the server still comes up. If that happens, Server
  Settings → Plugins shows it as disabled with the error; the previous version is gone at that point, so
  roll back with the manual workflow and an earlier tag.
- Manual install without GitHub, from the server: run the same command as in step 4 with the wanted release
  URL, or upload the `.btcpay` through Server Settings → Plugins → Upload plugin.

## Uninstall

In BTCPay: Server Settings → Plugins → Fiat on-ramp → Uninstall, then restart. Remove the deploy user and
the GitHub secret/variables if the pipeline is no longer wanted.

## Alternative: pull model (no inbound SSH from GitHub)

If the server must not accept SSH from the internet, skip the deploy user and key, and instead let the
server poll for releases, as root, every 15 minutes:

```bash
sudo crontab -e
```

```
*/15 * * * * /usr/local/bin/btcpay-install-plugin "$(curl -fsSL https://api.github.com/repos/mokokolalala/btcpayserver-plugin-fiatramp/releases/latest | sed -n 's/.*"browser_download_url": *"\([^"]*\.btcpay\)".*/\1/p' | head -1)" >> /var/log/btcpay-plugin-install.log 2>&1
```

Note that this reinstalls and restarts BTCPay every 15 minutes as written; wrap it in a version check
(compare the latest tag with the version in the running container's log) before using it for real.
The push model above is recommended because every deploy leaves a log in GitHub and nothing goes live
without someone tagging it.
