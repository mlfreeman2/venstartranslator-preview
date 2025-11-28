using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using VenstarTranslator.Models.Enums;

namespace VenstarTranslator.Models.Db;

[ExcludeFromCodeCoverage]
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
