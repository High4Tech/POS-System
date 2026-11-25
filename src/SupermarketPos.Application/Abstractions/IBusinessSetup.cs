using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Application.Abstractions;

public record SetupState(BusinessProfile Profile, int Step, bool Completed, bool HasUsers, bool ImportedExisting);
public interface IBusinessSetup
{
    Task<SetupState> LoadAsync(CancellationToken ct = default);
    Task SaveDraftAsync(BusinessProfile profile, int step, CancellationToken ct = default);
    Task<string?> CompleteAsync(BusinessProfile profile, string? username, string? password, CancellationToken ct = default);
    Task RestartAsync(CancellationToken ct = default);
    Task RecoverAsync(string username, string recoveryCode, string password, CancellationToken ct = default);
    Task<string> RegenerateRecoveryAsync(string currentPassword, CancellationToken ct = default);
    Task SaveTaxRuleAsync(ProductTaxRule rule, CancellationToken ct = default, decimal? priceInput = null, bool taxExclusive = false);
    Task EndTaxRuleAsync(int id, DateTime endDate, CancellationToken ct = default);
    Task<IReadOnlyList<ProductTaxRule>> TaxRulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BusinessAudit>> AuditAsync(CancellationToken ct = default);
}
public interface IBusinessCompliance
{
    Task ValidateCheckoutAsync(Cart cart, CancellationToken ct);
    Task CaptureAsync(Sale sale, CancellationToken ct);
}
public record FiscalValidation(bool Valid, string Status, IReadOnlyList<string> Errors);
public interface IFbrAdapter
{
    string Route { get; }
    bool IsProductionAuthorized { get; }
    Task<FiscalValidation> ValidateAsync(BusinessProfile profile, string invoiceJson, CancellationToken ct = default);
}
