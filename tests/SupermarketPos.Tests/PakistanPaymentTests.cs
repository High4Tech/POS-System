using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
namespace SupermarketPos.Tests;
public sealed class PakistanPaymentTests
{
    [Theory][InlineData("03001234567")][InlineData("+92 300 1234567")][InlineData("923001234567")][InlineData("00923001234567")]
    public void PakistaniMobileFormatsNormalizeWithoutLosingDigits(string value)=>Assert.Equal("923001234567",PakistanPayment.Mobile(value));
    [Theory][InlineData("0300123456")][InlineData("030012345678")][InlineData("02001234567")][InlineData("+14155552671")][InlineData("03abcdefgh1")]
    public void InvalidMobileNumbersAreRejected(string value)=>Assert.Throws<DomainException>(()=>PakistanPayment.Mobile(value));
    [Fact]public void IbanNeedsPakistanStructureAndValidChecksum()
    {
        Assert.Equal("PK36SCBL0000001123456702",PakistanPayment.Recipient("pk36 scbl 0000 0011 2345 6702","Unknown bank"));
        Assert.Throws<DomainException>(()=>PakistanPayment.Recipient("PK37SCBL0000001123456702","Unknown bank"));
        Assert.Throws<DomainException>(()=>PakistanPayment.Recipient("PK36SCBL1123456702","Unknown bank"));
    }
    [Fact]public void AccountLengthsBelongToTheProviderAndKeepLeadingZeroes()
    {
        Assert.Equal("00123456789012",PakistanPayment.Recipient("0012-3456789012","Habib Bank Limited"));
        Assert.Equal("001234",PakistanPayment.Recipient("001234","JS Bank Limited"));
        Assert.Throws<DomainException>(()=>PakistanPayment.Recipient("1234567890123","Habib Bank Limited"));
        Assert.Throws<DomainException>(()=>PakistanPayment.Recipient("1234567890","Unknown bank"));
        Assert.Equal("0012345678",PakistanPayment.Recipient("0012345678","Custom bank","10,12"));
        Assert.Equal("923001234567",PakistanPayment.Recipient("03001234567","Easypaisa"));
    }
    [Fact]public void PercentageChargesRoundMoneyAndFixedChargesRemainFixed()
    {
        var provider=new ServiceProviderAccount{CustomerFeeMode="PERCENT",CustomerFeePercent=2};Assert.Equal(20,provider.CustomerCharge(1000));Assert.Equal(1578.90m,provider.CustomerCharge(78945));
        provider.CustomerFeeMode="FIXED";provider.DefaultCustomerFee=20;Assert.Equal(20,provider.CustomerCharge(1000));Assert.Equal(20,provider.CustomerCharge(2000));
    }
}
