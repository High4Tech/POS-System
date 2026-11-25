using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task RestorationInteractionChecks(IServiceProvider sp,string screens)
    {
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);var setup=sp.GetRequiredService<IBusinessSetup>();var state=await setup.LoadAsync();state.Profile.BusinessType="pharmacy";state.Profile.MedicineTemplate="pakistan-drap";await setup.SaveDraftAsync(state.Profile,state.Step);
        var inventoryService=sp.GetRequiredService<PharmacyInventory>();var sample=await inventoryService.QuickAddAsync(new(null,"QA strip medicine","RESTORE-1","tablet",12,0,false));sample.ConfigurePackaging("pack",30,null,null,330,null);sample.ConfigureMedicineUnits("tablet",10,115);await sp.GetRequiredService<PosDbContext>().SaveChangesAsync();await inventoryService.VerifyStockAsync(sample.Id,30,"QA counted opening stock");
        var main=sp.GetRequiredService<MainViewModel>();await main.ShowPosCommand.ExecuteAsync(null);var pos=(PosViewModel)main.CurrentViewModel;foreach(var line in pos.Lines.ToArray())pos.RemoveLineCommand.Execute(line);pos.PharmacyWorkspace?.CancelEntry();
        var view=new PosView{DataContext=pos};var root=new Grid{Children={view}};var host=new Window{Width=1600,Height=1000,DataContext=main,Content=root};host.SetResourceReference(Control.FontSizeProperty,"TextSize14");host.SetResourceReference(Control.FontFamilyProperty,"UiFont");System.Windows.Application.Current.MainWindow=host;host.Show();Pump();
        Assert(((ScrollViewer)view.FindName("CategoryStrip")).IsVisible&&pos.ShowRegisterCards,"Restored pharmacy categories/cards are present");await pos.AddFromResultsCommand.ExecuteAsync(sample);Assert(pos.Lines.Count==1&&!pos.PharmacyWorkspace!.HasPendingEntry,"A configured multi-unit medicine adds directly without a universal checklist");
        var lineVm=pos.Lines.Single();lineVm.Quantity=2;lineVm.SelectedSellingUnit="strip";Assert(lineVm.Line.Quantity==20&&lineVm.Quantity==2&&lineVm.Line.GrossUnitPrice==115&&lineVm.Line.GrossTotal==230,"Cart unit editing preserves displayed quantity and applies the configured strip price");
        lineVm.SelectedSellingUnit="pack";Assert(lineVm.SelectedSellingUnit=="strip"&&lineVm.Line.Quantity==20,"A unit change cannot exceed tracked stock");lineVm.Quantity=1;lineVm.SelectedSellingUnit="pack";Assert(lineVm.Line.Quantity==30&&lineVm.Line.GrossTotal==330,"One pack represents exactly thirty base tablets");lineVm.PriceText="350";Assert(sample.PackageSellingPrice==330,"Cart override does not overwrite the configured pack price");
        var category=pos.Categories.First(c=>c.Category.Id==sample.CategoryId);await pos.SelectCategoryCommand.ExecuteAsync(category);pos.SearchText="QA";var selected=pos.SelectedCategoryId;var quantity=lineVm.Line.Quantity;pos.RegisterList=true;Pump();Assert(((DataGrid)view.FindName("RegisterListTable")).IsVisible&&pos.SearchText=="QA"&&pos.SelectedCategoryId==selected&&lineVm.Line.Quantity==quantity,"Cards/List switching preserves category, query and cart");pos.RegisterList=false;Assert(!new CartLineViewModel(lineVm.Line,()=>{},(_,_)=>true,canEditPrice:false).CanEditPrice,"Pharmacy price editing respects its permission");pos.RemoveLineCommand.Execute(lineVm);
        var catalog=sp.GetRequiredService<MedicineCatalogService>();var reference=(await catalog.SearchAsync("moxiflox",1)).Single(m=>m.ProductId is null);var db=sp.GetRequiredService<PosDbContext>();var productsBefore=await db.Products.CountAsync();await pos.SelectMedicineCommand.ExecuteAsync(reference);var provisional=pos.Lines.Single();Assert(provisional.Line.Product.Id==0&&provisional.HasInvalidPrice&&!pos.OpenCashCommand.CanExecute(null),"Catalog-only selection stages a cart row and requires its missing price");Assert(await db.Products.CountAsync()==productsBefore,"Merely selecting a catalog medicine creates no inventory or stock record");
        provisional.SelectedSellingUnit="bottle";provisional.PriceText="65";Assert(!provisional.HasInvalidPrice&&provisional.Line.UnitCode=="bottle"&&pos.OpenCashCommand.CanExecute(null),"Missing price and base unit are entered inline in the cart");
        var salesBefore=await db.Sales.CountAsync();pos.OpenCashCommand.Execute(null);var payment=pos.CompletePaymentCommand.ExecuteAsync(null);var until=DateTime.UtcNow.AddSeconds(10);ReceiptPreviewWindow? receipt=null;while(receipt is null&&!payment.IsCompleted&&DateTime.UtcNow<until){Pump();receipt=Descendants(root).OfType<ReceiptPreviewWindow>().FirstOrDefault();await Task.Delay(10);}if(receipt is not null)receipt.RequestClose?.Invoke();await payment;
        Assert(await db.Sales.CountAsync()==salesBefore+1&&pos.Lines.Count==0,"A catalog-only cart completes through the existing payment and receipt flow");var stored=await db.Products.AsNoTracking().SingleAsync(p=>p.MedicineReferenceId==reference.ReferenceId);var sale=await db.Sales.AsNoTracking().OrderByDescending(s=>s.Id).FirstAsync();Assert(!stored.StockTracked&&stored.BaseStockQuantity==0&&stored.UnitPrice==65&&stored.SellingUnit=="bottle"&&sale.GrossProfit is null,"Provisional sale remembers its confirmed unit/price without fictional stock/cost/profit");
        await main.ShowProductsCommand.ExecuteAsync(null);var inventory=(ProductsViewModel)main.CurrentViewModel;inventory.NewCommand.Execute(null);Assert(inventory.IsEditorOpen&&!InAppDialog.IsOpen,"New Product opens the complete editor directly");inventory.Editor.Name="Validation test";inventory.Editor.Price="-1";inventory.Editor.VatPercent="0";root.Children.Clear();root.Children.Add(new ProductEditorView{DataContext=inventory});Pump();Assert(Descendants(root).OfType<TextBox>().Any(Validation.GetHasError),"Invalid product input is marked on its field");var countBefore=await db.Products.CountAsync();await inventory.SaveCommand.ExecuteAsync(null);Assert(inventory.IsEditorOpen&&inventory.Editor.Price=="-1"&&await db.Products.CountAsync()==countBefore,"Invalid product save preserves its draft and cannot write inventory");inventory.Editor.Price="25.00";Assert(inventory.Editor.FirstFieldError.Length==0,"Correcting the input clears its field validation: "+System.Text.Json.JsonSerializer.Serialize(new{inventory.Editor.Name,inventory.Editor.Price,inventory.Editor.Stock,inventory.Editor.VatPercent,inventory.Editor.MinimumStock,inventory.Editor.FirstFieldError}));inventory.CancelEditCommand.Execute(null);
        inventory.NewCommand.Execute(null);
        inventory.Editor.Name="QA name-only medicine";
        await inventory.SaveCommand.ExecuteAsync(null);
        var basic=await db.Products.AsNoTracking().SingleAsync(p=>p.Name=="QA name-only medicine");
        Assert(!basic.StockTracked&&!basic.SellingUnitConfigured&&basic.UnitPrice==0&&new ProductRowViewModel(basic,5).NeedsSetup,"Name-only medicine saves with missing unit and stock, and enters Needs Setup");
        await inventory.OpenProductAsync(basic.Id);
        inventory.Editor.MedicineUnit="vial";inventory.Editor.Price="110";
        await inventory.SaveCommand.ExecuteAsync(null);
        var completed=await db.Products.AsNoTracking().SingleAsync(p=>p.Id==basic.Id);
        Assert(completed.SellingUnitConfigured&&completed.SellingUnit=="vial"&&completed.UnitPrice==110&&!completed.StockTracked,"Completing prices edits the original record without inventing stock");
        inventory.NewCommand.Execute(null);inventory.Editor.Name="QA setup tablet";inventory.Editor.MedicineUnit="tablet";inventory.Editor.UnitsPerStrip="10";inventory.Editor.MedicinePackStrips="3";
        inventory.Editor.Price="12";inventory.Editor.StripPrice="100";inventory.Editor.PackagePrice="280";inventory.Editor.Stock="1";inventory.Editor.OpeningStockUnit="pack";
        await inventory.SaveCommand.ExecuteAsync(null);
        var opened=await db.Products.AsNoTracking().SingleAsync(p=>p.Name=="QA setup tablet");
        Assert(opened.BaseStockQuantity==30&&opened.UnitsPerPackage==30&&await db.StockMovements.CountAsync(m=>m.ProductId==opened.Id)==1,"One pack opens thirty tablets and records one movement");
        await inventory.OpenProductAsync(opened.Id);inventory.Editor.Price="15";
        await inventory.SaveCommand.ExecuteAsync(null);
        Assert((await db.Products.AsNoTracking().SingleAsync(p=>p.Id==opened.Id)).BaseStockQuantity==30&&await db.StockMovements.CountAsync(m=>m.ProductId==opened.Id)==1,"Price edits preserve the current stock and movement count");
        root.Children.Clear();root.Children.Add(new ProductsView{DataContext=inventory});Pump();Assert(((ScrollViewer)((ProductsView)root.Children[0]).FindName("InventoryFilters")).IsVisible,"Inventory filters are permanently visible");Assert(!Descendants(root).OfType<CheckBox>().Any(c=>Equals(c.Content,LocalizationManager.Instance["Pharmacy_CardView"]))&&!Descendants(root).OfType<Button>().Any(b=>Equals(b.Content,LocalizationManager.Instance["Bulk_SelectVisible"])),"Inventory has no Card View or Select Visible control");
        await inventory.OpenProductAsync(stored.Id);Assert(inventory.Editor.MedicineGeneric==reference.GenericName,"Editing a reference-linked product prefills available catalog composition");inventory.CancelEditCommand.Execute(null);
        foreach(var language in new[]{"en","ur"})
        {
            LocalizationManager.Instance.SetLanguage(language);sp.GetRequiredService<DesktopTextSize>().Save(200);var manage=inventory.ManageStockLocationsCommand.ExecuteAsync(null);Pump();for(var attempt=0;attempt<100&&Descendants(root).OfType<StockLocationsPanel>().Any(p=>!p.IsEnabled);attempt++){await Task.Delay(10);Pump();}
            Assert(Descendants(root).OfType<StockLocationsPanel>().Single().IsVisible,"Rack management remains in the existing app");CheckWindow(host,"racks-"+language+"-200");Capture(host,Path.Combine(screens,"racks-"+language+"-200.png"));var move=Descendants(root).OfType<Button>().Single(b=>b.IsVisible&&Equals(b.Content,LocalizationManager.Instance["Pharmacy_MoveStock"]));move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(Descendants(root).OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text==LocalizationManager.Instance["Pharmacy_FromLocation"]),"Stock transfer exposes explicit source and destination");Capture(host,Path.Combine(screens,"rack-transfer-"+language+"-200.png"));Descendants(root).OfType<Button>().Single(b=>b.IsVisible&&Equals(b.Content,LocalizationManager.Instance["Common_Close"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await manage;
        }
        host.Close();Pump();await SearchAndPanelChecks(sp);
        var window=new SupermarketPos.Wpf.MainWindow(sp.GetRequiredService<PosStorage>()){DataContext=main,Width=1366,Height=768};
        System.Windows.Application.Current.MainWindow=window;main.IsLoggedIn=true;
        await main.RefreshFeaturesAsync();window.Show();Pump();
        foreach(var width in new[]{1920,1366,1280,820})
        {
            window.Width=width;Pump();
            var bar=(DockPanel)window.FindName("NavigationBar");
            var strip=(StackPanel)window.FindName("Destinations");
            Assert(bar.IsVisible&&strip.Children.OfType<Button>().Any(b=>b.IsVisible),"The same top navigation works at "+width);
            Assert(!Descendants(window).OfType<GridSplitter>().Any(g=>g.Name.StartsWith("Sidebar")),"No sidebar returns on small windows");
            Assert(((Button)window.FindName("DigitalDestination")).Visibility==Visibility.Collapsed,"Pharmacy never exposes digital navigation");
        }
        window.Close();Pump();
        state.Profile.BusinessType="other";await setup.SaveDraftAsync(state.Profile,state.Step);await inventory.RefreshAsync();Assert(!inventory.Editor.IsPharmacy,"Non-pharmacy profile cannot inherit pharmacy-specific product fields");
    }
}
