using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;
public sealed class MeasuredSaleWindow : Window
{
    private readonly ComboBox _product;
    private readonly ComboBox _mode;
    private readonly ComboBox _unit;
    private readonly TextBox _quantity=new(){Text="1",FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBlock _result=new(){TextWrapping=TextWrapping.Wrap};
    public Product? Product=>_product.SelectedItem as Product;
    public decimal Quantity {get;private set;}
    public bool Packaged=>_mode.SelectedValue as string=="package";
    private static string T(string key)=>LocalizationManager.Instance[key];
    public MeasuredSaleWindow(IReadOnlyList<Product> products,bool packages=false,bool byAmount=false)
    {
        Title=T(packages?"Measure_SellPack":"Measure_LooseItems");SetResourceReference(StyleProperty,"FormDialogWindow");WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FlowDirection=LocalizationManager.Instance.FlowDirection;SetResourceReference(BackgroundProperty,"BgBrush");
        var f=new BusinessForm();_product=new(){ItemsSource=products,DisplayMemberPath="Name",IsEditable=true,IsTextSearchEnabled=true};f.Add("Pos_Product",_product,true);
        _mode=new(){ItemsSource=packages?new[]{new BusinessForm.Choice("package",T("Measure_SellPack"))}:new[]{new BusinessForm.Choice("quantity",T("Measure_ByWeight")),new BusinessForm.Choice("amount",T("Measure_ByAmount"))},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedIndex=byAmount?1:0};f.Add("Measure_Method",_mode);
        _unit=new(){ItemsSource=packages?new[]{new BusinessForm.Choice("unit",T("Measure_pack"))}:new[]{new BusinessForm.Choice("unit",T("Measure_KgLitre")),new BusinessForm.Choice("base",T("Measure_GramMl"))},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedIndex=0};f.Add("Measure_InputUnit",_unit);
        f.Add("Measure_QuantityOrAmount",_quantity,true);
        var quick=new WrapPanel();foreach(var pair in new[]{("Measure_Kilo",1m),("Measure_Half",.5m),("Measure_Pao",.25m)})quick.Children.Add(BusinessForm.Button(pair.Item1,(_,_)=>{_mode.SelectedValue="quantity";_unit.SelectedValue="unit";_quantity.Text=pair.Item2.ToString(CultureInfo.InvariantCulture);Calculate();},"GhostButton"));
        if(!packages)f.Add("",quick,true);
        f.Add("",_result,true);f.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>DialogResult=false,"GhostButton"));f.Add("",BusinessForm.Button("Measure_Add",(_,_)=>{if(Calculate())DialogResult=true;}));
        _quantity.TextChanged+=(_,_)=>Calculate();_product.SelectionChanged+=(_,_)=>Calculate();_mode.SelectionChanged+=(_,_)=>Calculate();_unit.SelectionChanged+=(_,_)=>Calculate();
        var card=new Border{Width=800,Child=f};card.SetResourceReference(StyleProperty,"Card");var fit=new Viewbox{Child=card};fit.SetResourceReference(StyleProperty,"DialogFit");Content=fit;
    }
    private bool Calculate()
    {
        try
        {
            if(Product is not{} p)throw new DomainException("Purchase_ProductRequired");
            if(p.UnitPrice<=0)throw new DomainException("Measure_InvalidAmount");
            if(!decimal.TryParse(_quantity.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var entered)||entered<=0)throw new DomainException("Measure_InvalidQuantity");
            decimal charge;
            if(Packaged){if(entered!=decimal.Truncate(entered)||p.UnitsPerPackage<=1)throw new DomainException("Measure_InvalidPackage");Quantity=checked(entered*p.UnitsPerPackage);charge=Money.Round2(entered*(p.PackageSellingPrice??p.UnitPrice*p.UnitsPerPackage));}
            else if(_mode.SelectedValue as string=="amount"){var result=MeasuredGoods.ForAmount(entered,p.UnitPrice,p.Unit);Quantity=result.Quantity;charge=result.Charge;}
            else {Quantity=_unit.SelectedValue as string=="base"?entered/1000m:entered;_=MeasuredGoods.BaseQuantity(Quantity,p.Unit);charge=Money.Round2(Quantity*p.UnitPrice);}
            if(Quantity>p.StockQuantity)throw new InsufficientStockException(p.Name,p.StockQuantity,Quantity);
            _result.Text=$"{Quantity:0.###} {T(p.Unit==Unit.Kilogram?"Unit_Kg":p.Unit==Unit.Litre?"Measure_litre":"Unit_Piece")} · {Money.Format(charge)}";_result.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");return true;
        }
        catch(Exception e){_result.Text=T(e is DomainException?e.Message:"Common_InvalidNumber");_result.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");return false;}
    }
}
