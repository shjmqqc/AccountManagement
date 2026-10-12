using System.Diagnostics;
using System.Reflection;
using AccountManagement.Services;

internal static class SingleInstanceChecks
{
    internal static int Probe(string name, bool hold)
    {
        using var instance = new SingleInstance(name);
        if (!instance.IsOwner) { instance.RequestActivation(); return 10; }
        if (hold) { Console.WriteLine("READY"); Console.Out.Flush(); Console.ReadLine(); }
        return 0;
    }

    internal static void Run(Action<bool, string> check)
    {
        string name = @"Local\AccountManagement.Test." + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstance(name))
        {
            check(first.IsOwner, "First instance owns lock");
            using var second = Start(name, false);
            check(second.WaitForExit(15000) && second.ExitCode == 10, "Second process exits without taking lock");
            check(first.TakeActivation(), "Second process requests existing window activation");
        }
        using (var restarted = new SingleInstance(name)) check(restarted.IsOwner, "Normal exit releases lock");
        using var crashed = Start(name, true);
        try
        {
            check(crashed.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult() == "READY", "Crash probe acquired lock");
            // Keep a handle open to verify recovery of an abandoned mutex, not just a new name.
            using var observer = new SingleInstance(name);
            check(!observer.IsOwner, "Live process retains ownership");
            crashed.Kill(); check(crashed.WaitForExit(15000), "Crash probe exits");
            using var recovered = new SingleInstance(name);
            check(recovered.IsOwner, "Abandoned lock recovered after crash");
        }
        finally { if (!crashed.HasExited) { crashed.Kill(); crashed.WaitForExit(); } }
    }

    private static Process Start(string name, bool hold)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
        if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(hold ? "--instance-hold" : "--instance-probe");
        start.ArgumentList.Add(name);
        return Process.Start(start)!;
    }
}
