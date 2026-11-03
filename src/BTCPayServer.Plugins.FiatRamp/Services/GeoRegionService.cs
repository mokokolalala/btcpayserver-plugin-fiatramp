#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.FiatRamp.Services;

public record GeoResult(Region? Region, string? Country);

public class GeoRegionService(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<GeoRegionService> logger)
{
    public const string HttpClientName = "fiatramp-geo";

    // EU-27 + EEA + UK + Switzerland: the countries where "EU" ramps and SEPA usually apply.
    static readonly HashSet<string> EuCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV", "LT", "LU",
        "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
        "IS", "LI", "NO", "GB", "CH"
    };

    static readonly Regex CountryCode = new(@"^[A-Za-z]{2}$", RegexOptions.Compiled);
    static readonly Regex JsonCountry = new(@"""(?:country_code|countryCode|country)""\s*:\s*""([A-Za-z]{2})""", RegexOptions.Compiled);

    public static Region FromCountry(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return Region.Other;
        if (EuCountries.Contains(countryCode)) return Region.EU;
        return countryCode.Equals("US", StringComparison.OrdinalIgnoreCase) ? Region.US : Region.Other;
    }

    public static Region? ParseRegion(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "eu" or "europe" => Region.EU,
        "us" or "usa" => Region.US,
        "other" or "row" => Region.Other,
        _ => null
    };

    public async Task<GeoResult> LookupAsync(IPAddress? ip, FiatRampSettings settings, CancellationToken ct)
    {
        if (!settings.GeoLookupEnabled || ip is null || string.IsNullOrWhiteSpace(settings.GeoLookupUrl))
            return new GeoResult(null, null);
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IsPrivate(ip))
            return new GeoResult(null, null);

        var key = "fiatramp-geo-" + ip;
        if (cache.TryGetValue(key, out GeoResult? cached) && cached is not null)
            return cached;

        var result = new GeoResult(null, null);
        try
        {
            var url = settings.GeoLookupUrl.Replace("{ip}", Uri.EscapeDataString(ip.ToString()));
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
                string? country = null;
                if (CountryCode.IsMatch(body))
                    country = body.ToUpperInvariant();
                else if (JsonCountry.Match(body) is { Success: true } m)
                    country = m.Groups[1].Value.ToUpperInvariant();
                if (country is not null)
                    result = new GeoResult(FromCountry(country), country);
            }
            else
            {
                logger.LogWarning("FiatRamp geo lookup returned {Status} for {Url}", (int)response.StatusCode, settings.GeoLookupUrl);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "FiatRamp geo lookup failed");
        }

        // Cache hits for a day, misses for 10 minutes so a flaky lookup service recovers.
        cache.Set(key, result, result.Region is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(24));
        return result;
    }

    static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254)
                   || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || (b[0] & 0xfe) == 0xfc;
    }
}
