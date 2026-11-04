# Fiat on-ramp plugin for BTCPay Server

Customers who don't own Bitcoin yet can buy it with a **credit card, Apple Pay, Google Pay, PayPal, bank transfer or cash** at an on-ramp site and send it to the invoice address. The plugin adds a walkthrough to the on-chain Bitcoin checkout:

- provider cards for the customer's **region** (EU / US / other, detected from the IP address) and the chosen **payment method**;
- a **step slider** per provider: one screen at a time, with a screenshot, arrows and swipe, a Copy button on the step that needs the amount or the address, and an "Open provider" button;
- a **"you receive" check**: before paying, the customer types the amount the provider says it will send; the page says whether that covers the invoice or how much more to buy;
- a "buy at least X" amount with a configurable buffer, so the provider's fee and the network fee don't leave the invoice short.

No partner keys are needed: providers are plain public links, with the amount filled into the link where the site accepts it, and the customer pastes the address. BTCPay keeps doing what it already does: the page updates by itself when the purchase lands, and a short payment shows as partially paid with the remaining amount.

## Install

1. Download `BTCPayServer.Plugins.FiatRamp.btcpay` from the latest release (or the *Build plugin* workflow artifact).
2. As a server admin open **Server Settings → Plugins → Upload plugin**, select the file, upload.
3. Restart BTCPay Server when prompted.

## Configure

In the store, open **Plugins → Fiat on-ramp**.

1. Tick **Enabled**.
2. Go through the providers: untick the ones you don't want, adjust regions and payment methods, and edit the **steps**. Each step has a text, an optional screenshot **or video** URL (https; an image, an `.mp4`/`.webm` file, or a YouTube/Vimeo link, which is embedded), what to copy on that step (the fiat amount, the BTC amount or the address), and flags for the *Open provider* button and the *you receive* check. Steps without a screenshot show a drawn stand-in. Real screenshots make the walkthrough much better: take them on a phone from your own account at the provider, put them somewhere public (BTCPay's file storage, your site, a GitHub repo) and paste the URLs.
3. Set the buffer percentage and the texts. **Save**.

The provider list ships with defaults (MoonPay, Paybis, Ramp, Mercuryo, Transak, Guardarian, plus a Bitcoin-ATM locator and Azteco vouchers for cash) and a seven-step walkthrough each. MoonPay takes the amount from the link; Paybis, Mercuryo, Transak and Guardarian ignore it, so their amount step shows a real screenshot of their amount screen and the customer types it. Ramp's widget needs a partner key, so its default is Ramp's public buy page. *Reset to defaults* restores the list.

### Provider links and placeholders

Each provider is a link template. Placeholders are replaced with the invoice's values and URL-encoded:

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

If a provider has given you a partner key, it goes in the provider's *Partner key and signing* section, and `{apiKey}` and a pre-filled `{address}` can be used in the link. Ramps that pre-fill the wallet address require a **signature**; the plugin computes it on the server, so the secret never reaches the browser. Supported schemes: `MoonPay` (base64 HMAC-SHA256 of the query string), `Mercuryo` (SHA-512 of address + secret), `Paybis` and two generic HMAC-SHA256 variants (hex / base64).

The *Open provider* button links to `/plugins/fiatramp/go/{invoiceId}/{providerId}` on your server, which fills the live amount into the link (and signs it when configured) and redirects.

### Two modes

- **No method in the link**: BTCPay's regular Bitcoin checkout, with a small "Don't have Bitcoin?" offer under it that leads into the walkthrough.
- **Method in the link** (`#credit-card` and friends): the walkthrough takes the top of the page and the Bitcoin pieces collapse behind "Already have Bitcoin?". The customer sees the fiat amount, picks a provider for their region and goes through that provider's steps one screen at a time. Under the slider, a "Your details" card keeps the amount to buy and the address with Copy buttons, and a live status box watches the blockchain. This is the mode to use when a shop lists "Credit card", "Apple Pay" and so on as separate payment methods that all land on BTCPay.

### Fees and short payments

Providers take their fee out of what the customer pays, so the amount that arrives is less than the amount bought. Two safeguards:

- before paying, the *you receive* step compares the provider's quoted BTC with the invoice's due amount and tells the customer how much more to buy if it falls short;
- after paying, BTCPay's own partial-payment handling shows what arrived and what is still missing, with the same address to send the rest to.

Because buying at a provider takes longer than paying from a wallet, raise the store's invoice expiration (Store Settings → Checkout experience → *Invoice expires if the full amount has not been paid after …*) to at least 60–120 minutes for stores that use this.

### Payment method from the checkout link

Send the customer to the normal checkout link with a hash to open the walkthrough on a method:

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
