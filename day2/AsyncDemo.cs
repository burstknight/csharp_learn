// Stopwatch 不在 ImplicitUsings 裡，要自己 using。
using System.Diagnostics;

namespace CSharpLearn.Day2;

// Day 2 第一支範例：async / await、取消、例外處理。
//
// ⚠ 本檔案未經編譯驗證（撰寫環境沒有 .NET SDK）。
//
// 這支範例的所有延遲都是「模擬 I/O 等待」，不需要網路或設備，離線可跑。

public static class AsyncDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        await Section1_WhyAsync(ct);
        await Section2_Concurrency(ct);
        await Section3_BlockingIsHarmful(ct);
        await Section4_TaskVsValueTask(ct);
        await Section5_Cancellation(ct);
        await Section6_Exceptions(ct);
    }

    /// <summary>模擬讀一個 tag：等待一段時間後回傳一個值。</summary>
    private static async Task<double> FetchAsync(string tag, int latencyMs, CancellationToken ct)
    {
        await Task.Delay(latencyMs, ct);
        return Random.Shared.NextDouble() * 100;
    }

    // ── 1. async 的意義 ────────────────────────────────────────────────
    private static async Task Section1_WhyAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 1. await 等待時不會佔住執行緒 ---");

        ThreadPool.GetAvailableThreads(out var available);
        Console.WriteLine($"  ThreadPool 目前可用執行緒: {available}");
        Console.WriteLine("  await 遇到未完成的 I/O 時會把執行緒還給 ThreadPool，");
        Console.WriteLine("  等 I/O 完成後再排回來繼續往下執行。執行緒沒有被『睡著』卡住。");
        Console.WriteLine();

        // 這裡每個 Task.Delay 內部都會釋放執行緒。
        // ThreadPool 預設只有幾個執行緒（背景服務裡通常不到 10 個），
        // 但這裡同時掛了 100 個未完成的操作 —— 這就是 async 的價值。
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => FetchAsync($"T{i}", 50, ct)));
        sw.Stop();

        Console.WriteLine($"  100 個各延遲 50ms 的工作同時發出 → 總花費 {sw.ElapsedMilliseconds}ms");
        Console.WriteLine("  循序 await 會需要 100 × 50 = 5000ms。");
        Console.WriteLine();
    }

    // ── 2. 併發 vs 循序 ────────────────────────────────────────────────
    private static async Task Section2_Concurrency(CancellationToken ct)
    {
        Console.WriteLine("--- 2. Task.WhenAll：讓等待同時發生 ---");

        var sw = Stopwatch.StartNew();

        // ❌ 循序：等 A 完成才開始 B。300 + 300 + 300。
        var a = await FetchAsync("A", 300, ct);
        var b = await FetchAsync("B", 300, ct);
        var c = await FetchAsync("C", 300, ct);
        sw.Stop();
        Console.WriteLine($"  循序 await:            {sw.ElapsedMilliseconds,5}ms  （300 × 3 = 900）");

        // ✅ 併發：同時發出三個要求，只等最慢的那個。約 300。
        sw.Restart();
        var results = await Task.WhenAll(
            FetchAsync("A", 300, ct),
            FetchAsync("B", 300, ct),
            FetchAsync("C", 300, ct));
        sw.Stop();
        Console.WriteLine($"  Task.WhenAll 併發:     {sw.ElapsedMilliseconds,5}ms  （max = 300）");
        Console.WriteLine($"  結果: {string.Join(", ", results.Select(v => v.ToString("F1")))}");
        Console.WriteLine();

        Console.WriteLine("  ⚠ 只有 I/O bound 的工作才有這個效果。");
        Console.WriteLine("    CPU bound（純計算）的工作就算用 WhenAll 也不會變快，");
        Console.WriteLine("    因為 CPU 只有一顆 —— 細節見 day4。");
        Console.WriteLine();
    }

    // ── 3. .Result / .Wait()：把執行緒卡住 ─────────────────────────────
    private static async Task Section3_BlockingIsHarmful(CancellationToken ct)
    {
        Console.WriteLine("--- 3. .Result / .Wait() 的代價 ---");

        // ❌ 反例：把目前這個執行緒卡住，等 Task 完成。
        var sw = Stopwatch.StartNew();
        var task = FetchAsync("A", 300, ct);
        var blocked = task.Result;          // ← 這 300ms 內，這個執行緒什麼都不能做
        sw.Stop();

        Console.WriteLine($"  task.Result 取得 {blocked:F2}，花了 {sw.ElapsedMilliseconds}ms");
        Console.WriteLine("  這段時間裡，這個執行緒完全被浪費掉。");
        Console.WriteLine();

        // ✅ 正確：await 把執行緒還回去，程式碼還是循序的樣子，但等待不佔資源。
        sw.Restart();
        var awaited = await FetchAsync("A", 300, ct);
        sw.Stop();
        Console.WriteLine($"  await 取得 {awaited:F2}，花了 {sw.ElapsedMilliseconds}ms（執行緒全程可服務別的工作）");
        Console.WriteLine();

        Console.WriteLine("  關於「死鎖」：");
        Console.WriteLine("    Console App 沒有 SynchronizationContext，所以 .Result 在這裡只是浪費執行緒，不會死鎖。");
        Console.WriteLine("    但 WPF / WinForms / 舊版 ASP.NET 有。");
        Console.WriteLine("    在那些環境裡，UI（或 request）執行緒呼叫 .Result 會等一個需要");
        Console.WriteLine("    同一個執行緒才能完成的 continuation —— 永遠等不到，直接死鎖。");
        Console.WriteLine();

        Console.WriteLine("  其他常見寫法：");
        Console.WriteLine("    .Wait()                    一樣會卡住，而且把例外包成 AggregateException");
        Console.WriteLine("    .GetAwaiter().GetResult()  不包 AggregateException，語意上比較乾淨，");
        Console.WriteLine("                                但「卡住執行緒」的問題一樣存在");
        Console.WriteLine("    Task.Run(async () => ...)   這是關鍵：把工作丟到 ThreadPool 執行緒，");
        Console.WriteLine("                                就不會卡住呼叫端執行緒。");
        Console.WriteLine();

        // 示範 Task.Run 正確的用法。
        sw.Restart();
        var offloaded = await Task.Run(() => FetchAsync("B", 300, ct), ct);
        sw.Stop();
        Console.WriteLine($"  await Task.Run(() => FetchAsync(...)): {sw.ElapsedMilliseconds}ms，值 {offloaded:F2}");
        Console.WriteLine("  這是 Console 裡唯一合理的 .Result 替代方案。");
        Console.WriteLine("  （在 Library 裡還是把方法定義成 async、讓呼叫端去 await。）");
        Console.WriteLine();
    }

    // ── 4. Task vs ValueTask ───────────────────────────────────────────
    private static async Task Section4_TaskVsValueTask(CancellationToken ct)
    {
        Console.WriteLine("--- 4. Task vs ValueTask ---");

        // 快取命中最常同步完成 —— 這是 ValueTask 唯一值得用的情境。
        // 注意 ValueTask 一定要 await 才能取值；直接插值會印出型別名稱。
        var cacheHit = GetCachedValueAsync("temperature", ct);
        Console.WriteLine($"  GetCachedValueAsync(\"temperature\") 同步就完成了: {cacheHit.IsCompleted}");
        Console.WriteLine($"    值 = {await cacheHit}");

        var cacheMiss = GetCachedValueAsync("humidity", ct);
        Console.WriteLine($"  GetCachedValueAsync(\"humidity\")    同步就完成了: {cacheMiss.IsCompleted}");
        Console.WriteLine($"    值 = {(await cacheMiss) ?? "（null）"}");
        Console.WriteLine();

        // 每次都要實際 I/O → 用 Task
        Console.WriteLine($"  每次真的查詢: {await GetFromDeviceAsync("pressure", 100, ct):F1}");
        Console.WriteLine();

        Console.WriteLine("  ValueTask 的規則：");
        Console.WriteLine("    1. 只在高頻呼叫、而且常常同步完成時才用");
        Console.WriteLine("    2. 不要對同一個 ValueTask await 兩次（結果可能已被消耗）");
        Console.WriteLine("    3. 不要把 ValueTask 存成欄位");
        Console.WriteLine("    4. 不能直接丟進 Task.WhenAll（要先呼叫 .AsTask()）");
        Console.WriteLine("    5. 除非你很確定自己在做什麼，否則用 Task");
    }

    /// <summary>可能同步完成的非同步操作 —— ValueTask 的典型使用情境。</summary>
    private static ValueTask<string?> GetCachedValueAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(key == "temperature" ? "Line1.Temp" : null);
    }

    private static async Task<double> GetFromDeviceAsync(string tag, int latencyMs, CancellationToken ct)
    {
        await Task.Delay(latencyMs, ct);
        return Random.Shared.NextDouble() * 50;
    }

    // ── 5. CancellationToken ───────────────────────────────────────────
    private static async Task Section5_Cancellation(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("--- 5. CancellationToken ---");

        // ⚠ 重點：token 應該從「外面」傳進來，方法裡不要自己 new CancellationTokenSource()。
        // 否則呼叫端無法取消，也無法統一管理。整套程式的取消應該從最上層一路傳到底。
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var sw = Stopwatch.StartNew();
        try
        {
            await LongRunningAsync(1000, cts.Token);
            Console.WriteLine("  （不會走到這裡，因為 200ms 就被取消了）");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            sw.Stop();
            Console.WriteLine($"  1000ms 的工作在 {sw.ElapsedMilliseconds}ms 就被取消");
        }

        Console.WriteLine();
        Console.WriteLine("  為什麼 OperationCanceledException 要單獨 catch：");
        Console.WriteLine("    它繼承自 Exception。如果被一般的 catch (Exception) 抓到，");
        Console.WriteLine("    上層就分不出「這是取消」還是「這是真的出錯」，");
        Console.WriteLine("    可能會把正常的取消當成故障回報出去。");
        Console.WriteLine();
        Console.WriteLine("  兩個常見的配合寫法：");
        Console.WriteLine("    // 確認這次的取消是「我預期的」而不是別人剛好丟進來的 token");
        Console.WriteLine("    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }");
        Console.WriteLine();
        Console.WriteLine("    // 取消時清理資源（用 try/finally 或 using，不要讓例外蓋掉清理）");
        Console.WriteLine("    try { await Work(ct); }");
        Console.WriteLine("    finally { Cleanup(); }");
    }

    private static async Task LongRunningAsync(int ms, CancellationToken ct)
    {
        await Task.Delay(ms, ct);
    }

    // ── 6. 例外處理 ────────────────────────────────────────────────────
    private static async Task Section6_Exceptions(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("--- 6. await 與 AggregateException ---");

        Task<string>[] tasks =
        [
            WorkAsync("ok-1", 50, ct),
            WorkAsync("boom", 80, ct),
            WorkAsync("ok-2", 100, ct),
        ];

        var all = Task.WhenAll(tasks);

        try
        {
            await all;
        }
        catch (Exception ex)
        {
            // await 會自動把 AggregateException 拆開，直接丟出裡面第一個例外。
            // 所以這裡看到的是 InvalidOperationException，不是 AggregateException。
            Console.WriteLine($"  await 丟出: {ex.GetType().Name} —— 不是 AggregateException");
            Console.WriteLine($"  訊息: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("  ⚠ WhenAll 不是 fail-fast：一個失敗不會取消其他工作。");
        Console.WriteLine($"     ok-1: {tasks[0].Status}");
        Console.WriteLine($"     boom: {tasks[1].Status}");
        Console.WriteLine($"     ok-2: {tasks[2].Status}   ← 成功的那個照樣跑完");

        // 想看「所有」失敗原因，要從 Task.Exception 拿。
        if (all.Exception is { InnerExceptions.Count: > 0 } aggregate)
        {
            Console.WriteLine();
            Console.WriteLine($"  all.Exception 有 {aggregate.InnerExceptions.Count} 個內層例外:");
            foreach (var inner in aggregate.InnerExceptions)
            {
                Console.WriteLine($"    {inner.GetType().Name}: {inner.Message}");
            }
        }

        // 需要「一有問題就立刻知道」時，用 WhenAny。
        Console.WriteLine();
        Console.WriteLine("  需要 fail-fast 時用 Task.WhenAny：");

        var slow = WorkAsync("slow", 300, ct);
        var fast = WorkAsync("fast", 100, ct);

        var first = await Task.WhenAny(slow, fast);
        Console.WriteLine($"  先完成的是: {await first}");

        // ⚠ WhenAny 結束不代表另一個工作結束，要自己收尾。
        await Task.WhenAll(slow, fast);
        Console.WriteLine("  （剩下的工作要另外 await，否則會被丟著不管）");
    }

    private static async Task<string> WorkAsync(string name, int latencyMs, CancellationToken ct)
    {
        await Task.Delay(latencyMs, ct);

        if (name == "boom")
        {
            throw new InvalidOperationException($"工作 {name} 失敗");
        }

        return $"工作 {name} 成功";
    }
}