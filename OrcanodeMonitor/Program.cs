// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s;
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

// Fetcher.GetConfig is only available after Fetcher.Initialize is called,
// which we have to call after determining HttpClient which requires reading
// the offline config first.
HttpClient? httpClient = null;
ILoggerFactory? loggerFactory = null;
OrcasiteTestHelper.MockOrcasiteHelperContainer? container = null;
string isOffline = builder.Configuration?["ORCANODE_MONITOR_OFFLINE"] ?? "false";
if (isOffline == "true")
{
    Fetcher.IsOffline = true;
    loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var logger = loggerFactory.CreateLogger<Program>();
    container = OrcasiteTestHelper.GetMockOrcasiteHelperWithRequestVerification(logger);
    httpClient = container.MockHttp.ToHttpClient();
}

Fetcher.Initialize(builder.Configuration, httpClient);

string isReadOnly = Fetcher.GetConfig("ORCANODE_MONITOR_READONLY") ?? "false";
if (isReadOnly == "true")
{
    Fetcher.IsReadOnly = true;
}

// Add services to the container.
builder.Services.AddRazorPages();
if (Fetcher.IsOffline) // Use Test data with in-memory database.
{
    // Configure an in-memory database for offline/test mode so that OrcanodeMonitorContext
    // has a valid EF Core provider without requiring Cosmos DB.
    builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
        options.UseInMemoryDatabase("OrcanodeMonitorOffline"));
}
else // Use Cosmos DB.
{
    // First see if an environment variable specifies a connection string.
    var connection = Fetcher.GetConfig("AZURE_COSMOS_CONNECTIONSTRING");
    if (connection.IsNullOrEmpty())
    {
        connection = builder.Configuration.GetConnectionString("OrcanodeMonitorContext") ?? throw new InvalidOperationException("Connection string 'OrcanodeMonitorContext' not found.");
    }

    string databaseName = Fetcher.GetConfig("AZURE_COSMOS_DATABASENAME") ?? "orcasound-cosmosdb";

    builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
    options.UseCosmos(
        connection,
        databaseName: databaseName,
        options =>
        { options.ConnectionMode(ConnectionMode.Gateway); }));
}

// Register Kubernetes client.
builder.Services.AddSingleton<IKubernetes>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    return OrcaHelloFetcher.CreateK8sClient(logger);
});

// Register OrcaHelloFetcher.
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
