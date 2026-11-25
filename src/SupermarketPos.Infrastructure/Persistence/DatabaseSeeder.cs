using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Infrastructure.Persistence;

/// <summary>Creates the initial store profile and operator account.</summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(PosDbContext db, IPasswordHasher hasher, CancellationToken ct = default, bool createDefaultUser = true)
    {
        if (!await db.Settings.AnyAsync(ct))
        {
            db.Settings.Add(new AppSettings(
                storeName: "Tech Store",
                storeAddress: string.Empty,
                language: "en",
                lowStockThreshold: 5m,
                vatRates: new[] { 0.01m, 0.10m, 0.20m }));
        }

        if (createDefaultUser && !await db.Users.AnyAsync(ct))
        {
            db.Users.Add(new User("admin", hasher.Hash("admin"), "Store operator", UserRole.Admin));
        }

        await db.SaveChangesAsync(ct);
    }
}
