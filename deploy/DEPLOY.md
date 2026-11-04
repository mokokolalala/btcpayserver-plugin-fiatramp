# Deploying the plugin to the live BTCPay Server

After a one-time setup, every `v*` tag of this repository installs itself on the server from GitHub Actions.
The release files are **pushed** to the server over SSH, so the server never talks to GitHub and the
repository can be private.

## One-time setup

### On the server (the sysadmin, 2 minutes)

One command, with the deploy public key as its argument; it is safe to run again:

```bash
curl -fsSL https://raw.githubusercontent.com/mokokolalala/btcpayserver-plugin-fiatramp/master/deploy/setup-server.sh | sudo bash -s -- "ssh-ed25519 AAAA... deploy key"
```

`setup-server.sh` creates a `btcpaydeploy` user in the `docker` group, installs
`deploy/btcpay-install-plugin.sh` as `/usr/local/bin/btcpay-install-plugin`, authorises the key as an SSH
forced command (no shell, no forwarding; the key can only run the installer), and does one real install of
the latest release. It ends by printing the host, port and user to send back.
`deploy/BTCPay-FiatRamp-plugin-server-setup.docx` is the version of this to send to a sysadmin without GitHub access.

The installer does what BTCPay's *Upload plugin* button does: `<Name>.btcpay` and `<Name>.json` into the plugins
folder inside the `btcpayserver` container, `install:<Name>` appended to the `commands` file, container restart,
then it waits for `Running plugin BTCPayServer.Plugins.FiatRamp - <version>` in the log and fails otherwise.
Non-standard container name or plugin dir: `BTCPAY_CONTAINER` / `BTCPAY_PLUGINDIR`, see the script headers.

### On your machine (the repo owner)

Generate the key pair; the private half never leaves your machine except into GitHub's secret store:

```bash
ssh-keygen -t ed25519 -N "" -C "github-actions fiatramp deploy" -f fiatramp_deploy
```

Send `fiatramp_deploy.pub` (one line) to the sysadmin with the document. Then in the repository,
Settings → Secrets and variables → Actions:

| Kind | Name | Value |
|---|---|---|
| Secret | `DEPLOY_SSH_KEY` | contents of `fiatramp_deploy` (the private key file, BEGIN/END lines included) |
| Variable | `DEPLOY_HOST` | host or IP the sysadmin sent back |
| Variable | `DEPLOY_PORT` | only if not 22 |
| Variable | `DEPLOY_USER` | only if not `btcpaydeploy` |

The deploy job is skipped until `DEPLOY_HOST` exists. It runs in the `production` environment (created on
first use); require a reviewer there if an install should wait for a click.

## Day to day

- **Release and deploy**: `git tag v0.2.3 && git push origin v0.2.3`. The build job publishes the release; the
  deploy job sends the files to the server and ends with the `Running plugin … - 0.2.3.0` line from its log.
- **Redeploy or roll back**: Actions → *Deploy plugin to server* → *Run workflow* → any existing tag.
- **Manual install on the server, no GitHub**: `sudo -u btcpaydeploy btcpay-install-plugin <release-url>` with a
  `releases/download/<tag>/BTCPayServer.Plugins.FiatRamp.btcpay` URL, or pipe the two files in:
  `tar c BTCPayServer.Plugins.FiatRamp.btcpay BTCPayServer.Plugins.FiatRamp.btcpay.json | sudo -u btcpaydeploy btcpay-install-plugin`.
- **Verify**: `docker logs --since 10m generated_btcpayserver_1 | grep "Running plugin BTCPayServer.Plugins.FiatRamp"`,
  or Server Settings → Plugins in BTCPay.

## If a deploy fails

The job log shows the server's recent plugin log lines. Usual causes: the host rejected the key (check
`/var/log/auth.log`, `AllowUsers` in `sshd_config`), a firewall that does not admit GitHub's runners
(allow the `actions` ranges from `https://api.github.com/meta`), or the plugin failed to load, in which case
BTCPay disables it and shows the error under Server Settings → Plugins; roll back with the manual workflow.

## Making the repository private

Works after the server setup is done: deploys push files, the guide screenshots ship inside the plugin
(`~/Resources/guides/`), and nothing on the server fetches from GitHub. The only things that need the repo
public are the one-time `setup-server.sh` download and the manual *URL* form of the installer.
