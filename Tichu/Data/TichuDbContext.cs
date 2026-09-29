using Microsoft.EntityFrameworkCore;
using Tichu.Models;

namespace Tichu.Data
{
    public class TichuDbContext : DbContext
    {
        public TichuDbContext(DbContextOptions<TichuDbContext> options) : base(options)
        {
        }

        public DbSet<PlayerStats> PlayerStats => Set<PlayerStats>();
        public DbSet<Account> Accounts => Set<Account>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlayerStats>().HasKey(s => s.UserId);

            modelBuilder.Entity<Account>().HasKey(a => a.Id);
            modelBuilder.Entity<Account>()
                .HasIndex(a => new { a.Provider, a.ProviderUserId })
                .IsUnique();
        }
    }
}
