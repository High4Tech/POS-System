using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task PerformanceChecks(IServiceProvider sp,string folder)
    {
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);
        var data=new Dictionary<string,object>();var clock=Stopwatch.StartNew();var setup=sp.GetRequiredService<IBusinessSetup>();var state=await setup.LoadAsync();state.Profile.BusinessType="pharmacy";state.Profile.MedicineTemplate="pakistan-drap";await setup.SaveDraftAsync(state.Profile,state.Step);
        var catalog=sp.GetRequiredService<MedicineCatalogService>();await catalog.EnsureSelectedAsync();data["catalogInitialImportMs"]=clock.Elapsed.TotalMilliseconds;
        var sample=await sp.GetRequiredService<PharmacyInventory>().QuickAddAsync(new(null,"Performance sample","PERF-123","tablet",12,0,true));
        var main=sp.GetRequiredService<MainViewModel>();clock.Restart();await main.ShowPosCommand.ExecuteAsync(null);data["registerDataLoadMs"]=clock.Elapsed.TotalMilliseconds;
        var pos=(PosViewModel)main.CurrentViewModel;var root=new Grid{Children={new PosView{DataContext=pos}}};var window=new Window{Width=1600,Height=1000,DataContext=main,Content=root};window.SetResourceReference(Control.FontSizeProperty,"TextSize14");System.Windows.Application.Current.MainWindow=window;
        clock.Restart();window.Show();Pump();data["registerFirstRenderMs"]=clock.Elapsed.TotalMilliseconds;
        var queryTimes=new List<double>();foreach(var term in new[]{"pana","moxi","amox","para","cef","moxi","pana","amox"}){clock.Restart();var result=await catalog.SearchAsync(term,20);queryTimes.Add(clock.Elapsed.TotalMilliseconds);if(result.Count>20)throw new Exception("Unbounded search");}data["catalogQueriesMs"]=queryTimes;
        clock.Restart();foreach(var tile in pos.Categories.Take(4))await pos.SelectCategoryCommand.ExecuteAsync(tile);Pump();data["fourCategoryLoadsAndRenderMs"]=clock.Elapsed.TotalMilliseconds;
        clock.Restart();await sp.GetRequiredService<ScannerController>().DispatchPhysicalAsync(sample.Barcode);Pump();data["knownScanAndCartRenderMs"]=clock.Elapsed.TotalMilliseconds;
        clock.Restart();for(var i=0;i<10;i++){pos.Lines.Single().Quantity=i+1;Pump();}data["tenCartEditsAndRenderMs"]=clock.Elapsed.TotalMilliseconds;
        var list=typeof(PosViewModel).GetProperty("RegisterList");if(list is null)data["registerViewSwitch"]="not implemented in baseline";else{clock.Restart();for(var i=0;i<6;i++){list.SetValue(pos,i%2==0);Pump();}data["sixRegisterViewSwitchesMs"]=clock.Elapsed.TotalMilliseconds;}
        clock.Restart();await main.ShowProductsCommand.ExecuteAsync(null);data["inventoryDataLoadMs"]=clock.Elapsed.TotalMilliseconds;var inventory=(ProductsViewModel)main.CurrentViewModel;
        clock.Restart();root.Children.Clear();root.Children.Add(new ProductsView{DataContext=inventory});Pump();data["inventoryFirstRenderMs"]=clock.Elapsed.TotalMilliseconds;
        clock.Restart();for(var i=0;i<10;i++){inventory.Filter=i%2==0?"Cable":"";Pump();}data["tenInventoryFiltersMs"]=clock.Elapsed.TotalMilliseconds;
        clock.Restart();await inventory.OpenProductAsync(sample.Id);Pump();data["productEditorOpenAndRenderMs"]=clock.Elapsed.TotalMilliseconds;
        data["catalogRows"]=(await catalog.StatusAsync())?.RowCount??0;data["storeProducts"]=inventory.Products.Count;data["environment"]=Environment.OSVersion.VersionString;data["processorCount"]=Environment.ProcessorCount;data["note"]="Isolated desktop harness, 1600x1000, English at 100%. Timings include awaited work and stated WPF layout; they are not process cold-start or phone hardware benchmarks.";
        File.WriteAllText(Path.Combine(folder,"performance.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));window.Close();Pump();Console.WriteLine(JsonSerializer.Serialize(data));
    }
}
