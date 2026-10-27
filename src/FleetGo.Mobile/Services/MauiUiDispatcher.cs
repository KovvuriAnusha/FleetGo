using FleetGo.Mobile.Core.Threading;
using Microsoft.Maui.ApplicationModel;

namespace FleetGo.Mobile.Services;

/// <summary>
/// Posts work to MAUI's main thread, so the sync engine's background events can safely reach
/// bound properties. See <see cref="IUiDispatcher"/> for why this is an interface at all.
/// </summary>
public sealed class MauiUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Already on the main thread is the common case on a pull-to-refresh; invoking
        // directly avoids a pointless trip through the dispatcher queue.
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.BeginInvokeOnMainThread(action);
    }
}
