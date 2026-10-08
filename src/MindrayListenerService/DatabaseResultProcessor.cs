using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MindrayMiddleware;

public class DatabaseResultProcessor : IResultProcessor
{
    public const string Pending = "Pending";
    public const string Ready = "Ready";

    private static readonly string[] ParameterOrder =
    [
        "WBC", "Lymph#", "Mid#", "Gran#", "Lymph%", "Mid%", "Gran%",
        "HGB", "RBC", "HCT", "MCV", "MCH", "MCHC", "RDW-CV", "RDW-SD",
        "PLT", "MPV", "PDW", "PCT", "P-LCR"
    ];

    private static readonly (string Block, string Field, string Type)[] Histograms =
    [
        ("WBCHisto", "WHistoData", "WBC"),
        ("RBCHisto", "RHistoData", "RBC"),
        ("PLTHisto", "PHistoData", "PLT")
    ];

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DatabaseResultProcessor> _logger;

    public DatabaseResultProcessor(IServiceScopeFactory scopes, ILogger<DatabaseResultProcessor> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task ProcessAsync(MindrayMessage message, byte[] rawBytes, CancellationToken ct)
    {
        var sample = message.FindBlock("SampleInfo");
        var sampleId = sample is not null && sample.Fields.TryGetValue("SampleID", out var id) ? id.Trim() : "";

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ListenerDbContext>();
        await SaveAsync(db, message, rawBytes, ct);
        _logger.LogInformation("Saved analyzer result for SampleID {SampleId}", sampleId.Length == 0 ? "(none)" : sampleId);
    }

    internal static async Task SaveAsync(ListenerDbContext db, MindrayMessage message, byte[] rawBytes, CancellationToken ct)
    {
        var sample = message.FindBlock("SampleInfo");
        var sampleId = sample is not null && sample.Fields.TryGetValue("SampleID", out var id)
            ? id.Trim()
            : "";

        DateTime? deviceTime = null;
        if (sample is not null
            && sample.Fields.TryGetValue("TestTime", out var testTime)
            && DateTime.TryParseExact(testTime, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            deviceTime = parsed;
        }

        var result = new TestResult
        {
            SampleIdReceived = sampleId,
            DeviceTestTime = deviceTime,
            ReceivedAt = DateTime.UtcNow,
            RawPayload = rawBytes.ToArray()
        };

        foreach (var block in message.Blocks)
        {
            if (!block.Fields.TryGetValue("Val", out var value))
                continue;

            var low = block.Fields.GetValueOrDefault("Low") ?? "";
            var high = block.Fields.GetValueOrDefault("High") ?? "";
            var order = Array.IndexOf(ParameterOrder, block.Name);
            result.Parameters.Add(new ResultParameter
            {
                ParamName = block.Name,
                Value = value,
                Low = low,
                High = high,
                Unit = block.Fields.GetValueOrDefault("Unit") ?? "",
                Flag = Flag(value, low, high),
                SortOrder = order >= 0 ? order : ParameterOrder.Length
            });
        }

        foreach (var (blockName, field, type) in Histograms)
        {
            var block = message.FindBlock(blockName);
            if (block is not null && block.BinaryFields.TryGetValue(field, out var bytes))
            {
                result.Histograms.Add(new ResultHistogram
                {
                    HistoType = type,
                    RawBytes = bytes.ToArray()
                });
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        TestRequest? request = null;
        if (sampleId.Length > 0)
        {
            request = await db.TestRequests
                .FirstOrDefaultAsync(r => r.SampleId == sampleId && r.Status == Pending, ct);
        }

        if (request is not null)
        {
            result.TestRequestId = request.TestRequestId;
            request.Status = Ready;
        }

        db.TestResults.Add(result);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    internal static string? Flag(string value, string low, string high)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return null;
        if (decimal.TryParse(high, NumberStyles.Number, CultureInfo.InvariantCulture, out var highValue) && parsed > highValue)
            return "H";
        if (decimal.TryParse(low, NumberStyles.Number, CultureInfo.InvariantCulture, out var lowValue) && parsed < lowValue)
            return "L";
        return null;
    }
}
