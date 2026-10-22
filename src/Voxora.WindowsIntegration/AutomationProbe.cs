using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Voxora.WindowsIntegration;

/// <summary>
/// Runs UI Automation calls on a dedicated single threaded apartment that owns a message
/// pump. Automation reaches into other processes and can block for a long time when one of
/// them is busy, so the user interface never performs the call itself and never waits for it
/// without a limit. A probe that runs past its budget is abandoned and replaced.
/// </summary>
public sealed class AutomationProbe : IDisposable
{
    private readonly BlockingCollection<Action> queue = new(new ConcurrentQueue<Action>());
    private readonly object gate = new();
    private Thread? worker;
    private volatile bool disposed;

    [DllImport("user32.dll")] private static extern bool PeekMessage(out MESSAGE message, nint window, uint filterMin, uint filterMax, uint flags);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MESSAGE message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MESSAGE message);
    [StructLayout(LayoutKind.Sequential)] private struct MESSAGE { public nint window; public uint id; public nint wParam, lParam; public uint time; public int x, y; }

    /// <summary>
    /// Runs <paramref name="work"/> with a hard time limit. Returns null when the call did
    /// not finish in time or failed; the caller then continues without that information.
    /// </summary>
    public T? TryRun<T>(Func<T> work, TimeSpan timeout) where T : class
    {
        if (disposed) return null;
        EnsureWorker();
        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            queue.Add(() =>
            {
                try { completion.TrySetResult(work()); }
                catch (Exception error) { completion.TrySetException(error); }
            });
        }
        catch (ObjectDisposedException) { return null; }
        catch (InvalidOperationException) { return null; }
        if (!completion.Task.Wait(timeout))
        {
            // The worker is stuck inside another process. It cannot be recovered, so it is
            // left behind and a fresh one takes over the next request.
            Abandon();
            return null;
        }
        return completion.Task.GetAwaiter().GetResult();
    }

    private void EnsureWorker()
    {
        lock (gate)
        {
            if (disposed) return;
            if (worker is { IsAlive: true }) return;
            var thread = new Thread(Pump) { IsBackground = true, Name = "SineFlow automation" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            worker = thread;
        }
    }

    private void Abandon()
    {
        lock (gate)
        {
            worker = null;
        }
    }

    private void Pump()
    {
        try
        {
            while (!disposed)
            {
                Action work;
                try { work = queue.Take(); }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                try { work(); }
                catch (Exception) { /* a failed probe must never take the worker down */ }
                Drain();
            }
        }
        catch (Exception) { }
    }

    private static void Drain()
    {
        try
        {
            while (PeekMessage(out var message, nint.Zero, 0, 0, 1))
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        disposed = true;
        try { queue.CompleteAdding(); } catch (ObjectDisposedException) { }
    }
}
