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
               .Property(item => item.id)
               .HasConversion<string>();

            modelBuilder.Entity<MonitorState>()
                .ToContainer("MonitorState")
                .HasPartitionKey(item=>item.id);

            modelBuilder.Entity<Orcanode>()
                .ToContainer("Orcanode")
                .Property(item => item.partitionvalue)
                .HasConversion<string>();

            modelBuilder.Entity<Orcanode>()
                .ToContainer("Orcanode")
                .Property(item => item.ID);
                 

            modelBuilder.Entity<Orcanode>()
                .ToContainer("Orcanode")
                .HasPartitionKey(item=>item.partitionvalue)
                .HasKey(item=>item.ID);

            modelBuilder.Entity<OrcanodeEvent>()
                .ToContainer("OrcanodeEvent")
                .Property(item => item.year)
                .HasConversion<string>();

            modelBuilder.Entity<OrcanodeEvent>()
                .ToContainer("OrcanodeEvent")
                .HasPartitionKey(item => item.year)
                .HasOne(item => item.Orcanode)
                .WithMany()
                .HasForeignKey(item=>item.OrcanodeId);

            /*modelBuilder.Entity<OrcanodeEvent>()
                .HasOne(e => e.Orcanode) // Navigation property
                .WithMany() // Configure the inverse navigation property if needed
                .HasForeignKey(e => e.OrcanodeId); // Foreign key */

            
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseLazyLoadingProxies();
            // Other configuration...
        }
    }
}
