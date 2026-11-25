using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SupermarketPos.Domain.Common;

namespace SupermarketPos.Wpf.Common;
public interface IInvoiceHandoff{bool HasDesktopHandler();bool Open(Uri uri);}
public sealed class WindowsInvoiceHandoff:IInvoiceHandoff
{
    [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]private static extern uint AssocQueryString(uint flags,uint query,string association,string? extra,StringBuilder value,ref uint length);
    public bool HasDesktopHandler(){var value=new StringBuilder(2048);uint length=2048;return AssocQueryString(0x1000,2,"whatsapp",null,value,ref length)==0&&value.Length>0;}
    public bool Open(Uri uri){try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});return true;}catch{return false;}}
}
public sealed class WhatsAppShareService(IInvoiceHandoff? handoff=null,Func<bool>? networkAvailable=null)
{
    private readonly IInvoiceHandoff _handoff=handoff??new WindowsInvoiceHandoff();
    private readonly Func<bool> _network=networkAvailable??System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable;
    public static Uri DesktopUri(string phone,string message)=>new($"whatsapp://send?phone={InvoicePhone.Normalize(phone)}&text={Uri.EscapeDataString(message)}");
    public static Uri BrowserUri(string phone,string message)=>new($"https://web.whatsapp.com/send?phone={InvoicePhone.Normalize(phone)}&text={Uri.EscapeDataString(message)}");
    public string Open(string phone,string message,bool browserOnly=false)
    {
        if(!_network())throw new DomainException("Invoice_Offline");
        if(!browserOnly&&_handoff.HasDesktopHandler()&&_handoff.Open(DesktopUri(phone,message)))return "Invoice_DesktopOpened";
        if(_handoff.Open(BrowserUri(phone,message)))return "Invoice_BrowserOpened";
        throw new DomainException("Invoice_OpenFailed");
    }
}
