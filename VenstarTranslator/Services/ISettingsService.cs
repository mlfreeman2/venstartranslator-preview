using VenstarTranslator.Models;

namespace VenstarTranslator.Services;

public interface ISettingsService
{
    SettingsDTO GetSettings();
    void SaveSettings(SettingsDTO settings);
}
