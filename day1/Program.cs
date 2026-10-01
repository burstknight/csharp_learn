// Day 1 的進入點。
//
// 設計重點：
// 1. 頂層語句（top-level statements）只能存在「一個」檔案。為了讓同一天可以放多個獨立範例，
//    每個範例都寫成 static class（見 SyntaxDemo.cs / OopDemo.cs），這裡用引數挑要跑哪一個。
// 2. 這裡出現 await，代表編譯器會把整份檔案轉成一個 async Task Main 方法。
//    這就是「頂層語句」的運作原理。
// 3. Ctrl+C 不直接砍掉程式，而是走 CancellationToken，讓範例有機會把資源收乾淨。

using CSharpLearn.Day1;

var demos = new (string Key, string Description, Func<CancellationToken, Task> Run)[]
{
    ("syntax", "C# 特有語法", SyntaxDemo.RunAsync),
    ("oop", "物件導向", OopDemo.RunAsync),
};

if (!TryResolve(args, demos, out var selected))
{
    return;
}

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // 交給我們自己收尾，不要被系統直接中止
    cts.Cancel();      // 通知範例取消
};

try
{
    await selected.Run(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("已取消。");
}

// ---- 底下是 local function：可以宣告在頂層語句之後，程式行為上等同於本檔案內的私有方法 ----

static bool TryResolve(
    string[] args,
    (string Key, string Description, Func<CancellationToken, Task> Run)[] demos,
    out (string Key, string Description, Func<CancellationToken, Task> Run) selected)
{
    if (args.Length == 0)
    {
        PrintUsage(demos);
        selected = default;
        return false;
    }

    selected = demos.FirstOrDefault(d => d.Key.Equals(args[0], StringComparison.OrdinalIgnoreCase));

    if (selected.Run is null)
    {
        Console.Error.WriteLine($"找不到 demo: {args[0]}");
        Console.Error.WriteLine();
        PrintUsage(demos);
        return false;
    }

    return true;
}

static void PrintUsage(
    (string Key, string Description, Func<CancellationToken, Task> Run)[] demos)
{
    Console.WriteLine("用法: dotnet run --project day1 -- <demo>");
    Console.WriteLine();
    Console.WriteLine("可用 demo:");
    foreach (var demo in demos)
    {
        Console.WriteLine($"  {demo.Key,-10} {demo.Description}");
    }
}