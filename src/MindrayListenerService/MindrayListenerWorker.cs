using System.IO.Ports;
using System.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Timer = System.Timers.Timer;

namespace MindrayMiddleware
{
    public class MindrayListenerWorker : BackgroundService
    {
        private const byte ACK = 0x06;
        private const byte ENQ = 0x05;
        private const byte DLE = 0x10;

        private readonly SerialSettings _settings;
        private readonly IResultProcessor _resultProcessor;
        private readonly ILogger<MindrayListenerWorker> _logger;

        private SerialPort? _port;
        private readonly List<byte> _messageBuffer = new();
        private readonly object _bufferLock = new();
        private Timer? _quietTimer;
        private bool _capturing;

        public MindrayListenerWorker(
            IOptions<SerialSettings> settings,
            IResultProcessor resultProcessor,
            ILogger<MindrayListenerWorker> logger)
        {
            _settings = settings.Value;
            _resultProcessor = resultProcessor;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _quietTimer = new Timer(_settings.InactivityTimeoutMs) { AutoReset = false };
            _quietTimer.Elapsed += OnQuietPeriodElapsed;

            OpenPort();

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            finally
            {
                ClosePort();
            }
        }

        private void OpenPort()
        {
            _port = new SerialPort(_settings.ComPort, _settings.BaudRate, _settings.Parity,
                                     _settings.DataBits, _settings.StopBits)
            {
                Handshake = _settings.Handshake,
                ReadTimeout = _settings.ReadTimeoutMs,
                WriteTimeout = _settings.WriteTimeoutMs,
                DtrEnable = true,
                RtsEnable = true
            };

            _port.DataReceived += Port_DataReceived;
            _port.ErrorReceived += (_, e) => _logger.LogWarning("Serial error: {EventType}", e.EventType);

            try
            {
                _port.Open();
                _logger.LogInformation("Listening on {Port} @ {Baud} baud (DTR/RTS asserted)",
                    _settings.ComPort, _settings.BaudRate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open {Port}", _settings.ComPort);
            }
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int bytesToRead = _port!.BytesToRead;
                if (bytesToRead == 0) return;

                byte[] chunk = new byte[bytesToRead];
                _port.Read(chunk, 0, bytesToRead);

                lock (_bufferLock)
                {
                    foreach (byte b in chunk)
                    {
                        if (!_capturing)
                        {
                            // Waiting for the analyzer's sync sequence: DLE, then ENQ.
                            // Only ENQ actually starts message capture — DLE is just
                            // a preliminary "are you there" that also expects an ACK.
                            if (b == DLE || b == ENQ)
                            {
                                _port.Write(new byte[] { ACK }, 0, 1);
                                _logger.LogDebug("Sync byte 0x{Byte:X2} received, ACK sent", b);

                                if (b == ENQ)
                                {
                                    _capturing = true;
                                    _messageBuffer.Add(b);
                                }
                            }
                            // Any other stray byte while idle is ignored.
                        }
                        else
                        {
                            _messageBuffer.Add(b);
                        }
                    }

                    if (_capturing)
                    {
                        // Reset the inactivity window every time bytes arrive;
                        // it only fires once the analyzer has actually stopped sending.
                        _quietTimer!.Stop();
                        _quietTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading serial data");
            }
        }

        private async void OnQuietPeriodElapsed(object? sender, ElapsedEventArgs e)
        {
            byte[] completeMessage;

            lock (_bufferLock)
            {
                if (!_capturing || _messageBuffer.Count == 0) return;

                completeMessage = _messageBuffer.ToArray();
                _messageBuffer.Clear();
                _capturing = false;
            }

            _logger.LogInformation("Transmission complete: {ByteCount} bytes captured", completeMessage.Length);

            try
            {
                var parsed = MindrayProtocolParser.Parse(completeMessage);
                await _resultProcessor.ProcessAsync(parsed, completeMessage, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse/process transmission");
            }
        }

        private void ClosePort()
        {
            if (_port != null && _port.IsOpen)
            {
                _port.Close();
                _logger.LogInformation("Serial port closed");
            }
        }

        public override void Dispose()
        {
            _quietTimer?.Dispose();
            _port?.Dispose();
            base.Dispose();
        }
    }
}