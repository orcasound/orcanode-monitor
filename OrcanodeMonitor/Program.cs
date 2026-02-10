// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

HttpClient? httpClient = null;
ILoggerFactory? loggerFactory = null;

string isOffline = builder.Configuration["ORCANODE_MONITOR_OFFLINE"] ?? "false";
OrcasiteTestHelper.MockOrcasiteHelperContainer? container = null;
if (isOffline == "true")
{
    Fetcher.IsOffline = true;
    loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var logger = loggerFactory.CreateLogger<Program>();
    container = OrcasiteTestHelper.GetMockOrcasiteHelperWithRequestVerification(logger);
    httpClient = container.MockHttp.ToHttpClient();
}

string isReadOnly = builder.Configuration["ORCANODE_MONITOR_READONLY"] ?? "false";
if (isReadOnly == "true")
{
    Fetcher.IsReadOnly = true;
}

Fetcher.Initialize(builder.Configuration, httpClient);

// Add services to the container.
builder.Services.AddRazorPages();
if (Fetcher.IsOffline) // Use Test data with in-memory database.
{
    builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
        options.UseInMemoryDatabase("OrcanodeMonitorOffline")
    );
}
else // Use Cosmos DB.
{
    // First try to get the connection string from configuration using the AZURE_COSMOS_CONNECTIONSTRING key
    // (e.g., from environment variables, user secrets, or JSON configuration).
    var connection = builder.Configuration["AZURE_COSMOS_CONNECTIONSTRING"];
    if (connection.IsNullOrEmpty())
    {
        connection = builder.Configuration.GetConnectionString("OrcanodeMonitorContext") ?? throw new InvalidOperationException("Connection string 'OrcanodeMonitorContext' not found.");
    }

    string databaseName = builder.Configuration["AZURE_COSMOS_DATABASENAME"] ?? "orcasound-cosmosdb";
    builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
        options.UseCosmos(
            connection,
            databaseName: databaseName,
            options =>
            { options.ConnectionMode(ConnectionMode.Gateway); }));
}
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
            var orcanodes = new List<Orcanode>
            {
                new Orcanode
                {
                    ID = "andrews-bay",
                    PartitionValue = 1,
                    OrcasoundName = "Andrews Bay",
                    OrcasoundSlug = "andrews-bay",
                    DataplicityOnline = true,
                    OrcasoundVisible = true,
                    LatestUploadedUtc = DateTime.UtcNow.AddMinutes(-1),
                    AudioStreamStatus = OrcanodeOnlineStatus.Online,
                    OrcasoundHost = "live.orcasound.net"
                },
                new Orcanode
                {
                    ID = "orcasound-lab",
                    PartitionValue = 1,
                    OrcasoundName = "Orcasound Lab",
                    OrcasoundSlug = "orcasound-lab",
                    DataplicityOnline = true,
                    OrcasoundVisible = true,
                    LatestUploadedUtc = DateTime.UtcNow.AddMinutes(-1),
                    AudioStreamStatus = OrcanodeOnlineStatus.Online,
                    OrcasoundHost = "live.orcasound.net"
                },
                new Orcanode
                {
                    ID = "port-townsend",
                    PartitionValue = 1,
                    OrcasoundName = "Port Townsend",
                    OrcasoundSlug = "port-townsend",
                    DataplicityOnline = true,
                    OrcasoundVisible = true,
                    LatestUploadedUtc = DateTime.UtcNow.AddMinutes(-1),
                    AudioStreamStatus = OrcanodeOnlineStatus.Online,
                    OrcasoundHost = "live.orcasound.net"
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
