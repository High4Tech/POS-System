using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Common;

namespace SupermarketPos.Application.Services;

public sealed record BarcodeMatch(Product Product, decimal? Quantity,bool Packaged=false);

public sealed class RegisterBarcodeLookup(IProductRepository products, IBarcodeService barcodes)
{
    public async Task<BarcodeMatch?> FindGs1Async(Gs1Barcode gs1, CancellationToken ct = default)
    {
        var product = await FindTradeItemAsync(gs1.Gtin, ct);
        var packaged=product?.PackageBarcode is{} box&&box.PadLeft(14,'0')==gs1.Gtin.PadLeft(14,'0');
        return product is null ? null : new BarcodeMatch(product,packaged?product.UnitsPerPackage:1m,packaged);
    }

    public async Task<Product?> FindTradeItemAsync(string code, CancellationToken ct = default)
    {
        if (!Gs1Barcode.HasValidCheckDigit(code)) return null;
        var gtin = code.PadLeft(14, '0');
        foreach (var length in new[] { 14, 13, 12, 8 })
        {
            var omitted = 14 - length;
            if (gtin[..omitted].Any(c => c != '0')) continue;
            if (await products.GetByBarcodeAsync(gtin[omitted..], ct) is { } product)
                return product;
        }
        return null;
    }

    public async Task<BarcodeMatch?> FindAsync(string raw, CancellationToken ct = default)
    {
        var code = barcodes.Normalize(raw);
        if (code.Length == 0) return null;

        if (barcodes.ParseWeight(code) is { } weight &&
            await products.GetByPluAsync(weight.Plu, ct) is { } weighed)
            return new BarcodeMatch(weighed, weight.WeightKg);

        var exact = await products.GetByBarcodeAsync(code, ct);
        if (exact is null && AlternateGtin(code) is { } alternate)
            exact = await products.GetByBarcodeAsync(alternate, ct);
        var packaged=exact?.PackageBarcode is{} box&&(box==code||box==AlternateGtin(code));
        return exact is null ? null : new BarcodeMatch(exact,packaged?exact.UnitsPerPackage:null,packaged);
    }

    private string? AlternateGtin(string code)
    {
        if (!code.All(char.IsAsciiDigit)) return null;
        var ean = code.Length == 12 ? "0" + code : code;
        if (ean.Length != 13 || ean[0] != '0' || barcodes.Ean13CheckDigit(ean[..12]) != ean[12] - '0')
            return null;
        return code.Length == 12 ? ean : ean[1..];
    }
}
