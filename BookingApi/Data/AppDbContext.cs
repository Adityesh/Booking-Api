using Microsoft.EntityFrameworkCore;

namespace BookingApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<ResourceEntity> Resources => Set<ResourceEntity>();
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<WaitlistEntryEntity> WaitlistEntries => Set<WaitlistEntryEntity>();
    public DbSet<AuditLogEntryEntity> AuditLogEntries => Set<AuditLogEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>()
            .Property(u => u.Role)
            .HasConversion<string>();

        modelBuilder.Entity<UserEntity>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<ResourceEntity>()
            .Property(b => b.Type)
            .HasConversion<string>();

        modelBuilder.Entity<BookingEntity>()
            .Property(b => b.Status)
            .HasConversion<string>();

        modelBuilder.Entity<WaitlistEntryEntity>()
            .Property(w => w.Status)
            .HasConversion<string>();

        modelBuilder.Entity<AuditLogEntryEntity>()
            .Property(a => a.Action)
            .HasConversion<string>();

        modelBuilder.Entity<AuditLogEntryEntity>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BookingEntity>()
            .HasOne(b => b.Resource)
            .WithMany(r => r.Bookings)
            .HasForeignKey(b => b.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WaitlistEntryEntity>()
            .HasOne(w => w.Resource)
            .WithMany()
            .HasForeignKey(w => w.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WaitlistEntryEntity>()
            .HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BookingEntity>()
            .HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}