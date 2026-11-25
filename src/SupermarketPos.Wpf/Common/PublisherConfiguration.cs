using System.Reflection;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.Common;
public static class PublisherConfiguration
{
    public static PublisherOptions Read()
    {
        var metadata = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value ?? "");
        string Value(string key) => metadata.GetValueOrDefault(key, "");
        var commercial = bool.TryParse(Value("CommercialBuild"),out var enabled)&&enabled;
        var channel = Value("ReleaseChannel");
        var publicKey = (!commercial ? Environment.GetEnvironmentVariable("POS_DEV_PUBLIC_KEY") ?? Value("PublisherPublicKey") : Value("PublisherPublicKey")).Replace("\\n", "\n");
        if (publicKey.Length > 0 && !publicKey.Contains("BEGIN PUBLIC KEY")) publicKey = "-----BEGIN PUBLIC KEY-----\n" + publicKey + "\n-----END PUBLIC KEY-----";
        return new(!commercial ? Environment.GetEnvironmentVariable("POS_DEV_API") ?? Value("PublisherApi") : Value("PublisherApi"),
            publicKey, commercial,
            channel is "development" or "testing" or "stable" ? channel : commercial ? "stable" : "development", Support: Value("PublisherSupport"),
            PublishedAt: DateTimeOffset.TryParse(Value("ReleasePublishedAt"), out var published) ? published : null);
    }
}
