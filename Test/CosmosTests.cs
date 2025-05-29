// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

// Tests in this file require the following Repository Secrets to be configured
// if running in github, or environment variables to be configured if running locally:
// AZURE_COSMOS_CONNECTIONSTRING
// AZURE_COSMOS_DATABASENAME

using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mono.TextTemplating;
using OrcanodeMonitor.Data;

namespace Test
{
    public class ProductionOrcanodeMonitorContext : OrcanodeMonitorContext
    {
        public ProductionOrcanodeMonitorContext(DbContextOptions<OrcanodeMonitorContext> options)
            : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Production specific configurations
        }
    }

    public class StagingOrcanodeMonitorContext : OrcanodeMonitorContext
    {
        public StagingOrcanodeMonitorContext(DbContextOptions<OrcanodeMonitorContext> options)
            : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Staging specific configurations
        }
    }

    public class DevelopmentOrcanodeMonitorContext : OrcanodeMonitorContext
    {
        public DevelopmentOrcanodeMonitorContext(DbContextOptions<OrcanodeMonitorContext> options)
            : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Staging specific configurations
        }
    }


    [TestClass]
    public class CosmosTests
    {
        private DbContextOptions<OrcanodeMonitorContext> GetCosmosDbContextOptions()
        {
            // Code to set connection and databaseName duplicated from Program.cs.
            // TODO: Should be moved to a common location.
            var connection = Environment.GetEnvironmentVariable("AZURE_COSMOS_CONNECTIONSTRING");
            if (connection.IsNullOrEmpty())
            {
                throw new InvalidOperationException("AZURE_COSMOS_CONNECTIONSTRING not found.");
            }
            string databaseName = Environment.GetEnvironmentVariable("AZURE_COSMOS_DATABASENAME") ?? "orcasound-cosmosdb";
            if (databaseName.IsNullOrEmpty())
            {
                throw new InvalidOperationException("AZURE_COSMOS_DATABASENAME not found.");
            }

            return new DbContextOptionsBuilder<OrcanodeMonitorContext>()
                .UseCosmos(
                    connection,
                    databaseName: databaseName,
                    options => { options.ConnectionMode(ConnectionMode.Gateway); }).Options;
        }

        private async Task VerifyCanReadEntityAsync<T>(DbSet<T> dbSet, string entityName) where T : class
        {
            var items = await dbSet.ToListAsync();
            Assert.IsNotNull(items, $"{entityName} should not be null");
            // Add specific assertions based on expected test data
        }

        [TestMethod]
        public async Task CanReadProductionAsync()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

            var options = GetCosmosDbContextOptions();
            using (OrcanodeMonitorContext context = new ProductionOrcanodeMonitorContext(options))
            {
                Assert.IsNotNull(context, "Context initialization failed");

                await VerifyCanReadEntityAsync(context.MonitorState, "MonitorState");
                await VerifyCanReadEntityAsync(context.Orcanodes, "Orcanodes");
                await VerifyCanReadEntityAsync(context.OrcanodeEvents, "OrcanodeEvents");
            }
        }

        [TestMethod]
        public async Task CanReadStagingAsync()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Staging");

            var options = GetCosmosDbContextOptions();
            using (OrcanodeMonitorContext context = new StagingOrcanodeMonitorContext(options))
            {
                Assert.IsNotNull(context, "Context initialization failed");

                await VerifyCanReadEntityAsync(context.MonitorState, "MonitorState");
                await VerifyCanReadEntityAsync(context.Orcanodes, "Orcanodes");
                await VerifyCanReadEntityAsync(context.OrcanodeEvents, "OrcanodeEvents");
            }
        }

        [TestMethod]
        public async Task CanReadDevelopmentAsync()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            var options = GetCosmosDbContextOptions();
            using (OrcanodeMonitorContext context = new DevelopmentOrcanodeMonitorContext(options))
            {
                Assert.IsNotNull(context, "Context initialization failed");

                await VerifyCanReadEntityAsync(context.MonitorState, "MonitorState");
                await VerifyCanReadEntityAsync(context.Orcanodes, "Orcanodes");
                await VerifyCanReadEntityAsync(context.OrcanodeEvents, "OrcanodeEvents");
            }
        }
    }
}

