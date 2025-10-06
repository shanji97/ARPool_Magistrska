using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.Mathematics;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;

public class UsbSocketReceiver : MonoBehaviour
{
    public const int Port = 5005;
    public bool AutoStart = true;
    public bool VerboseLogs = true;
    public bool UseTableYFromStream = true;
    public float OverrideTableY = 0.80f;

    private TcpListener _listener;
    private Thread _listenerThread;
    private volatile bool _running = false;
    private readonly ConcurrentQueue<string> _blocks = new();

    // parsed state (XZ pairs in TL,TR,ML,MR,BL,BR order)
    private readonly (float x, float z)[] _pocketsXZ = new (float, float)[6];
    private bool _havePockets = false;
    private float _tableY = .8f;
    private Vector2 _tableSize = new(2.54f, 1.27f);

    void Start() { if (AutoStart) StartServer(); }
    void OnDestroy() => StopServer();

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

    public static bool TryNextToken(ref ReadOnlySpan<char> s, char sep, out ReadOnlySpan<char> tok)
    {
        int i = s.IndexOf(sep);
        if (i < 0) { tok = s; s = ReadOnlySpan<char>.Empty; return tok.Length > 0; }
        tok = s[..i]; s = s[(i + 1)..]; return true;
    }
    public static bool TryParseFloat(ReadOnlySpan<char> span, out float value) =>
        float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public void Update()
    {
        var svc = PocketMarkerService.Instance;
        if (svc == null) return;

        while (_blocks.TryDequeue(out var block))
        {
            try { ParseBlock(block); } catch (System.Exception e) { Debug.LogWarning(e); }
        }

        // apply only if NOT locked and NOT finalized
        if (!_havePockets || svc.IsLocked || svc.LockFinalized)
        {
            // ignore only pockets, keep the rest.
            return;
        }
        else
        {
            ApplyPockets(svc, UseTableYFromStream ? _tableY : OverrideTableY);
        }
    }



    private void ParseBlock(string block)
    {
        ReadOnlySpan<char> span = block.AsSpan().Trim();
        int start = 0;
        byte parsedPockets = 0;
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

                if (token.SequenceEqual("p"))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (body.IsEmpty) break;

                        TryNextToken(ref body, ';', out var pair);
                        if (pair.IsEmpty) break; // stop instead of continue so we don't mark 'havePockets' prematurely

                        // split "x,y"
                        TryNextToken(ref pair, ',', out var xS);
                        var zS = pair;

                        // (optional) trim spaces if your tokenizer doesn't already
                        xS = xS.Trim();
                        zS = zS.Trim();

                        if (TryParseFloat(xS, out float x) && TryParseFloat(zS, out float z))
                        {
                            _pocketsXZ[i] = (x, z);   // Python (x,y) -> Unity (x,z)
                            parsedPockets++;
                        }
                        else
                        {
                            break; // malformed pair: stop and don't claim success
                        }
                    }
                    _havePockets = parsedPockets == 6;
                }
                else if (token.SequenceEqual("ts"))
                {
                    TryNextToken(ref body, ',', out var lS);
                    TryNextToken(ref body, ',', out var wS);
                    var yS = body;
                    if (TryParseFloat(lS, out float L) && TryParseFloat(wS, out float W) && TryParseFloat(yS, out float Y))
                    {
                        _tableSize = new Vector2(L, W);
                        _tableY = Y;
                        if (VerboseLogs) Debug.Log($"[USB] Table: {L:F3}×{W:F3} @ {Y:F3} m");
                    }
                }
                // c/e/so/st ignored for now
            }
            if (newLine < 0) break;
            start += newLine + 1;
        }
    }

    private void ApplyPockets(PocketMarkerService svc, float y)
    {
        svc.SetTable(_tableSize.x, _tableSize.y, y);
        svc.SetPocketsXZ(_pocketsXZ, y);
    }

#if UNITY_EDITOR
    [ContextMenu("USB/Test Inject Sample Block")]
    private void TestInjectSampleBlock()
    {
        // standard 9ft table + 6 pockets in your order (TL,TR,ML,MR,BL,BR)
        string block =
            "p 0.0320000,1.2400000;2.5080001,1.2400000;1.2700000,0.0600000;1.2700000,1.2100000;0.0320000,0.0320000;2.5080001,0.0320000\n" +
            "ts 2.5400000,1.2700000,0.8000000\n";
        ParseBlock(block);
    }
#endif
}