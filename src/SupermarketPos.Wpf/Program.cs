namespace SupermarketPos.Wpf;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();
        using var instance = new Mutex(true, "Local\\SupermarketPosDesktop", out var firstInstance);
        if (!firstInstance) return;
        var pathIndex = Array.IndexOf(args, "--data-directory");
        if (pathIndex >= 0 && args.Length > pathIndex + 1)
        {
            AppContext.SetData("SupermarketPos.DataDirectory", args[pathIndex + 1]);
            AppContext.SetData("SupermarketPos.IsolatedDataDirectory",true);
        }
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
