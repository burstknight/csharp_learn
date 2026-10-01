using System.Diagnostics;
using System.Threading.Channels;

namespace CSharpLearn.Day2;

// Day 2 第二支範例：System.Threading.Channels —— 生產者/消費者之間的緩衝。
//
// 為什麼不用 ConcurrentQueue<T>？
//   ConcurrentQueue 會「無限成長」。PLC 斷線重連時資料暴增，記憶體就被吃光了。
//   bounded Channel 有容量上限，滿了之後可以選擇「等」（背壓）或「丟」（DropOldest）。
//   這是工業採集程式的必備特性。
//
// ⚠ 本檔案未經編譯驗證（撰寫環境沒有 .NET SDK）。

/// <summary>一筆採集到的資料。</summary>
internal readonly record struct TagSample(long Tick, string Tag, double Value, DateTimeOffset Timestamp);

public static class CollectorDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        // ⚠ 這裡一定要 await：Section1 是 async Task。
        //    少了 await 會有 CS4014 警告，而且背壓示範不會依預期被等待。
        await Section1_BackpressureDemo(ct);
        await Section2_PlcCollector(ct);
        Section3_DropModes();
    }

    // ── 1. 背壓（backpressure）──
    private static async Task Section1_BackpressureDemo(CancellationToken ct)
    {
        Console.WriteLine("--- 1. bounded Channel 的背壓 ---");

        // 容量只有 5。FullMode = Wait 表示「滿了就等」。
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,     // 只有一個消費者 → 通知可以省略，吞吐更好
            SingleWriter = true,
        });

        var produced = 0;
        var consumed = 0;

        // 消費者：每筆花 20ms，所以消化得很慢
        var consumer = Task.Run(
            async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync(ct))
                {
                    await Task.Delay(20, ct);
                    consumed++;
                }
            },
            ct);

        // 生產者：試圖快速寫 20 筆
        var sw = Stopwatch.StartNew();
        for (var i = 1; i <= 20; i++)
        {
            // 容量滿了之後，這一行會「等」到消費者騰出空間。
            // 這就是背壓：生產者被迫降速，而不是把資料堆在記憶體裡。
            await channel.Writer.WriteAsync(i, ct);
            produced++;
        }

        sw.Stop();
        Console.WriteLine($"  容量 5、消費者每筆 20ms → 生產 20 筆花了 {sw.ElapsedMilliseconds}ms");

        channel.Writer.TryComplete();
        await consumer;

        Console.WriteLine($"  生產 {produced} 筆，消費 {consumed} 筆，沒有任何資料遺失");
        Console.WriteLine("  如果把 channel 改成無上限，生產者會瞬間寫完，但記憶體可能被吃光。");
        Console.WriteLine();
    }

    // ── 2. 生產者/消費者：模擬 PLC 採集 ────────────────────────────────
    private static async Task Section2_PlcCollector(CancellationToken ct)
    {
        Console.WriteLine("--- 2. 生產者/消費者：模擬 PLC 採集 ---");

        // 採集速度和處理速度一定不同。用 bounded channel 把兩者解耦。
        // 這裡選 DropOldest：處理太慢時丟掉最舊的資料，絕不卡住採集迴圈。
        // 對「顯示最新狀態」這類需求，舊資料本來就沒價值。
        var channel = Channel.CreateBounded<TagSample>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

        // 讓範例跑 3 秒就自己結束，同時示範「取消從上層一路傳下來」
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(3));

        var token = runCts.Token;
        var producer = ProduceAsync(channel.Writer, token);
        var consumer = ConsumeAsync(channel.Reader, token);

        try
        {
            await Task.WhenAll(producer, consumer);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("  （時間到，正常結束。取消有從最上層傳到兩個工作）");
        }
        finally
        {
            // 收尾：通知讀取端不會再有任何資料
            channel.Writer.TryComplete();
        }

        Console.WriteLine();
    }

    private static async Task ProduceAsync(ChannelWriter<TagSample> writer, CancellationToken ct)
    {
        var random = new Random(42);
        var tick = 0L;
        string[] tags = ["Line1.Temp", "Line1.Humidity", "Line2.Pressure"];

        while (!ct.IsCancellationRequested)
        {
            tick++;

            foreach (var tag in tags)
            {
                await writer.WriteAsync(
                    new TagSample(tick, tag, random.NextDouble() * 100, DateTimeOffset.UtcNow),
                    ct);
            }

            await Task.Delay(200, ct);
        }
    }

    private static async Task ConsumeAsync(ChannelReader<TagSample> reader, CancellationToken ct)
    {
        var latest = new Dictionary<string, TagSample>(StringComparer.Ordinal);
        var total = 0;
        var report = Stopwatch.StartNew();

        // WaitToReadAsync：等「有東西可以讀」或「通道被關閉」。
        while (await reader.WaitToReadAsync(ct))
        {
            // TryRead 把目前已排隊的資料一次倒出來 → 批次處理。
            // 這在「更新畫面」或「寫資料庫」時很重要：
            // 一次處理 100 筆，遠比呼叫 100 次更新好。
            while (reader.TryRead(out var sample))
            {
                latest[sample.Tag] = sample;   // 只保留每個 tag 的最新值
                total++;
            }

            // 每 500ms 輸出一次，而不是每筆都輸出
            if (report.ElapsedMilliseconds < 500)
            {
                continue;
            }

            report.Restart();

            var summary = latest.Values
                .OrderBy(s => s.Tag, StringComparer.Ordinal)
                .Select(s => $"{s.Tag}={s.Value:F1}");

            Console.WriteLine($"  收到 {total,4} 筆 → {string.Join(" | ", summary)}");
        }
    }

    // ── 3. 通道滿了怎麼辦：FullMode ───────────────────────────────────
    private static void Section3_DropModes()
    {
        Console.WriteLine("--- 3. bounded Channel 滿了的處理方式 ---");

        // 預設是 Wait：WriteAsync 會等待有空間，TryWrite 則回傳 false。
        var waiting = Channel.CreateBounded<int>(3);

        for (var i = 1; i <= 3; i++)
        {
            waiting.Writer.TryWrite(i);
        }

        Console.WriteLine($"  容量 3，已寫入 3 筆。第 4 筆 TryWrite = {waiting.Writer.TryWrite(4)}  ← false，寫不進去");
        Console.WriteLine("  這就是 Wait 模式：滿了就等（WriteAsync）或失敗（TryWrite）。");
        Console.WriteLine();

        // DropOldest：丟掉最舊的，騰空間給新的。TryWrite 會回傳 true。
        var dropOldest = Channel.CreateBounded<int>(new BoundedChannelOptions(3)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        for (var i = 1; i <= 5; i++)
        {
            dropOldest.Writer.TryWrite(i);
        }

        var kept = new List<int>();
        while (dropOldest.Reader.TryRead(out var value))
        {
            kept.Add(value);
        }

        Console.WriteLine($"  DropOldest 寫入 1..5（容量 3）→ 留下: {string.Join(", ", kept)}");
        Console.WriteLine("  舊的 1、2 被丟掉。適合「只要最新狀態」的顯示用途。");
        Console.WriteLine();

        // DropWrite：丟掉新來的。適合「絕對不能漏掉收到的資料」的場合。
        var dropWrite = Channel.CreateBounded<int>(new BoundedChannelOptions(3)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
        });

        for (var i = 1; i <= 5; i++)
        {
            dropWrite.Writer.TryWrite(i);
        }

        kept.Clear();
        while (dropWrite.Reader.TryRead(out var value))
        {
            kept.Add(value);
        }

        Console.WriteLine($"  DropWrite  寫入 1..5（容量 3）→ 留下: {string.Join(", ", kept)}");
        Console.WriteLine("  新的 4、5 被丟掉。若資料不能漏，用 Wait 讓生產者降速。");
    }
}