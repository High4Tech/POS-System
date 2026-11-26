using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task CatalogOptionsChecks(IServiceProvider sp,MainViewModel shell,MainWindow window,string screenshots)
    {
        var setup=sp.GetRequiredService<IBusinessSetup>();
        var thumbnail=(Style)window.FindResource("CatalogThumbnail");
        foreach(var type in new[]{"electronics","pharmacy","retail"})
        foreach(var images in new[]{false,true})
        {
            var state=await setup.LoadAsync();
            var profile=state.Profile with{BusinessType=type,EnableCatalogImages=images};profile.ApplyBusinessDefaults();
            await setup.SaveDraftAsync(profile,state.Step);
            await shell.RefreshFeaturesAsync();
            Assert(shell.StockAlerts.ShowProductImages==images,"Low-stock thumbnails share the saved image setting");
            foreach(var size in new[]{100,200})
            {
                sp.GetRequiredService<DesktopTextSize>().Save(size);
                await shell.ShowPosCommand.ExecuteAsync(null);Pump();
                var pos=(PosViewModel)shell.CurrentViewModel;
                pos.SearchText="";pos.SelectedCategoryId=0;Pump();
                var sales=Descendants(window).OfType<PosView>().Single();
                Assert(pos.ShowProductImages==images,"Catalog setting controls Sales images for "+type);
                Assert(Descendants(sales).OfType<ContentControl>().Any(c=>c.Style==thumbnail&&c.IsVisible)==images,"Sales thumbnails follow setting at "+size+"%");
                Assert(Descendants(sales).OfType<TextBox>().Any(c=>c.IsVisible),"Sales search remains visible with images "+images);
                Assert(pos.EnableMobileRecharge==(type=="electronics"),"Tech Retail recharge stays enabled independently of images");
                if(type=="electronics"&&!images&&size==100)
                {
                    await shell.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();
                    var service=Descendants(window).OfType<DigitalServicesView>().Single();
                    Assert(service.IsVisible&&shell.ActiveScreen=="digital","Digital Services has its own header workspace");
                    Assert(!Descendants(window).OfType<PosView>().Any(c=>c.IsVisible),"Services does not embed a second panel in Sales");
                    Capture(window,System.IO.Path.Combine(screenshots,"tech-digital-services.png"));
                    await shell.ShowPosCommand.ExecuteAsync(null);Pump();
                    Assert(ReferenceEquals(pos,shell.CurrentViewModel),"Returning from services preserves the register instance");
                }
                Capture(window,System.IO.Path.Combine(screenshots,$"sales-{type}-{images}-{size}.png"));
                await shell.ShowProductsCommand.ExecuteAsync(null);Pump();
                var inventory=(ProductsViewModel)shell.CurrentViewModel;
                var view=Descendants(window).OfType<ProductsView>().Single();
                Assert(inventory.ShowProductImages==images&&inventory.Editor.ShowProductImages==images,"Inventory and editor share image preference");
                Assert(Descendants(view).OfType<ComboBox>().Any(c=>c.IsVisible&&c.GetBindingExpression(ComboBox.SelectedValueProperty)?.ParentBinding.Path?.Path==nameof(ProductsViewModel.FilterCategoryId)),"Common inventory filters are visible for "+type);
                inventory.FilterCategoryId=inventory.Products.First().Product.CategoryId;
                Assert(inventory.VisibleProducts.Cast<ProductRowViewModel>().All(row=>row.Product.CategoryId==inventory.FilterCategoryId),"Category filter applies for "+type);
                inventory.ClearPharmacyFiltersCommand.Execute(null);
                inventory.NewCommand.Execute(null);Pump();
                var editor=Descendants(view).OfType<ProductEditorView>().Single(c=>c.IsVisible);
                Assert(Descendants(editor).OfType<TextBox>().Any(c=>c.IsVisible&&c.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="Editor.Price"),"Product price stays visible independently of images");
                Assert(Descendants(editor).OfType<ContentControl>().Any(c=>c.Style==thumbnail&&c.IsVisible)==images,"Product image chooser follows setting for "+type);
                if(size==200&&!images)Capture(window,System.IO.Path.Combine(screenshots,$"editor-{type}-200.png"));
                inventory.CancelEditCommand.Execute(null);Pump();
                Capture(window,System.IO.Path.Combine(screenshots,$"catalog-{type}-{images}-{size}.png"));
            }
        }
        var formProfile=new BusinessProfile();var form=new BusinessForm();BusinessFeatureFields.Add(form,formProfile);
        ((CheckBox)form.Fields[nameof(formProfile.EnableCatalogImages)]).IsChecked=true;
        Assert(formProfile.EnableCatalogImages,"Settings checkbox enables catalog images through its binding");
        var badge=new Border{Style=(Style)window.FindResource("Badge")};
        Assert(badge.CornerRadius==new CornerRadius(6),"Shared status badge has small rectangular corners");
        sp.GetRequiredService<DesktopTextSize>().Save(100);
    }
}
