using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.Common;
public static class WindowsPublisherTrust
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct FileInfo { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File, Subject; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct TrustData
    {
        public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File; public uint Action; public IntPtr State, Url; public uint Flags, Context;
    }
    [DllImport("wintrust.dll", ExactSpelling=true)] private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
    public static void Verify(string path, string thumbprint)
    {
        var info = new FileInfo { Size=(uint)Marshal.SizeOf<FileInfo>(), Path=path };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(info,ptr,false);
            var data = new TrustData { Size=(uint)Marshal.SizeOf<TrustData>(), Ui=2, Choice=1, File=ptr, Flags=0x1000 }; // cache-only revocation for offline updates
            var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0) throw new CommercialException("Updates_Untrusted");
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var actual = thumbprint.Length == 64 ? certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) : certificate.Thumbprint;
            if (!actual.Equals(thumbprint, StringComparison.OrdinalIgnoreCase)) throw new CommercialException("Updates_Untrusted");
        }
        finally { Marshal.DestroyStructure<FileInfo>(ptr); Marshal.FreeHGlobal(ptr); }
    }
}
