using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace NFC_System
{
    public static class DispatcherQueueExtensions
    {
        public static Task TryEnqueueAsync(this DispatcherQueue dispatcherQueue, Action callback)
        {
            if (dispatcherQueue.HasThreadAccess)
            {
                callback();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<object?>();
            bool queued = dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    callback();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!queued)
            {
                tcs.SetException(new InvalidOperationException("Unable to queue work on the UI thread."));
            }

            return tcs.Task;
        }

        public static Task TryEnqueueAsync(this DispatcherQueue dispatcherQueue, Func<Task> callback)
        {
            if (dispatcherQueue.HasThreadAccess)
            {
                return callback();
            }

            var tcs = new TaskCompletionSource<object?>();
            bool queued = dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await callback();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!queued)
            {
                tcs.SetException(new InvalidOperationException("Unable to queue async work on the UI thread."));
            }

            return tcs.Task;
        }
    }
}
