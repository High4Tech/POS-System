using System.Runtime.InteropServices;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace SupermarketPos.Infrastructure.Documents;

/// <summary>Renders an EAN-13 barcode (with human-readable digits) to a PNG byte array.</summary>
public static class BarcodeImage
{
    public static byte[]? TryRenderEan13(string rawBarcode, int width = 300, int height = 110)
    {
        if(rawBarcode.Length is not (12 or 13)||!rawBarcode.All(char.IsAsciiDigit))return null;
        return Render(rawBarcode,BarcodeFormat.EAN_13,width,height);
    }
    public static byte[]? TryRenderCode128(string barcode,int width=600,int height=150)=>Render(barcode,BarcodeFormat.CODE_128,width,height);
    public static byte[]? TryRenderQr(string barcode,int size=300)=>Render(barcode,BarcodeFormat.QR_CODE,size,size);
    private static byte[]? Render(string content,BarcodeFormat format,int width,int height)
    {

        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new EncodingOptions { Width = width, Height = height, Margin = format==BarcodeFormat.CODE_128?16:4, PureBarcode = false }
        };

        try
        {
            var pixels = writer.Write(content);
            using var bitmap = new SKBitmap(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            Marshal.Copy(pixels.Pixels, 0, bitmap.GetPixels(), pixels.Pixels.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch
        {
            return null; // unencodable barcode — label falls back to text only
        }
    }
}
