#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.FiatRamp.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.FiatRamp.Controllers;

[Area(Plugin.Area)]
public class FiatRampController(
    FiatRampSettingsService settingsService,
    ProviderUrlBuilder urlBuilder,
    InvoiceRepository invoiceRepository,
    StoreRepository storeRepository) : Controller
{
    static readonly PaymentMethodId OnChain = PaymentMethodId.Parse("BTC-CHAIN");

    // ---------------------------------------------------------------
    // Store settings page
    // ---------------------------------------------------------------

    [HttpGet("~/stores/{storeId}/plugins/fiatramp")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Settings(string storeId)
    {
        var settings = await settingsService.Get(storeId);
        settings.Providers = settings.Providers.OrderBy(p => p.Order).ToList();
        return View("Settings", settings);
    }

    [HttpPost("~/stores/{storeId}/plugins/fiatramp")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Settings(string storeId, FiatRampSettings vm, string? command)
    {
        if (command == "reset-providers")
        {
            var current = await settingsService.Get(storeId);
            // Keep the keys the merchant already typed for providers with the same id
            var defaults = FiatRampSettings.DefaultProviders();
            foreach (var d in defaults)
            {
                var existing = current.Providers.FirstOrDefault(p => p.Id == d.Id);
                if (existing is null) continue;
                d.ApiKey = existing.ApiKey;
                d.Secret = existing.Secret;
                d.Enabled = existing.Enabled;
            }
            current.Providers = defaults;
            await settingsService.Set(storeId, current);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = "Provider list reset to defaults. Your API keys were kept."
            });
            return RedirectToAction(nameof(Settings), new { storeId });
        }

        vm.Providers ??= new();
        vm.Providers = vm.Providers
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) || !string.IsNullOrWhiteSpace(p.UrlTemplate))
            .Select((p, i) =>
            {
                p.Name = p.Name?.Trim() ?? "";
                p.UrlTemplate = p.UrlTemplate?.Trim() ?? "";
                p.ApiKey = Blank(p.ApiKey);
                p.Secret = Blank(p.Secret);
                p.Note = Blank(p.Note);
                p.Guide = Blank(p.Guide?.Replace("\r\n", "\n"));
                p.GuideImageUrl = Blank(p.GuideImageUrl);
                p.Id = Slug(string.IsNullOrWhiteSpace(p.Id) ? p.Name : p.Id);
                if (string.IsNullOrWhiteSpace(p.Id)) p.Id = "provider-" + i;
                p.Methods ??= new();
                p.Regions ??= new();
                return p;
            })
            .OrderBy(p => p.Order)
            .ToList();

        // Ids must be unique: they appear in URLs
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var p in vm.Providers)
        {
            var baseId = p.Id;
            var n = 2;
            while (!seen.Add(p.Id)) p.Id = $"{baseId}-{n++}";
        }

        foreach (var p in vm.Providers)
        {
            if (p.Enabled && !p.IsUsable)
                ModelState.AddModelError("", $"\"{p.Name}\" is enabled but is missing its {(p.RequiresApiKey && string.IsNullOrWhiteSpace(p.ApiKey) ? "API key" : "secret")}.");
            if (!string.IsNullOrWhiteSpace(p.UrlTemplate))
            {
                var probe = p.UrlTemplate.Replace("{apiKey}", "key");
                if (!Uri.TryCreate(probe, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                    ModelState.AddModelError("", $"\"{p.Name}\": the URL must be an absolute https:// URL.");
            }
            if (p.GuideImageUrl is not null && (!Uri.TryCreate(p.GuideImageUrl, UriKind.Absolute, out var img) || img.Scheme != Uri.UriSchemeHttps))
                ModelState.AddModelError("", $"\"{p.Name}\": the screenshot must be an absolute https:// image URL.");
        }
        if (vm.BufferPercent < 0 || vm.BufferPercent > 50)
            ModelState.AddModelError(nameof(vm.BufferPercent), "Buffer must be between 0 and 50 percent.");
        if (vm.GeoLookupEnabled && !Uri.TryCreate(vm.GeoLookupUrl?.Replace("{ip}", "1.1.1.1"), UriKind.Absolute, out _))
            ModelState.AddModelError(nameof(vm.GeoLookupUrl), "The geo lookup URL must be an absolute URL containing {ip}.");

        if (!ModelState.IsValid)
            return View("Settings", vm);

        await settingsService.Set(storeId, vm);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "Fiat on-ramp settings saved."
        });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    // ---------------------------------------------------------------
    // Checkout → provider redirect. Builds and signs the provider URL
    // server-side so secrets never reach the browser.
    // ---------------------------------------------------------------

    [HttpGet("~/plugins/fiatramp/go/{invoiceId}/{providerId}")]
    [AllowAnonymous]
    public async Task<IActionResult> Go(string invoiceId, string providerId)
    {
        var invoice = await invoiceRepository.GetInvoice(invoiceId);
        if (invoice is null) return NotFound();

        var settings = await settingsService.Get(invoice.StoreId);
        if (!settings.Enabled) return NotFound();
        var provider = settings.Providers.FirstOrDefault(p => p.Id == providerId && p.IsUsable);
        if (provider is null) return NotFound();

        var prompt = invoice.GetPaymentPrompt(OnChain);
        if (string.IsNullOrEmpty(prompt?.Destination)) return NotFound();

        var accounting = prompt.Calculate();
        var dueBtc = accounting.Due;
        decimal fiatBuy = 0;
        var currency = invoice.Currency ?? "";
        if (!IsCrypto(currency))
        {
            decimal rate;
            try { rate = prompt.Rate; } catch (InvalidOperationException) { rate = 0; }
            var fiatDue = dueBtc * rate;
            fiatBuy = Math.Round(fiatDue * (1 + settings.BufferPercent / 100m), 2, MidpointRounding.AwayFromZero);
        }

        var store = await storeRepository.FindStore(invoice.StoreId);
        var returnUrl = Url.Action("Checkout", "UIInvoice", new { area = "", invoiceId }, Request.Scheme) ?? "";
        var ctx = new ProviderUrlContext(
            prompt.Destination, dueBtc, fiatBuy, currency, store?.StoreName ?? "", invoiceId, returnUrl);

        return Redirect(urlBuilder.Build(provider, ctx));
    }

    static bool IsCrypto(string currency)
        => currency.Equals("BTC", StringComparison.OrdinalIgnoreCase) || currency.Equals("SATS", StringComparison.OrdinalIgnoreCase);

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static string Slug(string s)
        => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
}
