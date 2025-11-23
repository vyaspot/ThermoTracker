using Microsoft.EntityFrameworkCore;
using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Data;

public class SensorDbContext(DbContextOptions<SensorDbContext> options) : DbContext(options)
{

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SensorData>(entity =>
       {
           // Use a proper primary key that allows multiple readings per sensor
           entity.HasKey(e => new { e.Id });
       });
    }
    public DbSet<SensorData> SensorData { get; set; }

}