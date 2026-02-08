// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

HttpClient? httpClient = null;

string isOffline = builder.Configuration["ORCANODE_MONITOR_OFFLINE"] ?? "false";
OrcasiteTestHelper.MockOrcasiteHelperContainer? container = null;
if (isOffline == "true")
{
    Fetcher.IsOffline = true;
    ILogger logger = null; // TODO
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
if (Fetcher.IsOffline) // Use Test data.
{
    // TODO: what is the right way to do this?
    // We have IOrcanodeMonitorContext but this creates an OrcanodeMonitorContext object.
    // Ex: No database provider has been configured for this DbContext. A provider can
    //     be configured by overriding the 'DbContext.OnConfiguring' method or by using
    //     'AddDbContext' on the application service provider. If 'AddDbContext' is used,
    //     then also ensure that your DbContext type accepts a DbContextOptions<TContext>
    //     object in its constructor and passes it to the base constructor for DbContext.
    builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
        options.UseInternalServiceProvider(null)
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
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();
