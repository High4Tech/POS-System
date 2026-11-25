using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Business;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Infrastructure.Persistence;
namespace SupermarketPos.Tests;
public sealed class BackupScheduleTests
{
    private sealed class Clock:TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    [Theory][InlineData(false)][InlineData(true)]public async Task DailyBackupsAreConsistentAndNotRepeatedWithinADay(bool fail)
    {
        var folder=Path.Combine(Path.GetTempPath(),"pos-backup-schedule-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var destination=Path.Combine(folder,"Backups");if(fail)File.WriteAllText(destination,"Deliberately blocked test destination");
            await using(var db=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source="+Path.Combine(folder,"pos.db")).Options)){await db.Database.MigrateAsync();db.BusinessConfigurations.Add(new(){Completed=true,ProfileJson=JsonSerializer.Serialize(new BusinessProfile{DailyBackups=true,BackupFolder=destination})});await db.SaveChangesAsync();}
            var clock=new Clock();var schedule=new BackupSchedule(folder,clock);var first=await schedule.RunDueAsync();
            if(fail){Assert.Equal("Storage_AutoFailed",first.Error);Assert.Null(first.LastSuccess);Assert.True(File.Exists(Path.Combine(folder,"pos.db")));return;}
            Assert.NotNull(first.LastSuccess);Assert.True(File.Exists(first.File));Assert.Equal("",first.Error);Assert.Equal(first.File,(await schedule.RunDueAsync()).File);Assert.Single(Directory.GetFiles(destination,"*.zip"));clock.Now=clock.Now.AddDays(1);var second=await schedule.RunDueAsync();Assert.NotEqual(first.File,second.File);Assert.Equal(2,Directory.GetFiles(destination,"*.zip").Length);
            var extracted=Path.Combine(folder,"verified.db");using(var zip=System.IO.Compression.ZipFile.OpenRead(second.File)){using var input=zip.GetEntry("pos.db")!.Open();using var output=File.Create(extracted);await input.CopyToAsync(output);}await DataBackup.VerifyAsync(extracted);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
    }
}
