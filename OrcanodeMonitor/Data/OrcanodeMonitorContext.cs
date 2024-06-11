using System;
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
            modelBuilder.Entity<Orcanode>().ToTable("Orcanode");
            modelBuilder.Entity<OrcanodeEvent>().ToTable("OrcanodeEvent");

            modelBuilder.Entity<OrcanodeEvent>()
                .HasOne(e => e.Orcanode) // Navigation property
                .WithMany() // Configure the inverse navigation property if needed
                .HasForeignKey(e => e.OrcanodeId); // Foreign key

            modelBuilder.Entity<MonitorState>().ToTable("MonitorState");
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseLazyLoadingProxies();
            // Other configuration...
        }
    }
}
