using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;

namespace BoxWise.Server.Tests;

public static class TestDbContextFactory
{
    public static AppDbContext Create()
        => Create(Guid.NewGuid().ToString());

    public static AppDbContext Create(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options);
    }
}
