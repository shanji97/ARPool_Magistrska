using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Linq;

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
    private TableService svc;
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

        var env = AppSettings.Instance.Settings.EnviromentInfo;
        if (env != null)
            ApplyEnvironment(env, true);

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
#if UNITY_EDITOR
        HandleIssue84OverrideRegressionRepeat();
#endif
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
        List<IncomingDetectedBallRecord> parsedBallSnapshot = null;
        bool hasParsedCueStickRecord = false;
        IncomingCueStickRecord parsedCueStickRecord = default;
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

                if (BallTypeWire.TryParseToken(token, out var ballType))
                {
                    parsedBallSnapshot ??= new List<IncomingDetectedBallRecord>(svc.MAX_BALL_COUNT);
                    ParseBalls(line, ballType, parsedBallSnapshot);
                }

                if (!svc.LockFinalized && token.SequenceEqual("E"))
                {
                    var environmentJsonData = body.ToString().Trim().Replace(".json", string.Empty);
                    if (string.Equals(_lastAppliedEnvironmentKey, environmentJsonData, StringComparison.Ordinal))
                    {
                        if (newLine < 0)
                            break;

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
                                var newEnvironment = JsonConvert.DeserializeObject<EnvironmentInfo>(data);
                                if (newEnvironment == null)
                                {
                                    Debug.LogError($"[Unity] Failed to deserialize EnvironmentInfo from {resourcePath}.json");
                                }
                                else
                                {
                                    bool applied = ApplyEnvironment(newEnvironment, isLoadedFromBackup: false);

                                    if (applied)
                                    {
                                        _lastAppliedEnvironmentKey = environmentJsonData;

                                        if (AppSettings.Instance?.Settings != null)
                                        {
                                            AppSettings.Instance.Settings.EnviromentInfo = newEnvironment;
                                            AppSettings.Instance.Save();
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[Unity] Environment apply failed for {resourcePath}.json: {ex}");
                            }
                        }
                    }
                }
                else if (!svc.LockFinalized && !svc.HasAllPockets() && token.SequenceEqual("p"))
                {
                    var parsedPockets = new (float x, float z)[svc.MAX_POCKET_COUNT];
                    byte parsedPocketCount = 0;
                    var pocketBody = body;

                    while (parsedPocketCount < svc.MAX_POCKET_COUNT && !pocketBody.IsEmpty)
                    {
                        TryNextToken(ref pocketBody, ';', out var pair);
                        if (pair.IsEmpty)
                            break;

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
                else if (token.SequenceEqual("s"))
                {
                    if (TryParseCueStick(body, out var cueStickRecord))
                    {
                        parsedCueStickRecord = cueStickRecord;
                        hasParsedCueStickRecord = true;

                        if (VerboseLogs)
                        {
                            Debug.Log(
                                $"[USB] Parsed cue stick: " +
                                $"line=({cueStickRecord.LinePointXZ.X:F3},{cueStickRecord.LinePointXZ.Y:F3}) " +
                                $"dir=({cueStickRecord.DirectionXZ.X:F4},{cueStickRecord.DirectionXZ.Y:F4}) " +
                                $"hit=({cueStickRecord.HitPointXZ.X:F3},{cueStickRecord.HitPointXZ.Y:F3}) " +
                                $"conf={cueStickRecord.Confidence:F2}");
                        }
                    }
                    else if (VerboseLogs)
                    {
                        Debug.LogWarning($"[USB] Ignored malformed cue stick line: '{line.ToString()}'");
                    }
                }
            }

            if (newLine < 0)
                break;

            start += newLine + 1;
        }

        if (parsedBallSnapshot != null && parsedBallSnapshot.Any())
            svc.ApplyDetectedBallSnapshot(parsedBallSnapshot);
        if (hasParsedCueStickRecord)
            svc.ApplyCueStickRecord(parsedCueStickRecord);
    }

    private bool ApplyEnvironment(float playfieldLength = -1,
                              float playfieldWidth = -1,
                              float playfieldHeight = -1,
                              float ballDiameter = -1,
                              float ballCircumference = -1,
                              float cameraHeightFromFloor = -1,
                              bool isLoadedFromBackup = false)
    {

        EnsureSvc();
        if (svc == null)
        {
            Debug.LogError("[USB] ApplyEnvironment failed because TableService is null.");
            return false;
        }
        svc.SetEnvironmentLoadedFromBackup(isLoadedFromBackup);

        if (isLoadedFromBackup)
        {
            if (!svc.ArePropertiesParsed())
            {
                svc.SetBallDiameter(ballDiameter);
                svc.SetBallCircumference(ballCircumference);
                svc.SetCamera(cameraHeightFromFloor);

                svc.SetTableLenght(playfieldLength);
                svc.SetTableWidth(playfieldWidth);

                if (playfieldHeight > 0f)
                {
                    svc.SetTable(playfieldLength, playfieldWidth, playfieldHeight);

                    if (svc.HasAllPockets())
                        svc.ReapplyPockets();
                }
            }
        }
        else
        {
            svc.SetBallDiameter(ballDiameter);
            svc.SetBallCircumference(ballCircumference);
            svc.SetCamera(cameraHeightFromFloor);

            svc.SetTableLenght(playfieldLength);
            svc.SetTableWidth(playfieldWidth);

            if (playfieldHeight > 0f)
            {
                svc.SetTable(playfieldLength, playfieldWidth, playfieldHeight);

                if (svc.HasAllPockets())
                    svc.ReapplyPockets();
            }
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

    private bool ApplyEnvironment(EnvironmentInfo env, bool isLoadedFromBackup = false)
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
            env.CameraData?.HeightFromFloorM ?? -1f,
            isLoadedFromBackup
        );
    }

    private void ParseBalls(ReadOnlySpan<char> line, BallType ballType, List<IncomingDetectedBallRecord> targetSnapshot)
    {
        int spaceIndex = line.IndexOf(' ');
        if (spaceIndex < 0 || spaceIndex >= line.Length - 1)
            return;

        ReadOnlySpan<char> data = line[(spaceIndex + 1)..].Trim();
        if (data.IsEmpty)
            return;

        string[] entries = data.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries);

        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            string entry = entries[entryIndex].Trim();
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            string[] parts = entry.Split(',', StringSplitOptions.None);
            if (parts.Length < 6)
            {
                if (VerboseLogs)
                    Debug.LogWarning($"[USB] Skipping malformed ball entry (expected 6 fields): '{entry}'");

                continue;
            }

            if (!TryParseFlexibleFloat(parts[0], out float x) ||
                !TryParseFlexibleFloat(parts[1], out float y))
            {
                if (VerboseLogs)
                    Debug.LogWarning($"[USB] Skipping ball entry due to invalid position: '{entry}'");

                continue;
            }

            byte rawIncomingId = (byte)ballType;

            if (!TryParseFlexibleFloat(parts[3], out float conf))
            {
                if (VerboseLogs)
                    Debug.LogWarning($"[USB] Skipping ball entry due to invalid confidence: '{entry}'");

                continue;
            }

            TryParseFlexibleFloat(parts[4], out float vx, defaultValue: 0f);
            TryParseFlexibleFloat(parts[5], out float vy, defaultValue: 0f);

            targetSnapshot.Add(
                new IncomingDetectedBallRecord(
                    ballType,
                    rawIncomingId,
                    new Vector2Float(x, y),
                    conf,
                    new Vector2Float(vx, vy)));
        }
    }

    private static bool TryParseCueStick(ReadOnlySpan<char> body, out IncomingCueStickRecord cueStickRecord)
    {
        cueStickRecord = default;

        ReadOnlySpan<char> payload = body.Trim();
        if (payload.IsEmpty)
            return false;

        if (!TryNextToken(ref payload, ';', out var linePointRaw))
            return false;

        if (!TryNextToken(ref payload, ';', out var directionRaw))
            return false;

        if (!TryNextToken(ref payload, ';', out var hitPointRaw))
            return false;

        var confidenceRaw = payload.Trim();

        if (!TryParseVector2Float(linePointRaw, out var linePointXZ))
            return false;

        if (!TryParseVector2Float(directionRaw, out var directionXZ))
            return false;

        if (!TryParseVector2Float(hitPointRaw, out var hitPointXZ))
            return false;

        if (!TryParseFloat(confidenceRaw, out float confidence))
            return false;

        cueStickRecord = new IncomingCueStickRecord(
            linePointXZ,
            directionXZ,
            hitPointXZ,
            confidence);

        return true;
    }


    private static bool TryParseFlexibleFloat(string raw, out float value, float defaultValue = 0f)
    {
        value = defaultValue;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        string cleaned = raw.Trim();

        // UPDATED: tolerate current placeholder tokens coming from debug / partial Python payloads.
        if (cleaned == "\\" ||
            cleaned == "/" ||
            cleaned == "u" ||
            cleaned == "\"\"" ||
            cleaned == "''" ||
            cleaned.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("nan", StringComparison.OrdinalIgnoreCase))
        {
            value = defaultValue;
            return true;
        }

        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseVector2Float(ReadOnlySpan<char> raw, out Vector2Float value)
    {
        value = default;

        ReadOnlySpan<char> pair = raw.Trim();
        if (pair.IsEmpty)
            return false;

        if (!TryNextToken(ref pair, ',', out var xS))
            return false;

        var yS = pair;

        xS = xS.Trim();
        yS = yS.Trim();

        if (!TryParseFloat(xS, out float x) || !TryParseFloat(yS, out float y))
            return false;

        value = new Vector2Float(x, y);
        return true;
    }


    private static float MmToM(float valueMm) // UPDATED: keep JSON in mm, convert only at Unity runtime boundary
    {
        return valueMm > 0f ? valueMm / 1000f : valueMm;
    }

#if UNITY_EDITOR

    [SerializeField] private bool repeatIssue84OverrideRegressionBlock = false;
    [SerializeField] private float repeatIssue84OverrideRegressionIntervalSec = 0.25f;
    private float _nextIssue84OverrideRegressionInjectTime = 0f;

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
            "so 0.3500000,0.3000000,1,0.95,0.10,0.00; 0.5000000,0.3200000,2,0.93,-0.12,0.04; 0.6500000,0.3400000,3,\\,-0.05,\\; 0.8000000,0.3600000,4,0.85,0.00,0.00; 0.9500000,0.3800000,5,0.70,\\,\\; 1.1000000,0.4000000,6,0.78,0.03,-0.01; 1.2500000,0.4200000,7,0.82,0.01,0.02\n";

        ParseBlock(block, pocketsXZ);
    }

    [ContextMenu("USB/Test Inject Sample Block (ISSUE-83 stripe near lower-middle pocket)")]
    private void TestInjectIssue83StripeNearLowerMiddlePocket()
    {
        (float x, float z)[] pocketsXZ = null;

        EnsureSvc();
        if (svc == null)
        {
            Debug.LogError("[USB] TableService.Instance is still null — make sure a TableService is present in the scene.");
            return;
        }

        string block = BuildIssue83StripeNearLowerMiddlePocketBlock();

        Debug.Log(
            "[USB] Injecting ISSUE-83 sample block. " +
            "Expected result: the stripe near the lower-middle pocket should be suppressed " +
            "or marked ambiguous by the near-pocket logic.");

        ParseBlock(block, pocketsXZ);
    }

    private static string BuildIssue83StripeNearLowerMiddlePocketBlock()
    {
        return
            "E predator_9ft_virtual_debug.json\n" +
            "p 0.0320000,1.2400000;2.5080001,1.2400000;1.2700000,0.0600000;1.2700000,1.2100000;0.0320000,0.0320000;2.5080001,0.0320000\n" +
            "e 0.6196690,0.5729381,8,0.91796875,\\,\\\n" +
            "c 0.1438348,0.5885691,/,0.935546875,\\,\\\n" +
            "st 2.1871898,1.1307166,u,0.94091796875,\\,\\; 1.6080190,0.4053252,u,0.93994140625,\\,\\; 1.8339624,1.0690025,u,0.92431640625,\\,\\; 2.1732988,0.4029377,u,0.92333984375,\\,\\; 0.4337689,0.7016096,u,0.91845703125,\\,\\; 1.0316985,0.4764176,u,0.9111328125,\\,\\; 1.2302539,-0.0113007,u,0.8681640625,\\,\\\n" +
            "so 0.2275915,0.5222517,u,0.93505859375,\\,\\; 0.2587466,1.1564102,u,0.93115234375,\\,\\; 0.5787677,0.2162453,u,0.92431640625,\\,\\; 1.9773390,0.2994787,u,0.92431640625,\\,\\; 1.6321940,0.5848715,u,0.9228515625,\\,\\; 1.3385810,0.4352357,u,0.9208984375,\\,\\\n";

    }


    [ContextMenu("USB/Start Repeating ISSUE-84 Override Regression Block")]
    private void StartRepeatingIssue84OverrideRegressionBlock()
    {
        repeatIssue84OverrideRegressionBlock = true;
        _nextIssue84OverrideRegressionInjectTime = 0f;
        Debug.Log("[USB] Started repeating ISSUE-84 override regression block.");
    }

    [ContextMenu("USB/Stop Repeating ISSUE-84 Override Regression Block")]
    private void StopRepeatingIssue84OverrideRegressionBlock()
    {
        repeatIssue84OverrideRegressionBlock = false;
        Debug.Log("[USB] Stopped repeating ISSUE-84 override regression block.");
    }

    [ContextMenu("USB/Test Inject ISSUE-84 Override Regression Block")]
    private void TestInjectIssue84OverrideRegressionBlock()
    {
        TestInjectIssue83StripeNearLowerMiddlePocket();
    }

    private void HandleIssue84OverrideRegressionRepeat()
    {
        if (!repeatIssue84OverrideRegressionBlock)
            return;

        if (Time.unscaledTime < _nextIssue84OverrideRegressionInjectTime)
            return;

        _nextIssue84OverrideRegressionInjectTime =
            Time.unscaledTime + Mathf.Max(0.05f, repeatIssue84OverrideRegressionIntervalSec);

        Debug.Log("Looop executed");

        TestInjectIssue83StripeNearLowerMiddlePocket();
    }

    [ContextMenu("USB/Test Inject Cue Stick Block")]
    private void TestInjectCueStickBlock()
    {
        (float x, float z)[] pocketsXZ = null;
        EnsureSvc();
        if (svc == null)
        {
            Debug.LogError("[USB] TableService.Instance is still null — make sure a TableService is in the scene.");
            return;
        }

        const string block ="s 0.3069,0.8606;-0.2317,-0.9728;0.2485,0.6155;0.98\n";

        Debug.Log("[USB] Injecting cue stick test block.");
        ParseBlock(block, pocketsXZ);
    }
#endif
}
