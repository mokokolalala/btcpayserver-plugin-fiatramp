# Fiat on-ramp plugin for BTCPay Server

Customers who don't own Bitcoin yet can buy it with a **credit card, Apple Pay, Google Pay, PayPal, bank transfer or cash** at an on-ramp provider and have it sent straight to the invoice address. The plugin adds a panel to the on-chain Bitcoin checkout, below *Pay in wallet*:

- the address, the BTC amount and the fiat amount, each with a copy button (ramps ask for the fiat amount);
- a "buy at least X" notice with a configurable buffer, so the provider's fee and the network fee don't leave the invoice short;
- buttons to the providers that serve the customer's **region** (EU / US / other, detected from the IP address) and the chosen **payment method**.

BTCPay keeps doing what it already does: the page updates by itself when the purchase lands, and a short payment shows as partially paid with the remaining amount.

## Install

1. Download `BTCPayServer.Plugins.FiatRamp.btcpay` from the latest release (or the *Build plugin* workflow artifact).
2. As a server admin open **Server Settings → Plugins → Upload plugin**, select the file, upload.
3. Restart BTCPay Server when prompted.

## Configure

In the store, open **Plugins → Fiat on-ramp**.

1. Tick **Enabled**.
2. For each provider you want to offer, enter its **API key / partner ID** and, where required, its **secret**, then tick *Enabled*. Providers without the keys they need are not shown to customers.
3. Adjust the regions and payment methods per provider, the buffer percentage and the texts. **Save**.

The provider list ships with sensible defaults (MoonPay, Transak, Ramp, Mercuryo, Banxa, Paybis, Guardarian, plus a Bitcoin-ATM locator and Azteco vouchers for cash). *Reset to defaults* restores it and keeps the keys you typed.

### Provider URLs and signing

Each provider is a URL template. Placeholders are replaced with the invoice's values and URL-encoded:

| Placeholder | Value |
|---|---|
| `{address}` | the invoice's on-chain address |
| `{btc}` | amount still due, in BTC |
| `{fiat}` | amount to buy in the invoice currency, buffer included |
| `{currency}` / `{currency_lower}` | invoice currency, `USD` / `usd` |
| `{apiKey}` | the provider's API key / partner ID field |
| `{store}` | store name |
| `{invoiceId}` | invoice id |
| `{returnUrl}` | the checkout page URL |

Most ramps require a **signature** when the wallet address is pre-filled. The plugin computes it on the server, so the secret never reaches the browser. Supported schemes: `MoonPay` (base64 HMAC-SHA256 of the query string), `Mercuryo` (SHA-512 of address + secret), `Paybis` and two generic HMAC-SHA256 variants (hex / base64). Check the exact scheme in your provider contract; it is set per provider.

Provider buttons link to `/plugins/fiatramp/go/{invoiceId}/{providerId}` on your server, which builds and signs the provider URL at click time and redirects.

### Payment method from the checkout link

Send the customer to the normal checkout link with a hash to open the panel on a method:

```
https://your-btcpay/i/INVOICE_ID#credit-card
https://your-btcpay/i/INVOICE_ID#apple-pay
https://your-btcpay/i/INVOICE_ID#google-pay
https://your-btcpay/i/INVOICE_ID#paypal
https://your-btcpay/i/INVOICE_ID#bank-transfer
https://your-btcpay/i/INVOICE_ID#cash
```

`?method=credit-card` works too. The panel scrolls into view, is titled for that method and shows only matching providers; the customer can still switch methods with the chips in the panel.

### Region

The customer's country is looked up from their IP address through a configurable GET endpoint (default `https://ipapi.co/{ip}/country/`, which returns the two-letter code; any endpoint returning plain text or JSON with `country_code` works). Results are cached for 24 hours. EU means EU-27, EEA, UK and Switzerland; US is the United States; everything else is *Other*. Private and local IPs are skipped and the browser's timezone is used as a fallback. Append `?region=eu` or `?region=us` to the checkout link to force a region, which is handy for testing.

## Develop

Requires the .NET 10 SDK. The BTCPay Server source is a git submodule pinned to the release the plugin targets.

```bash
git clone --recurse-submodules https://github.com/mokokolalala/btcpayserver-plugin-fiatramp.git
cd btcpayserver-plugin-fiatramp
dotnet build src/BTCPayServer.Plugins.FiatRamp/BTCPayServer.Plugins.FiatRamp.csproj -c Release
dotnet test tests/BTCPayServer.Plugins.FiatRamp.Tests/BTCPayServer.Plugins.FiatRamp.Tests.csproj
```

Pack it into a `.btcpay` file:

```bash
dotnet run --project submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj -- \
  src/BTCPayServer.Plugins.FiatRamp/bin/Release/net10.0 BTCPayServer.Plugins.FiatRamp artifacts
```

Pushing a tag such as `v0.1.0` makes the GitHub Actions workflow build, test, pack and attach the `.btcpay` file to a release.

## License

MIT
