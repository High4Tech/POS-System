using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task RetailKeyboardFlow(IServiceProvider sp,string screenshots,bool digitalOnly=false)
    {
        var shell=sp.GetRequiredService<MainViewModel>();
        shell.IsLoggedIn=true;
        var view=new PosView();
        var window=new MainWindow(sp.GetRequiredService<PosStorage>()){DataContext=shell,Width=1366,Height=768};
        var template=new DataTemplate(typeof(PosViewModel)){VisualTree=new FrameworkElementFactory(typeof(PosView))};
        window.Resources.Add(template.DataTemplateKey,template);
        var productsTemplate=new DataTemplate(typeof(ProductsViewModel)){VisualTree=new FrameworkElementFactory(typeof(ProductsView))};
        window.Resources.Add(productsTemplate.DataTemplateKey,productsTemplate);
        var reportsTemplate=new DataTemplate(typeof(ReportsViewModel)){VisualTree=new FrameworkElementFactory(typeof(ReportsView))};
        window.Resources.Add(reportsTemplate.DataTemplateKey,reportsTemplate);
        var customersTemplate=new DataTemplate(typeof(CustomersViewModel)){VisualTree=new FrameworkElementFactory(typeof(CustomersView))};
        window.Resources.Add(customersTemplate.DataTemplateKey,customersTemplate);
        System.Windows.Application.Current.MainWindow=window;
        window.Show();
        Assert(window.Title.StartsWith(AppBrand.Name,StringComparison.Ordinal)&&window.Icon is not null,"Main app caption and icon use edition branding");
        await shell.ShowPosCommand.ExecuteAsync(null);
        Pump();
        if(digitalOnly){await DigitalCartChecks(sp,shell,window,screenshots);window.Close();return;}
        view=Descendants(window).OfType<PosView>().Single();
        var model=(PosViewModel)shell.CurrentViewModel;
        var search=Descendants(view).OfType<TextBox>().Single(text=>text.Name=="SearchBox");
        var table=Descendants(view).OfType<DataGrid>().Single(grid=>grid.Name=="CartTable");
        Assert(window.FindName("NavigationStoreName") is null,"Store name appears only in native caption");
        Assert(!model.IsPharmacy&&model.PharmacyWorkspace is null,"Tech Retail keeps pharmacy search separate");
        Assert(table.Columns.Take(4).Select(column=>column.Header).SequenceEqual(new object[]{
            LocalizationManager.Instance["Pos_Product"],LocalizationManager.Instance["Pos_Qty"],
            LocalizationManager.Instance["Products_Unit"],LocalizationManager.Instance["Pos_CartPrice"]}),
            "Cart order is Product, Qty, Unit, Price");
        model.SearchText="Cable";
        Assert(model.CollectionCount==LocalizationManager.Instance.Format("Pos_ProductCount",model.ProductTiles.Count),
            "Product heading reports the filtered shop count");
        var product=model.ProductTiles.First().Product;
        await model.AddFromResultsCommand.ExecuteAsync(product);
        Pump();
        var line=model.SelectedCartLine!;
        var quantity=Descendants(table).OfType<TextBox>().Single(text=>Equals(text.Tag,"quantity"));
        Assert(quantity.IsKeyboardFocused&&quantity.SelectionLength==quantity.Text.Length,"Manual add focuses and selects Qty");
        quantity.Text="0";
        SendKey(quantity,Key.Enter);
        Assert(quantity.IsKeyboardFocused&&line.HasInvalidQuantity&&!model.OpenCashCommand.CanExecute(null),"Invalid Qty remains focused and blocks checkout");
        quantity.Text="2";
        SendKey(quantity,Key.Enter);
        Pump();
        Assert(line.Quantity==2&&!line.HasInvalidQuantity,"Qty Enter commits valid input");
        var price=Descendants(table).OfType<TextBox>().Single(text=>Equals(text.Tag,"price"));
        Assert(price.IsKeyboardFocused&&price.SelectionLength==price.Text.Length,"Single-unit Qty skips Unit and selects Price");
        SendKey(price,Key.Enter);
        Assert(search.IsKeyboardFocused,"Price Enter restores product search");
        var existing=line.Quantity;
        await model.AddFromResultsCommand.ExecuteAsync(product);
        Pump();
        Assert(model.Lines.Count==1&&line.Quantity==existing+1&&quantity.IsKeyboardFocused,"Repeated manual selection focuses the incremented line");
        search.Focus();
        var scanner=sp.GetRequiredService<ScannerController>();
        await scanner.DispatchPhysicalAsync(product.Barcode);
        Pump();
        Assert(search.IsKeyboardFocused,"Scan addition preserves scan/search focus");
        var more=(Button)window.FindName("MoreButton");
        more.Focus();
        SendKey(more,Key.OemQuestion);
        Assert(search.IsKeyboardFocused,"Slash outside an editor focuses search");
        SendKey(search,Key.OemQuestion);
        Assert(search.IsKeyboardFocused,"Slash inside a text field keeps normal editing focus");
        model.OpenCashCommand.Execute(null);Pump();
        Assert(!model.Customer.IncludeDetails,"Customer details start unchecked in checkout");
        var cash=Descendants(view).OfType<TextBox>().Single(text=>text.Name=="CashAmountInput");
        Assert(cash.IsKeyboardFocused&&cash.SelectionLength==cash.Text.Length,"Cash starts with amount received selected");
        model.CancelPaymentCommand.Execute(null);Pump();
        Assert(search.IsKeyboardFocused,"Payment cancellation restores search");
        Capture(window,System.IO.Path.Combine(screenshots,"tech-retail-keyboard.png"));
        foreach(var item in model.Lines.ToArray())model.RemoveLineCommand.Execute(item);
        var medicine=new SupermarketPos.Domain.Entities.Product("UNIT-TEST","Unit conversion",12,0,
            SupermarketPos.Domain.Common.Unit.Piece,100,product.CategoryId);
        medicine.ConfigureMedicineUnits("tablet",10,110);
        var unitLine=new CartLineViewModel(new SupermarketPos.Domain.Pricing.CartLine(medicine,1,medicineUnit:"tablet"),()=>{},(_,_)=>true);
        model.Lines.Add(unitLine);
        Pump();
        quantity=Descendants(table).OfType<TextBox>().Single(text=>Equals(text.Tag,"quantity"));
        quantity.Focus();quantity.Text="2";SendKey(quantity,Key.Enter);Pump();
        var unit=Descendants(table).OfType<ComboBox>().Single(combo=>Equals(combo.Tag,"unit"));
        Assert(unit.IsKeyboardFocusWithin&&unit.IsDropDownOpen,"Multi-unit Qty enters the unit selector");
        unit.SelectedValue="strip";SendKey(unit,Key.Enter);Pump();
        Assert(unitLine.Line.UnitCode=="strip"&&unitLine.Line.GrossUnitPrice==110&&unitLine.Line.Quantity==20,
            "Unit selection keeps configured strip price and base-unit stock conversion");
        price=Descendants(table).OfType<TextBox>().Single(text=>Equals(text.Tag,"price"));
        Assert(price.IsKeyboardFocused,"Unit Enter advances to authorized Price");
        SendKey(price,Key.Enter);
        Assert(search.IsKeyboardFocused,"Multi-unit final Enter restores search");
        model.Lines.Clear();
        var restricted=new CartLineViewModel(new SupermarketPos.Domain.Pricing.CartLine(product,1),()=>{},(_,_)=>true,canEditPrice:false);
        model.Lines.Add(restricted);Pump();
        quantity=Descendants(table).OfType<TextBox>().Single(text=>Equals(text.Tag,"quantity"));
        quantity.Focus();SendKey(quantity,Key.Enter);
        Assert(search.IsKeyboardFocused,"Unauthorized price editing is skipped");
        model.Lines.Clear();
        await UnifiedRetailChecks(sp,shell,window,screenshots);
        var setup=sp.GetRequiredService<SupermarketPos.Application.Abstractions.IBusinessSetup>();
        var state=await setup.LoadAsync();
        await setup.SaveDraftAsync(state.Profile with{BusinessType="pharmacy",MedicineTemplate="none"},state.Step);
        await shell.ShowDashboardCommand.ExecuteAsync(null);
        await shell.ShowPosCommand.ExecuteAsync(null);
        Pump();
        model=(PosViewModel)shell.CurrentViewModel;
        model.SearchText="Isolated pharmacy entry";
        model.UseMedicineNameCommand.Execute(null);
        Pump();
        var pharmacyView=Descendants(window).OfType<PosView>().Single();
        var pharmacyQuantity=Descendants(pharmacyView).OfType<TextBox>().Single(text=>Equals(text.Tag,"quantity"));
        Assert(model.IsPharmacy&&pharmacyQuantity.IsKeyboardFocused&&pharmacyQuantity.SelectionLength==pharmacyQuantity.Text.Length,
            "Manual pharmacy entry uses the same Qty autofocus without exposing Tech Retail fields");
        foreach(var item in model.Lines.ToArray())model.RemoveLineCommand.Execute(item);
        await shell.ShowProductsCommand.ExecuteAsync(null);
        Pump();
        var products=(ProductsViewModel)shell.CurrentViewModel;
        var productCount=products.Products.Count;
        products.NewCommand.Execute(null);Pump();
        var editor=Descendants(window).OfType<Grid>().Single(grid=>grid.IsVisible&&grid.GetBindingExpression(UIElement.VisibilityProperty)?.ParentBinding.Path.Path==nameof(ProductsViewModel.IsEditorOpen));
        Assert(editor.IsKeyboardFocusWithin,"New product moves keyboard focus into its editor");
        SendEscape();Pump();
        Assert(!products.IsEditorOpen&&products.Products.Count==productCount,"Escape closes a new product without saving inventory");
        products.EditCategoriesCommand.Execute(null);Pump();
        Assert(products.IsCategoryEditorOpen,"Category editor opens");
        SendEscape();Pump();
        Assert(!products.IsCategoryEditorOpen,"Escape closes the category editor");
        foreach(var row in products.Products.Take(2))row.IsSelected=true;
        products.OpenBulkEditCommand.Execute(null);Pump();
        Assert(products.IsBulkEditorOpen,"Bulk editor opens for selected products");
        SendEscape();Pump();
        Assert(!products.IsBulkEditorOpen,"Escape closes bulk editing without saving");
        await CatalogOptionsChecks(sp,shell,window,screenshots);
        window.Close();
        Pump();

        void SendKey(FrameworkElement target,Key key) => target.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,PresentationSource.FromVisual(target)!,0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        void SendEscape() => ((UIElement)Keyboard.FocusedElement).RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
    }
}
