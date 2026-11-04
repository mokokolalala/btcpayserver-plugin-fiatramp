#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Plugins.FiatRamp;

/// <summary>How the customer wants to pay the on-ramp. Selectable from the checkout URL, e.g. <c>#credit-card</c>.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum PayMethod
{
    Card,
    ApplePay,
    GooglePay,
    PayPal,
    Bank,
    Cash
}

/// <summary>Coarse customer region, detected from the IP address. Providers are tagged with the regions they serve.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum Region
{
    EU,
    US,
    Other
}

/// <summary>How the outgoing provider URL must be signed. Most ramps require a signature when a wallet address is pre-filled.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum UrlSigning
{
    None,
    /// <summary>MoonPay: base64(HMAC-SHA256(secret, query string including the leading "?")) appended as <c>signature</c>.</summary>
    MoonPay,
    /// <summary>Paybis: HMAC-SHA256(secret, query string without "?"), hex, appended as <c>signature</c>. Verify against your Paybis contract.</summary>
    Paybis,
    /// <summary>Mercuryo: SHA-512(address + secret), hex, appended as <c>signature</c>.</summary>
    Mercuryo,
    /// <summary>Generic: HMAC-SHA256(secret, query string without "?"), hex, appended as <c>signature</c>.</summary>
    HmacSha256QueryHex,
    /// <summary>Generic: HMAC-SHA256(secret, query string without "?"), base64, appended as <c>signature</c>.</summary>
    HmacSha256QueryBase64
}

public static class PayMethodInfo
{
    public record Entry(PayMethod Method, string Slug, string Label, string[] Aliases);

    public static readonly Entry[] All =
    {
        new(PayMethod.Card, "credit-card", "Credit / debit card", new[] { "card", "creditcard", "credit_card", "debit-card" }),
        new(PayMethod.ApplePay, "apple-pay", "Apple Pay", new[] { "applepay", "apple_pay", "apple" }),
        new(PayMethod.GooglePay, "google-pay", "Google Pay", new[] { "googlepay", "google_pay", "google", "gpay" }),
        new(PayMethod.PayPal, "paypal", "PayPal", new[] { "pay-pal", "pp" }),
        new(PayMethod.Bank, "bank-transfer", "Bank transfer", new[] { "bank", "sepa", "ach", "wire", "transfer" }),
        new(PayMethod.Cash, "cash", "Cash", new[] { "atm", "voucher" }),
    };
}

/// <summary>One screen of the walkthrough shown to the customer: text, an optional screenshot, and what to copy or do on it.</summary>
public class GuideStep
{
    /// <summary>Placeholders: {buy} (fiat amount incl. buffer), {btc}, {address}, {address_short}, {provider}.</summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Screenshot or video of the provider's screen for this step (absolute https URL): an image, an .mp4/.webm file,
    /// or a YouTube / Vimeo link (embedded). Without one, a drawn stand-in is shown.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>What this step asks the customer to paste: "fiat", "btc" or "address". Shows the value with a Copy button.</summary>
    public string? Copy { get; set; }

    /// <summary>Show the "Open provider" button on this step.</summary>
    public bool Open { get; set; }

    /// <summary>Show the "the provider says you receive ... BTC" check, which compares with the amount due.</summary>
    public bool Check { get; set; }

    /// <summary>Picks the drawn stand-in when there is no screenshot: open, signin, amount, address, check, pay, wait, info.</summary>
    public string? Kind { get; set; }
}

public class ProviderConfig
{
    /// <summary>Stable slug used in URLs. Generated from the name when left empty.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }

    /// <summary>
    /// URL with placeholders: {address} {btc} {fiat} {currency} {currency_lower} {apiKey} {store} {invoiceId} {returnUrl}.
    /// Values are URL-encoded when substituted.
    /// </summary>
    public string UrlTemplate { get; set; } = "";

    /// <summary>Public key, partner id or widget id, substituted for {apiKey}.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Secret used only for signing. Never sent to the browser.</summary>
    public string? Secret { get; set; }

    public UrlSigning Signing { get; set; }
    public List<PayMethod> Methods { get; set; } = new();
    public List<Region> Regions { get; set; } = new();

    /// <summary>Short hint shown under the provider button, e.g. "Min. $30 · KYC".</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Step-by-step instructions shown when the customer picks this provider, one step per line.
    /// Placeholders: {buy} (fiat amount incl. buffer), {btc}, {address}, {address_short}.
    /// </summary>
    public string? Guide { get; set; }

    /// <summary>Optional screenshot shown above the steps (absolute https URL, e.g. an image uploaded to BTCPay's file storage).</summary>
    public string? GuideImageUrl { get; set; }

