using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Business;
namespace SupermarketPos.Tests;
public sealed partial class DigitalLedgerTests
{
    [Fact]
    public async Task UnsettledRecordsStillRequireAValidRecipient()
    {
        using var test=new TestDb();var(ledger,_)=await Fixture(test,true);var account=(await ledger.ProvidersAsync()).Single();
        var entry=CartEntry(account,"LOAD",100,10);entry.Status="Pending";entry.CustomerPhone="03001234";
        await Assert.ThrowsAsync<DomainException>(()=>ledger.SaveUnsettledAsync(entry));
        Assert.Empty(await ledger.HistoryAsync());Assert.Empty(await ledger.PostingsAsync());
    }
    [Theory][InlineData("Pending")][InlineData("Failed")]
    public async Task DirectUnsettledRecordsNeverPostCashOrIncome(string status)
    {
        using var test=new TestDb();var(ledger,_)=await Fixture(test,true);var account=(await ledger.ProvidersAsync()).Single();
        var entry=CartEntry(account,"LOAD",100,10);entry.Status=status;
        var saved=await ledger.SaveUnsettledAsync(entry);Assert.Equal(status,saved.Status);Assert.Empty(await ledger.PostingsAsync());
        Assert.Equal(saved.Id,(await ledger.SaveUnsettledAsync(entry)).Id);
        if(status=="Pending")
        {
            await ledger.SetStatusAsync(saved.Id,"Confirmed",false);await ledger.SetStatusAsync(saved.Id,"Confirmed",false);
            Assert.Single(await ledger.PostingsAsync());
        }
        else await Assert.ThrowsAsync<DomainException>(()=>ledger.SetStatusAsync(saved.Id,"Confirmed",false));
    }
    [Fact]
    public async Task PendingCompletionPostsOnTheCompletionDayRatherThanRewritingYesterday()
    {
        using var test=new TestDb();var(ledger,_)=await Fixture(test,true);var account=(await ledger.ProvidersAsync()).Single();
        var entry=CartEntry(account,"TRANSFER",100,10);entry.Status="Pending";entry.OccurredAt=DateTime.Today.AddDays(-1).AddHours(12);
        await ledger.SaveUnsettledAsync(entry);await ledger.SetStatusAsync(entry.Id,"Confirmed",false);
        Assert.Equal(0,(await ledger.ReportAsync(DateTime.Today.AddDays(-1),DateTime.Today)).CashChange);
        Assert.Equal(110,(await ledger.ReportAsync(DateTime.Today,DateTime.Today.AddDays(1))).CashChange);
        Assert.Equal(10,(await ledger.ReportAsync(DateTime.Today,DateTime.Today.AddDays(1))).Income);
        Assert.Equal(entry.OccurredAt.Date,DateTime.Today.AddDays(-1));
    }
}
