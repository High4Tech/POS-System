using System.Windows;
using System.Windows.Controls;
namespace SupermarketPos.Wpf.Common;

public static class WindowCaption
{
    public static readonly DependencyProperty ActionProperty=DependencyProperty.RegisterAttached("Action",typeof(string),typeof(WindowCaption),new PropertyMetadata(null,Changed));
    public static string? GetAction(DependencyObject d)=>(string?)d.GetValue(ActionProperty);
    public static void SetAction(DependencyObject d,string value)=>d.SetValue(ActionProperty,value);
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e){if(d is Button button){button.Click-=Click;if(e.NewValue is string)button.Click+=Click;}}
    private static void Click(object sender,RoutedEventArgs e)
    {
        var button=(Button)sender;if(Window.GetWindow(button) is not{} window)return;
        switch(GetAction(button))
        {
            case "minimize":window.WindowState=WindowState.Minimized;break;
            case "maximize":window.SizeToContent=SizeToContent.Manual;window.MaxWidth=window.MaxHeight=double.PositiveInfinity;window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;break;
            case "close":window.Close();break;
        }
    }
}
