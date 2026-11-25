using System.Text.Json;
using Microsoft.Data.Sqlite;
using SupermarketPos.Domain.Business;
namespace SupermarketPos.Infrastructure.Commercial;
public sealed record BackupScheduleStatus(DateTimeOffset? LastSuccess=null,DateTimeOffset? LastAttempt=null,string File="",string Error="");
public sealed class BackupSchedule(string dataDirectory,TimeProvider? clock=null)
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly TimeProvider _clock=clock??TimeProvider.System;
    private string StatePath=>Path.Combine(dataDirectory,"backup-status.json");
    public BackupScheduleStatus Status{get;private set;}=new();
    public BackupScheduleStatus ReadStatus()
    {
        try{Status=File.Exists(StatePath)?JsonSerializer.Deserialize<BackupScheduleStatus>(File.ReadAllText(StatePath))??new():new();}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){Status=new(Error:"Storage_AutoStatusInvalid");}
        return Status;
    }
    public async Task<BackupScheduleStatus> RunDueAsync(CancellationToken ct=default)
    {
        if(!await _gate.WaitAsync(0,ct))return Status;
        try
        {
            ReadStatus();var now=_clock.GetLocalNow();
            if(Status.LastSuccess is{} last&&last.ToOffset(now.Offset).Date==now.Date||Status.Error.Length>0&&Status.LastAttempt is{} attempt&&now-attempt<TimeSpan.FromHours(1))return Status;
            using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(dataDirectory,"pos.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await connection.OpenAsync(ct);using var command=connection.CreateCommand();command.CommandText="SELECT ProfileJson FROM BusinessConfigurations WHERE Completed=1 LIMIT 1";
            var json=await command.ExecuteScalarAsync(ct) as string;if(json is null)return Status;var profile=JsonSerializer.Deserialize<BusinessProfile>(json);if(profile?.DailyBackups!=true)return Status;
            Status=Status with{LastAttempt=now,Error=""};
            try
            {
                var folder=profile.BackupFolder.Length>0?profile.BackupFolder:Path.Combine(dataDirectory,"Backups");var target=Path.Combine(folder,"Daily-"+now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".zip");
                await DataBackup.CreateAsync(dataDirectory,target,ct);Status=new(now,now,target);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or SqliteException or CommercialException or ArgumentException or NotSupportedException){Status=Status with{Error="Storage_AutoFailed"};}
            var temporary=StatePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(Status),ct);File.Move(temporary,StatePath,true);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return Status;
        }
        finally{_gate.Release();}
    }
}
