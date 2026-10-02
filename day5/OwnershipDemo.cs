using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CSharpLearn.Day5;

// Day 5 第一支：所有權（ownership）。
//
// Rust 的核心保證：「每個值有且只有一個擁有者，擁有者離開 scope 就一定會 drop」。
// 編譯器用 move / borrow 檢查強制你遵守這件事，所以你不可能忘記釋放資源。
//
// C# 的做法完全不同：型別自己宣告「我需要釋放」，編譯器只負責
//   1. 在 using 離開 scope 時呼叫 Dispose
//   2. 在 async 方法離開時用 await using 呼叫 DisposeAsync
// 但「誰擁有這個 IDisposable」是**你**要記錄的，編譯器不會幫你檢查有沒有漏。
//
// 這支範例的重點：Rust 保證了什麼、C# 用哪條紀律補。

public static class OwnershipDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 1. Rust 保證了什麼 ---");
        Console.WriteLine();
        Console.WriteLine("  Rust:");
        Console.WriteLine("    let s = String::from(\"hi\");");
        Console.WriteLine("    drop(s);            // 明確釋放");
        Console.WriteLine("    println!(\"{s}\");    // ❌ 編譯錯誤：s 已被 move");
        Console.WriteLine();
        Console.WriteLine("    編譯器保證：");
        Console.WriteLine("      • 每個值恰好一個擁有者（move 會轉移擁有權）");
        Console.WriteLine("      • 擁有者離開 scope 時必定 drop（不漏資源）");
        Console.WriteLine("      • 借用結束後不能被 move（迭代器失效）");
        Console.WriteLine("      • Send / Sync 標記決定能不能跨執行緒");
        Console.WriteLine();

        Console.WriteLine("  C#:");
        Console.WriteLine("    using var s = new MemoryStream();  // 離開 scope 自動 Dispose");
        Console.WriteLine("    // 編譯器保證：");
        Console.WriteLine("      • using / await using 離開 scope 必定呼叫 Dispose");
        Console.WriteLine("    編譯器不保證：");
        Console.WriteLine("      • 這個 IDisposable 應該被誰擁有（要你自己記）");
        Console.WriteLine("      • 有沒有漏掉某個 using（少寫一個就漏了）");
        Console.WriteLine("      • 擁有者還活著時有沒有別人拿著它的參考");
        Console.WriteLine("      • 那個參考有沒有跨執行緒（要看你的紀律，不是型別）");
        Console.WriteLine();

        await UsingDisciplineAsync(ct);
        await CancellationTokenOwnershipAsync(ct);
        await HttpClientOwnershipAsync(ct);
        await WhatRustWouldHaveCaughtAsync(ct);
    }

    // ── using / await using ──────────────────────────────────────────
    private static async Task UsingDisciplineAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 2. using 與 await using：C# 的 drop 等價物 ---");
        Console.WriteLine();

        // IDisposable：離開 scope 呼叫 Dispose()
        using (var timer = new StopwatchLike("synchronous resource"))
        {
            timer.MarkStart();
            await Task.Delay(30, ct);
        }

        Console.WriteLine();

        // IAsyncDisposable：離開 scope 呼叫 DisposeAsync()
        // ⚠ 關鍵差別：資源的釋放本身是非同步的（網路、檔案 flush）。
        //    用 await using 之外的 using 會讓你被迫呼叫 .Dispose() 而不是 .DisposeAsync()，
        //    編譯器不會提醒你。
        await using (var conn = new FakeConnection("async resource"))
        {
            await conn.OpenAsync(ct);
        }

        Console.WriteLine();
        Console.WriteLine("  寫法對照:");
        Console.WriteLine("    Rust     let _g = Guard::new();        // 自動 drop");
        Console.WriteLine("    C#       using var g = new Guard();   // 自動 Dispose");
        Console.WriteLine("    C# async await using var g = ...;    // 自動 DisposeAsync");
        Console.WriteLine();
        Console.WriteLine("  ⚠ 兩個常見錯誤:");
        Console.WriteLine("    1. 把 IDisposable 存成欄位卻沒有 Dispose  → 洩漏，編譯器不報錯");
        Console.WriteLine("    2. 在 async 方法裡用 using 而非 await using → 釋放沒被 await，");
        Console.WriteLine("       資源可能還沒釋完就 return 了");
        Console.WriteLine();
    }

    // ── CancellationToken 的「轉讓」紀律 ────────────────────────────
    private static async Task CancellationTokenOwnershipAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 3. CancellationToken：C# 版的「move」---");
        Console.WriteLine();

        // Rust 沒有內建的取消機制；tokio 用 drop 來取消 future。
        // C# 的 CancellationToken 是「共享的」：token 是 struct（值語意），
        // 複製一份給下游**不會**影響上游，轉移也沒有被強制。
        //
        // 所以 Rust 靠型別系統保證的東西，在 C# 要靠命名與文件紀律：
        //   - 方法參數叫 ct，文件寫「我會轉移取消權」還是「我會連到 parent 上」
        using var inner = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.WriteLine($"  連結後的 token 可以取消: {inner.Token.CanBeCanceled}");

        // 把 token 往下傳。接收方**不能**自己 Cancel 掉上游的 token。
        var worker = new LongRunningWorker();
        var run = worker.RunAsync(inner.Token);

        await Task.Delay(50, ct);
        inner.Cancel();

        // ⚠ 這裡一定要自己接住 OperationCanceledException。
        //    若讓它往外傳，Program.cs 會當成「使用者按 Ctrl+C」而直接結束，
        //    後面的章節就不會跑了。
        //    而且 inner.Cancel() 是**我們自己**觸發的，不是外部取消，
        //    所以在 catch filter 裡要明確區分。
        try
        {
            await run;
            Console.WriteLine("  [demo] 正常結束");
        }
        catch (OperationCanceledException) when (inner.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Console.WriteLine("  [demo] 捕捉到取消，這是我們自己呼叫 inner.Cancel() 造成的");
        }

        Console.WriteLine();
        Console.WriteLine("  Rust 對照:");
        Console.WriteLine("    Rust  傳出去的 future 只能被 drop（丟棄）來取消，");
        Console.WriteLine("          沒有任何一方能『從外面強制取消別人的 future』。");
        Console.WriteLine("    C#    CancellationToken 是共用的，任何拿到 token 的人");
        Console.WriteLine("          理論上都能 Cancel() —— 包括你不希望他取消的下游。");
        Console.WriteLine();
        Console.WriteLine("  → C# 紀律：只把 CancellationToken（唯讀的 token）往下傳，");
        Console.WriteLine("    只有「擁有者」才留著 CancellationTokenSource 並呼叫 Cancel()。");
        Console.WriteLine("    這是 Rust 的 &self / 擁有權觀念在 C# 的對應做法。");
        Console.WriteLine();
    }

    // ── HttpClient 的生命週期 ────────────────────────────────────────
    private static async Task HttpClientOwnershipAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 4. 真實資源：HttpClient 的擁有者 ---");
        Console.WriteLine();

        // HttpClient 實作 IDisposable，但幾乎不該被 Dispose（除非 app 要關了）。
        // 真正的成本是它持有的「連線」和「DNS 名稱快取」。
        using (var throwaway = new HttpClient())
        {
            // ⚠ 在迴圈裡 new HttpClient 是最常見的資源洩漏。
            //    每個實例有自己的連線池，socket 會被耗盡：
            //      SocketException: Address already in use
            //    而且 DNS 名稱不會重新解析。
            for (var i = 0; i < 3; i++)
            {
                _ = throwaway;
            }

            Console.WriteLine("  ❌ 迴圈內 new HttpClient → 每次一個新連線池");
        }

        // 正確做法：長駐的 static 實例。
        // 這裡用 local static 只示範概念，實際專案應該放在 static 欄位。
        Console.WriteLine("  ✅ 長駐一個 static readonly HttpClient，全程式共用");
        Console.WriteLine("     ASP.NET Core 用 IHttpClientFactory（它處理 DNS 更新 + 生命週期）");
        Console.WriteLine();
        Console.WriteLine("  對應 Rust:");
        Console.WriteLine("    reqwest::Client 內部就是 Arc<Inner>，clone 是廉價的");
        Console.WriteLine("    C# 的 HttpClient 沒有 Clone()，所以只能靠「自己管好一個」");
        Console.WriteLine();
        Console.WriteLine("  Rust 的編譯器會擋下 T: 'static 要求不符的跨執行緒傳遞。");
        Console.WriteLine("  C# 完全不檢查，所以 IDisposable 被丟到 Task.Run 裡");
        Console.WriteLine("  可能在別的執行緒上被 Dispose —— 這就是「有 IDisposable 卻沒有擁有者」的危險。");
        Console.WriteLine();

        await Task.CompletedTask;
    }

    // ── Rust 能擋下、C# 擋不下的情況 ───────────────────────────────
    private static async Task WhatRustWouldHaveCaughtAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 5. Rust 會擋下、C# 會放行的四件事 ---");
        Console.WriteLine();
        Console.WriteLine("  1. use-after-move");
        Console.WriteLine("     Rust  : 編譯錯誤");
        Console.WriteLine("     C#    : 正常執行，但物件已被 Dispose，行為未定義");
        Console.WriteLine();
        Console.WriteLine("  2. 迭代器失效（借用的 buffer 被釋放）");
        Console.WriteLine("     Rust  : 編譯錯誤（lifetimes 不允許）");
        Console.WriteLine("     C#    : ObjectDisposedException（如果你幸運有檢查）");
        Console.WriteLine();
        Console.WriteLine("  3. 跨執行緒共享非 Send 的值");
        Console.WriteLine("     Rust  : 編譯錯誤（Rc<T> 不能 Send）");
        Console.WriteLine("     C#    : 正常執行，資料競爭，結果不確定");
        Console.WriteLine();
        Console.WriteLine("  4. 忘記 drop");
        Console.WriteLine("     Rust  : 不可能發生");
        Console.WriteLine("     C#    : 洩漏檔案句柄 / socket / 記憶體，長期跑才發現");
        Console.WriteLine();
        Console.WriteLine("  → C# 沒有編譯器把關，就靠三條紀律補：");
        Console.WriteLine("    a) 所有 IDisposable 一律宣告在 using / await using 裡，不要存成欄位");
        Console.WriteLine("    b) 跨執行緒前確認該物件有執行緒安全（或用 immutable 快照）");
        Console.WriteLine("    c) 讓 IDisposable 的建構點盡量靠近使用點，範圍越小越好");
        Console.WriteLine();

        await Task.CompletedTask;
    }
}

