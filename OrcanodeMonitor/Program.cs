// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.Extensions.Hosting;
using OrcanodeMonitor.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrcanodeMonitor.Data;
using Newtonsoft.Json.Converters;

var builder = WebApplication.CreateBuilder(args);

var connection = String.Empty;
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddEnvironmentVariables().AddJsonFile("appsettings.Development.json");
    connection = builder.Configuration.GetConnectionString("AZURE_SQL_CONNECTIONSTRING");
}
else
{
    connection = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTIONSTRING");
}

// Remove this line to use the local SQL database instead of the Azure one.
// connection = builder.Configuration.GetConnectionString("OrcanodeMonitorContext") ?? throw new InvalidOperationException("Connection string 'OrcanodeMonitorContext' not found.");

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDbContext<OrcanodeMonitorContext>(options =>
    options.UseSqlServer(connection));
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
    context.Database.EnsureCreated();
    // DbInitializer.Initialize(context);
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();
