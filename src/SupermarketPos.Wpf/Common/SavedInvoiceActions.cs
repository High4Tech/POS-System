using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Common;
public sealed record InvoiceActionInfo(string Number,DateTime Date,decimal Total,string StoreName,string? CustomerName,string? Phone,string Language,string Paper,string Printer,string ExportFolder,string Template,string Revision="",Func<string,Task>? SavePhone=null,Func<string,Task>? SavePrinter=null);
public sealed class SavedInvoiceActions(ReceiptPreview preview,InvoiceActionInfo info,PosStorage storage)
{
    public ReceiptPreview Preview=>preview;
    public InvoiceActionInfo Info=>info;
    public string FileName=>"Invoice-"+new string(info.Number.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray())+".pdf";
    public string CachePath=>Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!,"PDFs",FileName);
    public string Message()
    {
        var template=string.IsNullOrWhiteSpace(info.Template)?Domain.Localization.PosText.Get("Invoice_DefaultMessage",Domain.Localization.PosText.Culture(info.Language)):info.Template;
        foreach(var pair in new[]{("{CustomerName}",info.CustomerName??""),("{StoreName}",info.StoreName),("{InvoiceNumber}",info.Number),("{InvoiceDate}",info.Date.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)),("{InvoiceTotal}",info.Total.ToString("N2",CultureInfo.InvariantCulture))})template=template.Replace(pair.Item1,pair.Item2,StringComparison.Ordinal);
        return template;
    }
    public async Task<string> EnsurePdfAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new{info.Number,info.Date,info.Total,info.StoreName,info.CustomerName,info.Phone,info.Language,info.Paper,info.Revision}))));
        var stamp=CachePath+".revision";
        if(File.Exists(CachePath)&&File.Exists(stamp))
        {
            var bytes=await File.ReadAllBytesAsync(CachePath);var hash=Convert.ToHexString(SHA256.HashData(bytes));
            if(await File.ReadAllTextAsync(stamp)==fingerprint+":"+hash&&bytes.Length>5&&Encoding.ASCII.GetString(bytes,0,5)=="%PDF-")return CachePath;
        }
        var temporary=CachePath+"."+Guid.NewGuid().ToString("N")+".tmp";await File.WriteAllBytesAsync(temporary,preview.Pdf);File.Move(temporary,CachePath,true);await File.WriteAllTextAsync(stamp,fingerprint+":"+Convert.ToHexString(SHA256.HashData(preview.Pdf)));return CachePath;
    }
    public async Task DownloadAsync(string destination)
    {
        var source=await EnsurePdfAsync();if(!Path.GetFullPath(source).Equals(Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))File.Copy(source,destination,true);
    }
}
