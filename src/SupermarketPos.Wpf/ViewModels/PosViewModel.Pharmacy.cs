using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class PosViewModel
{
    [ObservableProperty]private PharmacySalePanel? _pharmacyWorkspace;
    private bool _pharmacyScanned;
    private async Task<bool> AddMedicineAsync(Product p,decimal quantity,string unit,InventoryBatch? batch,bool prescription,string? reference,int? locationId,string? serial)
    {
        if(!CanKeepScan()||_readingBarcode)return false;
        _readingBarcode=true;NotifyPaymentAvailability();
        try
        {
            p=await _products.GetCurrentForRegisterAsync(p.Id)??throw new DomainException("Pharmacy_InvalidProduct");
            if(!CanKeepScan())return false;
            if(p.RequiresPrescription&&!prescription||p.RequiresDispensingReference&&string.IsNullOrWhiteSpace(reference))throw new DomainException("Pharmacy_TraceabilityRequired");
            if(p.TrackExpiry&&(batch is null||batch.ExpiryDate<DateOnly.FromDateTime(DateTime.Today)||batch.HoldReason.Length>0))throw new DomainException("Pharmacy_TraceabilityRequired");
            if(serial is not null&&(Lines.Any(l=>l.Line.Product.Id==p.Id&&l.Line.SerialNumber==serial)||await _saleHistory.HasSoldSerialAsync(p.Id,serial)))throw new DomainException(Text.Format("Batch_SerialDuplicate",serial));
            if(!ValidateCartAddition(p,quantity,batch,out _))return false;
            var added=_cart.AddOrIncrement(p,quantity,batch,serial:serial,medicineUnit:unit,prescriptionChecked:prescription,dispensingReference:reference,locationId:locationId);
            if(Lines.FirstOrDefault(l=>ReferenceEquals(l.Line,added)) is{} line)line.Refresh();else Lines.Add(CreateCartLine(added));
            await RefreshLocationLimitsAsync(p.Id);
            SelectedCartLine=Lines.First(item=>ReferenceEquals(item.Line,added));
            if(!_pharmacyScanned)ManualProductAdded?.Invoke(SelectedCartLine);
            Recalculate();Status=Text.Format("Scanner_Added",p.Name);if(_pharmacyScanned)_sound.PlayScanResult(new("added",p.Name,p.StockTracked?p.StockQuantity-QuantityInCart(p.Id):null),_profile.LowStockThreshold);_pharmacyScanned=false;return true;
        }
        finally{_readingBarcode=false;NotifyPaymentAvailability();}
    }
}
