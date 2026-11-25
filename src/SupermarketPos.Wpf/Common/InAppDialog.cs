using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SupermarketPos.Wpf.Common;

/// <summary>A modal layer in the existing window, without a second OS window.</summary>
public static class InAppDialog
{
    private static int _openCount;
    public static bool IsOpen=>_openCount>0;
    public static async Task ShowAsync(FrameworkElement content,Action<Action> setClose,Window? owner=null,double? contentWidth=null,
        bool sizeToContent=false,IInputElement? returnFocus=null)
    {
        owner??=System.Windows.Application.Current.MainWindow;
        if(owner.Content is not Panel root)throw new InvalidOperationException("The host must contain a panel.");
        var previous=returnFocus??Keyboard.FocusedElement;var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disabled=root.Children.OfType<UIElement>().Where(c=>c.IsEnabled).ToArray();foreach(var c in disabled)c.IsEnabled=false;
        var overlay=new Grid{Background=new SolidColorBrush(Color.FromArgb(110,15,23,42)),Margin=new Thickness(0)};
        Panel.SetZIndex(overlay,int.MaxValue);Grid.SetRowSpan(overlay,Math.Max(1,root is Grid g?g.RowDefinitions.Count:1));Grid.SetColumnSpan(overlay,Math.Max(1,root is Grid grid?grid.ColumnDefinitions.Count:1));
        Viewbox? fit=null;if(!sizeToContent&&contentWidth is{} width){fit=new Viewbox{Child=new Border{Width=width,Child=content}};fit.SetResourceReference(FrameworkElement.StyleProperty,"DialogFit");}
        var card=new Border{Child=fit is null?content:fit,Margin=new Thickness(20),MaxWidth=contentWidth+32??980,Padding=new Thickness(16),CornerRadius=new CornerRadius(16),Background=Brushes.White};
        void FitCard()
        {
            card.Width=Math.Min(contentWidth??620,Math.Max(0,root.ActualWidth-40));
            card.MaxHeight=Math.Min(root.ActualHeight*.78,Math.Max(0,root.ActualHeight-40));
        }
        void ResizeCard(object sender,SizeChangedEventArgs args)=>FitCard();
        if(sizeToContent)
        {
            card.HorizontalAlignment=HorizontalAlignment.Center;
            card.VerticalAlignment=VerticalAlignment.Center;
            FitCard();
            root.SizeChanged+=ResizeCard;
        }
        overlay.Children.Add(card);KeyboardNavigation.SetTabNavigation(overlay,KeyboardNavigationMode.Cycle);
        void Close(){done.TrySetResult();}
        void DismissOutside(object sender,MouseButtonEventArgs e)
        {
            if(e.ChangedButton!=MouseButton.Left||!ReferenceEquals(e.OriginalSource,overlay))return;
            e.Handled=true;Close();
        }
        void Escape(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}}
        void OwnerClosed(object? sender,EventArgs e)=>Close();
        setClose(Close);overlay.PreviewKeyDown+=Escape;overlay.MouseDown+=DismissOutside;owner.Closed+=OwnerClosed;_openCount++;root.Children.Add(overlay);
        overlay.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        try{await done.Task;}
        finally{_openCount--;owner.Closed-=OwnerClosed;if(sizeToContent)root.SizeChanged-=ResizeCard;root.Children.Remove(overlay);if(fit?.Child is Border wrapper)wrapper.Child=null;card.Child=null;foreach(var c in disabled)c.IsEnabled=true;if(previous is not null)Keyboard.Focus(previous);}
    }
}
