using SupermarketPos.Domain.Common;
namespace SupermarketPos.Tests;
public sealed class DigitalLinkTests
{
    [Fact]public void UncompressedGs1LinkPreservesGtinBatchExpiryAndSerialWithoutNetworkAccess()
    {
        const string code="https://id.example.com/products/01/09524000059109/10/ABC%2B1/21/S123?17=271231&utm_source=label";
        Assert.True(Gs1Barcode.IsMarked(code,null));var result=Gs1Barcode.Parse(code,new DateOnly(2026,11,20));Assert.NotNull(result);Assert.Equal("09524000059109",result.Gtin);Assert.Equal("ABC+1",result.Batch);Assert.Equal("S123",result.SerialNumber);Assert.Equal(new DateOnly(2027,12,31),result.ExpiryDate);
    }
    [Theory]
    [InlineData("https://example.com/01/09524000059109/21/S/10/L")]
    [InlineData("https://example.com/01/09524000059109?17=271231&17=281231")]
    [InlineData("https://user@example.com/01/09524000059109")]
    [InlineData("https://example.com/01/09524000059109/10/LOT%1D17271231")]
    [InlineData("https://example.com/01/09524000059108")]
    [InlineData("https://example.com/01/09524000059109?21=SN")]
    public void AmbiguousMalformedOrInjectedLinksAreRejected(string code)=>Assert.Null(Gs1Barcode.Parse(code,new DateOnly(2026,11,20)));
    [Fact]public void WebsiteQrIsNotMarkedAsMedicine()=>Assert.False(Gs1Barcode.IsMarked("https://example.com/shop",null));
}
