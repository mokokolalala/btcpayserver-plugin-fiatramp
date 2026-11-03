#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BTCPayServer.Plugins.FiatRamp.Services;

public record ProviderUrlContext(
    string Address,
    decimal BtcAmount,
    decimal FiatAmount,
    string FiatCurrency,
    string StoreName,
    string InvoiceId,
    string ReturnUrl);

public class ProviderUrlBuilder
{
    static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public string Build(ProviderConfig provider, ProviderUrlContext ctx)
    {
        var url = Expand(provider, ctx);
        var secret = provider.Secret ?? "";
        return provider.Signing switch
        {
            UrlSigning.MoonPay => Append(url, "signature", Convert.ToBase64String(Hmac(secret, QueryOf(url, includeQuestionMark: true)))),
            UrlSigning.Paybis => Append(url, "signature", Hex(Hmac(secret, QueryOf(url, includeQuestionMark: false)))),
            UrlSigning.HmacSha256QueryHex => Append(url, "signature", Hex(Hmac(secret, QueryOf(url, includeQuestionMark: false)))),
            UrlSigning.HmacSha256QueryBase64 => Append(url, "signature", Convert.ToBase64String(Hmac(secret, QueryOf(url, includeQuestionMark: false)))),
            UrlSigning.Mercuryo => Append(url, "signature", Hex(SHA512.HashData(Encoding.UTF8.GetBytes(ctx.Address + secret)))),
            _ => url
        };
    }

    public static string Expand(ProviderConfig provider, ProviderUrlContext ctx)
    {
        var inv = CultureInfo.InvariantCulture;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["address"] = ctx.Address,
            ["btc"] = ctx.BtcAmount.ToString("0.########", inv),
            ["fiat"] = ctx.FiatAmount.ToString("0.##", inv),
            ["currency"] = ctx.FiatCurrency.ToUpperInvariant(),
            ["currency_lower"] = ctx.FiatCurrency.ToLowerInvariant(),
            ["apiKey"] = provider.ApiKey ?? "",
            ["store"] = ctx.StoreName,
            ["invoiceId"] = ctx.InvoiceId,
            ["returnUrl"] = ctx.ReturnUrl,
        };
        return Placeholder.Replace(provider.UrlTemplate.Trim(), m =>
            map.TryGetValue(m.Groups[1].Value, out var v) ? Uri.EscapeDataString(v) : m.Value);
    }

    static string Append(string url, string name, string value)
        => url + (url.Contains('?') ? "&" : "?") + name + "=" + Uri.EscapeDataString(value);

    static string QueryOf(string url, bool includeQuestionMark)
    {
        var i = url.IndexOf('?');
        if (i < 0) return "";
        return includeQuestionMark ? url[i..] : url[(i + 1)..];
    }

    static byte[] Hmac(string key, string data)
        => HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data));

    static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
