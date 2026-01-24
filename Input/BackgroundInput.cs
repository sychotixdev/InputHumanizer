using System;
using System.IO;
using System.IO.Pipes;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace InputHumanizer.Input
{
    public enum CommandType : uint
    {
        SetCursorPath = 1,
        SetKeyState = 2,
        ClearKeyState = 3,
        Ping = 4,
        MouseClick = 5,
        ClearCursor = 6,
        GetForcedCursor = 7,
        ClientIdle = 8
    }

    public enum ResponseType : uint
    {
        CursorPathComplete = 1,
        KeyStateAccepted = 2,
        Pong = 3,
        MouseClickComplete = 4,
        CursorCleared = 5,
        ForcedCursorPosition = 6,
        ClientIdleAck = 7
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandHeader
    {
        public CommandType Type;
        public uint DataSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ResponseHeader
    {
        public ResponseType Type;
        public uint DataSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct ForcedCursorPositionResponse
    {
        public int X;
        public int Y;
        public int IsForced; // 1 or 0
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CursorPointWithDelay
    {
        public int X;
        public int Y;
        public uint DelayMs;

        public CursorPointWithDelay(int x, int y, uint delayMs)
        {
            X = x;
            Y = y;
            DelayMs = delayMs;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CursorPathCommand
    {
        public uint PointCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct KeyStateCommand
    {
        public int VirtualKey;
        public bool IsDown;
    }

    public enum MouseButton : uint
    {
        Left = 1,
        Right = 2
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MouseClickCommand
    {
        public byte Button;
        public byte Modifiers;
        public byte HasPosition;
        public int X;
        public int Y;
    }



    public class BackgroundInput : IDisposable
    {
        private const string PipeName = "InputHookPipe";
        private NamedPipeClientStream _pipeClient;
        private readonly SemaphoreSlim _responseLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);

        private InputHumanizer _plugin;
        private long _lastSendTicks = 0;

        public bool IsConnected => _pipeClient != null && _pipeClient.IsConnected;

        public BackgroundInput(InputHumanizer plugin)
        {
            _plugin = plugin;
        }

        private void TouchLastSendTime()
        {
            Interlocked.Exchange(ref _lastSendTicks, DateTime.UtcNow.Ticks);
            LogMessage("Touched last send time.");
        }


        private void DebugLog(string message)
        {
            // Use reflection to call _plugin.LogMessage if available
            _plugin?.DebugLog(message);
        }
        private void LogMessage(string message)
        {
            // Use reflection to call _plugin.LogMessage if available
            _plugin?.LogMessage(message);
        }

        private void LogError(string message)
        {
            // Use reflection to call _plugin.LogError if available
            _plugin?.LogError(message);
        }

        public async Task<bool> ConnectAsync(int timeoutMs = 5000)
        {
            try
            {
                _pipeClient = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                await _pipeClient.ConnectAsync(timeoutMs);

                // Validate server responsiveness
                byte[] ping = new byte[8];
                BitConverter.GetBytes((uint)CommandType.Ping).CopyTo(ping, 0);
                BitConverter.GetBytes(0u).CopyTo(ping, 4);

                await _pipeClient.WriteAsync(ping, 0, ping.Length);
                await _pipeClient.FlushAsync();

                byte[] header = new byte[8];
                await ReadExactAsync(header, 8, timeoutMs);

                var response = (ResponseType)BitConverter.ToUInt32(header, 0);
                if (response != ResponseType.Pong)
                    throw new IOException("Ping validation failed");

                _plugin.DebugLog("Pipe connected and ping validated.");
                return true;
            }
            catch (Exception ex)
            {
                _plugin.LogError($"Connect failed: {ex.Message}");
                HandlePipeFailure();
                return false;
            }
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (IsConnected)
                return true;

            await _connectLock.WaitAsync();
            try
            {
                if (IsConnected)
                    return true;

                return await ConnectAsync();
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public async Task<bool> ClickAsync(
    bool rightButton,
    Vector2? position,
    MouseModifiers modifiers,
    int timeoutMs = 3000)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write((uint)CommandType.MouseClick);

            uint dataSize = 1 + 1 + 1 + (position.HasValue ? 8u : 0u);
            w.Write(dataSize);

            w.Write((byte)(rightButton ? 1 : 0));
            w.Write((byte)modifiers);
            w.Write((byte)(position.HasValue ? 1 : 0));

            if (position.HasValue)
            {
                w.Write((int)position.Value.X);
                w.Write((int)position.Value.Y);
            }

            var response = await SendCommandAndWaitAsync(ms.ToArray(), timeoutMs);
            return response.type == ResponseType.MouseClickComplete;
        }

        public async Task<bool> SetCursorPathAsync(
            CursorPointWithDelay[] points, int timeoutMs)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            uint dataSize = sizeof(uint) +
                (uint)(points.Length * Marshal.SizeOf<CursorPointWithDelay>());

            w.Write((uint)CommandType.SetCursorPath);
            w.Write(dataSize);
            w.Write((uint)points.Length);

            foreach (var p in points)
            {
                w.Write(p.X);
                w.Write(p.Y);
                w.Write(p.DelayMs);
            }

            DebugLog("Sending SetCursorPath");
            var response = await SendCommandAndWaitAsync(ms.ToArray(), timeoutMs);
            if (response.type == ResponseType.CursorPathComplete)
            {
                DebugLog("Successful cursor path response");
            }
            else {
                LogError("Failed cursor path response");
            }
            return response.type == ResponseType.CursorPathComplete;
        }

        public async Task<bool> KeyDownAsync(int virtualKey, int timeoutMs = 5000)
        {
            DebugLog($"Sending KeyDown for {virtualKey}");

            bool response = await SetKeyStateInternalAsync(virtualKey, true, timeoutMs);
            if (response == true)
            {
                DebugLog($"KeyDownAsync succeeded for virtual key {virtualKey}");
            }
            else
            {
                LogError($"KeyDownAsync failed for virtual key {virtualKey}");
            }
            return response;
        }

        public async Task<bool> KeyUpAsync(int virtualKey, int timeoutMs = 5000)
        {
            DebugLog($"Sending KeyUp for {virtualKey}");

            bool response = await SetKeyStateInternalAsync(virtualKey, false, timeoutMs);
            if (response == true)
            {
                DebugLog($"KeyUpAsync succeeded for virtual key {virtualKey}");
            }
            else
            {
                LogError($"KeyUpAsync failed for virtual key {virtualKey}");
            }
            return response;
        }

        private async Task<bool> SetKeyStateInternalAsync(int virtualKey, bool isDown, int timeoutMs)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write((uint)CommandType.SetKeyState);
            // Explicitly use 4 bytes for the boolean to match standard Win32 BOOL
            w.Write((uint)(sizeof(int) + 4));
            w.Write(virtualKey);
            w.Write(isDown ? 1 : 0); // Write as a 4-byte integer

            var response = await SendCommandAndWaitAsync(ms.ToArray(), timeoutMs);
            return response.type == ResponseType.KeyStateAccepted;
        }

        public async Task<bool> SendPingAsync()
        {
            byte[] ping = new byte[8];
            BitConverter.GetBytes((uint)CommandType.Ping).CopyTo(ping, 0);
            BitConverter.GetBytes(0u).CopyTo(ping, 4);

            try
            {
                var response = await SendCommandAndWaitAsync(ping, 3000);
                if (response.type == ResponseType.Pong)
                {
                    DebugLog("Ping successful.");
                }
                else
                {
                    LogError("Ping failed: unexpected response.");
                }
                return response.type == ResponseType.Pong;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ClearCursorAsync(int timeoutMs = 3000)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write((uint)CommandType.ClearCursor);
            w.Write(0u); // no payload

            var response = await SendCommandAndWaitAsync(ms.ToArray(), timeoutMs);
            return response.type == ResponseType.CursorCleared;
        }

        public async Task<Vector2?> GetForcedCursorPositionAsync(int timeoutMs = 2000)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // Command header
            w.Write((uint)CommandType.GetForcedCursor);
            w.Write(0u); // no payload

            var (type, payload) = await SendCommandAndWaitAsync(ms.ToArray(), timeoutMs);

            if (type != ResponseType.ForcedCursorPosition)
            {
                LogError($"Unexpected response type: {type}");
                return null;
            }

            if (payload == null || payload.Length != 12) // 3 ints = 12 bytes
            {
                LogError($"Invalid forced cursor payload size: {payload?.Length ?? 0}");
                return null;
            }

            // Read the 3 integers directly from the byte array
            int x = BitConverter.ToInt32(payload, 0);
            int y = BitConverter.ToInt32(payload, 4);
            int isForced = BitConverter.ToInt32(payload, 8);

            if (isForced == 0)
                return null;

            return new Vector2(x, y);
        }

        public async Task<bool> ReleaseControlAsync(int timeoutMs = 3000)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write((uint)CommandType.ClientIdle);
            w.Write(0u); // no payload

            var response = await SendCommandAndWaitAsync(ms.ToArray(), timeoutMs);
            return response.type == ResponseType.ClientIdleAck;
        }

        private async Task<(ResponseType type, byte[]? payload)> SendCommandAndWaitAsync(
            byte[] commandBuffer,
            int timeoutMs)
        {
            await _responseLock.WaitAsync();

            try
            {
                if (!await EnsureConnectedAsync())
                    throw new IOException("Pipe not connected");

                await _pipeClient.WriteAsync(commandBuffer, 0, commandBuffer.Length);
                await _pipeClient.FlushAsync();

                byte[] headerBuf = new byte[8];
                await ReadExactAsync(headerBuf, 8, timeoutMs);

                ResponseType type = (ResponseType)BitConverter.ToUInt32(headerBuf, 0);
                uint dataSize = BitConverter.ToUInt32(headerBuf, 4);

                byte[]? payload = null;

                if (dataSize > 0)
                {
                    byte[] buffer = new byte[dataSize];
                    await ReadExactAsync(buffer, (int)dataSize, timeoutMs);

                    payload = buffer;
                }

                TouchLastSendTime();
                return (type, payload);
            }
            catch (Exception ex)
            {
                LogError($"Pipe command failed: {ex.Message}");
                HandlePipeFailure();
                throw;
            }
            finally
            {
                _responseLock.Release();
            }
        }

        private async Task ReadExactAsync(byte[] buffer, int size, int timeoutMs)
        {
            int read = 0;
            using var cts = new CancellationTokenSource(timeoutMs);

            while (read < size)
            {
                int r = await _pipeClient.ReadAsync(
                    buffer, read, size - read, cts.Token);

                if (r == 0)
                    throw new IOException("Pipe closed");

                read += r;
            }
        }


        private void HandlePipeFailure()
        {
            try
            {
                _pipeClient?.Dispose();
                _pipeClient = null;
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (IsConnected)
                {
                    // Best-effort cleanup; ignore failures
                    ReleaseControlAsync(1000).GetAwaiter().GetResult();
                }
            }
            catch { }

            _pipeClient?.Dispose();
            _responseLock?.Dispose();
        }
    }
}