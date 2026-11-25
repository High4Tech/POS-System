using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    private readonly ProtectedLocalStore _draftStorage;
    private Guid _checkoutId = Guid.NewGuid();
    private sealed record Draft(Guid CheckoutId, SaleCustomer? Customer, DraftDiscount? Discount, DraftLine[] Lines);
    private sealed record DraftLine(int ProductId, decimal Quantity, string Unit, int Factor, string? MedicineUnit, bool Packaged,
        decimal? PriceOverride, DraftDiscount? Discount);
    private sealed record DraftDiscount(decimal? Rate, decimal? Amount)
    {
        internal static DraftDiscount? From(Discount? value) => value switch
        {
            PercentageDiscount percent => new(percent.Rate, null),
            FixedAmountDiscount fixedAmount => new(null, fixedAmount.Amount),
            _ => null
        };
        internal Discount? Build() => Rate is { } rate ? new PercentageDiscount(rate) :
            Amount is { } amount ? new FixedAmountDiscount(amount) : null;
    }

    [RelayCommand]
    private void SaveDraft()
    {
        if (_auth.CurrentUser is not { } user || InAppDialog.IsOpen || !CanOpenPayment() || IsPaymentOpen) return;
        if (_cart.Lines.Any(line => line.Product.Id == 0 || line.Product.TrackExpiry || line.Product.TrackSerial ||
            line.Product.DefaultLocationId is not null || line.LocationId is not null || line.PrescriptionChecked ||
            line.DispensingReference is not null || line.Product.RequiresPrescription || line.Product.RequiresDispensingReference))
        {
            Status = Text["Trade_DraftUnsupported"];
            return;
        }
        try
        {
            var lines = _cart.Lines.Select(line => new DraftLine(line.Product.Id, line.Quantity, line.UnitCode,
                line.PackageSize, line.MedicineUnit, line.IsPackage && line.MedicineUnit is null,
                line.HasPriceOverride ? line.GrossUnitPrice : null, DraftDiscount.From(line.LineDiscount))).ToArray();
            var draft = new Draft(_checkoutId, Customer.Build(), DraftDiscount.From(_cart.CartDiscount), lines);
            _draftStorage.Write(DraftName(user), JsonSerializer.Serialize(draft));
            Status = Text["Trade_DraftSaved"];
        }
        catch (Exception) { Status = Text["Trade_DraftError"]; }
    }

    [RelayCommand]
    private async Task ResumeDraftAsync()
    {
        if (_auth.CurrentUser is not { } user || !_cart.IsEmpty || !CanReadBarcode() || InAppDialog.IsOpen) return;
        _readingBarcode = true;
        UpdateScanWorkflow();
        try
        {
            var raw = _draftStorage.Read(DraftName(user));
            var draft = raw is null ? null : JsonSerializer.Deserialize<Draft>(raw);
            if (draft is null || await _saleHistory.GetByCheckoutIdAsync(draft.CheckoutId) is not null)
            { Status = Text["Trade_NoDraft"]; return; }

            var staged = new Cart();
            foreach (var saved in draft.Lines)
            {
                var product = await _products.GetCurrentForRegisterAsync(saved.ProductId);
                if (product is null || !product.IsActive || !product.SellingUnitConfigured || product.TrackExpiry || product.TrackSerial ||
                    product.RequiresPrescription || product.RequiresDispensingReference || product.DefaultLocationId is not null ||
                    (saved.MedicineUnit is not null && !IsPharmacy) || saved.PriceOverride is not null && !user.IsAdmin ||
                    IsPharmacy && (await _stockLocations.StockAsync(product.Id)).Length > 0)
                    throw new DomainException("Trade_DraftChanged");
                var line = staged.AddOrIncrement(product, saved.Quantity, packaged: saved.Packaged, medicineUnit: saved.MedicineUnit, merge: false);
                if (line.PackageSize != saved.Factor || line.UnitCode != saved.Unit || product.StockTracked &&
                    staged.Lines.Where(item => item.Product.Id == product.Id).Sum(item => item.Quantity) > product.AvailableForSale(Today))
                    throw new DomainException("Trade_DraftChanged");
                if (saved.PriceOverride is { } price) line.SetUnitPrice(price);
                line.ApplyDiscount(saved.Discount?.Build());
            }
            if (!CanKeepScan() || !_cart.IsEmpty) return;
            _checkoutId = draft.CheckoutId;
            Customer.Load(draft.Customer);
            _cart.ApplyCartDiscount(draft.Discount?.Build());
            CartDiscountText = _cart.CartDiscount?.Describe();
            foreach (var item in staged.Lines)
            {
                var line = _cart.AddOrIncrement(item.Product, item.Quantity, packaged: item.IsPackage && item.MedicineUnit is null, medicineUnit: item.MedicineUnit, merge: false);
                if (item.HasPriceOverride) line.SetUnitPrice(item.GrossUnitPrice);
                line.ApplyDiscount(item.LineDiscount);
                Lines.Add(CreateCartLine(line));
            }
            Recalculate();
            Status = Text["Trade_DraftResumed"];
        }
        catch (DomainException error) { Status = Text[error.Message]; }
        catch (Exception) { Status = Text["Trade_DraftError"]; }
        finally { _readingBarcode = false; UpdateScanWorkflow(); }
    }

    private static string DraftName(User user) => $"register-draft-{user.Id}";
}
