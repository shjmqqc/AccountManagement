using System.Security.Principal;

namespace AccountManagement.Services;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    public bool IsOwner { get; }

    internal SingleInstance(string? name = null)
    {
        name ??= @"Local\Shjmqqc.AccountManagement." + WindowsIdentity.GetCurrent().User!.Value;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        mutex = new Mutex(false, name);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }

    public void RequestActivation() => activation.Set();
    public bool TakeActivation() => activation.WaitOne(0);

    public void Dispose()
    {
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
        activation.Dispose();
    }
}
