using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.FiatRamp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.FiatRamp;

public class Plugin : BaseBTCPayServerPlugin
{
    public const string Area = "FiatRamp";
    public const string ViewsDirectory = "/Plugins/" + Area + "/Views";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.4.5" }
    };

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<FiatRampSettingsService>();
        services.AddSingleton<GeoRegionService>();
        services.AddSingleton<ProviderUrlBuilder>();
        services.AddHttpClient(GeoRegionService.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(2.5));

        // Store settings page, listed in the store's "Plugins" navigation section
        services.AddUIExtension("store-integrations-nav", $"{ViewsDirectory}/NavExtension.cshtml");

        // Checkout: registers the Vue component and mounts it under the on-chain address (see CheckoutEnd.cshtml
        // for why the mount is done from script rather than the checkout-bitcoin-post-content extension point).
        services.AddUIExtension("checkout-end", $"{ViewsDirectory}/CheckoutEnd.cshtml");
    }
}
