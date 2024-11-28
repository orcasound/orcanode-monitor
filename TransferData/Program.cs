using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System;

namespace OrcanodeMonitor
{
    public class FromOrcanodeMonitorContext : OrcanodeMonitorContext
    {
        public FromOrcanodeMonitorContext(DbContextOptions<OrcanodeMonitorContext> options)
            : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Staging specific configurations
        }
    }

    public class ToOrcanodeMonitorContext : OrcanodeMonitorContext
    {
        public ToOrcanodeMonitorContext(DbContextOptions<OrcanodeMonitorContext> options)
            : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Staging specific configurations
        }
    }

    public class Program
    {
        private static string toConnection = "<to be filled in>";
        private static string fromConnection = "<to be filled in>";
        private static string databaseName = Environment.GetEnvironmentVariable("AZURE_COSMOS_DATABASENAME") ?? "orcasound-cosmosdb";

        public static async Task Main(string[] args)
        {
            if (string.IsNullOrEmpty(databaseName))
            {
                throw new InvalidOperationException("AZURE_COSMOS_DATABASENAME not found.");
            }

            var fromOptions = CreateDbContextOptions(fromConnection);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            using (OrcanodeMonitorContext fromContext = new FromOrcanodeMonitorContext(fromOptions))
            {
                // Force model creation.
                List<Orcanode> fromNodes = fromContext.Orcanodes.ToList();

                var toOptions = CreateDbContextOptions(toConnection);
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
                using (OrcanodeMonitorContext toContext = new ToOrcanodeMonitorContext(toOptions))
                {
                    await TransferDataAsync(fromContext, toContext);
                }
            }
        }

        private static DbContextOptions<OrcanodeMonitorContext> CreateDbContextOptions(string connectionString)
        {
            return new DbContextOptionsBuilder<OrcanodeMonitorContext>()
                        .UseCosmos(
                            connectionString,
                            databaseName: databaseName,
                            options => { options.ConnectionMode(ConnectionMode.Gateway); }).Options;
        }

        private static bool IsCovered(OrcanodeEvent item, List<OrcanodeEvent> items)
        {
            var all = items.Where(i => i.NodeName == item.NodeName && i.Type == item.Type);
            OrcanodeEvent before = all.Where(i => i.DateTimeUtc <= item.DateTimeUtc).OrderByDescending(i => i.DateTimeUtc).FirstOrDefault();
            if (before == null)
            {
                return false;
            }
            if (before.Value != item.Value)
            {
                return false;
            }
            return true;
        }

        private static async Task TransferDataAsync(OrcanodeMonitorContext fromContext, OrcanodeMonitorContext toContext)
        {
            List<Orcanode> fromNodes = fromContext.Orcanodes.Where(n => n.OrcasoundHost != "dev.orcasound.net").ToList();
            List<Orcanode> toNodes = toContext.Orcanodes.Where(n => n.OrcasoundHost != "dev.orcasound.net").ToList();
            List<OrcanodeEvent> fromItems = fromContext.OrcanodeEvents.ToList();
            List<OrcanodeEvent> toItems = toContext.OrcanodeEvents.ToList();

            foreach (OrcanodeEvent fromItem in fromItems)
            {
                if (fromItem.Orcanode == null)
                {
                    continue;
                }
                if (!fromNodes.Contains(fromItem.Orcanode))
                {
                    continue;
                }
                if (IsCovered(fromItem, toItems))
                {
                    continue;
                }

                Console.WriteLine(fromItem.Description);

                // Create an equivalent "to" event.
                Orcanode? toNode = toNodes.Where(n => n.DisplayName == fromItem.NodeName).FirstOrDefault();
                if (toNode == null)
                {
                    continue;
                }
                OrcanodeEvent toEvent = new OrcanodeEvent(toNode, fromItem.Type, fromItem.Value, fromItem.DateTimeUtc);
                toContext.OrcanodeEvents.Add(toEvent);
            }
            await toContext.SaveChangesAsync();
            Console.WriteLine("Data transfer complete.");
        }
    }
}
