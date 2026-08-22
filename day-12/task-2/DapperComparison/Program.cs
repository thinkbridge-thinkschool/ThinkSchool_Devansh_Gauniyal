using DapperComparison;

if (args.Length > 0 && args[0] == "run-comparison")
{
    var dbPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "dappercomparison.db");
    var outputDir = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "output");
    EvidenceRunner.Run(dbPath, outputDir);
    return;
}

Console.WriteLine("Usage: dotnet run -- run-comparison [dbPath] [outputDir]");
