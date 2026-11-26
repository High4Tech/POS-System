using SupermarketPos.Domain.Business;

namespace SupermarketPos.Tests;
public sealed class RetailFeatureTests
{
    [Theory]
    [InlineData("grocery",true,true,false)] [InlineData("supermarket",true,true,false)]
    [InlineData("retail",false,true,false)] [InlineData("pharmacy",false,true,false)]
    [InlineData("electronics",false,false,true)] [InlineData("mobile",false,true,true)] [InlineData("other",false,false,false)]
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
    [Theory]
    [InlineData("electronics")][InlineData("pharmacy")][InlineData("retail")]
    public void CatalogImagesAreOptInAndPersistIndependentlyOfStoreType(string type)
    {
        var profile=new BusinessProfile{BusinessType=type};profile.ApplyBusinessDefaults();
        Assert.False(profile.EnableCatalogImages);
        profile.EnableCatalogImages=true;profile.EnforceBusinessFeatures();
        var saved=System.Text.Json.JsonSerializer.Deserialize<BusinessProfile>(System.Text.Json.JsonSerializer.Serialize(profile))!;
        Assert.True(saved.EnableCatalogImages);Assert.Equal(type,saved.BusinessType);
        saved.EnableCatalogImages=false;Assert.False(saved.EnableCatalogImages);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<BusinessProfile>("{}")!.EnableCatalogImages);
    }
}