    /// <summary>The walkthrough, one screen per step. When empty, <see cref="Guide"/> lines are used as plain text steps.</summary>
    public List<GuideStep> Steps { get; set; } = new();

    /// <summary>The steps the checkout shows: <see cref="Steps"/>, or the <see cref="Guide"/> lines turned into steps (first one opens the provider).</summary>
    public IEnumerable<GuideStep> EffectiveSteps()
    {
        if (Steps.Count > 0) return Steps;
        var lines = (Guide ?? "").Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        return lines.Select((l, i) => new GuideStep { Text = l, Open = i == 0, ImageUrl = i == 0 ? GuideImageUrl : null });
    }

    public int Order { get; set; }

    [JsonIgnore]
    public bool RequiresApiKey => UrlTemplate?.Contains("{apiKey}") == true;

    [JsonIgnore]
    public bool IsUsable => Enabled
                            && !string.IsNullOrWhiteSpace(UrlTemplate)
                            && (!RequiresApiKey || !string.IsNullOrWhiteSpace(ApiKey))
                            && (Signing == UrlSigning.None || !string.IsNullOrWhiteSpace(Secret));
}

public class FiatRampSettings
{
    public const string SettingName = "FiatRamp";

    public bool Enabled { get; set; }

    /// <summary>Extra percentage the customer is told to buy, to cover the provider's fee and the network fee.</summary>
    public decimal BufferPercent { get; set; } = 3;

    public string Headline { get; set; } = "Don't have Bitcoin?";

    public string Intro { get; set; } =
        "Buy it with a card or bank transfer and have it sent straight to this address. " +
        "Copy the details below; they are the same ones the provider will ask for.";

    public bool GeoLookupEnabled { get; set; } = true;

    /// <summary>GET endpoint returning a 2-letter country code (plain text or JSON). {ip} is replaced with the client IP.</summary>
    public string GeoLookupUrl { get; set; } = "https://ipapi.co/{ip}/country/";

    public List<ProviderConfig> Providers { get; set; } = new();

    public static FiatRampSettings CreateDefault() => new() { Providers = DefaultProviders() };

    /// <summary>The walkthrough for a hosted on-ramp site that the customer uses with their own account (no partner key, no pre-filled address).</summary>
    static List<GuideStep> SiteSteps(string provider, string payWith, bool amountInUrl, string? signInImage = null, string? amountImage = null) => new()
    {
        new() { Kind = "open", Open = true, Text = amountInUrl
            ? $"Tap the button below. {provider} opens in a new tab with the amount already filled in. Keep this page open too; you will come back to it."
            : $"Tap the button below. {provider} opens in a new tab. Keep this page open too; you will come back to it." },
        new() { Kind = "signin", ImageUrl = signInImage, Text = $"Sign in, or create an account if you don't have one. First time? {provider} asks for a phone number and a photo of your ID. That is normal and takes a few minutes." },
        new() { Kind = "amount", Copy = "fiat", ImageUrl = amountImage, Text = "Choose Bitcoin (BTC) and make sure the amount is at least this. If the field is empty or shows less, paste this number." },
        new() { Kind = "address", Copy = "address", Text = $"When {provider} asks where to send the Bitcoin (\"wallet address\" or \"receiving address\"), paste this address. Don't let it create a wallet for you." },
        new() { Kind = "check", Check = true, Text = $"Before you pay, {provider} shows what you receive after its fees. Type that number here so we can check it covers the order." },
        new() { Kind = "pay", Text = $"Pay with {payWith}." },
        new() { Kind = "wait", Text = $"When {provider} says the purchase is complete, come back to this page. The status at the bottom updates by itself, usually within 10 to 30 minutes." },
    };

    const string GuideImages = "https://raw.githubusercontent.com/mokokolalala/btcpayserver-plugin-fiatramp/master/docs/guides/";

