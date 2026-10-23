using Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Tests.Persistence;

/// <summary>Compares the EF model with the migration history. Building a model never opens a connection, so no database is needed.</summary>
public class QuotesModelTests
{
    private static QuotesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<QuotesDbContext>()
            .UseNpgsql("Host=unused")
            .UseSnakeCaseNamingConvention()
            .Options);

    [Fact]
    public void MigrationsMatchTheModel()
    {
        using var db = NewContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var applied = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true, validationLogger: null);

        // If this fails, the mapping changed without a migration: run `dotnet ef migrations add`.
        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(
            applied.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
    }
}
