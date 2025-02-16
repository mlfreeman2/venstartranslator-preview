using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace VenstarTranslator.DB
{
    public class VenstarTranslatorDataCache : DbContext
    {
        public DbSet<TranslatedVenstarSensor> Sensors { get; set; }

        public VenstarTranslatorDataCache() { }

        public VenstarTranslatorDataCache(DbContextOptions<VenstarTranslatorDataCache> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<TranslatedVenstarSensor>()
                .Property(e => e.Purpose)
                .HasConversion(v => v.ToString(), v => (SensorPurpose)Enum.Parse(typeof(SensorPurpose), v));
            modelBuilder
                .Entity<TranslatedVenstarSensor>()
                .Property(e => e.Scale)
                .HasConversion(v => v.ToString(), v => (TemperatureScale)Enum.Parse(typeof(TemperatureScale), v));
            modelBuilder
                .Entity<TranslatedVenstarSensor>()
                .OwnsMany(a => a.Headers);

        }
    }

    public class TranslatedVenstarSensor 
    {
        public static string macPrefix = "";

        [JsonProperty(Order = 1)]
        [Key]
        [Required(AllowEmptyStrings=false)]
        [Range(0, 19, MinimumIsExclusive = false, MaximumIsExclusive = false)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public uint SensorID { get; set; }

        [JsonProperty(Order = 2)]
        [MaxLength(14)]
        [Required(AllowEmptyStrings=false)]
        public string Name { get; set; }

        [JsonProperty(Order = 3)]
        public bool Enabled { get; set; }

        [JsonIgnore]
        public uint Sequence { get; set; }

        [JsonProperty(Order = 4)]
        public string MacAddress => (macPrefix + SensorID.ToString("X2")).ToLower();

        [JsonProperty(Order = 5)]
        public string Signature_Key { 
            get 
            {
                using (var sha256 = SHA256.Create()) 
                {
                    var bytes = Encoding.UTF8.GetBytes(MacAddress);
                    return Convert.ToBase64String(sha256.ComputeHash(bytes));
                }
            }
        } 

        [JsonProperty(Order = 6)]
        [Required(AllowEmptyStrings=false)]
        public SensorPurpose Purpose { get; set; }

        [JsonProperty(Order = 7)]
        [Required(AllowEmptyStrings=false)]
        public TemperatureScale Scale { get; set; }

        [JsonProperty(Order = 8)]
        [Required(AllowEmptyStrings=false)]
        [Url]
        public string URL { get; set; }

        [JsonProperty(Order = 9)]
        public bool IgnoreSSLErrors { get; set; }

        [JsonProperty(Order = 10)]
        [Required(AllowEmptyStrings=false)]
        public string JSONPath { get; set; }

        [JsonProperty(Order = 11)]
        public List<DataSourceHttpHeader> Headers { get; set; }
    }

    [Owned]
    public class DataSourceHttpHeader 
    {
        [JsonIgnore]
        [Key]
        public int ID { get; set; }

        [JsonProperty(Order = 1)]
        [Required(AllowEmptyStrings=false)]
        public string Name { get; set; }

        [JsonProperty(Order = 2)]
        [Required(AllowEmptyStrings=false)]
        public string Value { get; set;}
    }

    public enum SensorPurpose
    {
        Outdoor = 1,
        
        Return = 2,
        
        Remote = 3,
        
        Supply = 4,
    }

    public enum TemperatureScale 
    { 
        F,

        C
    }


}