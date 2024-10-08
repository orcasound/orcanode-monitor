﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Data
{
    public class OrcanodeMonitorContext : DbContext
    {
        public OrcanodeMonitorContext (DbContextOptions<OrcanodeMonitorContext> options)
            : base(options)
        {
        }

        public DbSet<OrcanodeMonitor.Models.Orcanode> Orcanodes { get; set; } = default!;
        public DbSet<OrcanodeMonitor.Models.OrcanodeEvent> OrcanodeEvents { get; set; } = default!;
        public DbSet<OrcanodeMonitor.Models.MonitorState> MonitorState { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MonitorState>()
               .ToContainer("MonitorState")
               .Property(item => item.ID)
               .HasConversion<string>();

            modelBuilder.Entity<MonitorState>()
                .ToContainer("MonitorState")
                .HasPartitionKey(item=>item.ID);

            modelBuilder.Entity<Orcanode>()
                .ToContainer("Orcanode")
                .Property(item => item.PartitionValue)
                .HasConversion<string>();

            modelBuilder.Entity<Orcanode>()
                .ToContainer("Orcanode")
                .Property(item => item.ID);

            modelBuilder.Entity<Orcanode>()
                .ToContainer("Orcanode")
                .Property(item => item.AudioStreamStatus)
                .HasDefaultValue(OrcanodeOnlineStatus.Absent);

            modelBuilder.Entity<Orcanode>()
                .ToContainer("Orcanode")
                .HasPartitionKey(item => item.PartitionValue)
                .HasKey(item=>item.ID);

            modelBuilder.Entity<OrcanodeEvent>()
                .ToContainer("OrcanodeEvent")
                .Property(item => item.Year)
                .HasConversion<string>();

            modelBuilder.Entity<OrcanodeEvent>()
                .ToContainer("OrcanodeEvent")
                .HasPartitionKey(item => item.Year)
                .HasOne(item => item.Orcanode)
                .WithMany()
                .HasForeignKey(item=>item.OrcanodeId);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseLazyLoadingProxies();
            // Other configuration...
        }
    }
}
