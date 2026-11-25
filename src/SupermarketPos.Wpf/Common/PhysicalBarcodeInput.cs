using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SupermarketPos.Wpf.ViewModels;
namespace SupermarketPos.Wpf.Common;
public static class PhysicalBarcodeInput
{
    public static void Attach(Window window,ScannerController scanner)
    {
        var text=new StringBuilder();long last=0;
        // Focused input fields keep their own capture/Enter behavior. Elsewhere,
        // accept the rapid character burst and Enter produced by a keyboard wedge.
        window.PreviewTextInput+=(_,e)=>
        {
            if(Keyboard.FocusedElement is TextBoxBase or PasswordBox||InAppDialog.IsOpen){text.Clear();return;}
            var now=Environment.TickCount64;if(now-last>80)text.Clear();last=now;if(text.Length+e.Text.Length<=512)text.Append(e.Text);else text.Clear();
        };
        window.PreviewKeyDown+=async(_,e)=>
        {
            if(e.Key!=Key.Enter||Keyboard.FocusedElement is TextBoxBase or PasswordBox||text.Length<4||Environment.TickCount64-last>150)return;
            var barcode=text.ToString();text.Clear();e.Handled=true;await scanner.DispatchPhysicalAsync(barcode);
        };
        window.Deactivated+=(_,_)=>text.Clear();
    }
}
