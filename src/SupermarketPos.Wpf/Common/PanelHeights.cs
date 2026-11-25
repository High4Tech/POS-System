using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SupermarketPos.Wpf.Common;

public static class PanelHeights
{
    private static Dictionary<string,double[]> _sizes=new(StringComparer.Ordinal);
    private static LayoutPreferences<Dictionary<string,double[]>>? _file;
    public static void Load(PosStorage storage){_file=new(storage,"panel-heights.json");_sizes=_file.Read()??new(StringComparer.Ordinal);}
    public static readonly DependencyProperty KeyProperty=DependencyProperty.RegisterAttached("Key",typeof(string),typeof(PanelHeights),new PropertyMetadata(null,Changed));
    private static readonly DependencyProperty DefaultsProperty=DependencyProperty.RegisterAttached("Defaults",typeof(GridLength[]),typeof(PanelHeights));
    public static string? GetKey(DependencyObject target)=>(string?)target.GetValue(KeyProperty);
    public static void SetKey(DependencyObject target,string key)=>target.SetValue(KeyProperty,key);
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not Grid grid)return;
        grid.Loaded-=Restore;grid.Loaded+=Restore;
        if(args.OldValue is not null)return;
        grid.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,e)=>{if(e.OriginalSource is GridSplitter&&!e.Canceled)Save(grid);}),true);
        grid.AddHandler(Keyboard.KeyUpEvent,new KeyEventHandler((_,e)=>{if(e.OriginalSource is GridSplitter&&e.Key is Key.Up or Key.Down)Save(grid);}),true);
    }
    private static int[] Rows(Grid grid)=>grid.Children.OfType<GridSplitter>().SelectMany(s=>new[]{Grid.GetRow(s)-1,Grid.GetRow(s)+1}).Where(r=>r>=0&&r<grid.RowDefinitions.Count).Distinct().Order().ToArray();
    private static void Restore(object sender,RoutedEventArgs args)
    {
        if(sender is not Grid grid||GetKey(grid) is not{} key)return;
        foreach(var splitter in grid.Children.OfType<GridSplitter>()){splitter.MouseDoubleClick-=ResetOnDoubleClick;splitter.MouseDoubleClick+=ResetOnDoubleClick;}
        var rows=Rows(grid);foreach(var row in rows)grid.RowDefinitions[row].MinHeight=Math.Max(96,grid.RowDefinitions[row].MinHeight);if(grid.GetValue(DefaultsProperty) is null)grid.SetValue(DefaultsProperty,rows.Select(r=>grid.RowDefinitions[r].Height).ToArray());
        if(_sizes.TryGetValue(key,out var sizes)&&sizes is not null&&sizes.Length==rows.Length&&sizes.All(s=>double.IsFinite(s)&&s>0&&s<=1))
            for(var i=0;i<rows.Length;i++)grid.RowDefinitions[rows[i]].Height=new(sizes[i],GridUnitType.Star);
    }
    private static void Save(Grid grid)
    {
        if(GetKey(grid) is not{} key)return;grid.UpdateLayout();var sizes=Rows(grid).Select(r=>grid.RowDefinitions[r].ActualHeight).ToArray();var total=sizes.Sum();if(total<=0||sizes.Any(s=>s<=0))return;
        _sizes[key]=sizes.Select(s=>s/total).ToArray();_file?.Write(_sizes);
    }
    private static void ResetOnDoubleClick(object sender,MouseButtonEventArgs args){if(sender is GridSplitter{Parent:Grid grid}){Reset(grid);args.Handled=true;}}
    private static void Reset(Grid grid)
    {
        if(GetKey(grid) is not{} key||grid.GetValue(DefaultsProperty) is not GridLength[] defaults)return;
        var rows=Rows(grid);for(var i=0;i<Math.Min(rows.Length,defaults.Length);i++)grid.RowDefinitions[rows[i]].Height=defaults[i];_sizes.Remove(key);_file?.Write(_sizes);
    }
}
