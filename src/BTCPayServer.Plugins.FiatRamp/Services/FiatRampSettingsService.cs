#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.FiatRamp.Services;

public class FiatRampSettingsService(StoreRepository storeRepository)
{
    public async Task<FiatRampSettings> Get(string storeId)
        => await storeRepository.GetSettingAsync<FiatRampSettings>(storeId, FiatRampSettings.SettingName)
           ?? FiatRampSettings.CreateDefault();

    public Task Set(string storeId, FiatRampSettings settings)
        => storeRepository.UpdateSetting(storeId, FiatRampSettings.SettingName, settings);
}
