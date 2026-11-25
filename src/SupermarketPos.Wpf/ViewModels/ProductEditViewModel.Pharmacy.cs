using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.ViewModels;
public sealed partial class ProductEditViewModel
{
    public int? MedicineReferenceId{get;set;}
    [ObservableProperty]private string _medicineUnit="piece";
    [ObservableProperty]private string _unitsPerStrip="1";
    [ObservableProperty]private string _stripPrice="";
    [ObservableProperty]private bool _dispensingRulesReviewed;
    [ObservableProperty]private bool _requiresPrescription;
    [ObservableProperty]private bool _requiresDispensingReference;
    [ObservableProperty]private int _defaultLocationId;
    [ObservableProperty]private string _medicineGeneric="";
    [ObservableProperty]private string _medicineStrength="";
    [ObservableProperty]private string _medicineForm="";
    [ObservableProperty]private string _medicineManufacturer="";
    [ObservableProperty]private string _defaultSellingUnit="piece";
    [ObservableProperty]private string _openingStockUnit="piece";
    public bool UsesStrips=>MedicineUnit is "tablet" or "capsule";
    public BusinessFormChoice[] SaleUnits=>new[]{MedicineUnit}.Concat(UsesStrips&&int.TryParse(UnitsPerStrip,out var strip)&&strip>1?["strip"]:Array.Empty<string>()).Concat(UsesPackaging&&int.TryParse(UnitsPerPackage,out var pack)&&pack>1?[PurchaseUnit]:Array.Empty<string>()).Select(u=>new BusinessFormChoice(u,LocalizationManager.Instance["Measure_"+u])).ToArray();
    public string StripsPerPack{get=>int.TryParse(UnitsPerPackage,out var pack)&&int.TryParse(UnitsPerStrip,out var strip)&&strip>0&&pack%strip==0?(pack/strip).ToString():"";set{if(int.TryParse(value,out var packs)&&packs>0&&int.TryParse(UnitsPerStrip,out var strip)&&strip>0&&packs<=1000000/strip)UnitsPerPackage=(packs*strip).ToString();OnPropertyChanged();}}
    private void NotifySaleUnits(){OnPropertyChanged(nameof(SaleUnits));OnPropertyChanged(nameof(UsesStrips));OnPropertyChanged(nameof(StripsPerPack));if(!SaleUnits.Any(u=>u.Code==DefaultSellingUnit))DefaultSellingUnit=MedicineUnit;if(!SaleUnits.Any(u=>u.Code==OpeningStockUnit))OpeningStockUnit=MedicineUnit;}
    partial void OnMedicineUnitChanged(string value){if(!UsesStrips){UnitsPerStrip="1";StripPrice="";}NotifySaleUnits();}
    partial void OnUnitsPerStripChanged(string value)=>NotifySaleUnits();
    partial void OnUnitsPerPackageChanged(string value)=>NotifySaleUnits();
    partial void OnPurchaseUnitChanged(string value)=>NotifySaleUnits();
    public System.Collections.ObjectModel.ObservableCollection<ProductLocationOption> Locations{get;}=[];
    public sealed record ProductLocationOption(int Id,string Name);
    public BusinessFormChoice[] MedicineUnits=>new[]{"piece","tablet","capsule","bottle","tube","sachet","vial","other"}.Select(c=>new BusinessFormChoice(c,LocalizationManager.Instance["Measure_"+c])).ToArray();
    private void LoadPharmacy(Product? p)
    {
        MedicineReferenceId=p?.MedicineReferenceId;MedicineUnit=p?.SellingUnit??"piece";UnitsPerStrip=(p?.UnitsPerStrip??1).ToString();StripPrice=p?.StripSellingPrice?.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)??"";DispensingRulesReviewed=p?.DispensingRulesReviewed==true;RequiresPrescription=p?.RequiresPrescription==true;RequiresDispensingReference=p?.RequiresDispensingReference==true;
        DefaultLocationId=p?.DefaultLocationId??0;
        MedicineGeneric=p?.MedicineGeneric??"";MedicineStrength=p?.MedicineStrength??"";MedicineForm=p?.MedicineForm??"";MedicineManufacturer=p?.MedicineManufacturer??"";
        DefaultSellingUnit=p?.DefaultSellingUnit??MedicineUnit;OpeningStockUnit=MedicineUnit;NotifySaleUnits();
    }
}
