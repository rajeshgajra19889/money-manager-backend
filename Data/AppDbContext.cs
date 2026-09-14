using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Recurring> Recurrings => Set<Recurring>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Transaction>().HasIndex(t => new { t.UserId, t.Date });
        mb.Entity<Budget>().HasIndex(b => new { b.UserId, b.CategoryId, b.Month, b.Year });
        mb.Entity<Account>().HasIndex(a => a.UserId);
        mb.Entity<Recurring>().HasIndex(r => new { r.UserId, r.NextDueDate });

        mb.Entity<Transaction>().Property(t => t.Amount).HasColumnType("decimal(18,2)");
        mb.Entity<Budget>().Property(b => b.MonthlyLimit).HasColumnType("decimal(18,2)");
        mb.Entity<Account>().Property(a => a.InitialBalance).HasColumnType("decimal(18,2)");
        mb.Entity<Recurring>().Property(r => r.Amount).HasColumnType("decimal(18,2)");

        base.OnModelCreating(mb);
    }
}
