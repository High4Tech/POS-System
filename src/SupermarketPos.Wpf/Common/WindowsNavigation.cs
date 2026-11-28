using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SupermarketPos.Wpf.ViewModels;
namespace SupermarketPos.Wpf.Common;
internal sealed class WindowsNavigation:IDisposable
{
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr window,int id);
    internal static readonly Key[] Keys=[Key.H,Key.S,Key.D,Key.P,Key.T,Key.R,Key.C,Key.X,Key.Z];
    private readonly MainWindow _window;private readonly HashSet<int> _registered=[];private readonly HashSet<Key> _conflicts=[];private HwndSource? _source;
    internal bool IsConflict(Key key)=>_conflicts.Contains(key);
    internal WindowsNavigation(MainWindow window)
    {
        _window=window;window.SourceInitialized+=(_,_)=>{_source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);_source?.AddHook(Message);Refresh();};window.Activated+=(_,_)=>Refresh();window.Deactivated+=(_,_)=>Release();window.Closed+=(_,_)=>Dispose();
    }
    internal void Refresh()
    {
        Release();if(!_window.IsActive||_window.DataContext is not MainViewModel{IsLoggedIn:true})return;
        // Windows owns reserved combinations. Never install a keyboard hook to steal them; Alt alternatives remain available.
        if(AppContext.GetData("SupermarketPos.IsolatedDataDirectory") is true)return;
        _conflicts.Clear();var handle=new WindowInteropHelper(_window).Handle;if(handle==IntPtr.Zero)return;
        for(var i=0;i<Keys.Length;i++){if(_window.DataContext is MainViewModel model&&(Keys[i]==Key.D&&!model.DigitalServicesVisible||Keys[i] is Key.P or Key.T&&!model.CanManageStock))continue;if(RegisterHotKey(handle,0x5400+i,0x4008,(uint)KeyInterop.VirtualKeyFromKey(Keys[i])))_registered.Add(i);else _conflicts.Add(Keys[i]);}
    }
    private IntPtr Message(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message!=0x0312)return IntPtr.Zero;
        var id=wParam.ToInt64();
        if(id<0x5400||id>=0x5400+Keys.Length)return IntPtr.Zero;
        var index=(int)(id-0x5400);
        if(_registered.Contains(index)&&_window.IsActive&&!InAppDialog.IsOpen&&_window.DataContext is MainViewModel{IsLoggedIn:true} model){handled=Shortcuts.TryRun(_window,model,Keys[index],ModifierKeys.Windows);}
        return IntPtr.Zero;
    }
    private void Release(){var handle=new WindowInteropHelper(_window).Handle;foreach(var i in _registered)UnregisterHotKey(handle,0x5400+i);_registered.Clear();}
    public void Dispose(){Release();_source?.RemoveHook(Message);_source=null;}
    internal static bool Probe(Key key)
    {
        const int id=0x54FF;if(!RegisterHotKey(IntPtr.Zero,id,0x4008,(uint)KeyInterop.VirtualKeyFromKey(key)))return false;
        try{return true;}finally{UnregisterHotKey(IntPtr.Zero,id);}
    }
}
