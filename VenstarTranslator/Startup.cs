using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.Extensions.FileProviders;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using VenstarTranslator.DB;
using Newtonsoft.Json.Converters;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

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

            var sensorIdOffset = _config.GetValue<int>("SensorIdOffset");
            if (sensorIdOffset < 0)
            {
                throw new InvalidOperationException("The sensor ID offset is too low. It can not be below zero.");
            }

            if (sensorIdOffset > 18)
            {
                throw new InvalidOperationException("The sensor ID offset is too high. It can not be above 18. That would leave no IDs open for actual sensors.");
            }

            var fakeMacPrefix = _config.GetValue<string>("FakeMacPrefix");

            if (string.IsNullOrWhiteSpace(fakeMacPrefix))
            {
                fakeMacPrefix = "428e0486d7";
            }
            else
            {
                if (fakeMacPrefix.Length != 10)
                {
                    throw new InvalidOperationException("The prefix to use in the fake MAC addresses included in each data packet has to be exactly 10 characters long.");
                }
                fakeMacPrefix = fakeMacPrefix.ToLowerInvariant();
                if (!Regex.IsMatch(fakeMacPrefix, @"[a-f0-9]{10}"))
                {
                    throw new InvalidOperationException("The prefix to use in the fake MAC addresses included in each data packet can only be numbers and lowercase a-f (in other words, hexadecimal).");
                }
            }

            TranslatedVenstarSensor.macPrefix = fakeMacPrefix;

            var sensorFilePath = _config.GetValue<string>("SensorFilePath");

            if (string.IsNullOrWhiteSpace(sensorFilePath))
            {
                throw new FileNotFoundException($"Sensor JSON file path not provided.");
            }

            if (!File.Exists(sensorFilePath))
            {
                throw new FileNotFoundException($"The sensor JSON file was supposed to be at '{sensorFilePath}' but could not be found.");
            }

            var sensors = JsonConvert.DeserializeObject<List<TranslatedVenstarSensor>>(File.ReadAllText(sensorFilePath));

            if (sensorIdOffset + sensors.Count > 20)
            {
                throw new InvalidOperationException($"The sensor ID offset is too high. (sensor id offset: {sensorIdOffset}) + (number of sensors: {sensors.Count}) must be less than or equal to 20 (it was {sensorIdOffset + sensors.Count}).");
            }

            if (sensors.Count > 20)
            {
                throw new InvalidOperationException("There are too many sensors specified. Only 20 sensors are supported.");
            }

            if (sensors.Count == 0)
            {
                throw new InvalidOperationException("No sensors found in the config file.");
            }

            if (sensors.All(a => a.Enabled == false))
            {
                throw new InvalidOperationException("No sensors enabled in the config file, not starting up further.");
            }

            if (sensors.Select(a => a.Name).Distinct().Count() < sensors.Count)
            {
                throw new InvalidOperationException("One or more of the sensor names was included in multiple sensor entries.");
            }

            dbContext.Database.EnsureCreated();

            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "web")),
                RequestPath = "/ui"
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

            var rjo = new RecurringJobOptions() { TimeZone = TimeZoneInfo.Local };

            for (int i = 0; i < sensors.Count; i++)
            {
                sensors[i].SensorID = Convert.ToByte(i + sensorIdOffset);

                if (string.IsNullOrWhiteSpace(sensors[i].Name))
                {
                    throw new InvalidOperationException($"The name specified for sensor #{sensors[i].SensorID} was null, blank, or white space.");
                }

                if (sensors[i].Name.Length > 14)
                {
                    throw new InvalidOperationException($"The name specified for sensor #{sensors[i].SensorID} is too long. The limit is 14 characters.");
                }

                if (string.IsNullOrWhiteSpace(sensors[i].URL))
                {
                    throw new InvalidOperationException($"The URL provided for sensor #{sensors[i].SensorID} was null or whitespace.");
                }

                if (!Uri.IsWellFormedUriString(sensors[i].URL, UriKind.Absolute))
                {
                    throw new InvalidOperationException($"The URL provided for sensor #{sensors[i].SensorID} was not a properly formed URL.");
                }

                if (sensors[i].Headers != null && sensors[i].Headers.Count > 0)
                {
                    if (sensors[i].Headers.Any(a => string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Value)))
                    {
                        throw new InvalidOperationException($"The HTTP headers block for sensor #{sensors[i].SensorID} contains an entry with null, blank, or white space.");
                    }

                    if (sensors[i].Headers.Select(a => a.Name).Distinct().Count() < sensors[i].Headers.Count)
                    {
                        throw new InvalidOperationException($"The HTTP headers block for sensor #{sensors[i].SensorID} contains a repeated header name. The .NET runtime will complain if the 'Authorization' header is repeated, so this in general is not supported.");
                    }
                }

                // test the JSONPath against an empty shell to check it for syntax errors.
                try
                {
                    JObject obj = [];
                    obj.SelectToken(sensors[i].JSONPath);
                }
                catch (JsonException e)
                {
                    if (e.Message == "Unexpected character while parsing path query: \"")
                    {
                        throw new InvalidOperationException($"Sensor #{sensors[i].SensorID} had a syntax error in its JSONPath. Replace double quotes \" with single quotes '.");
                    }
                    var message = $"Sensor #{sensors[i].SensorID} had a syntax error in its JSONPath: '{e.Message}'";
                    throw new InvalidOperationException(message);
                }

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

            foreach (var newSensor in dbContext.Sensors.ToList())
            {
                if (!sensors.Any(a => a.Name == newSensor.Name && a.SensorID == newSensor.SensorID))
                {
                    dbContext.Sensors.Remove(newSensor);
                }
            }
            dbContext.SaveChanges();

            foreach (var sensor in dbContext.Sensors.ToList())
            {
                if (!sensor.Enabled)
                {
                    continue;
                }

                var cronString = sensor.Purpose != SensorPurpose.Outdoor ? "* * * * *" : "*/5 * * * *";
                RecurringJob.AddOrUpdate<Tasks>($"'{sensorFilePath}' - Sensor #{sensor.SensorID}: {sensor.Name}", a => a.SendDataPacket(sensor.SensorID), cronString, rjo);
            }
        }
    }
}
