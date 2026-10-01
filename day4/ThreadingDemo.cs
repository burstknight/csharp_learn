// 這兩個命名空間不在 ImplicitUsings 裡，要自己 using。
using System.ComponentModel;
using System.Diagnostics;

namespace CSharpLearn.Day4;

// Day 4 第一支範例：執行緒、鎖、Interlocked、Parallel、Process。
//
// ⚠ 本檔案未經編譯驗證（撰寫環境沒有 .NET SDK）。

public static class ThreadingDemo
{
    private const int Iterations = 100_000;

    /// <summary>多執行緒同時寫 Console 會讓輸出交錯，用一個鎖把寫入序列化成一個。</summary>
    private static readonly object ConsoleLock = new();

    public static async Task RunAsync(CancellationToken ct)
    {
        Section1_ThreadPool();
        await Section2_CpuVsIo(ct);
        await Section3_InterlockedVsLock(ct);
        await Section4_LimitConcurrency(ct);
        Section5_Parallel();
        await Section6_Process(ct);
    }

    private static void SafeWrite(string message)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine(message);
        }
    }

    // ── 1. ThreadPool ────────────────────────────────────────────────
    private static void Section1_ThreadPool()
    {
        Console.WriteLine("--- 1. ThreadPool 是什麼 ---");

        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out _);
        ThreadPool.GetAvailableThreads(out var available, out _);

        Console.WriteLine($"  最小 worker 執行緒: {minWorker}");
        Console.WriteLine($"  最大 worker 執行緒: {maxWorker}   ← 實際會依 CPU 數與負載調整");
        Console.WriteLine($"  目前可用:          {available}");
        Console.WriteLine($"  最小 I/O 執行緒:   {minIo}");
        Console.WriteLine();

        Console.WriteLine("  重點：");
        Console.WriteLine("    • 不要自己 new Thread。ThreadPool 會重用執行緒，共用數量可控。");
        Console.WriteLine("    • 你的 new Thread() 在 ThreadPool 忙碌時要排隊等，");
        Console.WriteLine("      而且 .NET 已不建議（前景執行緒還會阻止程序結束）。");
        Console.WriteLine("    • 環境變數 DOTNET_ThreadPool_MinThreads 可以調下限，");
        Console.WriteLine("      但先確認你真的需要，不要亂調。");
        Console.WriteLine();
    }

    // ── 2. CPU bound vs I/O bound ────────────────────────────────────
    private static async Task Section2_CpuVsIo(CancellationToken ct)
    {
        Console.WriteLine("--- 2. CPU bound vs I/O bound ---");

        // I/O bound：等待網路。await 會把執行緒還回去，10 個也沒問題。
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => SimulateIoAsync(100, ct)));
        sw.Stop();
        Console.WriteLine($"  I/O bound × 10（各 100ms）: {sw.ElapsedMilliseconds}ms  ← 幾乎等於 100ms");
        Console.WriteLine("    因為等待時不佔用執行緒。10 個、100 個都一樣快。");
        Console.WriteLine();

        // CPU bound：純計算。同時開 10 個只會搶同一顆 CPU，總時間不會變短。
        sw.Restart();
        var cpuResults = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => Task.Run(() => BurnCpu(Iterations), ct)));
        sw.Stop();
        Console.WriteLine($"  CPU bound  × 10（各 {Iterations:N0} 次迴圈）: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"    總和: {cpuResults.Sum():N0}  ← CPU 只有一顆，並行不會讓它更快");
        Console.WriteLine();

        Console.WriteLine("  結論：");
        Console.WriteLine("    I/O bound → 直接 await / Task.WhenAll，不用 Task.Run");
        Console.WriteLine("    CPU bound → Task.Run 或 Parallel.For，並行度不會超過核心數");
        Console.WriteLine();
    }

    private static async Task SimulateIoAsync(int latencyMs, CancellationToken ct)
    {
        await Task.Delay(latencyMs, ct);
    }

    private static long BurnCpu(int iterations)
    {
        long sum = 0;
        for (var i = 0; i < iterations; i++)
        {
            sum += i % 7;
        }

        return sum;
    }

    // ── 3. 沒有保護 vs Interlocked vs lock ────────────────────────────
    private static async Task Section3_InterlockedVsLock(CancellationToken ct)
    {
        Console.WriteLine("--- 3. 共用資料的三種處理方式 ---");

        // a) 完全沒有保護 —— 會「遺失更新」
        var unsafeCounter = new Counter();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            () =>
            {
                for (var i = 0; i < Iterations; i++)
                {
                    unsafeCounter.UnsafeIncrement();
                }
            },
            ct)));

        var expected = 4 * Iterations;
        Console.WriteLine($"  無保護:        {unsafeCounter.Value,12:N0}（預期 {expected:N0}）");

        if (unsafeCounter.Value != expected)
        {
            Console.WriteLine("    → 有更新遺失。++ 是「讀—加—寫」三步，兩個執行緒可以交錯。");
        }
        else
        {
            Console.WriteLine("    → 這次沒有重現。競態是時序相關的，重跑幾次可能就會出現。");
        }

        Console.WriteLine();

        // b) Interlocked —— 單一變數的原子操作
        var atomicCounter = new Counter();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            () =>
            {
                for (var i = 0; i < Iterations; i++)
                {
                    atomicCounter.InterlockedIncrement();
                }
            },
            ct)));

        Console.WriteLine($"  Interlocked:   {atomicCounter.Value,12:N0}（預期 {expected:N0}）");
        Console.WriteLine("    → 一定正確。整個「讀—加—寫」是不可分割的。");
        Console.WriteLine();

        // c) lock —— 保護一段程式碼（不只一個欄位時才需要）
        var lockedCounter = new Counter();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            () =>
            {
                for (var i = 0; i < Iterations; i++)
                {
                    lockedCounter.LockedIncrement();
                }
            },
            ct)));

        Console.WriteLine($"  lock:          {lockedCounter.Value,12:N0}（預期 {expected:N0}）");
        Console.WriteLine();

        Console.WriteLine("  怎麼選：");
        Console.WriteLine("    • 單一整數的增減 → Interlocked（比 lock 快得多，沒有競爭時）");
        Console.WriteLine("    • 多個欄位要保持一致，或保護複雜邏輯 → lock");
        Console.WriteLine("    • 不確定 → lock（成本低，正確性重要）");
        Console.WriteLine();
    }

    // ── 4. 限制併發數量 ──────────────────────────────────────────────
    private static async Task Section4_LimitConcurrency(CancellationToken ct)
    {
        Console.WriteLine("--- 4. SemaphoreSlim：限制同時進行的工作數 ---");

        // 情境：要讀 20 台設備，但設備最多只能同時接 4 條連線。
        // 沒有 SemaphoreSlim 就是同時開 20 條 —— 設備會被搞垮。
        const int MaxConcurrent = 4;
        const int DeviceCount = 20;

        using var semaphore = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
        var current = 0;
        var peak = 0;

        async Task ReadDeviceAsync(int id)
        {
            await semaphore.WaitAsync(ct);

            try
            {
                var now = Interlocked.Increment(ref current);
                InterlockedMax(ref peak, now);

                await Task.Delay(50, ct);        // 模擬通訊

                Interlocked.Decrement(ref current);

                // ⚠ 這裡不用 SafeWrite 是故意的：想讓你看到輸出交錯。
                // 實際程式裡寫 log 一定要有鎖。
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                SafeWrite($"  設備 {id} 讀取失敗：{ex.Message}");
            }
        }

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(1, DeviceCount).Select(ReadDeviceAsync));
        sw.Stop();

        Console.WriteLine($"  {DeviceCount} 台設備，同時最多 {MaxConcurrent} 條連線 → {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  實際同時進行峰值: {peak}   ← 沒有超過 {MaxConcurrent}");
        Console.WriteLine($"  理論下界: {DeviceCount / MaxConcurrent} × 50 = {(DeviceCount / MaxConcurrent) * 50}ms");
        Console.WriteLine();

        Console.WriteLine("  關鍵：Release() 一定要在 finally 裡。");
        Console.WriteLine("  否則任何例外都會永久洩漏一個配額，最後所有工作都卡住。");
        Console.WriteLine();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;

        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    // ── 5. Parallel ───────────────────────────────────────────────────
    private static void Section5_Parallel()
    {
        Console.WriteLine("--- 5. Parallel：資料平行處理 ---");

        var data = Enumerable.Range(1, 1_000_000).ToArray();

        // 循序
        var sw = Stopwatch.StartNew();
        long sequential = 0;
        foreach (var value in data)
        {
            sequential += value % 7;
        }

        sw.Stop();
        var sequentialMs = sw.ElapsedMilliseconds;

        // Parallel.For：編譯器自己決定要不要並行
        sw.Restart();
        long parallel = 0;
        Parallel.For(0, data.Length, i => parallel += data[i] % 7);
        sw.Stop();

        Console.WriteLine($"  循序:        {sequentialMs,5}ms  結果 {sequential:N0}");
        Console.WriteLine($"  Parallel.For:{sw.ElapsedMilliseconds,5}ms  結果 {parallel:N0}");

        if (parallel != sequential)
        {
            Console.WriteLine("  ⚠ 結果不一致！Parallel.For 內部絕對不能寫共用變數。");
        }
        else
        {
            Console.WriteLine("  結果一致。（Parallel.For 自動處理了安全累加）");
        }

        Console.WriteLine();

        // 真正的陷阱：手動在 Parallel.For 裡累加
        sw.Restart();
        var broken = 0L;
        Parallel.For(0, data.Length, i => broken += data[i] % 7);
        sw.Stop();
        Console.WriteLine($"  手動累加:    {sw.ElapsedMilliseconds,5}ms  結果 {broken:N0}   ← 競態！值不對");
        Console.WriteLine();

        Console.WriteLine("  正確做法：用 Parallel.For 的累加多載，或用 thread-local：");
        sw.Restart();
        long correct = 0;
        Parallel.For(
            0,
            data.Length,
            () => 0L,                                        // 初始化 thread-local
            (i, _, localSum) => localSum + data[i] % 7,       // 累加
            localSum => Interlocked.Add(ref correct, localSum));  // 合併 ← 這裡才碰共用變數
        sw.Stop();
        Console.WriteLine($"  thread-local: {sw.ElapsedMilliseconds,5}ms  結果 {correct:N0}   ← 正確");
        Console.WriteLine();
        Console.WriteLine("  .NET 6 起有 Parallel.ForEachAsync（非同步版本）。");
        Console.WriteLine("  實際 API 與各版本可用性請以官方文件為準。");
        Console.WriteLine();
    }

    // ── 6. Process ───────────────────────────────────────────────────
    private static async Task Section6_Process(CancellationToken ct)
    {
        Console.WriteLine("--- 6. Process：啟動外部程式 ---");

        // Thread 和 Task 都在「同一個程序」裡跑。
        // 要呼叫外部程式（開瀏覽器、跑 ping、啟動另一個服務）要用 Process。
        Console.WriteLine($"  目前程序 PID: {Environment.ProcessId}");
        Console.WriteLine($"  目前執行緒 ID: {Environment.CurrentManagedThreadId}");
        Console.WriteLine();

        // 執行 dotnet --version 並擷取輸出
        Console.WriteLine("  執行 `dotnet --version`：");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                // ⚠ 這三個必須同時設定才能擷取輸出
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start 沒有回傳 Process（FileName 可能是目錄）");

            // ⚠ 同時讀 stdout 和 stderr。如果只讀一個，另一個可能塞滿緩衝區造成死鎖。
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(exitCts.Token);

            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();

            Console.WriteLine($"    結束代碼: {process.ExitCode}");
            Console.WriteLine($"    stdout:   {output}");

            if (error.Length > 0)
            {
                Console.WriteLine($"    stderr:   {error}");
            }
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"    ⚠ 找不到執行檔：{ex.Message}");
            Console.WriteLine("      如果是 `dotnet` 找不到，代表它不在 PATH 上。");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("    ⚠ 逾時（30 秒），程序可能沒結束");
        }

        Console.WriteLine();
        Console.WriteLine("  怎麼決定用 Thread、Task 還是 Process：");
        Console.WriteLine("    需要非同步等待        → Task（await）");
        Console.WriteLine("    需要平行計算          → Task.Run / Parallel");
        Console.WriteLine("    需要呼叫外部程式      → Process");
        Console.WriteLine("    需要完全隔離（會崩潰的第三方工具）→ Process");
        Console.WriteLine();
    }
}

/// <summary>示範三種增強計數的方式。</summary>
internal sealed class Counter
{
    private int _value;

    public int Value => Volatile.Read(ref _value);

    /// <summary>❌ 沒有保護：讀—加—寫三步可以交錯，會遺失更新。</summary>
    public int UnsafeIncrement() => ++_value;

    /// <summary>✅ 原子操作：整個「讀—加—寫」不可分割。</summary>
    public int InterlockedIncrement() => Interlocked.Increment(ref _value);

    private readonly Lock _lock = new();

    /// <summary>✅ 用鎖保護。適合「一次要改多個欄位」的情況。</summary>
    public int LockedIncrement()
    {
        lock (_lock)
        {
            return ++_value;
        }
    }
}
