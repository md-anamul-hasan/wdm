using System.Diagnostics;
using Wdm.Core.Engine;
using Xunit.Abstractions;

namespace Wdm.Tests;

/// <summary>
/// Loopback throughput benchmark. Skipped unless WDM_BENCH=1 so normal test runs stay fast:
/// <c>WDM_BENCH=1 dotnet test tests/Wdm.Tests -c Release --filter AuditBenchmark -v n</c>.
/// Loopback has no latency or throttling, so it measures engine/disk overhead, not real-network gains.
/// </summary>
public class AuditBenchmark(ITestOutputHelper output)
{
    [Fact]
    public async Task ConnectionCountThroughput()
    {
        if (Environment.GetEnvironmentVariable("WDM_BENCH") != "1") return;

        var data = new byte[256 * 1024 * 1024];
        new Random(1).NextBytes(data);
        using var server = AuditStressTests.RangeServer(data);
        string dir = Path.Combine(Path.GetTempPath(), $"wdm_bench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            output.WriteLine("connections | seconds | MB/s | peak working set MB | managed MB after GC");
            foreach (int connections in new[] { 1, 2, 4, 8, 16, 32 })
            {
                var times = new List<double>();
                long peak = 0;
                for (int run = 0; run < 3; run++)
                {
                    await using var engine = new DownloadEngine();
                    var clock = Stopwatch.StartNew();
                    await engine.StartAsync($"{server.Prefix}b{connections}_{run}.bin", dir, connections);
                    times.Add(clock.Elapsed.TotalSeconds);
                    peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64);
                    File.Delete(engine.Metadata!.TargetFilePath);
                }
                double median = times.OrderBy(t => t).ElementAt(1);
                long managed = GC.GetTotalMemory(forceFullCollection: true);
                output.WriteLine($"{connections,11} | {median,7:F2} | {data.Length / 1048576.0 / median,6:F0} | {peak / 1048576} | {managed / 1048576}");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
