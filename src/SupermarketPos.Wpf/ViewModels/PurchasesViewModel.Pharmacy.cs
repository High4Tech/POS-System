using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Wpf.Views;
namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class PurchasesViewModel
{
    [ObservableProperty]private string _medicinePurchaseUnit="piece";
    [ObservableProperty]private int _destinationLocationId;
    private bool _destinationExplicit,_settingDestination;
    [ObservableProperty]private IReadOnlyList<LocationOption> _locationOptions=[];
    public bool ShowMedicinePurchaseUnit=>IsPharmacy&&MedicinePurchaseUnits.Length>1;
    public BusinessForm.Choice[] MedicinePurchaseUnits
    {
        get
        {
            if(SelectedProduct is not{} p)return [];
            var units=new List<string>{p.SellingUnit};if(p.UnitsPerStrip>1)units.Add("strip");if(p.UnitsPerPackage>1)units.Add(p.PurchaseUnit);
            return units.Select(u=>new BusinessForm.Choice(u,_text["Measure_"+u])).ToArray();
        }
    }
    partial void OnMedicinePurchaseUnitChanged(string value)
    {
        if(IsPharmacy&&SelectedProduct is{} p)try{UnitCost=FormatCost(p.PurchaseCost*p.SellingUnitSize(value));}catch{UnitCost="";}
    }
    partial void OnDestinationLocationIdChanged(int value){if(!_settingDestination)_destinationExplicit=true;}
    private void ApplyProductDestination(Product? product)
    {
        if(!IsPharmacy||_destinationExplicit)return;_settingDestination=true;try{DestinationLocationId=product?.DefaultLocationId??0;}finally{_settingDestination=false;}
    }
    private void ResetDestination(){_destinationExplicit=false;ApplyProductDestination(null);}
    public string? MarginWarning
    {
        get
        {
            foreach(var row in Draft)
            {
                if(!TryNumber(row.Cost,out var cost)||cost<0||row.Product.UnitPrice<=0)continue;
                decimal price;try{price=row.MedicineUnit is{} unit?row.Product.ResolveSellingUnit(unit).Price:row.Packaged?row.Product.PackageSellingPrice??row.Product.UnitPrice*row.PackageSize:row.Product.UnitPrice;}catch{continue;}
                if(cost>price/(1+row.Product.VatRate))return _text["Pharmacy_MarginWarning"];
            }
            return null;
        }
    }
    private async Task LoadLocationsAsync()
    {
        var locations=await stockLocations.LocationsAsync();LocationOptions=new[]{new LocationOption(0,_text["Pharmacy_Unassigned"])}.Concat(locations.Select(l=>new LocationOption(l.Id,(l.ParentId is{} parent?locations.FirstOrDefault(p=>p.Id==parent)?.Name+" / ":"")+l.Name))).ToArray();
    }
    private async Task<ScanOutcome> SelectDestinationAsync(string code,Guid epoch)
    {
        if(!IsPharmacy||await stockLocations.FindAsync(code) is not{} location){Error=_text["Pharmacy_SelectStockLocation"];return new("invalid");}
        if(!CanAccept()||!scanner.CanApply(this,epoch))return new("paused");await LoadLocationsAsync();if(!CanAccept()||!scanner.CanApply(this,epoch))return new("paused");DestinationLocationId=location.Id;Status=_text["Pharmacy_DestinationSelected"]+": "+location.Name;return new("location_selected",location.Name);
    }
    public sealed record LocationOption(int Id,string Name);
}
