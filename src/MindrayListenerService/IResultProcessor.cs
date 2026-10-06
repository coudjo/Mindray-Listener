using Microsoft.Extensions.Logging;

namespace MindrayMiddleware
{
    /// <summary>
    /// Handles a fully parsed result. The real implementation (Phase 3) will look
    /// up the TestRequest by SampleID and persist TestResult/ResultParameters rows;
    /// this stub just logs, so the listener is runnable and testable before the
    /// data layer exists.
    /// </summary>
    public interface IResultProcessor
    {
        Task ProcessAsync(MindrayMessage message, byte[] rawBytes, CancellationToken ct);
    }

    public class LoggingResultProcessor : IResultProcessor
    {
        private readonly ILogger<LoggingResultProcessor> _logger;

        public LoggingResultProcessor(ILogger<LoggingResultProcessor> logger)
        {
            _logger = logger;
        }

        public Task ProcessAsync(MindrayMessage message, byte[] rawBytes, CancellationToken ct)
        {
            var sampleInfo = message.FindBlock("SampleInfo");
            string sampleId = sampleInfo != null && sampleInfo.Fields.TryGetValue("SampleID", out var id)
                ? id
                : "(unknown)";

            _logger.LogInformation(
                "Parsed result — SampleID: {SampleId}, Blocks: {BlockCount}, RawBytes: {ByteCount}",
                sampleId, message.Blocks.Count, rawBytes.Length);

            foreach (var block in message.Blocks)
            {
                if (block.Fields.TryGetValue("Val", out var val))
                {
                    _logger.LogInformation("  {Param}: {Val} (range {Low}-{High} {Unit})",
                        block.Name, val,
                        block.Fields.GetValueOrDefault("Low"),
                        block.Fields.GetValueOrDefault("High"),
                        block.Fields.GetValueOrDefault("Unit"));
                }
            }

            // TODO Phase 3: EF Core — find TestRequest by sampleId, save TestResult +
            // ResultParameters, mark Status = Ready. If no matching TestRequest,
            // save with RequestId = null for manual reconciliation.

            return Task.CompletedTask;
        }
    }
}