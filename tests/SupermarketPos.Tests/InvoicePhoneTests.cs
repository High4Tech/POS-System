using SupermarketPos.Domain.Common;
namespace SupermarketPos.Tests;
public sealed class InvoicePhoneTests
{
    [Theory][InlineData("03001234567","923001234567")][InlineData("+923001234567","923001234567")][InlineData("923001234567","923001234567")][InlineData("00923001234567","923001234567")][InlineData("+44 7700-900123","447700900123")][InlineData("00447700900123","447700900123")]
    public void SavedNumbersNormalizeWithoutAddingPakistanToForeignNumbers(string raw,string expected)=>Assert.Equal(expected,InvoicePhone.Normalize(raw));
    [Theory][InlineData("")][InlineData("123")][InlineData("0300ABC4567")][InlineData("+92+3001234567")][InlineData("0300123456789")]
    public void InvalidNumbersAreRejected(string raw)=>Assert.Throws<DomainException>(()=>InvoicePhone.Normalize(raw));
    [Fact]public void LocalForeignNumbersRequireExplicitCountry()=>Assert.Equal("447700900123",InvoicePhone.Normalize("07700900123","44"));
}
