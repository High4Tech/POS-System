using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    public async Task RepeatAsync(Sale sale)
    {
        // Batch/source and serial selection must be done for the physical item sold today.
        if (!CanReadBarcode() || ContextPanel is not null)
        { Status = Text["Trade_RepeatEmpty"]; return; }

        var replace = false;
        if (!_cart.IsEmpty)
        {
            var selection = await ChooseRepeatModeAsync();
            if (selection is null || !CanReadBarcode()) return;
            replace = selection == true;
        }
        _readingBarcode = true;
        NotifyPaymentAvailability();
        try
        {
            var skipped = new List<string>();
            var staged = new Cart();
            foreach (var prior in sale.Lines)
            {
                if (!CanKeepScan()) break;
                var product = await _products.GetCurrentForRegisterAsync(prior.ProductId);
                if (product is null || product.TrackSerial || product.TrackExpiry || product.RequiresPrescription ||
                    product.RequiresDispensingReference || prior.SerialNumber is not null || prior.BatchId is not null ||
                    !product.SellingUnitConfigured || IsPharmacy && product.Unit != Domain.Common.Unit.Piece)
                { skipped.Add(prior.Name); continue; }

                var medicineUnit = IsPharmacy ? (prior.UnitCode.Length > 0 ? prior.UnitCode : product.SellingUnit) : null;
                var packaged = !IsPharmacy && prior.UnitCode is "box" or "pack" or "carton";
                if ((IsPharmacy && medicineUnit != product.SellingUnit && medicineUnit != "strip" && medicineUnit != product.PurchaseUnit) ||
                    (!IsPharmacy && prior.UnitCode is "kg" && product.Unit != Domain.Common.Unit.Kilogram) ||
                    (!IsPharmacy && prior.UnitCode is "litre" && product.Unit != Domain.Common.Unit.Litre) ||
                    (packaged && (prior.UnitCode != product.PurchaseUnit || product.UnitsPerPackage <= 1)))
                { skipped.Add(prior.Name); continue; }

                int factor;
                try { factor = medicineUnit is not null ? product.SellingUnitSize(medicineUnit) : packaged ? product.UnitsPerPackage : 1; }
                catch (Domain.Common.DomainException) { skipped.Add(prior.Name); continue; }

                var quantity = prior.Quantity * factor;
                var cartQty = replace ? 0 : QuantityInCart(product.Id, null);
                var stagedQty = staged.Lines.Where(item => item.Product.Id == product.Id).Sum(item => item.Quantity);
                if (prior.BaseQuantity > 0 && MeasuredGoods.BaseQuantity(quantity, product.Unit) != prior.BaseQuantity ||
                    product.StockTracked && cartQty + stagedQty + quantity > product.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)) ||
                    IsPharmacy && _stockLocations is not null && (await _stockLocations.StockAsync(product.Id)).Length > 0)
                { skipped.Add(prior.Name); continue; }

                staged.AddOrIncrement(product, quantity, packaged: packaged, medicineUnit: medicineUnit);
            }
            if (!CanKeepScan()) return;
            if (replace && !staged.IsEmpty)
            {
                _cart.Clear();
                _checkoutId = Guid.NewGuid();
                Lines.Clear();
                CartDiscountPercent = CartDiscountAmount = string.Empty;
            }
            foreach (var item in staged.Lines)
            {
                var merge = !_cart.Lines.Any(line => line.Product.Id == item.Product.Id && line.UnitCode == item.UnitCode &&
                    (line.GrossUnitPrice != item.GrossUnitPrice || line.LineDiscount is not null));
                var line = _cart.AddOrIncrement(item.Product, item.Quantity, packaged: item.MedicineUnit is null && item.IsPackage,
                    medicineUnit: item.MedicineUnit, merge: merge);
                if (Lines.FirstOrDefault(item => ReferenceEquals(item.Line, line)) is { } existing) existing.Refresh();
                else Lines.Add(CreateCartLine(line));
            }
            Recalculate();
            Status = skipped.Count == 0 ? null : Text.Format("Trade_RepeatSkipped", string.Join(", ", skipped.Distinct()));
        }
        catch (Exception) { Status = Text["Trade_RepeatError"]; }
        finally { _readingBarcode = false; NotifyPaymentAvailability(); }
    }

    private static async Task<bool?> ChooseRepeatModeAsync()
    {
        var panel = new StackPanel();
        panel.Children.Add(BusinessForm.Label("Trade_RepeatCartChoice"));
        Action close = () => { };
        bool? replace = null;
        panel.Children.Add(BusinessForm.Button("Trade_RepeatAdd", (_, _) => { replace = false; close(); }));
        panel.Children.Add(BusinessForm.Button("Trade_RepeatReplace", (_, _) => { replace = true; close(); }, "GhostButton"));
        panel.Children.Add(BusinessForm.Button("Common_Cancel", (_, _) => close(), "GhostButton"));
        await InAppDialog.ShowAsync(panel, callback => close = callback, contentWidth: 440, sizeToContent: true);
        return replace;
    }
}
