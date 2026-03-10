using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UsbSocketReceiver : MonoBehaviour
{
    public const int Port = 5005;
    public bool AutoStart = true;
    public bool VerboseLogs = false;
    public const short RECEIVERBUFFER = 4096;

    private TcpListener _listener;
    private Thread _listenerThread;
    private volatile bool _running = false;
    private readonly ConcurrentQueue<string> _blocks = new();
    private TableService svc = TableService.Instance;
    private (float, float)[] pocketsXZ = null;


    [SerializeField] private byte MaxBlocksPerFrame = 32;
    [SerializeField] private bool logSocketTraffic = false;
    private readonly byte _queuedBlocksWarningThreshold = 128;

    private int _connectionSequence = 0;
    private string _lastAppliedEnvironmentKey = null;
    private bool _loggedEnvironmentSummaryThisSession = false;

    // parsed state (XZ pairs in TL,TR,ML,MR,BL,BR order)
    public void Start()
    {
        EnsureSvc();
        pocketsXZ ??= new (float, float)[6];

        if (AutoStart) StartServer();
    }

    public void OnDestroy() => StopServer();

    public void OnApplicationQuit() => StopServer();

    public void Update()
    {
        if (svc == null)
        {
            EnsureSvc();
            if (svc == null) return;
        }
        byte processedThisFrame = 0;
        while (processedThisFrame < MaxBlocksPerFrame && _blocks.TryDequeue(out var block))
        {
            try
            {
                ParseBlock(block, pocketsXZ);
            }
            catch (Exception e)
            {
                Debug.LogWarning(e);
            }
            processedThisFrame++;
        }

    }

    public void StartServer()
    {
        if (_running) return;

        _running = true;
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        _listenerThread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "UsbSocketReceiver_AcceptLoop"
        };
        _listenerThread.Start();

        if (VerboseLogs)
            Debug.Log($"[USB] TcpListener started on :{Port}");
    }

    public void StopServer()
    {
        _running = false;

        try { _listener?.Stop(); } catch { }
        try { _listenerThread?.Join(500); } catch { }

        _listener = null;
        _listenerThread = null;

        if (VerboseLogs)
            Debug.Log("[USB] TcpListener stopped.");
    }

    private void EnsureSvc()
    {
        if (svc != null) return;
        svc = TableService.Instance;
        if (svc == null)
        {
            Debug.LogWarning(
                "[UsbSocketReceiver] 'svc' is not assigned. "
            );
        }
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client = null;

            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (SocketException se)
            {
                if (_running)
                    Debug.LogWarning($"[USB] Accept failed: {se.Message}");
                continue;
            }
            catch (Exception e)
            {
                if (_running)
                    Debug.LogWarning($"[USB] Unexpected accept failure: {e}");
                continue;
            }

            int connectionId = Interlocked.Increment(ref _connectionSequence);

            try
            {
                client.NoDelay = true;
                try
                {
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                catch { }

                if (VerboseLogs)
                    Debug.Log($"[USB] Client #{connectionId} connected from {client.Client.RemoteEndPoint}");

                using var stream = client.GetStream();

                var buf = new byte[RECEIVERBUFFER];
                string tail = string.Empty;
                ulong totalBytes = 0;

                while (_running)
                {
                    int read;

                    try
                    {
                        read = stream.Read(buf, 0, buf.Length);
                    }
                    catch (IOException ioEx)
                    {
                        if (_running)
                            Debug.LogWarning($"[USB] Client #{connectionId} read failed: {ioEx.Message}");
                        break;
                    }
                    catch (SocketException se)
                    {
                        if (_running)
                            Debug.LogWarning($"[USB] Client #{connectionId} socket error: {se.Message}");
                        break;
                    }

                    if (read <= 0)
                        break;

                    totalBytes += (ulong)read;

                    string chunk = Encoding.UTF8.GetString(buf, 0, read);

                    if (logSocketTraffic && VerboseLogs)
                    {
                        Debug.Log($"[USB] Client #{connectionId} read {read} bytes (total {totalBytes})");
                        Debug.Log($"[USB] Client #{connectionId} chunk: '{chunk}'");
                    }

                    string data = tail + chunk;
                    int idx;

                    while ((idx = data.IndexOf('\n')) >= 0)
                    {
                        var one = data[..idx].TrimEnd('\r');
                        EnqueueBlock(one);
                        data = data[(idx + 1)..];
                    }

                    tail = data;
                }

                if (logSocketTraffic && VerboseLogs)
                    Debug.Log($"[USB] Client #{connectionId} read loop ended. totalBytes={totalBytes}");
            }
            catch (Exception e)
            {
                if (_running)
                    Debug.LogWarning($"[USB] Client #{connectionId} handler failed: {e}");
            }
            finally
            {
                try { client?.Close(); } catch { }

                if (VerboseLogs)
                    Debug.Log($"[USB] Client #{connectionId} disconnected.");
            }
        }
    }

    private void EnqueueBlock(string block)
    {
        block = block.Replace("\r\n", "\n").Trim('\r', '\n');
        if (string.IsNullOrWhiteSpace(block)) return;

        _blocks.Enqueue(block + "\n");

        if (VerboseLogs && _blocks.Count > _queuedBlocksWarningThreshold)
            Debug.LogWarning($"[USB] Receiver backlog growing: {_blocks.Count} queued blocks.");
    }

    private static bool TryNextToken(ref ReadOnlySpan<char> s, char sep, out ReadOnlySpan<char> tok)
    {
        int i = s.IndexOf(sep);
        if (i < 0) { tok = s; s = ReadOnlySpan<char>.Empty; return tok.Length > 0; }
        tok = s[..i]; s = s[(i + 1)..]; return true;
    }

    private static bool TryParseFloat(ReadOnlySpan<char> span, out float value) =>
        float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void ParseBlock(string block, (float x, float z)[] pocketsXZ)
    {
        EnsureSvc();
        if (svc == null)
        {
            Debug.LogWarning("[USB] ParseBlock skipped because TableService is not available yet.");
            return;
        }

        ReadOnlySpan<char> span = block.AsSpan().Trim();
        int start = 0;

        while (start < span.Length)
        {
            int newLine = span[start..].IndexOf('\n');
            ReadOnlySpan<char> line = newLine >= 0 ? span.Slice(start, newLine) : span[start..];
            line = line.Trim();

            if (!line.IsEmpty)
            {
                int separator = line.IndexOf(' ');
                ReadOnlySpan<char> token = separator > 0 ? line[..separator] : line;
                ReadOnlySpan<char> body = separator > 0 ? line[(separator + 1)..] : ReadOnlySpan<char>.Empty;

                if (svc.HasAllPockets() && svc.ArePropertiesParsed() && svc.LockFinalized && BallTypeWire.TryParseToken(token, out var ballType))
                {
                    ParseBalls(line, ballType);
                }
                else if (!svc.LockFinalized && !svc.HasAllPockets() && token.SequenceEqual("p"))
                {
                    var parsedPockets = new (float x, float z)[svc.MAX_POCKET_COUNT];
                    byte parsedPocketCount = 0;
                    var pocketBody = body;

                    while (parsedPocketCount < svc.MAX_POCKET_COUNT && !pocketBody.IsEmpty)
                    {
                        TryNextToken(ref pocketBody, ';', out var pair);
                        if (pair.IsEmpty) break;

                        TryNextToken(ref pair, ',', out var xS);
                        var zS = pair;

                        xS = xS.Trim();
                        zS = zS.Trim();

                        if (!TryParseFloat(xS, out float x) || !TryParseFloat(zS, out float z))
                        {
                            parsedPocketCount = 0;
                            break;
                        }

                        parsedPockets[parsedPocketCount] = (x, z);
                        parsedPocketCount++;
                    }

                    if (parsedPocketCount == svc.MAX_POCKET_COUNT)
                    {
                        pocketsXZ ??= new (float x, float z)[svc.MAX_POCKET_COUNT];
                        Array.Copy(parsedPockets, pocketsXZ, svc.MAX_POCKET_COUNT);

                        for (byte i = 0; i < svc.MAX_POCKET_COUNT; i++)
                            svc.IncrementSuccessfullyParsedPocketCount();

                        svc.SetPocketsXZ(pocketsXZ);

                        if (VerboseLogs)
                            Debug.Log("[USB] Parsed and applied all 6 pockets.");
                    }
                    else if (VerboseLogs)
                    {
                        Debug.LogWarning($"[USB] Ignored malformed pocket line. Parsed count={parsedPocketCount}.");
                    }
                }
                else if (!svc.LockFinalized && !svc.ArePropertiesParsed() && token.SequenceEqual("E"))
                {
                    var environmentJsonData = body.ToString().Trim().Replace(".json", string.Empty);
                    if (string.Equals(_lastAppliedEnvironmentKey, environmentJsonData, StringComparison.Ordinal))
                    {
                        if (newLine < 0) break;
                        start += newLine + 1;
                        continue;
                    }

                    var resourcePath = $"TableConfigurations/{environmentJsonData}";
                    var jsonAsset = Resources.Load<TextAsset>(resourcePath);

                    if (jsonAsset == null)
                    {
                        Debug.LogError($"[Unity] JSON resource not found at Resources/{resourcePath}.json");
                    }
                    else
                    {
                        var data = jsonAsset.text;
                        if (string.IsNullOrWhiteSpace(data))
                        {
                            Debug.LogError($"[Unity] JSON is empty at Resources/{resourcePath}.json");
                        }
                        else
                        {
                            try
                            {
                                var env = JsonConvert.DeserializeObject<EnvironmentInfo>(data);
                                if (env == null)
                                {
                                    Debug.LogError($"[Unity] Failed to deserialize EnvironmentInfo from {resourcePath}.json");
                                }
                                else
                                {
                                    bool applied = ApplyEnvironment(env);
                                    if (applied)
                                    {
                                        _lastAppliedEnvironmentKey = environmentJsonData;

                                        // MODIFIED: null-safe compare/update to avoid NullReferenceException.
                                        string currentName =
                                            AppSettings.Instance?.Settings?.EnviromentInfo?.Table?.Name;

                                        string newName = env.Table?.Name;

                                        if (!string.Equals(currentName, newName, StringComparison.Ordinal))
                                        {
                                            if (AppSettings.Instance?.Settings != null)
                                                AppSettings.Instance.Settings.EnviromentInfo = env;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // MODIFIED: This catch wraps both deserialize and post-deserialize processing,
                                // so the message should not claim deserialization only.
                                Debug.LogError($"[Unity] Environment apply failed for {resourcePath}.json: {ex}");
                            }
                        }
                    }
                }
            }

            if (newLine < 0) break;
            start += newLine + 1;
        }
    }

    private bool ApplyEnvironment(float playfieldLength = -1,
                              float playfieldWidth = -1,
                              float playfieldHeight = -1,
                              float ballDiameter = -1,
                              float ballCircumference = -1,
                              float cameraHeightFromFloor = -1)
    {
        EnsureSvc();
        if (svc == null)
        {
            Debug.LogError("[USB] ApplyEnvironment failed because TableService is null.");
            return false;
        }

        if (svc.ArePropertiesParsed())
            return true;

        svc.SetBallDiameter(ballDiameter);
        svc.SetBallCircumference(ballCircumference);
        svc.SetCamera(cameraHeightFromFloor);

        svc.SetTableLenght(playfieldLength);
        svc.SetTableWidth(playfieldWidth);

        if (!svc.IsTableHeightSet() && playfieldHeight > 0f)
        {
            svc.SetTable(playfieldLength, playfieldWidth, playfieldHeight);

            if (svc.HasAllPockets())
                svc.ReapplyPockets();
        }

        if (VerboseLogs && !_loggedEnvironmentSummaryThisSession)
        {
            Debug.Log($"[USB] Table(t): L={playfieldLength:F3}m,\r\n" +
                      $" W={playfieldWidth:F3}m,\r\n" +
                      $" H={playfieldHeight:F3}m,\r\n" +
                      $" BD={ballDiameter:F5}m,\r\n" +
                      $" BC={ballCircumference:F5}m,\r\n" +
                      $" C={cameraHeightFromFloor:F3}m");

            _loggedEnvironmentSummaryThisSession = true;
        }

        return svc.ArePropertiesParsed();
    }

    private bool ApplyEnvironment(EnvironmentInfo env)
    {
        if (env?.Table == null)
        {
            Debug.LogError("[USB] Environment info not properly cached or corrupted.");
            return false;
        }

        float lengthM = MmToM(env.Table.Length);
        float widthM = MmToM(env.Table.Width);
        float heightM = MmToM(env.Table.Height);

        return ApplyEnvironment(
            lengthM,
            widthM,
            heightM,
            env.BallSpec?.DiameterM ?? -1f,
            env.BallSpec?.BallCircumferenceM ?? -1f,
            env.CameraData?.HeightFromFloorM ?? -1f
        );
    }

    private void ParseBalls(ReadOnlySpan<char> line, BallType balltype)
    {
        //TODO: check the parsing algorithm.

        // skip token (c/e/so/st) + whitespace
        int spaceIndex = line.IndexOf(' ');
        var data = line[(spaceIndex + 1)..];

        // split by comma
        var parts = data.ToString().Split(',');

        // Example: x,y,id,conf,vx,vy
        float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
        float y = float.Parse(parts[1], CultureInfo.InvariantCulture);

        byte id = (byte)balltype;

        float conf = float.Parse(parts[3], CultureInfo.InvariantCulture);
        float vx = float.Parse(parts[4], CultureInfo.InvariantCulture);
        float vy = float.Parse(parts[5], CultureInfo.InvariantCulture);

        svc.PlaceBalls(x, y, id, conf, vx, vy);
    }

    private static float MmToM(float valueMm) // UPDATED: keep JSON in mm, convert only at Unity runtime boundary
    {
        return valueMm > 0f ? valueMm / 1000f : valueMm;
    }

#if UNITY_EDITOR
    [ContextMenu("USB/Test Inject Sample Block (full balls set)")]
    private void TestInjectSampleBlock()
    {
        (float x, float z)[] pocketsXZ = null;
        EnsureSvc();
        if (svc == null)
        {
            Debug.LogError("[USB] TableService.Instance is still null — make sure a TableService is in the scene.");
            return;
        }

        const string block =
            "E predator_9ft_virtual_debug.json\n" +
            "p 0.0320000,1.2400000;2.5080001,1.2400000;1.2700000,0.0600000;1.2700000,1.2100000;0.0320000,0.0320000;2.5080001,0.0320000\n" +
            "e 1.2500000,0.6350000,8,0.97,0.00,0.00\n" +
            "c 1.2700000,0.4000000,/,0.92,0.15,-0.10\n" +
            "st 0.3000000,0.5000000,9,0.88,0.20,-0.05; 0.4500000,0.5200000,10,0.91,\"\",''; 0.6000000,0.5400000,11,\\,-0.10,0.00; 0.7500000,0.5600000,12,0.66,0.00,0.00; 0.9000000,0.5800000,13,0.80,0.05,0.02; 1.0500000,0.6000000,14,0.74,-0.02,0.03; 1.2000000,0.6200000,15,0.60,\\,0.00\n" +
            "so 0.3500000,0.3000000,1,0.95,0.10,0.00; 0.5000000,0.3200000,2,0.93,-0.12,0.04; 0.6500000,0.3400000,3,\\,-0.05,\\; 0.8000000,0.3600000,4,0.85,0.00,0.00; 0.9500000,0.3800000,5,0.70,\\,\\; 1.1000000,0.4000000,6,0.78,0.03,-0.01; 1.2500000,0.4200000,7,0.82,0.01,0.02\n" +
            "t L=2.5400000; W=1.2700000; H=0.7850000; B=0.0571500; C=2.5000000\n";

        ParseBlock(block, pocketsXZ);
    }
#endif
}