using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SupermarketPos.Wpf.Common;

internal sealed class LayoutPreferences<T>(PosStorage storage, string filename) where T : class
{
    private readonly string _path = Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!, filename);

    public T? Read()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(_path)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("Could not read {0}: {1}", filename, error.Message);
            return null;
        }
    }

    public void Write(T value)
    {
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not save {0}: {1}", filename, error.Message);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
