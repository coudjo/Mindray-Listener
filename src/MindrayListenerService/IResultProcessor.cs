using Microsoft.Extensions.Logging;

namespace MindrayMiddleware
{
    /// <summary>
    /// Handles a fully parsed result. <see cref="DatabaseResultProcessor"/> writes it
    /// to the izi-labs database. <see cref="LoggingResultProcessor"/> is used only
    /// when no connection string is configured.
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

            return Task.CompletedTask;
        }
    }
}