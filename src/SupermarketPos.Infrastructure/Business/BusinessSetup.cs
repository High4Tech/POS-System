using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Infrastructure.Business;

public sealed class BusinessSetup(PosDbContext db, IUnitOfWork work, IAuthService auth, IPasswordHasher hasher) : IBusinessSetup
{
    public async Task<SetupState> LoadAsync(CancellationToken ct = default)
    {
        var row = await RowAsync(ct);
        return new(Read(row), row.Step, row.Completed, await db.Users.AnyAsync(ct), row.ImportedExisting);
    }
    private static BusinessProfile Read(BusinessConfiguration row) => JsonSerializer.Deserialize<BusinessProfile>(row.ProfileJson) ?? new();
    private async Task<BusinessConfiguration> RowAsync(CancellationToken ct)
    {
        if (await db.BusinessConfigurations.SingleOrDefaultAsync(ct) is { } row)
        {
            var profile=Read(row);
            if(row.Completed&&profile.StoreName is "Demo Account" or "Tech Store"&&!string.IsNullOrWhiteSpace(profile.LegalName))
            {
                profile=profile with{StoreName=profile.LegalName};row.ProfileJson=JsonSerializer.Serialize(profile);row.Revision++;
                var existing=await db.Settings.SingleAsync(ct);existing.Update(profile.StoreName,profile.Address,profile.Language,existing.LowStockThreshold,existing.VatRates);
                db.BusinessAudits.Add(new(){Actor="local-migration",Action="business.identity.normalized",Summary="placeholder-store-name"});await db.SaveChangesAsync(ct);
            }
            return row;
        }
        var settings = await db.Settings.SingleAsync(ct);
        row = new() { ProfileJson = JsonSerializer.Serialize(new BusinessProfile { Language = settings.Language, StoreName = settings.StoreName,
            Address = settings.StoreAddress, Logo = settings.LogoPath }), ImportedExisting = await db.Sales.AnyAsync(ct) || await db.Users.AnyAsync(ct) };
        db.BusinessConfigurations.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }
    public async Task SaveDraftAsync(BusinessProfile profile, int step, CancellationToken ct = default)
    {
        profile.EnforceBusinessFeatures();
        profile.ValidateDraft();
        var row = await RowAsync(ct);
        // Before authenticating, only language/progress may be changed on an existing store.
        if (await db.Users.AnyAsync(ct) && !auth.IsLoggedIn)
        {
            profile = Read(row) with { Language = profile.Language };
            step = Math.Min(step, 1);
        }
        else if (auth.IsLoggedIn) auth.RequireAdmin();
        var json=JsonSerializer.Serialize(profile);
        if(auth.IsLoggedIn&&row.ProfileJson!=json)db.BusinessAudits.Add(new(){Actor=auth.CurrentUser!.Username,Action="business.configuration.saved",Summary=$"step:{step};registered:{profile.Registered};fbr:{profile.FbrStatus}"});
        row.ProfileJson = json;
        row.Step = Math.Clamp(step, 0, 8);
        row.Revision++;
        var settings = await db.Settings.SingleAsync(ct);
        settings.Update(settings.StoreName, settings.StoreAddress, profile.Language, settings.LowStockThreshold, settings.VatRates);
        await db.SaveChangesAsync(ct);
    }
    public async Task<string?> CompleteAsync(BusinessProfile profile, string? username, string? password, CancellationToken ct = default)
    {
        profile.EnforceBusinessFeatures();
        if(profile.StoreName is "Demo Account" or "Tech Store"&&!string.IsNullOrWhiteSpace(profile.LegalName))profile=profile with{StoreName=profile.LegalName};
        profile.Validate();
        return await work.ExecuteInTransactionAsync(async token =>
        {
            var row = await RowAsync(token);
            string? recovery = null;
            if (await db.Users.AnyAsync(token)) auth.RequireAdmin();
            else
            {
                ValidatePassword(password);
                if (string.IsNullOrWhiteSpace(username) || username.Length > 64) throw new BusinessValidationException("Setup_UsernameRequired");
                var user = new User(username, hasher.Hash(password!), profile.Owner.Length > 0 ? profile.Owner : username, UserRole.Admin);
                db.Users.Add(user);
                recovery = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                row.RecoveryHash = hasher.Hash(recovery);
                row.RecoveryUsername = username.Trim();
            }
            var previous = Read(row);
            row.ProfileJson = JsonSerializer.Serialize(profile);
            row.Completed = true;
            row.Step = 7;
            row.Revision++;
            var settings = await db.Settings.SingleAsync(token);
            settings.Update(profile.StoreName, profile.Address, profile.Language, settings.LowStockThreshold, settings.VatRates);
            settings.SetLogoPath(profile.Logo);
            db.BusinessAudits.Add(new() { Actor = auth.CurrentUser?.Username ?? username!, Action = "business.setup.completed",
                Summary = $"registered:{previous.Registered}->{profile.Registered};sales-tax:{previous.SalesTaxRegistered}->{profile.SalesTaxRegistered};fbr:{profile.FbrStatus}" });
            await db.SaveChangesAsync(token);
            return recovery;
        }, ct);
    }
    public async Task RestartAsync(CancellationToken ct = default)
    {
        auth.RequireAdmin();
        var row = await RowAsync(ct);
        row.Completed = false;
        row.Step = 0;
        row.Revision++;
        db.BusinessAudits.Add(new() { Actor = auth.CurrentUser!.Username, Action = "business.setup.restarted" });
        await db.SaveChangesAsync(ct);
    }
    public static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < 12 || Encoding.UTF8.GetByteCount(password) > 72 ||
            !password.Any(char.IsLetter) || !password.Any(char.IsDigit)) throw new BusinessValidationException("Setup_PasswordStrength");
    }
    public async Task RecoverAsync(string username, string recoveryCode, string password, CancellationToken ct = default)
    {
        ValidatePassword(password);
        var row = await RowAsync(ct);
        if (row.RecoveryLockedUntil > DateTime.UtcNow) throw new BusinessValidationException("Setup_RecoveryLocked");
        if (row.RecoveryHash is null || row.RecoveryUsername != username.Trim() || !hasher.Verify(recoveryCode.Trim().ToUpperInvariant(), row.RecoveryHash))
        {
            row.RecoveryFailures++;
            if (row.RecoveryFailures >= 5) row.RecoveryLockedUntil = DateTime.UtcNow.AddMinutes(15);
            await db.SaveChangesAsync(ct);
            throw new BusinessValidationException("Setup_RecoveryInvalid");
        }
        var user = await db.Users.SingleAsync(u => u.Username == row.RecoveryUsername, ct);
        user.SetPasswordHash(hasher.Hash(password));
        row.RecoveryHash = null; // owner code is one use; an administrator can generate a replacement in setup
        row.RecoveryFailures = 0;
        db.BusinessAudits.Add(new() { Actor = username.Trim(), Action = "owner.recovery.used" });
        await db.SaveChangesAsync(ct);
    }
    public async Task<string> RegenerateRecoveryAsync(string currentPassword, CancellationToken ct = default)
    {
        auth.RequireAdmin();
        var user = auth.CurrentUser!;
        if (!hasher.Verify(currentPassword, user.PasswordHash)) throw new BusinessValidationException("Profile_CurrentInvalid");
        var row = await RowAsync(ct);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        row.RecoveryHash = hasher.Hash(code); row.RecoveryUsername = user.Username; row.RecoveryFailures = 0; row.RecoveryLockedUntil = null;
        db.BusinessAudits.Add(new() { Actor=user.Username, Action="owner.recovery.created" });
        await db.SaveChangesAsync(ct); return code;
    }
    public async Task SaveTaxRuleAsync(ProductTaxRule rule, CancellationToken ct = default, decimal? priceInput = null, bool taxExclusive = false)
    {
        auth.RequireAdmin();
        if (rule.Rate < 0 || rule.Rate > 1 || string.IsNullOrWhiteSpace(rule.AuthorityReference) ||
            rule.AuthorityReference.Length > 200 || rule.HsCode.Length > 12 || rule.Sro.Length > 100 || rule.SaleType.Length > 100 ||
            rule.Classification is not ("standard" or "exempt" or "zero-rated" or "special") ||
            rule.Classification is "exempt" or "zero-rated" && rule.Rate != 0 || rule.EffectiveTo < rule.EffectiveFrom)
            throw new BusinessValidationException("Tax_InvalidRule");
        var rules = await db.ProductTaxRules.Where(r => r.ProductId == rule.ProductId).ToArrayAsync(ct);
        if (rules.Any(r => (r.EffectiveTo ?? DateTime.MaxValue) >= rule.EffectiveFrom && (rule.EffectiveTo ?? DateTime.MaxValue) >= r.EffectiveFrom))
            throw new BusinessValidationException("Tax_Overlap");
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == rule.ProductId, ct) ?? throw new BusinessValidationException("Tax_InvalidRule");
        if (priceInput is < 0 || priceInput.HasValue && !(rule.EffectiveFrom <= DateTime.Today && (rule.EffectiveTo == null || rule.EffectiveTo >= DateTime.Today)))
            throw new BusinessValidationException("Tax_InvalidRule");
        if (priceInput.HasValue)
        {
            product.SetVatRate(rule.Rate);
            product.SetPrice(Money.Round2(priceInput.Value * (taxExclusive ? 1 + rule.Rate : 1)));
        }
        rule.Id = 0;
        db.ProductTaxRules.Add(rule);
        db.BusinessAudits.Add(new() { Actor = auth.CurrentUser!.Username, Action = "tax.rule.created", Summary = $"product:{rule.ProductId};from:{rule.EffectiveFrom:yyyy-MM-dd}" });
        await db.SaveChangesAsync(ct);
    }
    public async Task EndTaxRuleAsync(int id, DateTime endDate, CancellationToken ct = default)
    {
        auth.RequireAdmin();
        var rule = await db.ProductTaxRules.SingleAsync(r => r.Id == id, ct);
        if (endDate.Date < rule.EffectiveFrom.Date) throw new BusinessValidationException("Tax_InvalidRule");
        var others=await db.ProductTaxRules.AsNoTracking().Where(r=>r.ProductId==rule.ProductId&&r.Id!=id).ToArrayAsync(ct);
        if(others.Any(r=>(r.EffectiveTo??DateTime.MaxValue)>=rule.EffectiveFrom&&endDate.Date>=r.EffectiveFrom))throw new BusinessValidationException("Tax_Overlap");
        rule.EffectiveTo = endDate.Date;
        db.BusinessAudits.Add(new() { Actor = auth.CurrentUser!.Username, Action = "tax.rule.ended", Summary = $"rule:{id};until:{endDate:yyyy-MM-dd}" });
        await db.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<ProductTaxRule>> TaxRulesAsync(CancellationToken ct = default) => await db.ProductTaxRules.AsNoTracking().OrderByDescending(r => r.EffectiveFrom).ToArrayAsync(ct);
    public async Task<IReadOnlyList<BusinessAudit>> AuditAsync(CancellationToken ct = default)
    {
        auth.RequireAdmin();
        return await db.BusinessAudits.AsNoTracking().OrderByDescending(a => a.Id).Take(200).ToArrayAsync(ct);
    }
}
