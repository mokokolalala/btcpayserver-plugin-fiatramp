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

    public static List<ProviderConfig> DefaultProviders()
    {
        var all = new[] { Region.EU, Region.US, Region.Other };
        var cardish = new[] { PayMethod.Card, PayMethod.ApplePay, PayMethod.GooglePay };
        int order = 0;
        return new List<ProviderConfig>
        {
            new()
            {
                Id = "moonpay", Name = "MoonPay", Order = order++,
                UrlTemplate = "https://buy.moonpay.com/?apiKey={apiKey}&currencyCode=btc&walletAddress={address}&baseCurrencyCode={currency_lower}&baseCurrencyAmount={fiat}&showWalletAddressForm=true",
                Signing = UrlSigning.MoonPay,
                Methods = cardish.Concat(new[] { PayMethod.PayPal, PayMethod.Bank }).ToList(),
                Regions = all.ToList(),
                Note = "Card, Apple/Google Pay, PayPal, bank"
            },
            new()
            {
                Id = "transak", Name = "Transak", Order = order++,
                UrlTemplate = "https://global.transak.com/?apiKey={apiKey}&cryptoCurrencyCode=BTC&network=mainnet&walletAddress={address}&fiatCurrency={currency}&fiatAmount={fiat}&disableWalletAddressForm=true",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = all.ToList(),
                Note = "Card, Apple/Google Pay, bank"
            },
            new()
            {
                Id = "ramp", Name = "Ramp", Order = order++,
                UrlTemplate = "https://app.rampnetwork.com/?hostApiKey={apiKey}&hostAppName={store}&userAddress={address}&enabledCryptoAssets=BTC_BTC&defaultAsset=BTC_BTC&fiatCurrency={currency}&fiatValue={fiat}",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = new() { Region.EU, Region.US },
                Note = "Card, Apple/Google Pay, SEPA"
            },
            new()
            {
                Id = "mercuryo", Name = "Mercuryo", Order = order++,
                UrlTemplate = "https://exchange.mercuryo.io/?widget_id={apiKey}&type=buy&currency=BTC&address={address}&fiat_currency={currency}&fiat_amount={fiat}&fix_currency=true&fix_fiat_currency=true&fix_fiat_amount=true",
                Signing = UrlSigning.Mercuryo,
                Methods = cardish.ToList(),
                Regions = new() { Region.EU, Region.Other },
                Note = "Card, Apple/Google Pay"
            },
            new()
            {
                Id = "banxa", Name = "Banxa", Order = order++,
                UrlTemplate = "https://{apiKey}.banxa.com/?coinType=BTC&blockchain=BTC&fiatType={currency}&fiatAmount={fiat}&walletAddress={address}",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = all.ToList(),
                Note = "{apiKey} = your Banxa partner subdomain"
            },
            new()
            {
                Id = "paybis", Name = "Paybis", Order = order++,
                UrlTemplate = "https://widget.paybis.com/?partnerId={apiKey}&cryptoAddress={address}&currencyCodeTo=BTC&currencyCodeFrom={currency}&amountFrom={fiat}",
                Signing = UrlSigning.Paybis,
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = new() { Region.EU, Region.US },
                Note = "Card, Apple/Google Pay, bank"
            },
            new()
            {
                Id = "guardarian", Name = "Guardarian", Order = order++,
                UrlTemplate = "https://guardarian.com/calculator/v1?partner_api_token={apiKey}&default_crypto_currency=BTC&default_fiat_currency={currency}&default_fiat_amount={fiat}&payout_address={address}",
                Methods = cardish.Concat(new[] { PayMethod.Bank }).ToList(),
                Regions = new() { Region.EU },
                Note = "Card, SEPA · EU"
            },
            new()
            {
                Id = "atm", Name = "Bitcoin ATM near you", Order = order++, Enabled = true,
                UrlTemplate = "https://coinatmradar.com/",
                Methods = new() { PayMethod.Cash },
                Regions = all.ToList(),
                Note = "Scan this page's QR code at the ATM"
            },
            new()
            {
                Id = "azteco", Name = "Azteco voucher", Order = order++, Enabled = true,
                UrlTemplate = "https://azte.co/",
                Methods = new() { PayMethod.Cash },
                Regions = all.ToList(),
                Note = "Buy a voucher with cash, redeem to this address"
            },
        };
    }
}
