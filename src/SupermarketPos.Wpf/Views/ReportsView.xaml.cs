using System.Windows.Controls;

namespace SupermarketPos.Wpf.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();Loaded+=(_,_)=>
        {
            if(System.Windows.Window.GetWindow(this)?.DataContext is not ViewModels.MainViewModel shell)return;
            DigitalHistoryTab.Visibility=DigitalReportTab.Visibility=shell.DigitalServicesVisible?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
            if(shell.DigitalServicesVisible){DigitalHistory.Content??=shell.DigitalSection("History");DigitalReport.Content??=shell.DigitalSection("Reports");}
        };
    }
}
