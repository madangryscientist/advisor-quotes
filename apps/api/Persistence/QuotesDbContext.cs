using Api.Advisers.Domain;
using Api.Quotes.Domain;
using Api.Quotes.Commands;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Persistence;

/// <summary>
/// Maps the domain to Postgres. Table and column names are snake_case by convention (see Program.cs);
/// only what a convention cannot say is spelled out here.
/// </summary>
public class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    /// <summary>The unique index that makes two creates with the same adviser and <c>Idempotency-Key</c> one row.</summary>
    public const string IdempotencyKeyIndex = "ix_quotes_adviser_id_idempotency_key";

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<Adviser> Advisers => Set<Adviser>();

    /// <summary>
    /// A new quote whose adviser already has a quote with its <c>Idempotency-Key</c> hits the unique index and throws
    /// <see cref="DuplicateIdempotencyKeyException"/>, and the losing insert is discarded so the caller can read the winner through this context.
    /// A quote changed by someone else since it was loaded throws <see cref="DbUpdateConcurrencyException"/> (the <c>xmin</c> row version).
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IdempotencyKeyIndex })
        {
            ChangeTracker.Clear();
            throw new DuplicateIdempotencyKeyException(e);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Adviser>(adviser =>
        {
            adviser.ToTable("advisers");
            adviser.HasKey(a => a.Id);
            adviser.Property(a => a.Id).HasMaxLength(Adviser.IdMaxLength);
            adviser.Property(a => a.Name).IsRequired();
            adviser.HasData(
                new Adviser { Id = "ADV-001", Name = "Thandi M." },
                new Adviser { Id = "ADV-002", Name = "Pieter V." },
                new Adviser { Id = "ADV-003", Name = "Aisha K." });
        });

        modelBuilder.Entity<Quote>(quote =>
        {
            quote.ToTable("quotes");
            quote.HasKey(q => q.Id);
            quote.Property(q => q.Id).ValueGeneratedNever();

            quote.Property(q => q.AdviserId).HasMaxLength(Adviser.IdMaxLength);
            quote.HasOne<Adviser>().WithMany().HasForeignKey(q => q.AdviserId).OnDelete(DeleteBehavior.Restrict);

            quote.Property(q => q.CoverAmount).HasPrecision(12, 2);
            quote.Property(q => q.Premium).HasPrecision(12, 2);
            quote.Property(q => q.Currency).HasMaxLength(3);
            quote.Property(q => q.PricingVersion).IsRequired();
            quote.Property(q => q.Status).HasConversion<string>().HasMaxLength(16);

            // Postgres bumps xmin on every row update, so a stale save throws DbUpdateConcurrencyException.
            quote.Property<uint>("xmin").IsRowVersion();

            quote.HasIndex(q => new { q.AdviserId, q.CreatedAt }).IsDescending(false, true);
            // No query reads by CreatedAt alone yet; kept for future cross-adviser reporting, not dropped by a migration.
            quote.HasIndex(q => q.CreatedAt).IsDescending();

            quote.Property(q => q.IdempotencyKey).HasMaxLength(SaveQuoteCommand.IdempotencyKeyMaxLength);
            quote.Property(q => q.RequestHash).HasMaxLength(64);
            quote.HasIndex(q => new { q.AdviserId, q.IdempotencyKey }).IsUnique().HasDatabaseName(IdempotencyKeyIndex);

            quote.OwnsMany(
                q => q.History,
                history =>
                {
                    history.ToTable("quote_status_changes");
                    history.WithOwner().HasForeignKey("QuoteId");
                    history.Property<long>("Id").ValueGeneratedOnAdd();
                    history.HasKey("Id");
                    history.Property(c => c.From).HasColumnName("from_status").HasConversion<string>().HasMaxLength(16);
                    history.Property(c => c.To).HasColumnName("to_status").HasConversion<string>().HasMaxLength(16);
                });
            quote.Navigation(q => q.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
