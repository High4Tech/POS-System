using System.Windows;
using System.Windows.Data;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.Common;

public static class WindowCaption
{
    public static readonly DependencyProperty NativeProperty=DependencyProperty.RegisterAttached("Native",typeof(bool),typeof(WindowCaption),new PropertyMetadata(false,Changed));
    public static bool GetNative(DependencyObject value)=>(bool)value.GetValue(NativeProperty);
    public static void SetNative(DependencyObject value,bool enabled)=>value.SetValue(NativeProperty,enabled);
    private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs e)
    {
        if(value is not Window window)return;
        if(e.NewValue is true){window.SourceInitialized+=Apply;window.Loaded+=Apply;}else{window.SourceInitialized-=Apply;window.Loaded-=Apply;}
    }
    private static void Apply(object? sender,System.EventArgs e)
    {
        if(sender is not Window window)return;
        // Keep OS chrome LTR; mirror only the application content.
        BindingOperations.ClearBinding(window,FrameworkElement.FlowDirectionProperty);window.FlowDirection=FlowDirection.LeftToRight;
        if(window.Content is FrameworkElement content)content.SetBinding(FrameworkElement.FlowDirectionProperty,new Binding(nameof(LocalizationManager.FlowDirection)){Source=LocalizationManager.Instance});
    }
}
