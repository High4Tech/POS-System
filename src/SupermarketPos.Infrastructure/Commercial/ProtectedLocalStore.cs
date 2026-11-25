using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SupermarketPos.Infrastructure.Commercial;

// Current-user Windows DPAPI, not a shared application encryption key.
public sealed class ProtectedLocalStore(string directory)
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CommercialException("License_ProtectedStorage");
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
    private string PathFor(string name)
    {
        if (name != Path.GetFileName(name) || name.Contains('\\') || name.Contains('/')) throw new ArgumentException(nameof(name));
        return Path.Combine(directory, name + ".protected");
    }
    public string? Read(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? Encoding.UTF8.GetString(Protect(File.ReadAllBytes(path), false)) : null;
    }
    public void Write(string name, string value)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(name); var temp = path + ".tmp";
        File.WriteAllBytes(temp, Protect(Encoding.UTF8.GetBytes(value), true));
        File.Move(temp, path, true);
    }
    public static string DeviceHash()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var id = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(id)) throw new CommercialException("License_DeviceUnavailable");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("supermarketpos/device/v1/" + id))).ToLowerInvariant();
    }
}
