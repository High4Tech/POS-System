using System.ComponentModel;
using System.Globalization;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class ProductEditViewModel:IDataErrorInfo
{
    public string Error=>"";
    public string this[string field]=>FieldError(field);
    public string FirstFieldError=>new[]{nameof(Name),nameof(Barcode),nameof(Price),nameof(VatPercent),nameof(Stock),nameof(PurchaseCost),nameof(CategoryId),nameof(UnitsPerStrip),nameof(StripPrice),nameof(UnitsPerPackage),nameof(PackagePrice),nameof(PackageCost),nameof(MinimumStock),nameof(BatchLot),nameof(BatchExpiry)}.Select(FieldError).FirstOrDefault(e=>e.Length>0)??"";
    private string FieldError(string field)
    {
        var text=LocalizationManager.Instance;
        string Number(string value,bool optional=false,bool positive=false,bool whole=false,int precision=2)
        {
            if(string.IsNullOrWhiteSpace(value))return optional?"":text["Validation_Required"];
            return !decimal.TryParse(value,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var n)||n<0||positive&&n==0||whole&&(!int.TryParse(value,out var count)||count is <1 or >1000000)||!whole&&n!=decimal.Round(n,precision)?text["Common_InvalidNumber"]:"";
        }
        switch(field)
        {
            case nameof(Name):return string.IsNullOrWhiteSpace(Name)?text["Validation_Required"]:Name.Trim().Length>200||Name.Any(char.IsControl)?text["Validation_Text"]:"";
            case nameof(Barcode):return Barcode.Length>64||Barcode.Any(char.IsControl)?text["Pharmacy_InvalidBarcode"]:"";
            case nameof(Price):return Number(Price);
            case nameof(VatPercent):return !decimal.TryParse(VatPercent,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var vat)||vat is <0 or >100?text[string.IsNullOrWhiteSpace(VatPercent)?"Validation_Required":"Products_InvalidVat"]:"";
            case nameof(Stock):return string.IsNullOrWhiteSpace(Stock)?"":!decimal.TryParse(Stock,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var stock)||stock<0||stock!=decimal.Round(stock,SellingMethod is "weight" or "volume"?3:0)?text["Purchase_QuantityInvalid"]:"";
            case nameof(CategoryId):return CategoryId==0&&!IsPharmacy?text["Validation_Required"]:"";
            case nameof(PurchaseCost):return Number(PurchaseCost,true);
            case nameof(MinimumStock):return Number(MinimumStock,true,precision:3);
            case nameof(UnitsPerStrip):return IsPharmacy&&UsesStrips?Number(UnitsPerStrip,positive:true,whole:true):"";
            case nameof(StripPrice):return IsPharmacy&&UsesStrips?Number(StripPrice,true,true):"";
            case nameof(UnitsPerPackage):return UsesPackaging?Number(UnitsPerPackage,positive:true,whole:true):"";
            case nameof(PackagePrice):return UsesPackaging?Number(PackagePrice,true,true):"";
            case nameof(PackageCost):return UsesPackaging?Number(PackageCost,true):"";
            case nameof(BatchLot):return TrackExpiry&&(IsNew&&decimal.TryParse(Stock,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var opening)&&opening>0||CanAssignBatch&&decimal.TryParse(BatchQuantity,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var allocation)&&allocation>0)&&string.IsNullOrWhiteSpace(BatchLot)?text["Batch_ExpiryRequired"]:"";
            case nameof(BatchExpiry):return TrackExpiry&&(IsNew&&decimal.TryParse(Stock,out var quantity)&&quantity>0||CanAssignBatch&&decimal.TryParse(BatchQuantity,out var assigned)&&assigned>0)&&BatchExpiry is null?text["Batch_ExpiryRequired"]:"";
            default:return "";
        }
    }
}
