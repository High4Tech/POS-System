using System.Windows;

namespace SupermarketPos.Wpf.Common;

public static class DialogBounds
{
    public static readonly DependencyProperty FitOwnerProperty = DependencyProperty.RegisterAttached(
        "FitOwner", typeof(bool), typeof(DialogBounds), new PropertyMetadata(false, OnFitOwnerChanged));

    public static bool GetFitOwner(DependencyObject element) => (bool)element.GetValue(FitOwnerProperty);
    public static void SetFitOwner(DependencyObject element, bool value) => element.SetValue(FitOwnerProperty, value);

    private static void OnFitOwnerChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Window window) return;
        if (e.NewValue is true) window.SourceInitialized += FitToOwner;
        else window.SourceInitialized -= FitToOwner;
    }

    private static void FitToOwner(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        // Callers set Owner after XAML initialization. Its measured size is available when this window opens.
        var owner = window.Owner;
        var width = owner is { ActualWidth: > 0 } ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var height = owner is { ActualHeight: > 0 } ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        window.MaxWidth = width;
        window.MaxHeight = height;
    }
}
