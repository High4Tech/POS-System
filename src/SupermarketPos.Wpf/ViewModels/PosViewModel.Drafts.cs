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
        decimal? PriceOverride, DraftDiscount? Discount,string? RetailKind=null,Unit? RetailUnit=null,ServiceDraft? Service=null);
    private sealed record ServiceDraft(Guid ConfirmationId,string Type,int? Provider,int? Package,string Recipient,string Reference,string Status,string Fee);
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
            var lines = Lines.Select(row => new DraftLine(row.Line.Product.Id,row.Line.Quantity,row.Line.UnitCode,
                row.Line.PackageSize,row.Line.MedicineUnit,row.Line.IsPackage&&row.Line.MedicineUnit is null,
                row.Line.HasPriceOverride?row.Line.GrossUnitPrice:null,DraftDiscount.From(row.Line.LineDiscount),
                IsPharmacy?null:row.ProductKind,IsPharmacy?null:row.Line.Product.Unit,
                row.IsFinancialService?new(row.ServiceConfirmationId,row.SelectedServiceType,row.SelectedServiceProviderId,row.SelectedServicePackageId,row.ServiceRecipient,row.ServiceReference,row.ServiceStatus,row.ServiceFeeText):null)).ToArray();
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
                if(!IsPharmacy&&product.QuickEntry&&saved.RetailKind is{} kind)
                {
                    product.SetUnit(saved.RetailUnit??product.Unit);product.SetRetailKind(kind);
                    if(saved.Service is{} service)product.ConfigureService(service.Type,service.Provider,service.Package);
                }
                if(product.Kind=="digital"||saved.RetailKind=="digital")throw new DomainException("Digital_Separate");
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
            var draftIndex=0;
            foreach (var item in staged.Lines)
            {
                var line = _cart.AddOrIncrement(item.Product, item.Quantity, packaged: item.IsPackage && item.MedicineUnit is null, medicineUnit: item.MedicineUnit, merge: false);
                if (item.HasPriceOverride) line.SetUnitPrice(item.GrossUnitPrice);
                line.ApplyDiscount(item.LineDiscount);
                var row=CreateCartLine(line);
                if(!IsPharmacy&&draft.Lines[draftIndex].Service is{} savedService&&row.IsFinancialService)
                {
                    row.SelectedServiceType=savedService.Type;row.SelectedServiceProviderId=savedService.Provider;row.SelectedServicePackageId=savedService.Package;
                    row.ServiceRecipient=savedService.Recipient;row.ServiceReference=savedService.Reference;row.ServiceStatus=savedService.Status;row.ServiceFeeText=savedService.Fee;
                    row.ServiceConfirmationId=savedService.ConfirmationId;row.ServiceExternallyConfirmed=false;
                }
                Lines.Add(row);draftIndex++;
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
