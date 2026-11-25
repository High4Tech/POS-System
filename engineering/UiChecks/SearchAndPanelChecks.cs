using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task SearchAndPanelChecks(IServiceProvider sp)
    {
        var search=new MedicineSearchBox(sp.GetRequiredService<MedicineCatalogService>());var other=new TextBox();var fields=new StackPanel();fields.Children.Add(search);fields.Children.Add(other);
        Grid MakeGrid()
        {
            var grid=new Grid();grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=new(12)});grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
            var splitter=new GridSplitter();splitter.SetResourceReference(FrameworkElement.StyleProperty,"HeightDivider");Grid.SetRow(splitter,1);grid.Children.Add(splitter);var bottom=new Border();Grid.SetRow(bottom,2);grid.Children.Add(bottom);PanelHeights.SetKey(grid,"qa-panels");return grid;
        }
        var grid=MakeGrid();grid.Children.Add(fields);var window=new Window{Content=grid,Width=1000,Height=850};window.Show();Pump();search.Input.Focus();
        var popup=(Popup)typeof(MedicineSearchBox).GetField("_popup",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(search)!;
        var matches=(ListBox)typeof(MedicineSearchBox).GetField("_matches",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(search)!;
        async Task Results(string term)
        {
            search.Input.Text=term;var until=DateTime.UtcNow.AddSeconds(5);while(DateTime.UtcNow<until&&(string?)typeof(MedicineSearchBox).GetField("_resultTerm",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(search)!=term){await Task.Delay(10);Pump();}
            Assert(matches.Items.Count>0&&popup.IsOpen&&search.Input.IsKeyboardFocusWithin,"Search stays editable while suggestions are visible: "+term);Assert(Mouse.Captured is null,"Suggestions never capture the mouse away from the input");
        }
        await Results("moxi");await Results("amox");search.Input.Text="cef";await Results("para");
        other.Focus();Pump();Assert(!popup.IsOpen,"Clicking/focusing another field dismisses suggestions");search.Input.Focus();await Results("moxi");var selected=0;search.Selected+=_=>selected++;matches.SelectedIndex=0;
        var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(search.Input),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};search.Input.RaiseEvent(key);Pump();Assert(selected==1&&!popup.IsOpen,"Keyboard selection closes suggestions once");await Results("amox");
        var splitter=grid.Children.OfType<GridSplitter>().Single();splitter.Focus();Pump();var before=grid.RowDefinitions[0].ActualHeight;var width=grid.ActualWidth;
        splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(splitter),0,Key.Up){RoutedEvent=Keyboard.KeyDownEvent});splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(splitter),0,Key.Up){RoutedEvent=Keyboard.KeyUpEvent});Pump();
        var height=grid.RowDefinitions[0].ActualHeight;Assert(height<before&&grid.RowDefinitions[2].ActualHeight>before,"Height divider changes only the two adjacent panel heights");Assert(Math.Abs(grid.ActualWidth-width)<.1,"Panel resizing never changes width");var ratio=height/(height+grid.RowDefinitions[2].ActualHeight);window.Close();Pump();
        PanelHeights.Load(sp.GetRequiredService<PosStorage>());grid=MakeGrid();window=new Window{Content=grid,Width=1000,Height=950};window.Show();Pump();var restored=grid.RowDefinitions[0].ActualHeight/(grid.RowDefinitions[0].ActualHeight+grid.RowDefinitions[2].ActualHeight);Assert(Math.Abs(restored-ratio)<.005,"Panel heights survive recreation and reload while fitting a changed screen height");grid.Children.OfType<GridSplitter>().Single().RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent});Pump();Assert(Math.Abs(grid.RowDefinitions[0].ActualHeight-grid.RowDefinitions[2].ActualHeight)<1,"Double-click resets the default panel heights");window.Close();Pump();
    }
}
