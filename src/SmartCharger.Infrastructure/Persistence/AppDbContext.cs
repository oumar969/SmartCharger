using Microsoft.EntityFrameworkCore;
using SmartCharger.Domain.Models;

namespace SmartCharger.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ChargeSession> ChargeSessions => Set<ChargeSession>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ChargeSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Strategy).HasConversion<string>();
        });
    }
}
