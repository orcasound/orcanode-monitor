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

#if false
        public OrcanodeMonitorContext() : base()
        {
        }
#endif

        public DbSet<OrcanodeMonitor.Models.Orcanode> Orcanodes { get; set; } = default!;
        public DbSet<OrcanodeMonitor.Models.OrcanodeEvent> OrcanodeEvents { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Orcanode>().ToTable("Orcanode");
            modelBuilder.Entity<OrcanodeEvent>().ToTable("OrcanodeEvent");
        }
    }
}
