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

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}
Fetcher.Initialize(builder.Configuration);

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

string databaseName = Environment.GetEnvironmentVariable("AZURE_COSMOS_DATABASENAME") ?? "orcasound-cosmosdb";

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
    options.UseCosmos(
        connection,
        databaseName: databaseName,
        options =>
        { options.ConnectionMode(ConnectionMode.Gateway); }));
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
