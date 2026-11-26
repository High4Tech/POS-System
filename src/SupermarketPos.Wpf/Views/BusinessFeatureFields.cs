using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SupermarketPos.Domain.Business;

namespace SupermarketPos.Wpf.Views;
public static class BusinessFeatureFields
{
    public static void Add(BusinessForm form,BusinessProfile p)
    {
        var type=form.Select("Features_StoreType",p,nameof(p.BusinessType),("grocery","Features_Grocery"),("supermarket","Features_Supermarket"),("retail","Features_Retail"),("pharmacy","Features_Pharmacy"),("electronics","Features_Electronics"),("mobile","Features_Mobile"),("other","Payment_Other"));
        form.Check("Features_Loose",p,nameof(p.EnableLooseGoods));form.Check("Features_Packaging",p,nameof(p.EnablePackagedUnits));
        form.Check("Features_Wallet",p,nameof(p.EnableDigitalWalletServices));form.Check("Features_Recharge",p,nameof(p.EnableMobileRecharge));
        form.Check("Features_CatalogImages",p,nameof(p.EnableCatalogImages));
        var catalog=form.Select("Pharmacy_Template",p,nameof(p.MedicineTemplate),("none","Pharmacy_NoTemplate"),("pakistan-drap","Pharmacy_DrapTemplate"));
        void Availability(){foreach(var field in new[]{nameof(p.EnableLooseGoods),nameof(p.EnableDigitalWalletServices),nameof(p.EnableMobileRecharge)})((System.Windows.FrameworkElement)form.Fields[field].Parent).Visibility=p.IsPharmacy?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;form.Fields[nameof(p.EnablePackagedUnits)].IsEnabled=!p.IsPharmacy;((System.Windows.FrameworkElement)catalog.Parent).Visibility=p.IsPharmacy?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;catalog.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.UpdateTarget();}
        type.SelectionChanged+=(_,_)=>{p.ApplyBusinessDefaults();foreach(var check in form.Fields.Values.OfType<CheckBox>())check.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();Availability();};Availability();
    }
}
