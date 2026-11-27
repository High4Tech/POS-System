using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task UnifiedRetailChecks(IServiceProvider sp,MainViewModel shell,MainWindow window,string screenshots)
    {
        var pos=(PosViewModel)shell.CurrentViewModel;
        var editor=new ProductEditViewModel{LooseEnabled=true,PackagesEnabled=true};editor.LoadNew(1,0);editor.RetailType="loose";
        Assert(!editor.ShowBarcode&&editor.SellingMethod=="weight","Loose product type hides barcode and selects measured selling");editor.SellingMethod="piece";
        Assert(editor.RetailType=="loose"&&!editor.ShowBarcode,"Loose pieces stay loose without a barcode requirement");editor.RetailType="pack";
        Assert(editor.UsesPackaging&&editor.ShowBarcode,"Packaged product type enables packaging fields");editor.RetailType="labeled";editor.WarrantyEnabled=true;
        Assert(editor[nameof(editor.WarrantyMonths)].Length>0,"Enabled warranty requires its duration");editor.WarrantyMonths="12";Assert(editor[nameof(editor.WarrantyMonths)].Length==0,"Whole warranty months are accepted");editor.WarrantyEnabled=false;Assert(editor.WarrantyMonths=="0","Clearing warranty removes the configured duration");
        pos.SearchText="QA loose retail cable";Pump();Assert(pos.CanQuickAddProduct,"An unmatched name offers Add Product under search");
        await pos.SubmitSearchCommand.ExecuteAsync(null);Pump();var row=pos.SelectedCartLine!;
        Assert(row.CanChooseKind&&row.HasInvalidPrice,"A quick product is added directly to the cart and needs a selling price");
        row.ProductKind="loose";row.SelectedSellingUnit="kg";row.Quantity=1.25m;row.PriceText="120";
        Assert(row.Line.Product.Unit==Unit.Kilogram&&row.Quantity==1.25m,"Quick loose goods use an exact measured quantity");
        pos.SaveDraftCommand.Execute(null);pos.RemoveLineCommand.Execute(row);await pos.ResumeDraftCommand.ExecuteAsync(null);Pump();row=pos.SelectedCartLine??pos.Lines.Single();
        Assert(row.ProductKind=="loose"&&row.Line.Product.Unit==Unit.Kilogram&&row.PriceText=="120.00","Saved quick drafts restore type, unit and selling price");
        foreach(var item in pos.Lines.ToArray())pos.RemoveLineCommand.Execute(item);
        var inventory=sp.GetRequiredService<RetailInventory>();var legacy=await inventory.QuickAddAsync("QA old digital service","digital",price:100);
        await pos.InitializeAsync();Assert(!pos.ProductTiles.Any(t=>t.Product.Kind=="digital"),"Physical Sales excludes all digital records");
        Assert(!pos.CatalogKinds.Any(k=>k.Code=="digital"),"Physical Sales has no digital kind or filter");
        var nav=(StackPanel)window.FindName("Destinations");Assert(nav.Children.OfType<Button>().Take(3).Select(b=>b.Tag).SequenceEqual(new object[]{"dashboard","pos","digital"}),"Header begins Home, Sales, Digital Services");Assert(!nav.Children.OfType<Button>().Any(b=>Equals(b.Tag,"settings")),"Settings is in the right-side actions");
        await DigitalCartChecks(sp,shell,window,screenshots);
        await shell.ShowPosCommand.ExecuteAsync(null);Pump();
    }
}
