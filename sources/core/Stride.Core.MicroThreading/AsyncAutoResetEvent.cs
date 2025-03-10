// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Core.MicroThreading;

public class AsyncAutoResetEvent
{
    // Credit: http://blogs.msdn.com/b/pfxteam/archive/2012/02/11/10266923.aspx
    private readonly Queue<TaskCompletionSource<bool>> waits = [];
    private bool signaled;

    public Task WaitAsync()
    {
        lock (waits)
        {
            if (signaled)
            {
                signaled = false;
                return Task.CompletedTask;
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                waits.Enqueue(tcs);
                return tcs.Task;
            }
        }
    }

    public void Set()
    {
        TaskCompletionSource<bool>? toRelease = null;
        lock (waits)
        {
            if (waits.Count > 0)
                toRelease = waits.Dequeue();
            else if (!signaled)
                signaled = true;
        }

        toRelease?.SetResult(true);
    }
}