    public static List<ProviderConfig> DefaultProviders()
    {
        var all = new[] { Region.EU, Region.US, Region.Other };
        var cardish = new[] { PayMethod.Card, PayMethod.ApplePay, PayMethod.GooglePay };
        int order = 0;
        return new List<ProviderConfig>
        {
            new()
            {
                Id = "moonpay", Name = "MoonPay", Order = order++, Enabled = true,
                UrlTemplate = "https://buy.moonpay.com/?currencyCode=btc&baseCurrencyCode={currency_lower}&baseCurrencyAmount={fiat}",
                Methods = cardish.Concat(new[] { PayMethod.PayPal, PayMethod.Bank }).ToList(),
                Regions = all.ToList(),
                Note = "Card, Apple/Google Pay, PayPal, bank",
                Steps = SiteSteps("MoonPay", "your card, Apple Pay, Google Pay, PayPal or a bank transfer", true, GuideImages + "moonpay-1-signin.jpg")
            },
            new()
            {
                Id = "paybis", Name = "Paybis", Order = order++, Enabled = true,
                UrlTemplate = "https://paybis.com/?currencyCodeFrom={currency}&currencyCodeTo=BTC&amountFrom={fiat}",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = new() { Region.EU, Region.US },
                Note = "Card, Apple/Google Pay, bank",
                Steps = SiteSteps("Paybis", "your card, Apple Pay, Google Pay or a bank transfer", false, null, GuideImages + "paybis-amount.jpg")
            },
            new()
            {
                Id = "ramp", Name = "Ramp", Order = order++, Enabled = true,
                UrlTemplate = "https://rampnetwork.com/buy-crypto",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = new() { Region.EU, Region.US },
                Note = "Card, Apple/Google Pay, SEPA",
                Steps = SiteSteps("Ramp", "your card, Apple Pay, Google Pay or a SEPA transfer", false)
            },
            new()
            {
                Id = "mercuryo", Name = "Mercuryo", Order = order++, Enabled = true,
                UrlTemplate = "https://exchange.mercuryo.io/?type=buy&currency=BTC&fiat_currency={currency}&fiat_amount={fiat}",
                Methods = cardish.ToList(),
                Regions = new() { Region.EU, Region.Other },
                Note = "Card, Apple/Google Pay",
                Steps = SiteSteps("Mercuryo", "your card, Apple Pay or Google Pay", false, null, GuideImages + "mercuryo-amount.jpg")
            },
            new()
            {
                Id = "transak", Name = "Transak", Order = order++, Enabled = true,
                UrlTemplate = "https://global.transak.com/?cryptoCurrencyCode=BTC&network=mainnet&fiatCurrency={currency}&fiatAmount={fiat}",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = all.ToList(),
                Note = "Card, Apple/Google Pay, bank",
                Steps = SiteSteps("Transak", "your card, Apple Pay, Google Pay or a bank transfer", false, null, GuideImages + "transak-amount.jpg")
            },
            new()
            {
                Id = "guardarian", Name = "Guardarian", Order = order++, Enabled = true,
                UrlTemplate = "https://guardarian.com/buy-btc",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = new() { Region.EU },
                Note = "Card, SEPA · EU",
                Steps = SiteSteps("Guardarian", "your card or a SEPA transfer", false, null, GuideImages + "guardarian-amount.jpg")
            },
            new()
            {
                Id = "atm", Name = "Bitcoin ATM near you", Order = order++, Enabled = true,
                UrlTemplate = "https://coinatmradar.com/",
                Methods = new() { PayMethod.Cash },
                Regions = all.ToList(),
                Note = "Scan this page's QR code at the ATM",
                Steps = new()
                {
                    new() { Kind = "open", Open = true, Text = "Tap the button below to find a Bitcoin ATM near you. Pick one that sells Bitcoin, and note its fee." },
                    new() { Kind = "address", Copy = "address", Text = "At the ATM choose Buy Bitcoin. When it asks for your wallet, scan this page's QR code (tap Show QR code) or type the address. Do not let the ATM create a wallet for you." },
                    new() { Kind = "amount", Copy = "fiat", Text = "Insert cash for at least this amount. ATM fees are high, often 10 to 20 percent, so round up." },
                    new() { Kind = "check", Check = true, Text = "Before you confirm, the ATM shows how much Bitcoin you get. Type it here to check it covers the order." },
                    new() { Kind = "wait", Text = "Keep the receipt and come back to this page. The payment shows up once the ATM sends it, usually within an hour." },
                }
            },
            new()
            {
                Id = "azteco", Name = "Azteco voucher", Order = order++, Enabled = true,
                UrlTemplate = "https://azte.co/",
                Methods = new() { PayMethod.Cash },
                Regions = all.ToList(),
                Note = "Buy a voucher with cash, redeem to this address",
                Steps = new()
                {
                    new() { Kind = "open", Open = true, Text = "Tap the button below and use the map to find a shop near you that sells Azteco Bitcoin vouchers." },
                    new() { Kind = "amount", Copy = "fiat", Text = "Buy a voucher for at least this amount, in cash." },
                    new() { Kind = "address", Copy = "address", Text = "Redeem it on azte.co: enter the voucher code and paste this address." },
                    new() { Kind = "wait", Text = "Come back to this page. It updates by itself once the voucher is redeemed." },
                }
            },
        };
    }
}
