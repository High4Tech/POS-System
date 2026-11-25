using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    public async Task RepeatAsync(Sale sale)
    {
        // Batch/source and serial selection must be done for the physical item sold today.
        if (!CanReadBarcode() || !_cart.IsEmpty || ContextPanel is not null)
        { Status = Text["Trade_RepeatEmpty"]; return; }

        _readingBarcode = true;
        NotifyPaymentAvailability();
        try
        {
            var skipped = new List<string>();
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
                if (prior.BaseQuantity > 0 && MeasuredGoods.BaseQuantity(quantity, product.Unit) != prior.BaseQuantity ||
                    product.StockTracked && QuantityInCart(product.Id, null) + quantity > product.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)) ||
                    IsPharmacy && _stockLocations is not null && (await _stockLocations.StockAsync(product.Id)).Length > 0)
                { skipped.Add(prior.Name); continue; }

                var line = _cart.AddOrIncrement(product, quantity, packaged: packaged, medicineUnit: medicineUnit);
                if (Lines.FirstOrDefault(item => ReferenceEquals(item.Line, line)) is { } existing) existing.Refresh();
                else Lines.Add(CreateCartLine(line));
            }
            Recalculate();
            Status = skipped.Count == 0 ? null : Text.Format("Trade_RepeatSkipped", string.Join(", ", skipped.Distinct()));
        }
        catch (Exception) { Status = Text["Trade_RepeatError"]; }
        finally { _readingBarcode = false; NotifyPaymentAvailability(); }
    }
}
