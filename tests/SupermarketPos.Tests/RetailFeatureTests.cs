using SupermarketPos.Domain.Business;

namespace SupermarketPos.Tests;
public sealed class RetailFeatureTests
{
    [Theory]
    [InlineData("grocery",true,true,false)] [InlineData("supermarket",true,true,false)]
    [InlineData("retail",false,true,false)] [InlineData("pharmacy",false,true,false)]
    [InlineData("electronics",false,false,false)] [InlineData("mobile",false,true,true)] [InlineData("other",false,false,false)]
    public void StoreDefaultsAreIndependentAndCanBeOverridden(string type,bool loose,bool packs,bool recharge)
    {
        var p=new BusinessProfile{BusinessType=type};p.ApplyBusinessDefaults();Assert.Equal(loose,p.EnableLooseGoods);Assert.Equal(packs,p.EnablePackagedUnits);Assert.Equal(recharge,p.EnableMobileRecharge);Assert.False(p.EnableDigitalWalletServices);
        p.EnableDigitalWalletServices=true;Assert.Equal(loose,p.EnableLooseGoods);Assert.True(p.EnableDigitalWalletServices);
        Assert.Equal(type!="pharmacy",p.DigitalWalletAvailable);
    }
    [Fact]public void PharmacyCannotEnableExcludedModulesThroughRawFlags()
    {
        var p=new BusinessProfile{BusinessType="Pharmacy",EnableLooseGoods=true,EnableDigitalWalletServices=true,EnableMobileRecharge=true};
        Assert.False(p.LooseGoodsAvailable);Assert.False(p.DigitalWalletAvailable);Assert.False(p.MobileRechargeAvailable);Assert.True(p.PackagingAvailable);
        p.EnforceBusinessFeatures();Assert.False(p.EnableLooseGoods);Assert.False(p.EnableDigitalWalletServices);Assert.False(p.EnableMobileRecharge);
    }
}
