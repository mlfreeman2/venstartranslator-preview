using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

using Hangfire;
using Hangfire.Storage.SQLite;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using VenstarTranslator.Models;
using VenstarTranslator.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Environment.WebRootPath = Path.Combine(builder.Environment.ContentRootPath, "web");

var config = builder.Configuration;

// Configure HTTPS if enabled via environment variable
ConfigureHttps(builder, config);
var hangfireDatabasePath = config.GetConnectionString("Hangfire");
var sqliteOptions = new SQLiteStorageOptions();

builder.Services.AddControllers().AddNewtonsoftJson(opts => opts.SerializerSettings.Converters.Add(new StringEnumConverter()));
builder.Services.AddHealthChecks();
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

    dbContext.Database.Migrate();

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

app.MapHealthChecks("/health");
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

static void ConfigureHttps(WebApplicationBuilder builder, IConfiguration config)
{
    var httpsUrl = config.GetValue<string>("Kestrel__Endpoints__Https__Url");
    if (string.IsNullOrWhiteSpace(httpsUrl))
    {
        // HTTPS not configured, skip
        return;
    }

    var logger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("HTTPS Setup");
    logger.LogInformation("HTTPS endpoint configured: {HttpsUrl}", httpsUrl);

    var certPath = config.GetValue<string>("HTTPS_CERTIFICATE_PATH");
    var certPassword = config.GetValue<string>("HTTPS_CERTIFICATE_PASSWORD");
    X509Certificate2 certificate;

    if (!string.IsNullOrWhiteSpace(certPath) && File.Exists(certPath))
    {
        logger.LogInformation("Loading user-provided certificate from: {CertPath}", certPath);
        certificate = string.IsNullOrWhiteSpace(certPassword)
            ? X509CertificateLoader.LoadPkcs12FromFile(certPath, null)
            : X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);
    }
    else
    {
        logger.LogInformation("No user certificate provided, generating self-signed certificate");
        var selfSignedPath = Path.Combine("/data", "self-signed-cert.pfx");

        // Check if we already have a self-signed cert
        if (File.Exists(selfSignedPath))
        {
            logger.LogInformation("Loading existing self-signed certificate from: {SelfSignedPath}", selfSignedPath);
            certificate = X509CertificateLoader.LoadPkcs12FromFile(selfSignedPath, "VenstarTranslator");
        }
        else
        {
            logger.LogInformation("Generating new self-signed certificate");
            certificate = GenerateSelfSignedCertificate();

            // Save for future use
            try
            {
                var pfxBytes = certificate.Export(X509ContentType.Pfx, "VenstarTranslator");
                File.WriteAllBytes(selfSignedPath, pfxBytes);
                logger.LogInformation("Self-signed certificate saved to: {SelfSignedPath}", selfSignedPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to save self-signed certificate to {SelfSignedPath}, will regenerate on restart", selfSignedPath);
            }
        }
    }

    // Configure Kestrel to use the certificate
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.ServerCertificate = certificate;
        });
    });

    logger.LogInformation("HTTPS certificate configured successfully");
}

static X509Certificate2 GenerateSelfSignedCertificate()
{
    using var rsa = RSA.Create(2048);
    var req = new CertificateRequest(
        "CN=VenstarTranslator",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1
    );

    // Add Subject Alternative Name for localhost
    var sanBuilder = new SubjectAlternativeNameBuilder();
    sanBuilder.AddDnsName("localhost");
    sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
    sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
    req.CertificateExtensions.Add(sanBuilder.Build());

    // Add key usage
    req.CertificateExtensions.Add(
        new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: false
        )
    );

    // Add extended key usage for server authentication
    req.CertificateExtensions.Add(
        new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
            critical: false
        )
    );

    // Create self-signed certificate valid for 5 years
    var cert = req.CreateSelfSigned(
        DateTimeOffset.Now.AddDays(-1),
        DateTimeOffset.Now.AddYears(5)
    );

    // Export and re-import to ensure private key is exportable on all platforms
    var pfxBytes = cert.Export(X509ContentType.Pfx, "VenstarTranslator");
    return X509CertificateLoader.LoadPkcs12(pfxBytes, "VenstarTranslator");
}

[ExcludeFromCodeCoverage]
public partial class Program
{
    [GeneratedRegex(@"[a-f0-9]{10}")]
    private static partial Regex MacPrefixRegex();
}
