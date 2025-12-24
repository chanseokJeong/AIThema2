using Microsoft.EntityFrameworkCore;
using AIThemaView2.Models;

namespace AIThemaView2.Data
{
    public class StockEventContext : DbContext
    {
        public DbSet<StockEvent> StockEvents { get; set; }
        public DbSet<AppSettings> AppSettings { get; set; }
        public DbSet<DataSourceStatus> DataSourceStatuses { get; set; }

        public StockEventContext(DbContextOptions<StockEventContext> options)
            : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=stockevents.db");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // StockEvent configuration
            modelBuilder.Entity<StockEvent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.EventTime);
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.Source);
                entity.HasIndex(e => e.Hash).IsUnique();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // AppSettings configuration
            modelBuilder.Entity<AppSettings>(entity =>
            {
                entity.HasKey(e => e.Key);
            });

            // DataSourceStatus configuration
            modelBuilder.Entity<DataSourceStatus>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SourceName);
            });
        }
    }
}
