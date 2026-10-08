using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindrayMiddleware;

namespace MindrayMiddleware.Tests;

public class DatabaseResultProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IResultProcessor _processor;

    public DatabaseResultProcessorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ListenerDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IResultProcessor, DatabaseResultProcessor>();
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ListenerDbContext>().Database.EnsureCreated();
        _processor = _provider.GetRequiredService<IResultProcessor>();
    }

    [Fact]
    public async Task Sample_1669_matches_a_pending_request_and_stores_flags()
    {
        await SeedPendingAsync("1669");
        var message = MindrayProtocolParser.Parse(MindraySampleCapture.MessageOnly);

        await _processor.ProcessAsync(message, MindraySampleCapture.MessageOnly, CancellationToken.None);

        await using var db = NewContext();
        var request = await db.TestRequests.SingleAsync();
        var result = await db.TestResults.Include(r => r.Parameters).Include(r => r.Histograms).SingleAsync();

        Assert.Equal(DatabaseResultProcessor.Ready, request.Status);
        Assert.Equal(request.TestRequestId, result.TestRequestId);
        Assert.Equal("1669", result.SampleIdReceived);
        Assert.Equal(new DateTime(2026, 10, 4, 15, 47, 33), result.DeviceTestTime);
        Assert.Equal(MindraySampleCapture.MessageOnly, result.RawPayload);
        Assert.Equal("H", result.Parameters.Single(p => p.ParamName == "WBC").Flag);
        Assert.Equal("L", result.Parameters.Single(p => p.ParamName == "MCH").Flag);
        Assert.Null(result.Parameters.Single(p => p.ParamName == "HGB").Flag);
        Assert.Equal(3, result.Histograms.Count);
        Assert.All(result.Histograms, h => Assert.Equal(256, h.RawBytes.Length));
        Assert.Equal(0, result.Parameters.Single(p => p.ParamName == "WBC").SortOrder);
    }

    [Fact]
    public async Task A_second_transmission_for_a_matched_sample_stays_unmatched()
    {
        await SeedPendingAsync("1669");
        var message = MindrayProtocolParser.Parse(MindraySampleCapture.MessageOnly);

        await _processor.ProcessAsync(message, MindraySampleCapture.MessageOnly, CancellationToken.None);
        await _processor.ProcessAsync(message, MindraySampleCapture.MessageOnly, CancellationToken.None);

        await using var db = NewContext();
        var results = await db.TestResults.OrderBy(r => r.TestResultId).ToListAsync();
        Assert.Equal(2, results.Count);
        Assert.NotNull(results[0].TestRequestId);
        Assert.Null(results[1].TestRequestId);
        Assert.Equal(DatabaseResultProcessor.Ready, (await db.TestRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Blank_sample_id_is_stored_unmatched()
    {
        await SeedPendingAsync("1669");

        await _processor.ProcessAsync(Message(""), [0x01], CancellationToken.None);

        await using var db = NewContext();
        var result = await db.TestResults.SingleAsync();
        Assert.Equal("", result.SampleIdReceived);
        Assert.Null(result.TestRequestId);
        Assert.Equal(DatabaseResultProcessor.Pending, (await db.TestRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Unknown_sample_id_is_stored_unmatched()
    {
        await SeedPendingAsync("1669");

        await _processor.ProcessAsync(Message("9999"), [0x02], CancellationToken.None);

        await using var db = NewContext();
        var result = await db.TestResults.Include(r => r.Parameters).SingleAsync();
        Assert.Equal("9999", result.SampleIdReceived);
        Assert.Null(result.TestRequestId);
        Assert.Equal("9.2", result.Parameters.Single().Value);
        Assert.Equal(DatabaseResultProcessor.Pending, (await db.TestRequests.SingleAsync()).Status);
    }

    private async Task SeedPendingAsync(string sampleId)
    {
        await using var db = NewContext();
        db.TestRequests.Add(new TestRequest { SampleId = sampleId, Status = DatabaseResultProcessor.Pending });
        await db.SaveChangesAsync();
    }

    private ListenerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ListenerDbContext>().UseSqlite(_connection).Options;
        return new ListenerDbContext(options);
    }

    private static MindrayMessage Message(string sampleId)
    {
        var message = new MindrayMessage();
        var info = new MindrayBlock { Name = "SampleInfo" };
        info.Fields["SampleID"] = sampleId;
        info.Fields["TestTime"] = "2026-10-04 15:47:33";
        var wbc = new MindrayBlock { Name = "WBC" };
        wbc.Fields["Val"] = "9.2";
        wbc.Fields["Low"] = "4.0";
        wbc.Fields["High"] = "10.0";
        wbc.Fields["Unit"] = "10^9/L";
        message.Blocks.Add(info);
        message.Blocks.Add(wbc);
        return message;
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
