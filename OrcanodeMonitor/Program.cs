// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.Extensions.Hosting;
using OrcanodeMonitor.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrcanodeMonitor.Data;
using Microsoft.IdentityModel.Tokens;
using System;
using Microsoft.Azure.Cosmos;
using k8s;
using OrcanodeMonitor.Models;

var builder = WebApplication.CreateBuilder(args);

// First see if an environment variable specifies a connection string.
var connection = Environment.GetEnvironmentVariable("AZURE_COSMOS_CONNECTIONSTRING");
if (connection.IsNullOrEmpty())
{
    connection = builder.Configuration.GetConnectionString("OrcanodeMonitorContext") ?? throw new InvalidOperationException("Connection string 'OrcanodeMonitorContext' not found.");
}

string isReadOnly = Environment.GetEnvironmentVariable("ORCANODE_MONITOR_READONLY") ?? "false";
if (isReadOnly == "true")
{
    Fetcher.IsReadOnly = true;
}

string isOffline = Environment.GetEnvironmentVariable("ORCANODE_MONITOR_OFFLINE") ?? "false";
if (isOffline == "true")
{
    Fetcher.IsOffline = true;
}

string databaseName = Environment.GetEnvironmentVariable("AZURE_COSMOS_DATABASENAME") ?? "orcasound-cosmosdb";

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
    options.UseCosmos(
        connection,
        databaseName: databaseName,
        options =>
        { options.ConnectionMode(ConnectionMode.Gateway); }));

// Register Kubernetes client
builder.Services.AddSingleton<IKubernetes>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    return OrcaHelloFetcher.CreateK8sClient(logger);
});

// Register OrcaHelloFetcher
builder.Services.AddSingleton<OrcaHelloFetcher>();

builder.Services.AddHostedService<PeriodicTasks>(); // Register your background service
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}

// Create the database if it doesn't exist.
// See https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/intro?view=aspnetcore-8.0&tabs=visual-studio
// for walkthrough.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var context = services.GetRequiredService<OrcanodeMonitorContext>();

    // Seed sample data for offline mode
    if (Fetcher.IsOffline)
    {
        context.Database.EnsureCreated();

        if (!context.Orcanodes.Any())
        {
            var orcanodes = new Orcanode[]
            {
                new Orcanode
                {
                    ID = Guid.NewGuid().ToString(),
                    PartitionValue = 1,
                    OrcasoundName = "Orcasound Lab",
                    S3NodeName = "rpi_orcasound_lab",
                    S3Bucket = "streaming-orcasound-net",
                    OrcasoundHost = "live.orcasound.net",   
                    OrcasoundSlug = "orcasound-lab",
                    OrcasoundVisible = true,
                    DataplicityName = "rpi_orcasound_lab",
                    DataplicityOnline = true,
                    LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-1),
                    ManifestUpdatedUtc = DateTime.UtcNow.AddSeconds(-30)
                },
                new Orcanode
                {
                    ID = Guid.NewGuid().ToString(),
                    PartitionValue = 1,
                    OrcasoundName = "Port Townsend",
                    S3NodeName = "rpi_port_townsend",
                    S3Bucket = "streaming-orcasound-net",
                    OrcasoundHost = "live.orcasound.net",
                    OrcasoundSlug = "port-townsend",
                    OrcasoundVisible = true,
                    DataplicityName = "rpi_port_townsend",
                    DataplicityOnline = true,
                    LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-1),
                    ManifestUpdatedUtc = DateTime.UtcNow.AddSeconds(-30)
                },
                new Orcanode
                {
                    ID = Guid.NewGuid().ToString(),
                    PartitionValue = 1,
                    OrcasoundName = "Bush Point",
                    S3NodeName = "rpi_bush_point",
                    S3Bucket = "streaming-orcasound-net",
                    OrcasoundHost = "live.orcasound.net",
                    OrcasoundSlug = "bush-point",
                    OrcasoundVisible = true,
                    DataplicityName = "rpi_bush_point",
                    DataplicityOnline = true,
                    LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-1),
                    ManifestUpdatedUtc = DateTime.UtcNow.AddSeconds(-30)
                }
            };

            context.Orcanodes.AddRange(orcanodes);
            context.SaveChanges();
        }
    }
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();
