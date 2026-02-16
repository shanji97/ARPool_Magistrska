using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UsbSocketReceiver : MonoBehaviour
{
    public const int Port = 5005;
    public bool AutoStart = true;
    public bool VerboseLogs = true;

    private TcpListener _listener;
    private Thread _listenerThread;
    private volatile bool _running = false;
    private readonly ConcurrentQueue<string> _blocks = new();
    private TableService svc = TableService.Instance;

    // parsed state (XZ pairs in TL,TR,ML,MR,BL,BR order)
    public void Start()
    {
        EnsureSvc();
        var environmentInfo = AppSettings.Instance.Settings.EnviromentInfo;
        if (environmentInfo != null)
            ApplyEnvironment(environmentInfo);

        if (AutoStart) StartServer();
    }

    public void OnDestroy() => StopServer();

    public void OnApplicationQuit() => StopServer();

    public void Update()
    {
        if (svc == null) return;

        var pocketsXZ = new (float, float)[6];
        while (_blocks.TryDequeue(out var block))
        {
            try { ParseBlock(block, pocketsXZ, svc.LockFinalized); } catch (Exception e) { Debug.LogWarning(e); }
        }
    }

    public void StartServer()
    {
        if (_running) return;
        _running = true;
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        _listenerThread = new Thread(AcceptLoop) { IsBackground = true };
        _listenerThread.Start();
        if (VerboseLogs) Debug.Log($"[USB] TcpListener started :{Port}");
    }
    public void StopServer()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _listenerThread?.Join(200); } catch { }
        _listener = null; _listenerThread = null;
        if (VerboseLogs) Debug.Log("[USB] TcpListener stopped.");
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
        try
        {
            while (_running)
            {
                var client = _listener.AcceptTcpClient();
                if (VerboseLogs) Debug.Log("[USB] Client connected.");
                using var stream = client.GetStream();
                var buf = new byte[4096];
                int read; string tail = "";
                while (_running && client.Connected && (read = stream.Read(buf, 0, buf.Length)) > 0)
                {
                    string chunk = Encoding.UTF8.GetString(buf, 0, read);
                    string data = tail + chunk;
                    int idx;
                    while ((idx = data.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
                    {
                        var one = data[..idx];
                        EnqueueBlock(one);
                        data = data[(idx + 2)..];
                    }
                    tail = data;
                }
                if (VerboseLogs) Debug.Log("[USB] Client disconnected.");
            }
        }
        catch (SocketException se)
        {
            if (_running) Debug.LogWarning($"[USB] {se.Message}");
        }
        catch (Exception e)
        {
            if (_running) Debug.LogWarning($"[USB] {e}");
        }
    }

    private void EnqueueBlock(string block)
    {
        block = block.Replace("\r\n", "\n").Trim('\r', '\n');
        if (string.IsNullOrWhiteSpace(block)) return;
        _blocks.Enqueue(block + "\n");
    }

    private static bool TryNextToken(ref ReadOnlySpan<char> s, char sep, out ReadOnlySpan<char> tok)
    {
        int i = s.IndexOf(sep);
        if (i < 0) { tok = s; s = ReadOnlySpan<char>.Empty; return tok.Length > 0; }
        tok = s[..i]; s = s[(i + 1)..]; return true;
    }

    private static bool TryParseFloat(ReadOnlySpan<char> span, out float value) =>
        float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void ParseBlock(string block, (float x, float z)[] pocketsXZ, bool isLockFinalized = false)
    {
        ReadOnlySpan<char> span = block.AsSpan().Trim();
        int start = 0;

        // p -> line intended for pocket parsing
        // t -> line intended for table parsing
        // L, W, H, BD, C, BC -> lines intended for parsing table parameters. Need only once

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

                if (svc == null) continue;

                if (svc.HasAllPockets() && svc.AreProperstiesParsed() && isLockFinalized && BallTypeWire.TryParseToken(token, out var ballType))
                {
                    // Tries to save the table. Only one table currently supported.
                    if (svc.TrySaveEnviroment())
                        Debug.Log("Enviroment successfully parsed. Saved to configuration.");


                    ParseBalls(line, ballType);
                    continue;
                }

                if (!isLockFinalized && !svc.HasAllPockets() && token.SequenceEqual("p"))
                {
                    for (byte i = 0; i < svc.MaxPocketCount; i++)
                    {
                        if (body.IsEmpty) break;

                        TryNextToken(ref body, ';', out var pair);
                        if (pair.IsEmpty) break;

                        // split "x,y"
                        TryNextToken(ref pair, ',', out var xS);
                        var zS = pair;

                        // (optional) trim spaces if your tokenizer doesn't already
                        xS = xS.Trim();
                        zS = zS.Trim();

                        if (TryParseFloat(xS, out float x) && TryParseFloat(zS, out float z))
                        {
                            pocketsXZ[i] = (x, z);   // Python (x,y) -> Unity (x,z)
                            svc.IncrementPocketCount();
                        }
                        else
                        {
                            break; // malformed pair: stop and don't claim success
                        }
                    }
                    if (svc.HasAllPockets())
                        svc.SetPocketsXZ(pocketsXZ);
                }
                else if (!isLockFinalized && !svc.AreProperstiesParsed() && token.SequenceEqual("t"))
                {
                    // Expect body like: "L=2.540; W=1.270; H=0.800; name=9ft (tournament); ..."
                    // We'll scan sequentially, split by ';', then split each into "key=value".
                    float playfieldHeight = 0;
                    float playFieldLength = 0;
                    float playFieldWidth = 0;
                    float ballDiameter = 0;
                    float cameraHeightFromFloor = 0;
                    float ballCircumference = 0;

                    while (!body.IsEmpty)
                    {
                        playfieldHeight = svc.IsTableHeightSet() ? svc.TableY : -1f;
                        (playFieldLength, playFieldWidth) = svc.Is2DTableSet() ? (svc.TableSize.x, svc.TableSize.y) : (-1f, -1f);
                        ballDiameter = svc.IsBallDiameterSet() ? svc.BallDiameterM : -1f;
                        cameraHeightFromFloor = svc.IsCameraFromFloorSet() ? svc.CameraHeightFromFloor : -1f;
                        ballCircumference = svc.IsBallCircumferenceSet() ? svc.BallCircumferenceM : -1f;
                        if (!TryNextToken(ref body, ';', out ReadOnlySpan<char> pair)) break;
                        pair = pair.Trim();
                        if (pair.IsEmpty) continue;

                        int eq = pair.IndexOf('=');
                        if (eq <= 0) continue;

                        var key = pair[..eq].Trim();
                        var val = pair[(eq + 1)..].Trim();

                        // Only pull what we actually need in Unity right now:
                        switch (key)
                        {
                            case var k when k.SequenceEqual("L"):
                                TryParseFloat(val, out playFieldLength);
                                break;

                            case var k when k.SequenceEqual("W"):
                                TryParseFloat(val, out playFieldWidth);
                                break;

                            case var k when k.SequenceEqual("H"):
                                TryParseFloat(val, out playfieldHeight);
                                break;

                            case var k when k.SequenceEqual("BD"):
                                TryParseFloat(val, out ballDiameter);
                                break;

                            case var k when k.SequenceEqual("C"):
                                TryParseFloat(val, out cameraHeightFromFloor);
                                break;

                            case var k when k.SequenceEqual("BC"):
                                TryParseFloat(val, out ballCircumference);
                                break;
                        }
                    }

                    ApplyEnvironment(playFieldLength, playFieldWidth, playfieldHeight, ballDiameter, ballCircumference, cameraHeightFromFloor);
                }
            }
            if (newLine < 0) break;
            start += newLine + 1;
        }
    }

    private void ApplyEnvironment(float playfieldLength = -1,
                                  float playfieldWidth = -1,
                                  float playfieldHeight = -1,
                                  float ballDiameter = -1,
                                  float ballCircumference = -1,
                                  float cameraHeightFromFloor = -1)
    {
        // Width and length of the table.
        svc.SetTableLenght(playfieldLength);
        svc.SetTableWidth(playfieldWidth);

        if (!svc.IsTableHeightSet() && playfieldHeight > 0f)
        {
            svc.SetTable(playfieldLength, playfieldWidth, playfieldHeight);
            if (svc.HasAllPockets())
                svc.ReapplyPockets();
        }

        svc.SetBallDiameter(ballDiameter);
        svc.SetCamera(cameraHeightFromFloor);
        svc.SetBallCircumference(ballCircumference);

        if (VerboseLogs)
        {
            Debug.Log($"[USB] Table(t): L={playfieldLength:F3}m,\r\n" +
            $" W={playfieldWidth:F3}m,\r\n" +
            $" H={playfieldHeight:F3}m,\r\n" +
            $" BD={ballDiameter}m,\r\n" +
            $" BC={ballCircumference}m,\r\n" +
            $" C={cameraHeightFromFloor}m,\r\n");
        }
    }

    private void ApplyEnvironment(EnvironmentInfo env)
    {
        if (env != null)
            ApplyEnvironment(env.PoolTable.L_m, env.PoolTable.W_m, env.PoolTable.H_m, env.PoolTable.BallDiameter_m, env.PoolTable.BallCircumference_m, env.CameraCharacteristics.HFromFloor_m);
        else
            Debug.LogError("Enviroment info not properly cached or corrupted.");
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
            "p 0.0320000,1.2400000;2.5080001,1.2400000;1.2700000,0.0600000;1.2700000,1.2100000;0.0320000,0.0320000;2.5080001,0.0320000\n" +
            "e 1.2500000,0.6350000,8,0.97,0.00,0.00\n" +
            "c 1.2700000,0.4000000,/,0.92,0.15,-0.10\n" +
            "st 0.3000000,0.5000000,9,0.88,0.20,-0.05; 0.4500000,0.5200000,10,0.91,\"\",''; 0.6000000,0.5400000,11,\\,-0.10,0.00; 0.7500000,0.5600000,12,0.66,0.00,0.00; 0.9000000,0.5800000,13,0.80,0.05,0.02; 1.0500000,0.6000000,14,0.74,-0.02,0.03; 1.2000000,0.6200000,15,0.60,\\,0.00\n" +
            "so 0.3500000,0.3000000,1,0.95,0.10,0.00; 0.5000000,0.3200000,2,0.93,-0.12,0.04; 0.6500000,0.3400000,3,\\,-0.05,\\; 0.8000000,0.3600000,4,0.85,0.00,0.00; 0.9500000,0.3800000,5,0.70,\\,\\; 1.1000000,0.4000000,6,0.78,0.03,-0.01; 1.2500000,0.4200000,7,0.82,0.01,0.02\n" +
            "t L=2.5400000; W=1.2700000; H=0.7850000; B=0.0571500; C=2.5000000\n";
        ParseBlock(block, pocketsXZ, false);
    }
#endif
}