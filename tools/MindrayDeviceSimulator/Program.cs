using System.IO.Ports;
using MindrayMiddleware;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: MindrayDeviceSimulator <com-port> [chunk-size] [chunk-delay-ms]");
    Console.Error.WriteLine("Replays the sample capture: DLE, wait for ACK, ENQ, wait for ACK, then the body in chunks.");
    Console.Error.WriteLine("Keep chunk-delay-ms below the worker InactivityTimeoutMs (500).");
    return 1;
}

string portName = args[0];
if (!int.TryParse(args.Length > 1 ? args[1] : "32", out int chunkSize) || chunkSize < 1)
{
    Console.Error.WriteLine("chunk-size must be a positive integer.");
    return 1;
}

if (!int.TryParse(args.Length > 2 ? args[2] : "20", out int delayMs) || delayMs < 0)
{
    Console.Error.WriteLine("chunk-delay-ms must be zero or a positive integer.");
    return 1;
}

byte[] bytes = MindraySampleCapture.RawWithHandshake;

using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
{
    Handshake = Handshake.None,
    ReadTimeout = 3000,
    WriteTimeout = 3000,
    DtrEnable = true,
    RtsEnable = true
};

try
{
    port.Open();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to open {portName}: {ex.Message}");
    return 1;
}

try
{
    port.Write(bytes, 0, 1);
    ExpectAck(port, "DLE");

    port.Write(bytes, 1, 1);
    ExpectAck(port, "ENQ");

    int remaining = bytes.Length - 2;
    for (int offset = 2; offset < bytes.Length; offset += chunkSize)
    {
        int count = Math.Min(chunkSize, bytes.Length - offset);
        port.Write(bytes, offset, count);
        remaining -= count;
        if (remaining > 0)
            Thread.Sleep(delayMs);
    }
}
catch (TimeoutException)
{
    Console.Error.WriteLine("Timed out waiting for ACK. Is the listener running on the paired port?");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Replay failed: {ex.Message}");
    return 1;
}

Console.WriteLine($"Replayed {bytes.Length} bytes on {portName}.");
return 0;

static void ExpectAck(SerialPort port, string step)
{
    int b = port.ReadByte();
    if (b != 0x06)
        throw new InvalidOperationException($"Expected ACK after {step}, got 0x{b:X2}.");
    Console.WriteLine($"ACK received after {step}.");
}
