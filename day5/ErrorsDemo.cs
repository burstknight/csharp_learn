using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CSharpLearn.Day5;

// Day 5 第三支：錯誤處理。
//
// Rust 的 Result：
//   - 函式回傳 Result<T, E>，呼叫端**必須** match 或 ? 處理
//   - ? 會在遇到 Err 時提早返回
//   - 型別系統保證你不會忘記處理錯誤
//
// C# 的例外：
//   - 錯誤是「例外」（未經檢查的流程中斷），不是回傳值的一部分
//   - 忘記 try/catch **不會**有任何編譯錯誤
//   - 但 .NET 也有一套 TryXxx 模式，回傳 bool 而不丟例外
//
// 這支範例的重點：C# 有一套跟 Rust Result 幾乎同構的機制（TryXxx），
// 知道什麼時候該用它，比學一套新的錯誤處理哲學更有價值。

public static class ErrorsDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 1. Rust 的 Result：型別強迫你處理 ---");
        Console.WriteLine();
        Console.WriteLine("  fn read_config(p: &str) -> Result<Config, ConfigError> { ... }");
        Console.WriteLine();
        Console.WriteLine("  // 必須處理：");
        Console.WriteLine("  match read_config(\"a.toml\") {");
        Console.WriteLine("      Ok(cfg) => println!(\"{}\", cfg.port),");
        Console.WriteLine("      Err(e)  => eprintln!(\"{e}\"),");
        Console.WriteLine("  }");
        Console.WriteLine();
        Console.WriteLine("  // 或用 ? 提早返回：");
        Console.WriteLine("  fn setup(p: &str) -> Result<(), Box<dyn Error>> {");
        Console.WriteLine("      let cfg = read_config(p)?;   // Err 就直接往上傳");
        Console.WriteLine("      start(cfg);");
        Console.WriteLine("      Ok(())");
        Console.WriteLine("  }");
        Console.WriteLine();
        Console.WriteLine("  編譯器保證：");
        Console.WriteLine("    • 沒 match 完整的 Result → 編譯錯誤");
        Console.WriteLine("    • 沒處理 Err 就想用值 → 編譯錯誤");
        Console.WriteLine("    • panic! / unwrap() 會明確標記在程式碼裡");
        Console.WriteLine();

        Console.WriteLine("  C# 的例外：");
        Console.WriteLine("    try { var cfg = ReadConfig(path); }");
        Console.WriteLine("    // 完全合法。不寫 catch 也能編譯，執行時才炸。");
        Console.WriteLine();
        Console.WriteLine("  這是 C# 的選擇：把「常見路徑」寫乾淨，錯誤走旁路。");
        Console.WriteLine("  對大部分商業應用來說這是對的，因為：");
        Console.WriteLine("    • 99% 的呼叫不會出錯，try/catch 讓 happy path 保持可讀");
        Console.WriteLine("    • 跨層傳遞錯誤不需要包一層");
        Console.WriteLine("    • .NET 的 stack trace 資訊比 Result 的 E 詳細");
        Console.WriteLine();

        ExceptionStyle();
        TryPattern();
        ResultTypeInCSharp();
        UnreachableSwitch();
        await WhenAllAsync(ct);
    }

    // ── 例外風格 ────────────────────────────────────────────────────
    private static void ExceptionStyle()
    {
        Console.WriteLine("--- 2. C# 例外：什麼該丟、什麼不該丟 ---");
        Console.WriteLine();

        // 該丟：無法從當前方法恢復的錯誤
        try
        {
            ParsePort("not-a-number");
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"  FormatException: {ex.Message}");
            Console.WriteLine("  → 呼叫端沒辦法把字串變成數字，這是呼叫端的問題 → 該丟");
        }

        // 不該丟：用 TryXxx 代替
        var ok = TryParsePort("not-a-number", out var port);
        Console.WriteLine($"  TryParsePort → {ok}, port={port}");
        Console.WriteLine("  → 失敗是預期內的 → 用 bool 回傳，不要丟例外");
        Console.WriteLine();

        Console.WriteLine("  Rust 對照:");
        Console.WriteLine("    parse(\"abc\")      → Result<u16, ParseError>");
        Console.WriteLine("    parse(\"123\").unwrap() → panic!");
        Console.WriteLine("    unwrap() 就是「我保證這不會失敗」，等同於 C# 丟例外");
        Console.WriteLine();

        Console.WriteLine("  ⚠ 不要這樣用例外：");
        Console.WriteLine("    • 用例外做正常流程控制（驗證失敗就 try/catch）");
        Console.WriteLine("    • 捕獲所有例外（catch (Exception)）然後忽略");
        Console.WriteLine("    • 在迴圈裡對每個項目丟預期的 FormatException");
        Console.WriteLine("    • 沒有含意的自訂例外（throw new Exception(\"...\")）");
        Console.WriteLine();
    }

    private static int ParsePort(string text)
    {
        if (!int.TryParse(text, out var value))
        {
            throw new FormatException($"'{text}' 不是有效的 port 號碼");
        }

        if (value is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(text), value, "port 必須在 1–65535");
        }

        return value;
    }

    private static bool TryParsePort(string text, out int port)
    {
        if (!int.TryParse(text, out port))
        {
            port = default;
            return false;
        }

        if (port is < 1 or > 65535)
        {
            port = default;
            return false;
        }

        return true;
    }

    // ── TryXxx 模式 ──────────────────────────────────────────────────
    private static void TryPattern()
    {
        Console.WriteLine("--- 3. TryXxx 模式：C# 版的 Result ---");
        Console.WriteLine();

        // int.TryParse 是 BCL 的 TryXxx。整個 .NET 都遵守這個慣例：
        //   TryParse / TryGetValue / TryAdd / TryDequeue ...
        // 看名字就知道「這個方法不會丟例外」。

        // Dictionary.TryGetValue：沒有 key 回 false，而不是拋 KeyNotFoundException
        var scores = new Dictionary<string, int>
        {
            ["alice"] = 90,
            ["bob"] = 75,
        };

        if (scores.TryGetValue("carol", out var carolScore))
        {
            Console.WriteLine($"  carol: {carolScore}");
        }
        else
        {
            Console.WriteLine("  carol: 查無此 key（回 false，不是例外）");
        }

        // 對照：不用 TryGetValue 的寫法
        Console.WriteLine();
        Console.WriteLine("  不用 TryXxx 就得這樣:");
        Console.WriteLine("    if (scores.ContainsKey(\"carol\")) {");
        Console.WriteLine("        var s = scores[\"carol\"];");
        Console.WriteLine("    }");
        Console.WriteLine("  ⚠ 兩次查找浪費效能，而且中間若有其他執行緒改動就不一致。");
        Console.WriteLine("    TryGetValue 一次查找就完成 → 這是它存在的主要理由。");
        Console.WriteLine();

        // 自己寫一個 Result 風格的方法
        Console.WriteLine("  自己寫 Result 風格的方法:");
        var result = Divide(10, 3);
        Console.WriteLine($"    Divide(10, 3) → Success={result.IsSuccess}, Value={result.Value}");

        var fail = Divide(1, 0);
        Console.WriteLine($"    Divide(1, 0)  → Success={fail.IsSuccess}, Error={fail.Error}");
        Console.WriteLine();

        Console.WriteLine("  Rust 對照:");
        Console.WriteLine("    fn divide(a: i32, b: i32) -> Result<i32, &'static str>");
        Console.WriteLine("    let r = divide(1, 0);");
        Console.WriteLine("    // r 是 Result，必須 match；不 match 編譯不過");
        Console.WriteLine();
        Console.WriteLine("  C# 的差異：Result<T> 用 struct 實作可以避免配置，");
        Console.WriteLine("  但 .NET 慣例是直接用 T? + bool，不要自己發明 Result<T>。");
        Console.WriteLine("  例外是「不太會失敗但失敗很嚴重」的操作，例如網路 I/O。");
        Console.WriteLine();
    }

    private static DivideResult Divide(int a, int b)
    {
        if (b == 0)
        {
            return DivideResult.Failure("除數不能為零");
        }

        return DivideResult.Success(a / b);
    }

    // ── Result 類型 ──────────────────────────────────────────────────
    private static void ResultTypeInCSharp()
    {
        Console.WriteLine("--- 4. 想要 Rust 式 Result 時怎麼寫 ---");
        Console.WriteLine();
        Console.WriteLine("  不要到處用 Result<T>。只在這三種情況考慮：");
        Console.WriteLine("    1. 錯誤是**預期的常見分支**（例如解析外部設定檔失敗）");
        Console.WriteLine("    2. 錯誤值本身**攜帶資訊**呼叫端需要（HTTP 狀態碼、錯誤分類）");
        Console.WriteLine("    3. 介面契約要求不能拋例外（例如 hot path 每毫秒呼叫一次）");
        Console.WriteLine();
        Console.WriteLine("  其他情況用例外。理由：");
        Console.WriteLine("    • 整個 .NET 都用例外，改用 Result 會跟生態系脫節");
        Console.WriteLine("    • TryXxx 已經覆蓋了八成的需求");
        Console.WriteLine("    • 大量 throw 的成本（stack unwinding）在熱路徑是實測得出來的");
        Console.WriteLine();

        // 錯誤分類：像 Rust 的 error enum
        Console.WriteLine("  錯誤分類建議照 Rust 的 error enum:");
        Console.WriteLine();
        Console.WriteLine("    enum ConfigError {");
        Console.WriteLine("        NotFound(PathBuf),        // 檔案不存在 → 不要重試");
        Console.WriteLine("        InvalidSyntax { line: u32 }, // 語法錯 → 不要重試");
        Console.WriteLine("        PermissionDenied,         // 權限 → 不要重試");
        Console.WriteLine("        TransientIo,              // 暫時性 → 可以重試");
        Console.WriteLine("    }");
        Console.WriteLine();
        Console.WriteLine("  C# 對應：自訂例外型別 + 屬性分類欄位。");
        Console.WriteLine("  重試判斷不要靠 exception type 的 if/else 串，");
        Console.WriteLine("  用一個 CanRetry 屬性或分類 enum 集中管理。");
        Console.WriteLine();
    }

    // ── UnreachableException ─────────────────────────────────────────
    private static void UnreachableSwitch()
    {
        Console.WriteLine("--- 5. UnreachableException：Rust 的 unreachable!() ---");
        Console.WriteLine();
        Console.WriteLine("  當你 exhaustively match 完所有 enum 成員，Rust 編譯器知道");
        Console.WriteLine("  剩下的分支不可能到達。加一個新 enum 成員，所有 match 都編譯錯誤。");
        Console.WriteLine();
        Console.WriteLine("  C# 的 enum switch 沒有這個保證 —— 你可以漏一個 case 而編譯通過。");
        Console.WriteLine("  慣例是給 default 丟 UnreachableException:");
        Console.WriteLine();

        // C# 14 的 exhaustiveness：對 enum 用沒有 default 的 switch，
        // 如果所有成員都列出來，編譯器知道剩下的不可能。
        // 但對非 enum 這就只是「執行期檢查」。
        var result = Classify(DeviceState.Running);
        Console.WriteLine($"  Classify(Running) → {result}");

        var result2 = Classify(DeviceState.Faulted);
        Console.WriteLine($"  Classify(Faulted) → {result2}");
        Console.WriteLine();

        Console.WriteLine("  Rust:");
        Console.WriteLine("    match state {");
        Console.WriteLine("        Running => ...");
        Console.WriteLine("        Faulted => ...");
        Console.WriteLine("        // 沒有 _ 分支 → 加新成員時編譯錯誤");
        Console.WriteLine("    }");
        Console.WriteLine();
        Console.WriteLine("  C#:");
        Console.WriteLine("    default:");
        Console.WriteLine("        throw new UnreachableException($\"沒處理 {state}\");");
        Console.WriteLine("  這個 throw 不是防禦性程式碼，是**文件**：");
        Console.WriteLine("  「我認為這裡到不了，如果到了就是我的 enum 漏了 case」。");
        Console.WriteLine();
    }

    private static string Classify(DeviceState state)
    {
        switch (state)
        {
            case DeviceState.Idle:
                return "待機中";
            case DeviceState.Running:
                return "運轉中";
            case DeviceState.Faulted:
                return "故障";
            default:
                // 這個 throw 不是防禦性程式碼，是文件。
                // 若將來加了新狀態卻忘了處理，就會在這裡炸出來。
                throw new UnreachableException($"沒處理 {state}");
        }
    }

    // ── WhenAll ──────────────────────────────────────────────────────
    private static async Task WhenAllAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 6. Task.WhenAll 與 Rust 的 join_all ---");
        Console.WriteLine();
        Console.WriteLine("  兩者行為相同：");
        Console.WriteLine("    • 全部完成才返回");
        Console.WriteLine("    • 任何一個 faulted → 整個 WhenAll faulted");
        Console.WriteLine("    • 但**其他 task 還是會繼續跑**（不會被取消）");
        Console.WriteLine();

        var stopwatch = Stopwatch.StartNew();

        var tasks = new List<Task<int>>
        {
            WorkAsync("A", 120, ct),
            WorkAsync("B", 80, ct),
            WorkAsync("C", 200, ct),
        };

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        Console.WriteLine($"  三個 task 完成: [{string.Join(", ", results)}]");
        Console.WriteLine($"  總耗時 {stopwatch.ElapsedMilliseconds}ms（不是 120+80+200=400ms，是並行）");
        Console.WriteLine();

        // 錯誤案例
        try
        {
            await Task.WhenAll(
                Task.FromException(new InvalidOperationException("task 1 失敗")),
                Task.FromException(new ArgumentException("task 2 失敗")));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  兩個 task 都失敗時，WhenAll 丟第一個: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine("  ⚠ 其他例外會被吞掉。要全部拿到就檢查每個 task.Exception。");
        }

        Console.WriteLine();
        Console.WriteLine("  Rust 對照:");
        Console.WriteLine("    join_all(vec![a, b, c]).await");
        Console.WriteLine("    • 行為幾乎一樣");
        Console.WriteLine("    • 但 Rust 的 Future 被 drop 時任務真的會停");
        Console.WriteLine("    • C# 的 Task 被 drop（沒 await）時**繼續跑到底**");
        Console.WriteLine();
        Console.WriteLine("  ⚠ 這是最容易踩的坑：");
        Console.WriteLine("    CancellationTokenSource.Cancel() 不會停掉已在執行的 Task；");
        Console.WriteLine("    那個 task 必須**自己**檢查 token 或捕獲 OperationCanceledException。");
        Console.WriteLine();
    }

    private static async Task<int> WorkAsync(string name, int delayMs, CancellationToken ct)
    {
        await Task.Delay(delayMs, ct);
        return delayMs;
    }
}

// ── Result 型別（示範用）─────────────────────────────────────────────

/// <summary>成功時帶值，失敗時帶原因。類似 Rust 的 Result&lt;T, E&gt;。</summary>
internal readonly struct DivideResult
{
    private DivideResult(bool isSuccess, int value, string error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    /// <summary>成功時的值。失敗時是 0 —— 這是 C# 沒有 sum type 的妥協。</summary>
    public int Value { get; }

    /// <summary>失敗原因。成功時是空字串。</summary>
    public string Error { get; }

    public static DivideResult Success(int value) => new(true, value, string.Empty);

    public static DivideResult Failure(string error) => new(false, 0, error);
}

/// <summary>示範 enum。</summary>
internal enum DeviceState
{
    Idle,
    Running,
    Faulted,
}
