// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;

using Xunit;

namespace Stride.Graphics.Tests;

public class TestBufferUploadPinning : GraphicTestGameBase
{
    // Large enough for the native copy to still be running when a collection started alongside it
    // completes, and above the large object heap threshold so a compaction can relocate it
    private const int UploadSize = 32 * 1024 * 1024;

    // A throwaway allocation just below the source leaves the free space a compaction slides it into
    private const int HoleSize = UploadSize / 4;

    private const int Iterations = 20;

    // How long the collector waits after the signal before collecting, so the upload has entered the backend
    private static readonly TimeSpan CollectDelay = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// A span-based upload to a <see cref="GraphicsResourceUsage.Default"/> buffer hands the backend a pointer into managed memory,
    /// so the array must stay pinned while the native copy runs; otherwise a compacting collection on another thread
    /// moves it from under the copy. Verify that a compacting collection run during the upload leaves the source array
    /// where it was.
    /// </summary>
    [Fact]
    public unsafe void SourceArrayStaysPinnedDuringUpload()
    {
        PerformTest(game =>
        {
            var device = game.GraphicsDevice;
            var commandList = game.GraphicsContext.CommandList;

            // Default usage goes through CommandList.UpdateSubResource, the path under test
            using var buffer = Buffer.New(device, UploadSize, BufferFlags.VertexBuffer, GraphicsResourceUsage.Default);

            var clock = Stopwatch.StartNew();
            using var uploadStarting = new AutoResetEvent(initialState: false);
            using var collected = new AutoResetEvent(initialState: false);
            byte[] source = null;
            long collectStart = 0, collectEnd = 0;
            nint addressAfterCollect = 0;
            var stop = false;

            // A collector thread that runs one full compacting collection, large object heap included,
            // shortly after each upload starts, and records where the source array is afterwards
            var collector = new Thread(() =>
            {
                while (uploadStarting.WaitOne() && !Volatile.Read(ref stop))
                {
                    var wakeAt = clock.Elapsed + CollectDelay;
                    while (clock.Elapsed < wakeAt)
                        Thread.SpinWait(100);

                    collectStart = clock.ElapsedTicks;
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    collectEnd = clock.ElapsedTicks;

                    fixed (byte* current = source)
                        addressAfterCollect = (nint) current;

                    collected.Set();
                }
            })
            { IsBackground = true, Name = "Compacting collector" };
            collector.Start();

            try
            {
                int measured = 0, moved = 0;

                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    var hole = new byte[HoleSize];
                    source = new byte[UploadSize];
                    hole = null;

                    nint addressBefore;
                    fixed (byte* before = source)
                        addressBefore = (nint) before;

                    uploadStarting.Set();
                    long callStart = clock.ElapsedTicks;
                    buffer.SetData(commandList, source);
                    long callEnd = clock.ElapsedTicks;

                    Assert.True(collected.WaitOne(TimeSpan.FromSeconds(10)), "The collector did not run.");

                    // Only a collection that ran entirely inside the call says anything about the pin
                    if (collectStart < callStart || collectEnd > callEnd)
                        continue;

                    measured++;
                    if (addressAfterCollect != addressBefore)
                        moved++;
                }

                Assert.True(measured > 0, "No collection completed inside an upload; the upload is too fast for this test to measure.");

                // An unpinned array moves on every measured collection: the free space below it is there each time.
                // A collection that caught the call before it reached the backend moves the array legitimately,
                // so a rare move is not a failure
                Assert.True(moved * 2 < measured, $"The source array moved during {moved} of {measured} uploads.");
            }
            finally
            {
                Volatile.Write(ref stop, true);
                uploadStarting.Set();
                collector.Join();
            }
        });
    }
}
