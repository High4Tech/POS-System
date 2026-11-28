namespace SupermarketPos.Wpf.Common;

public static class AppBrand
{
    static AppBrand()=>AppContext.SetData("SupermarketPos.SoftwareName",RetailTestEdition.Enabled?"Wijgandu":"ORMA");
    public static string Name=>SupermarketPos.Domain.Common.SoftwareBrand.Name;
}
