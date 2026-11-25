using System.IO;
using SupermarketPos.Domain.Business;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.Common;
public sealed class BusinessDocuments(PosStorage storage)
{
    private readonly ProtectedLocalStore _files=new(Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!,"Documents"));
    public LocalBusinessDocument Import(string source)
    {
        if(new FileInfo(source).Length>10_000_000||Path.GetExtension(source).ToLowerInvariant() is not (".pdf" or ".png" or ".jpg" or ".jpeg"))throw new CommercialException("Documents_Invalid");
        var token=Guid.NewGuid().ToString("N");
        _files.Write(token,Convert.ToBase64String(File.ReadAllBytes(source)));
        return new(token,Path.GetFileName(source));
    }
    public void Export(LocalBusinessDocument document,string destination)
    {
        var value=_files.Read(document.Token)??throw new CommercialException("Documents_Invalid");
        File.WriteAllBytes(destination,Convert.FromBase64String(value));
    }
}
