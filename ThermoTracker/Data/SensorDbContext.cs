using Microsoft.EntityFrameworkCore;
using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Data;

public class SensorDbContext(DbContextOptions<SensorDbContext> options) : DbContext(options)
{
    public DbSet<SensorData> SensorData { get; set; }

}