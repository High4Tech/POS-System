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
    public System.Collections.ObjectModel.ObservableCollection<ProductLocationOption> Locations{get;}=[];
    public sealed record ProductLocationOption(int Id,string Name);
    public BusinessFormChoice[] MedicineUnits=>new[]{"piece","tablet","capsule","bottle","tube","sachet","vial","other"}.Select(c=>new BusinessFormChoice(c,LocalizationManager.Instance["Measure_"+c])).ToArray();
    private void LoadPharmacy(Product? p)
    {
        MedicineReferenceId=p?.MedicineReferenceId;MedicineUnit=p?.SellingUnit??"piece";UnitsPerStrip=(p?.UnitsPerStrip??1).ToString();StripPrice=p?.StripSellingPrice?.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)??"";DispensingRulesReviewed=p?.DispensingRulesReviewed==true;RequiresPrescription=p?.RequiresPrescription==true;RequiresDispensingReference=p?.RequiresDispensingReference==true;
        DefaultLocationId=p?.DefaultLocationId??0;
        MedicineGeneric=p?.MedicineGeneric??"";MedicineStrength=p?.MedicineStrength??"";MedicineForm=p?.MedicineForm??"";MedicineManufacturer=p?.MedicineManufacturer??"";
    }
}
