using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Hangfire;
using Hangfire.Storage.SQLite;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using VenstarTranslator.Models;
using VenstarTranslator.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Environment.WebRootPath = Path.Combine(builder.Environment.ContentRootPath, "web");

var config = builder.Configuration;
var hangfireDatabasePath = config.GetConnectionString("Hangfire");
var sqliteOptions = new SQLiteStorageOptions();

builder.Services.AddControllers().AddNewtonsoftJson(opts => opts.SerializerSettings.Converters.Add(new StringEnumConverter()));
builder.Services.AddHangfire((provider, configuration) =>
{
    configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(hangfireDatabasePath, sqliteOptions);
});
builder.Services.AddHangfireServer();

builder.Services.AddDbContext<VenstarTranslatorDataCache>(options => options.UseSqlite(config.GetConnectionString("DataCache")));

// Register sensor dependencies
builder.Services.AddSingleton<IHttpDocumentFetcher, HttpDocumentFetcher>();
builder.Services.AddSingleton<IUdpBroadcaster, UdpBroadcaster>();
builder.Services.AddSingleton<ISensorOperations, SensorOperations>();
builder.Services.AddSingleton<IHangfireJobManager, HangfireJobManager>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

TranslatedVenstarSensor.macPrefix = ValidateAndGetMacPrefix(config.GetValue<string>("FakeMacPrefix"));

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<VenstarTranslatorDataCache>();
    var jobManager = scope.ServiceProvider.GetRequiredService<IHangfireJobManager>();

    dbContext.Database.EnsureCreated();

    var sensorFilePath = config.GetValue<string>("SensorFilePath");
    if (string.IsNullOrWhiteSpace(sensorFilePath))
    {
        throw new FileNotFoundException("Sensor JSON file path not provided.");
    }

    if (File.Exists(sensorFilePath))
    {
        var contents = File.ReadAllText(sensorFilePath);
        if (!string.IsNullOrWhiteSpace(contents))
        {
            var sensors = JsonConvert.DeserializeObject<List<TranslatedVenstarSensor>>(contents);

            if (sensors.Count > 20)
            {
                throw new InvalidOperationException("Too many sensors specified. Only 20 sensors are supported.");
            }

            // Check for duplicate names
            if (sensors.Select(s => s.Name).Distinct().Count() < sensors.Count)
            {
                throw new InvalidOperationException("One or more sensor names appear in multiple sensor entries.");
            }

            ValidateIndividualSensors(sensors);
            UpdateDatabaseSensors(dbContext, sensors);

            // update sensors.json
            SensorOperations.SyncToJsonFile(config, dbContext);

            foreach (var sensor in dbContext.Sensors.ToList())
            {
                if (sensor.Enabled)
                {
                    jobManager.AddOrUpdateRecurringJob(sensor.HangfireJobName, sensor.CronExpression, sensor.SensorID);
                }
            }
        }
    }
}

app.UseFileServer(new FileServerOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "web")),
    RequestPath = ""
});
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [],
    DefaultRecordsPerPage = 50,
});

app.Run();

static string ValidateAndGetMacPrefix(string fakeMacPrefix)
{
    if (string.IsNullOrWhiteSpace(fakeMacPrefix))
    {
        return "428e0486d8";
    }

    if (fakeMacPrefix.Length != 10)
    {
        throw new InvalidOperationException("The prefix to use in the fake MAC addresses included in each data packet has to be exactly 10 characters long.");
    }

    fakeMacPrefix = fakeMacPrefix.ToLowerInvariant();
    if (!MacPrefixRegex().IsMatch(fakeMacPrefix))
    {
        throw new InvalidOperationException("The prefix to use in the fake MAC addresses included in each data packet can only be numbers and lowercase a-f (in other words, hexadecimal).");
    }

    return fakeMacPrefix;
}

static void ValidateIndividualSensors(List<TranslatedVenstarSensor> sensors)
{
    foreach (var sensor in sensors)
    {
        var validationContext = new ValidationContext(sensor);
        var validationResults = new List<ValidationResult>();

        if (!Validator.TryValidateObject(sensor, validationContext, validationResults, true))
        {
            var errors = string.Join("; ", validationResults.Select(vr => vr.ErrorMessage));
            throw new InvalidOperationException($"Sensor validation failed: {errors}");
        }
    }
}

static void UpdateDatabaseSensors(VenstarTranslatorDataCache dbContext, List<TranslatedVenstarSensor> sensors)
{
    for (int i = 0; i < sensors.Count; i++)
    {
        sensors[i].SensorID = Convert.ToByte(i);

        if (!dbContext.Sensors.Any(a => a.SensorID == sensors[i].SensorID))
        {
            dbContext.Sensors.Add(sensors[i]);
            dbContext.SaveChanges();
        }
        else
        {
            var current = dbContext.Sensors.Include(a => a.Headers).Single(a => a.SensorID == sensors[i].SensorID);
            current.Name = sensors[i].Name;
            current.Enabled = sensors[i].Enabled;
            current.URL = sensors[i].URL;
            current.Purpose = sensors[i].Purpose;
            current.JSONPath = sensors[i].JSONPath;
            current.Scale = sensors[i].Scale;
            current.IgnoreSSLErrors = sensors[i].IgnoreSSLErrors;

            current.Headers.Clear();
            dbContext.SaveChanges();

            if (sensors[i].Headers != null && sensors[i].Headers.Count > 0)
            {
                current.Headers.AddRange(sensors[i].Headers);
            }
            dbContext.SaveChanges();
        }
    }

    // Remove sensors that are no longer in the configuration
    foreach (var newSensor in dbContext.Sensors.ToList())
    {
        if (!sensors.Any(a => a.Name == newSensor.Name && a.SensorID == newSensor.SensorID))
        {
            dbContext.Sensors.Remove(newSensor);
        }
    }
    dbContext.SaveChanges();
}

[ExcludeFromCodeCoverage]
public partial class Program
{
    [GeneratedRegex(@"[a-f0-9]{10}")]
    private static partial Regex MacPrefixRegex();
}
