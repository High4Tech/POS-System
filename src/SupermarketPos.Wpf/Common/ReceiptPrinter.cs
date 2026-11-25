using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SupermarketPos.Domain.Common;

namespace SupermarketPos.Wpf.Common;
public interface IReceiptPrinter
{
    Task<string[]> PrintersAsync();
    Task PrintAsync(IReadOnlyList<byte[]> pages,string printer,string paper,string jobName);
}
public sealed class ReceiptPrinter:IReceiptPrinter
{
    private static int _activeJobs;
    public static bool IsPrinting=>Volatile.Read(ref _activeJobs)>0;
    public Task<string[]> PrintersAsync()=>Sta(()=>{using var server=new LocalPrintServer();return server.GetPrintQueues(new[]{EnumeratedPrintQueueTypes.Local,EnumeratedPrintQueueTypes.Connections}).Select(q=>q.FullName).ToArray();});
    public async Task PrintAsync(IReadOnlyList<byte[]> pages,string printer,string paper,string jobName)
    {Interlocked.Increment(ref _activeJobs);try{await Sta(()=>
    {
        if(string.IsNullOrWhiteSpace(printer))throw new DomainException("Invoice_PrinterMissing");
        using var server=new LocalPrintServer();using var queue=server.GetPrintQueues(new[]{EnumeratedPrintQueueTypes.Local,EnumeratedPrintQueueTypes.Connections}).FirstOrDefault(q=>q.FullName.Equals(printer,StringComparison.OrdinalIgnoreCase))??throw new DomainException("Invoice_PrinterMissing");
        queue.Refresh();
        var bad=PrintQueueStatus.Error|PrintQueueStatus.Offline|PrintQueueStatus.PaperJam|PrintQueueStatus.PaperOut|PrintQueueStatus.NotAvailable|PrintQueueStatus.Paused|PrintQueueStatus.UserIntervention;
        if((queue.QueueStatus&bad)!=0)throw new DomainException("Invoice_PrinterUnavailable");
        var ticket=queue.DefaultPrintTicket?.Clone()??new PrintTicket();ticket.PageOrientation=PageOrientation.Portrait;
        var width=paper=="A4"?793.7:paper=="58"?219.21:302.36;
        var doc=new FixedDocument();
        foreach(var bytes in pages)
        {
            using var source=new MemoryStream(bytes);var bitmap=BitmapFrame.Create(source,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            var height=paper=="A4"?1122.52:Math.Max(144,width*bitmap.PixelHeight/bitmap.PixelWidth);
            var fixedPage=new FixedPage{Width=width,Height=height,Background=Brushes.White};
            var image=new Image{Source=bitmap,Width=width-8,Height=height-8,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Top};FixedPage.SetLeft(image,4);FixedPage.SetTop(image,4);fixedPage.Children.Add(image);
            var page=new PageContent();((IAddChild)page).AddChild(fixedPage);doc.Pages.Add(page);
            if(paper!="A4")ticket.PageMediaSize=new PageMediaSize(width,height);else ticket.PageMediaSize=new PageMediaSize(PageMediaSizeName.ISOA4);
        }
        if(doc.Pages.Count==0)throw new DomainException("Invoice_PrintFailed");
        queue.CurrentJobSettings.Description=jobName;
        PrintQueue.CreateXpsDocumentWriter(queue).Write(doc.DocumentPaginator,ticket);
        queue.Refresh();if((queue.QueueStatus&bad)!=0)throw new DomainException("Invoice_PrinterUnavailable");return true;
    });}finally{Interlocked.Decrement(ref _activeJobs);}}
    private static Task<T> Sta<T>(Func<T> action)
    {
        var complete=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{try{complete.SetResult(action());}catch(Exception e){complete.SetException(e);}}){IsBackground=true,Name="POS receipt printing"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return complete.Task;
    }
}
