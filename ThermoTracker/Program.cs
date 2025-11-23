<<<<<<< HEAD
<<<<<<< HEAD
=======
>>>>>>> 874521d (Merge pull request #10 from vyaspot/feat_8)
﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThermoTracker.ThermoTracker.Configurations;
using ThermoTracker.ThermoTracker.Data;
using ThermoTracker.ThermoTracker.Services;




try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configuration
    builder.Configuration.SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddEnvironmentVariables();

    // Services
<<<<<<< HEAD
    builder.Services.AddScoped<IDataService, DataService>();
    builder.Services.AddScoped<ISensorValidatorService, SensorValidatorService>();
=======
    builder.Services.AddSingleton<ISensorValidatorService, SensorValidatorService>();
    builder.Services.AddScoped<IDataService, DataService>();
    builder.Services.AddScoped<ISensorService, SensorService>();
>>>>>>> 874521d (Merge pull request #10 from vyaspot/feat_8)


    // Configuration sections
    builder.Services.Configure<SimulationConfig>(builder.Configuration.GetSection("SimulationConfig"));
    builder.Services.Configure<TemperatureRangeConfig>(builder.Configuration.GetSection("TemperatureRangeConfig"));


    // Database
    builder.Services.AddDbContext<SensorDbContext>((serviceProvider, options) =>
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
    });


    // Dashboard as hosted service



    // Logging
    builder.Services.AddLogging(configure =>
    {
        configure.AddConsole();
        configure.AddDebug();
        configure.AddConfiguration(builder.Configuration.GetSection("Logging"));
    });

    var host = builder.Build();

    // Initialize database
    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<SensorDbContext>();
            await context.Database.EnsureCreatedAsync();

            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Database initialized successfully at {Time}", DateTime.UtcNow);


        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while initializing the database");
            throw;
        }
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Application failed to start: {ex.Message}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
<<<<<<< HEAD
}
=======
﻿// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
>>>>>>> c9fe9ce (Project Init)
=======
}
>>>>>>> 874521d (Merge pull request #10 from vyaspot/feat_8)
