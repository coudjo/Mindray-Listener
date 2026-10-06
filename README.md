# Mindray Listener

A .NET worker that listens on a serial port for a Mindray BC-3000Plus hematology analyzer, completes the device handshake, and parses each result into structured blocks.

During development it runs as a console app (`dotnet run`). The same build installs as a Windows Service. Parsed results are handed to `IResultProcessor`. The current implementation logs them. Database persistence is a later phase and does not require changes to the worker.

Requires the .NET 9 SDK.

## Layout

```
src/MindrayListenerService/     serial listener and protocol parser
tests/MindrayMiddleware.Tests/  parser tests against a real sample-1669 capture
tools/MindrayDeviceSimulator/   replays that capture on a serial port
```

## Protocol

This is not ASTM or HL7. The framing below was taken from a live capture.

1. The analyzer sends `DLE` (`0x10`). The host replies `ACK` (`0x06`).
2. The analyzer sends `ENQ` (`0x05`), the header text (`CTR`), then `ETX` (`0x03`). The host replies `ACK` again. Capture starts at `ENQ`.
3. The analyzer streams the message body. There is no end-of-message byte. The worker treats the transmission as complete after `InactivityTimeoutMs` with no new bytes (500 ms by default).

Each block is `name`, `FF` (`0x0C`), then `key SYN (0x16) value BS (0x08)` fields, ended by `EOT` (`0x04`). Histogram blocks (`WBCHisto`, `RBCHisto`, `PLTHisto`) declare `DataLen` (256 on this device). The following `WHistoData`, `RHistoData`, or `PHistoData` value is that many raw bytes and must be read by length, because bin values can equal the delimiter bytes. A short binary footer follows the last block and is kept but not decoded.

The checked-in capture is sample **1669**, taken `2026-10-04 15:47:33`. Host `ACK` bytes from that log are not part of the message.

## Configure

Serial settings live in `src/MindrayListenerService/appsettings.json`.

| Setting | Default | Purpose |
|---|---|---|
| `ComPort` | `COM7` | Port name. On macOS or Linux this is a device path such as `/dev/cu.usbserial-XXXX`. |
| `BaudRate` | `9600` | 8 data bits, no parity, one stop bit, no handshake. |
| `InactivityTimeoutMs` | `500` | Quiet period that ends a transmission. |

`appsettings.Development.json` overrides `ComPort` only. `dotnet run` sets `DOTNET_ENVIRONMENT=Development` through `Properties/launchSettings.json`, so that file is what you edit for a local adapter.

## Run

From the repository root:

```bash
dotnet run --project src/MindrayListenerService
```

The log line for a parsed result includes the sample id, block count, and each parameter that has a `Val` field (value, low, high, unit).

## Test

Parser tests use the sample-1669 capture directly. No serial port is required.

```bash
dotnet test
```

## Simulator

`MindrayDeviceSimulator` plays the capture from the analyzer's side: `DLE`, wait for `ACK`, `ENQ`, wait for `ACK`, then the rest of the body in small chunks. Pair it with the worker through a virtual null-modem (com0com on Windows, or `socat` on macOS/Linux). Point the worker's `ComPort` at one side and the simulator at the other.

```bash
dotnet run --project tools/MindrayDeviceSimulator -- COM20
```

Optional arguments are chunk size (default 32) and delay between chunks in milliseconds (default 20). The delay must stay below `InactivityTimeoutMs` or the worker will split one result into two.

```bash
dotnet run --project tools/MindrayDeviceSimulator -- COM20 32 20
```

The simulator opens the port at 9600 8N1, matching the worker defaults.

## Install as a Windows Service

```bash
dotnet publish src/MindrayListenerService -c Release -o C:\Services\MindrayListener
sc create MindrayListener binPath="C:\Services\MindrayListener\MindrayListenerService.exe"
sc start MindrayListener
```

`UseWindowsService()` is a no-op when you run the console app. The service runs as `LocalSystem` by default, which can open COM ports in typical setups. If the port opens from the console but not as a service, check the service logon account. Set `DOTNET_ENVIRONMENT` only when you want the Development port override; an installed service uses `appsettings.json` otherwise.

## Out of scope for now

- Saving results, matching them to a test request, or any other database work
- A web app, notifications, or printing
- Interpreting histogram bytes or the trailing footer
