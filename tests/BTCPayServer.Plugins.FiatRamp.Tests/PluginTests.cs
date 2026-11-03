using System;
using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Plugins.FiatRamp.Services;

namespace BTCPayServer.Plugins.FiatRamp.Tests;

public class PluginTests
{
    static readonly ProviderUrlContext Ctx = new(
        Address: "bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh",
        BtcAmount: 0.0004459m,
        FiatAmount: 35.02m,
        FiatCurrency: "usd",
        StoreName: "pct mart",
        InvoiceId: "inv123",
        ReturnUrl: "https://pay.example.com/i/inv123");

    [Fact]
    public void Expands_and_encodes_placeholders()
    {
        var p = new ProviderConfig
        {
            UrlTemplate = "https://x.test/?a={address}&f={fiat}&c={currency}&cl={currency_lower}&b={btc}&k={apiKey}&s={store}&r={returnUrl}&keep={unknown}",
            ApiKey = "pk_test"
        };
        var url = ProviderUrlBuilder.Expand(p, Ctx);
        Assert.Equal(
            "https://x.test/?a=bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh&f=35.02&c=USD&cl=usd&b=0.0004459&k=pk_test&s=pct%20mart&r=https%3A%2F%2Fpay.example.com%2Fi%2Finv123&keep={unknown}",
            url);
    }

    [Fact]
    public void MoonPay_signature_signs_query_including_question_mark()
    {
        var p = new ProviderConfig
        {
            UrlTemplate = "https://buy.moonpay.com/?apiKey={apiKey}&walletAddress={address}",
            ApiKey = "pk_test",
            Secret = "sk_test",
            Signing = UrlSigning.MoonPay
        };
        var url = new ProviderUrlBuilder().Build(p, Ctx);
        var unsigned = "https://buy.moonpay.com/?apiKey=pk_test&walletAddress=bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh";
        var expected = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes("sk_test"), Encoding.UTF8.GetBytes("?apiKey=pk_test&walletAddress=bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh")));
        Assert.Equal(unsigned + "&signature=" + Uri.EscapeDataString(expected), url);
    }

    [Fact]
    public void Mercuryo_signature_is_sha512_of_address_and_secret()
    {
        var p = new ProviderConfig
        {
            UrlTemplate = "https://exchange.mercuryo.io/?widget_id={apiKey}&address={address}",
            ApiKey = "w1",
            Secret = "s3cret",
            Signing = UrlSigning.Mercuryo
        };
        var url = new ProviderUrlBuilder().Build(p, Ctx);
        var expected = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(Ctx.Address + "s3cret"))).ToLowerInvariant();
        Assert.EndsWith("&signature=" + expected, url);
    }

    [Theory]
    [InlineData("DE", Region.EU)]
    [InlineData("fr", Region.EU)]
    [InlineData("GB", Region.EU)]
    [InlineData("CH", Region.EU)]
    [InlineData("US", Region.US)]
    [InlineData("CA", Region.Other)]
    [InlineData("IL", Region.Other)]
    [InlineData(null, Region.Other)]
    public void Maps_country_to_region(string? country, Region expected)
    {
        Assert.Equal(expected, GeoRegionService.FromCountry(country));
    }

    [Fact]
    public void Provider_usability_requires_key_and_secret_when_needed()
    {
        var p = new ProviderConfig { Enabled = true, UrlTemplate = "https://x.test/?k={apiKey}", Signing = UrlSigning.MoonPay };
        Assert.False(p.IsUsable);
        p.ApiKey = "k";
        Assert.False(p.IsUsable);
        p.Secret = "s";
        Assert.True(p.IsUsable);
        p.Enabled = false;
        Assert.False(p.IsUsable);
    }

    [Fact]
    public void Default_settings_cover_every_method_and_region()
    {
        var s = FiatRampSettings.CreateDefault();
        foreach (var m in Enum.GetValues<PayMethod>())
            Assert.Contains(s.Providers, p => p.Methods.Contains(m));
        foreach (var r in Enum.GetValues<Region>())
            Assert.Contains(s.Providers, p => p.Regions.Contains(r));
        Assert.Equal(s.Providers.Count, s.Providers.Select(p => p.Id).Distinct().Count());
    }
}
