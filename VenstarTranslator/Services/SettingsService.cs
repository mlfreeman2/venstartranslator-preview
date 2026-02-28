using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VenstarTranslator.Models;

namespace VenstarTranslator.Services;

[ExcludeFromCodeCoverage]
public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(IConfiguration config, ILogger<SettingsService> logger)
    {
        _logger = logger;
        var sensorFilePath = config.GetValue<string>("SensorFilePath");
        var directory = Path.GetDirectoryName(sensorFilePath);
        _settingsFilePath = Path.Combine(directory ?? ".", "settings.json");
    }

    public SettingsDTO GetSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new SettingsDTO();
            }

            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SettingsDTO();
            }

            return JsonConvert.DeserializeObject<SettingsDTO>(json) ?? new SettingsDTO();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read settings from {Path}, using defaults", _settingsFilePath);
            return new SettingsDTO();
        }
    }

    public void SaveSettings(SettingsDTO settings)
    {
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        });
        File.WriteAllText(_settingsFilePath, json);
    }
}
