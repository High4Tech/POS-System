using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace SupermarketPos.Infrastructure.Commercial;

public sealed record DiagnosticReport(string AppVersion, string Os, string Category, string Module, string Frames, string At);
public sealed class Diagnostics(string directory)
{
    public void TryCapture(Exception exception,string module)
    {
        try { Capture(exception,module); }
        catch (Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { /* Diagnostics must never prevent recovery. */ }
    }
    public DiagnosticReport Capture(Exception? exception, string module)
    {
        // Deliberately omit exception messages, parameters, file paths and line text: these often contain customer data.
        var frames = exception is null ? "" : string.Join('\n', new StackTrace(exception, false).GetFrames()?.Take(24).Select(f =>
            f.GetMethod() is { } m ? $"{m.DeclaringType?.FullName}.{m.Name}" : "") ?? []);
        var report = new DiagnosticReport(Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0",
            Environment.OSVersion.VersionString, exception?.GetType().Name ?? "UserReport", module, frames, DateTimeOffset.UtcNow.ToString("O"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(report, SignedDocuments.Json));
        foreach (var old in new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(f => f.CreationTimeUtc).Skip(100)) old.Delete();
        return report;
    }
    public DiagnosticReport Latest() => Directory.Exists(directory) && new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(f => f.CreationTimeUtc).FirstOrDefault() is { } file
        ? JsonSerializer.Deserialize<DiagnosticReport>(File.ReadAllText(file.FullName), SignedDocuments.Json)! : Capture(null, "support");
    public async Task SubmitAsync(LicenseClient license, DiagnosticReport report, string description, bool consent, CancellationToken ct = default)
    {
        if (!consent) throw new CommercialException("Support_ConsentRequired");
        if (description.Length > 2000) throw new CommercialException("Setup_Invalid");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var result = await http.PostAsJsonAsync(license.Options.Endpoint("/v1/reports"), new { certificate = license.Certificate,
            diagnostics = report, description, consent = true }, SignedDocuments.Json, ct);
        if (!result.IsSuccessStatusCode) throw new CommercialException("Support_Failed");
    }
}
