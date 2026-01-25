// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Data
{
    public class OrcanodeMonitorContext : DbContext
    {
        public OrcanodeMonitorContext(DbContextOptions<OrcanodeMonitorContext> options)
            : base(options)
        {
        }

        public DbSet<Orcanode> Orcanodes { get; set; } = default!;
        public DbSet<OrcanodeEvent> OrcanodeEvents { get; set; } = default!;
        public DbSet<MonitorState> MonitorState { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Since ASPNETCORE_ENVIRONMENT is not a secret, it is safe to use it directly here.
            // We cannot use IConfiguration here because this method is called by design time tools as well.
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;
            if (environment == "Production")
            {
                environment = string.Empty;
            }

            modelBuilder.Entity<MonitorState>()
               .ToContainer(environment + "MonitorState")
               .Property(item => item.ID)
               .HasConversion<string>();

            modelBuilder.Entity<MonitorState>()
                .ToContainer(environment + "MonitorState")
                .HasPartitionKey(item => item.ID);

            modelBuilder.Entity<Orcanode>()
                .ToContainer(environment + "Orcanode")
                .Property(item => item.PartitionValue)
                .HasConversion<string>();

            modelBuilder.Entity<Orcanode>()
                .ToContainer(environment + "Orcanode")
                .Property(item => item.ID);

            modelBuilder.Entity<Orcanode>()
                .ToContainer(environment + "Orcanode")
                .Property(item => item.AudioStreamStatus)
                .HasDefaultValue(OrcanodeOnlineStatus.Absent);

            modelBuilder.Entity<Orcanode>()
                .ToContainer(environment + "Orcanode")
                .HasPartitionKey(item => item.PartitionValue)
                .HasKey(item => item.ID);

            modelBuilder.Entity<OrcanodeEvent>()
                .ToContainer(environment + "OrcanodeEvent")
                .Property(item => item.Year)
                .HasConversion<string>();

            modelBuilder.Entity<OrcanodeEvent>()
                .ToContainer(environment + "OrcanodeEvent")
                .HasPartitionKey(item => item.Year)
                .HasOne(item => item.Orcanode)
                .WithMany()
                .HasForeignKey(item => item.OrcanodeId);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseLazyLoadingProxies();
            // Other configuration...
        }
    }
}