// ── 示範用型別 ─────────────────────────────────────────────────────────

/// <summary>示範 IDisposable。Dispose 一定要可以重複呼叫（很多實作會）。</summary>
internal sealed class StopwatchLike : IDisposable
{
    private readonly string _name;
    private bool _disposed;

    public StopwatchLike(string name) => _name = name;

    public void MarkStart()
    {
        Console.WriteLine($"  [open]  開啟 {_name}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Console.WriteLine($"  [close] 釋放 {_name}（Dispose called）");
    }
}

/// <summary>示範 IAsyncDisposable。釋放本身是非同步的。</summary>
internal sealed class FakeConnection : IAsyncDisposable
{
    private readonly string _name;

    public FakeConnection(string name) => _name = name;

    public async Task OpenAsync(CancellationToken ct)
    {
        await Task.Delay(20, ct);
        Console.WriteLine($"  [open]  開啟 {_name}（async）");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(20);
        Console.WriteLine($"  [close] 釋放 {_name}（DisposeAsync awaited）");
    }
}

/// <summary>示範「只有擁有者能 Cancel」。</summary>
internal sealed class LongRunningWorker
{
    public async Task RunAsync(CancellationToken ct)
    {
        var iterations = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(10, ct);
                iterations++;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"  [worker] 收到取消訊號，已完成 {iterations} 輪");
            throw;
        }
    }
}
