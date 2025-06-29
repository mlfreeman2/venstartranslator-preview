using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Hangfire;
using Hangfire.Storage.SQLite;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using VenstarTranslator.DB;

namespace VenstarTranslator
{
    public class Startup
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public Startup(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _config = config;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var hangfireDatabasePath = _config.GetConnectionString("Hangfire");
            var sqliteOptions = new SQLiteStorageOptions();

            services.AddControllers().AddNewtonsoftJson(opts => opts.SerializerSettings.Converters.Add(new StringEnumConverter()));
            services.AddHangfire((provider, configuration) =>
            {
                configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSQLiteStorage(hangfireDatabasePath, sqliteOptions);
            });
            services.AddHangfireServer();

            services.AddDbContext<VenstarTranslatorDataCache>(options => options.UseSqlite(_config.GetConnectionString("DataCache")));
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, VenstarTranslatorDataCache dbContext, ILogger<Startup> _logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            TranslatedVenstarSensor.macPrefix = ValidateAndGetMacPrefix(_config.GetValue<string>("FakeMacPrefix"));

            dbContext.Database.EnsureCreated();

            var sensorFilePath = _config.GetValue<string>("SensorFilePath");
            if (string.IsNullOrWhiteSpace(sensorFilePath))
            {
                throw new FileNotFoundException("Sensor JSON file path not provided.");
            }

            if (File.Exists(sensorFilePath))
            {
                var sensors = JsonConvert.DeserializeObject<List<TranslatedVenstarSensor>>(File.ReadAllText(sensorFilePath));

                if (sensors.Count > 20)
                {
                    throw new InvalidOperationException("Too many sensors specified. Only 20 sensors are supported.");
                }

                if (sensors.Count == 0)
                {
                    throw new InvalidOperationException("No sensors found in the configuration.");
                }

                // Check if at least one sensor is enabled
                if (sensors.All(s => s.Enabled == false))
                {
                    throw new InvalidOperationException("No sensors enabled in the configuration.");
                }

                // Check for duplicate names
                if (sensors.Select(s => s.Name).Distinct().Count() < sensors.Count)
                {
                    throw new InvalidOperationException("One or more sensor names appear in multiple sensor entries.");
                }
                
                ValidateIndividualSensors(sensors);
                UpdateDatabaseSensors(dbContext, sensors);

                // update sensors.json
                var dbDump = dbContext.Sensors.Include(a => a.Headers).AsNoTracking().ToList();
                File.WriteAllText(sensorFilePath, JsonConvert.SerializeObject(dbDump, Formatting.Indented, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

                foreach (var sensor in dbContext.Sensors.ToList())
                {
                    sensor.SyncHangfire();
                }
            }

            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "web")),
                RequestPath = ""
            });
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHangfireDashboard("/hangfire", new DashboardOptions
                {
                    Authorization = [],
                    DefaultRecordsPerPage = 50,
                });
            });
        }

        private static string ValidateAndGetMacPrefix(string fakeMacPrefix)
        {
            if (string.IsNullOrWhiteSpace(fakeMacPrefix))
            {
                return "428e0486d7";
            }

            if (fakeMacPrefix.Length != 10)
            {
                throw new InvalidOperationException("The prefix to use in the fake MAC addresses included in each data packet has to be exactly 10 characters long.");
            }

            fakeMacPrefix = fakeMacPrefix.ToLowerInvariant();
            if (!Regex.IsMatch(fakeMacPrefix, @"[a-f0-9]{10}"))
            {
                throw new InvalidOperationException("The prefix to use in the fake MAC addresses included in each data packet can only be numbers and lowercase a-f (in other words, hexadecimal).");
            }

            return fakeMacPrefix;
        }


        private static void ValidateIndividualSensors(List<TranslatedVenstarSensor> sensors)
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

                // Additional validation using IValidatableObject
                var additionalResults = sensor.Validate(validationContext);
                if (additionalResults.Any())
                {
                    var additionalErrors = string.Join("; ", additionalResults.Select(vr => vr.ErrorMessage));
                    throw new InvalidOperationException($"Sensor validation failed: {additionalErrors}");
                }
            }
        }

        private static void UpdateDatabaseSensors(VenstarTranslatorDataCache dbContext, List<TranslatedVenstarSensor> sensors)
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

                    if (sensors[i].Headers != null && sensors[i].Headers.Any())
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
    }
}