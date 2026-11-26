using System.Reflection;
using System.IO;
namespace SupermarketPos.Wpf.Common;
public static class RetailTestEdition
{
    public static bool Enabled {get;}=typeof(RetailTestEdition).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(a=>a.Key=="RetailTestEdition"&&a.Value=="true");
    public static string DefaultDataDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"High4TechPosTest","Data");
}
