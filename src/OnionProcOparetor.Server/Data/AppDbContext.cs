using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Data;

/// <summary>
/// EF Core DbContext หลักของ Server ผูกกับ SQLite
/// ไฟล์ DB จริงจะอยู่ที่ %ProgramData%\OnionProcOparetor\onionprocoparetor.db
/// (ตั้งค่า connection string ใน appsettings.json)
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<ClientMachine> ClientMachines => Set<ClientMachine>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<GlobalState> GlobalStates => Set<GlobalState>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClientMachine>()
            .HasIndex(c => c.ClientGuid)
            .IsUnique();

        modelBuilder.Entity<AdminUser>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<AuthToken>()
            .HasIndex(t => t.Token)
            .IsUnique();

        modelBuilder.Entity<LogEntry>()
            .HasIndex(l => new { l.ClientGuid, l.Timestamp });

        modelBuilder.Entity<GlobalState>().HasData(
            new GlobalState { Id = 1, IsEnabled = true, Note = "Default state", UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}
