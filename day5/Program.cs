// Day 5（選讀 bonus）的進入點。
// 主題：把 Rust 的所有權／借用／錯誤處理／struct 觀念，落到 C# 的設計紀律上。
//
// 這一天不在 4 天主線裡，是給「已經會 Rust，想知道 C# 怎麼處理同樣的問題」的人看的。
// 對應教材：01-rust-to-csharp.md

using CSharpLearn.Day5;

var demos = new (string Key, string Description, Func<CancellationToken, Task> Run)[]
{
    ("ownership", "所有權：誰負責 Dispose、CTS、HttpClient 生命週期", OwnershipDemo.RunAsync),
    ("borrowing", "借用：in、readonly struct、Span<T> 與零配置", BorrowingDemo.RunAsync),
    ("errors", "錯誤處理：Result、TryXxx、UnreachableException", ErrorsDemo.RunAsync),
    ("struct", "struct：值語意、readonly record struct、溢位與循環參考", StructDemo.RunAsync),
};

if (!TryResolve(args, demos, out var selected))
{
    return;
}

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await selected.Run(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("已取消。");
}

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
    Console.WriteLine("用法: dotnet run --project day5 -- <demo>");
    Console.WriteLine();
    Console.WriteLine("可用 demo（選讀 bonus）:");
    foreach (var demo in demos)
    {
        Console.WriteLine($"  {demo.Key,-12} {demo.Description}");
    }
}
