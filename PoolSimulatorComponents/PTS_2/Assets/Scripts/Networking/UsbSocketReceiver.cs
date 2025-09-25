using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class UsbSocketReceiver : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("Port that ADB forwards PC:port -> Quest:port")]
    public const int Port = 5005;

    [Tooltip("Start listening automatically on Start()")]
    public bool AutoStart = true;

    [Header("Markers & Placement")]
    [Tooltip("Pocket marker prefab (e.g., small sphere). If null, a primitive sphere is used.")]
    public GameObject PocketMarkerPrefab;

    [Tooltip("Parent transform for spawned markers (e.g., your Table root). Optional.")]
    public Transform MarkersParent;

    [Tooltip("Use table_y from 'ts' line. If false, override with overrideTableY.")]
    public bool UseTableYFromStream = true;

    [Tooltip("Override table surface height in meters (used if useTableYFromStream = false).")]
    public float OverrideTableY = 0.80f;

    [Tooltip("Offset above the surface, meters, to reduce z-fighting.")]
    public float SurfaceLift = 0.01f;

    [Header("Debug")]
    public bool VerboseLogs = true;

    public TcpListener _listener;
    private Thread _listenerThread;
    private volatile bool _running = false;
    private readonly ConcurrentQueue<string> _blocks = new();
    private readonly StringBuilder _currentBlock = new();

    private readonly Vector3[] _pockets = new Vector3[6]; // TL,TR,ML,MR,BL,BR (x,z used; y decided later)
    private bool _havePockets = false;

    private float _tableY = .8f;
    private Vector2 _tableSize = new(2.54f, 1.27f); // lenght and width, default in meters, standard 9ft table, at some point make configurable

    private GameObject[] _pocketGOs;

    void Start()
    {
        if (AutoStart) StartServer();
    }

    void OnDestroy() => StopServer();

    public void StartServer()
    {
        if (_running) return;
        _running = true;
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        _listenerThread = new Thread(AcceptLoop) { IsBackground = true };
        _listenerThread.Start();
        if (VerboseLogs) Debug.Log($"[USB] TcpListener started on :{Port}");
    }

    public void StopServer()
    {
        _running = false;
        try
        {
            _listener?.Stop();
        }
        catch { }
        try
        {
            _listenerThread.Join(200);
        }
        catch { }
        _listener = null;
        _listenerThread = null;
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
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[4096];
                    int read;
                    var previous = string.Empty;
                    while (_running && client.Connected && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        var chunk = Encoding.UTF8.GetString(buffer, 0, read);
                        var data = previous + chunk;
                        var index = -1;
                        while ((index = data.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
                        {
                            var one = data[..index];
                            EnqueueBlock(one);
                            data = data[(index + 2)..];


                        }
                        previous = data;
                    }
                }
                if (VerboseLogs) Debug.Log("[USB] Client disconnected.");
            }
        }
        catch (SocketException se)
        {
            if (_running) Debug.LogWarning($"[USB] SocketException: {se.Message}");
        }
        catch (Exception e)
        {
            if (_running) Debug.LogWarning($"[USB] Exception: {e}");
        }
    }

    private void EnqueueBlock(string block)
    {
        // Normalize line endings
        block = block.Replace("\r\n", "\n").Trim('\r', '\n');
        if (string.IsNullOrWhiteSpace(block)) return;
        _blocks.Enqueue(block + "\n"); // keep trailing newline for parser simplicity
    }

    void Update()
    {
        // Apply at main thread
        while (_blocks.TryDequeue(out var block))
        {
            try
            {
                ParseAndApply(block);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[USB] Parse error: {e}");
            }
        }

        // Lazy-spawn markers if we have pockets parsed
        if (_havePockets && (_pocketGOs == null || _pocketGOs.Length != 6))
        {
            SpawnOrEnsureMarkers();
            UpdatePocketMarkers();
        }
        else if (_havePockets && _pocketGOs != null)
        {
            UpdatePocketMarkers();
        }
    }

    private void SpawnOrEnsureMarkers()
    {
        _pocketGOs ??= new GameObject[6];
        for (int i = 0; i < 6; i++)
        {
            if (_pocketGOs[i] != null) continue;

            GameObject go;
            if (PocketMarkerPrefab != null)
            {
                go = Instantiate(PocketMarkerPrefab, Vector3.zero, Quaternion.identity, MarkersParent);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.SetParent(MarkersParent, worldPositionStays: true);
                var col = go.GetComponent<Collider>();
                col.isTrigger = true; // Create collision matrix with balls
                if (col) Destroy(col); // no collisions needed
                go.transform.localScale = Vector3.one * 0.03f; // 3 cm sphere
            }
            go.name = $"PocketMarker_{i}";
            _pocketGOs[i] = go;
        }
    }

    private void UpdatePocketMarkers()
    {
        float y = UseTableYFromStream ? _tableY : OverrideTableY;
        float liftedY = y + SurfaceLift;
        for (byte i = 0; i < 6; i++)
        {
            if (_pocketGOs[i] == null) continue;
            var p = _pockets[i];
            _pocketGOs[i].transform.position = new Vector3(p.x, liftedY, p.z);
        }
    }

    public static bool TryNextToken(ref ReadOnlySpan<char> s, char separator, out ReadOnlySpan<char> token)
    {
        var i = s.IndexOf(separator);
        if (i < 0)
        {
            token = s; s = ReadOnlySpan<char>.Empty;
            return token.Length > 0;
        }
        token = s[..i]; // Slice
        s = s[(i + 1)..];
        return true;
    }

    public static bool TryParseFloat(ReadOnlySpan<char> span, out float value) => float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    void ParseAndApply(string block)
    {
        // block is several lines, ends with \n\n
        ReadOnlySpan<char> span = block.AsSpan().Trim();
        int lineStart = 0;
        while (lineStart < span.Length)
        {
            int nl = span[lineStart..].IndexOf('\n');
            ReadOnlySpan<char> line = nl >= 0 ? span.Slice(lineStart, nl) : span[lineStart..];
            line = line.Trim();
            if (!line.IsEmpty)
            {
                int sp = line.IndexOf(' ');
                ReadOnlySpan<char> token = sp > 0 ? line[..sp] : line;
                ReadOnlySpan<char> body = sp > 0 ? line[(sp + 1)..] : ReadOnlySpan<char>.Empty;

                if (token.SequenceEqual("p"))
                {
                    // p x,y;... 6 pairs in order TL,TR,ML,MR,BL,BR
                    for (int i = 0; i < 6; i++)
                    {
                        if (body.IsEmpty) break;
                        TryNextToken(ref body, ';', out ReadOnlySpan<char> pair);
                        if (pair.IsEmpty) continue;
                        TryNextToken(ref pair, ',', out ReadOnlySpan<char> xSpan);
                        ReadOnlySpan<char> ySpan = pair;
                        if (TryParseFloat(xSpan, out float x) && TryParseFloat(ySpan, out float z))
                        {
                            _pockets[i] = new Vector3(x, 0f, z);
                        }
                    }
                    _havePockets = true;
                }
                else if (token.SequenceEqual("c") || token.SequenceEqual("e"))
                {
                    // c/e x,y,z
                    TryNextToken(ref body, ',', out ReadOnlySpan<char> xS);
                    TryNextToken(ref body, ',', out ReadOnlySpan<char> yS);
                    ReadOnlySpan<char> zS = body;
                    if (TryParseFloat(xS, out float x) && TryParseFloat(yS, out float y) && TryParseFloat(zS, out float z))
                    {
                        var v = new Vector3(x, y, z);
                        // store if you need
                    }
                }
                else if (token.SequenceEqual("so") || token.SequenceEqual("st"))
                {
                    // so/st x,y,z,n;...
                    
                }
                else if (token.SequenceEqual("ts"))
                {
                    // ts L,W,table_y
                    TryNextToken(ref body, ',', out ReadOnlySpan<char> lS);
                    TryNextToken(ref body, ',', out ReadOnlySpan<char> wS);
                    ReadOnlySpan<char> yS = body;
                    if (TryParseFloat(lS, out float L) && TryParseFloat(wS, out float W) && TryParseFloat(yS, out float Y))
                    {
                        _tableSize = new Vector2(L, W);
                        _tableY = Y;
                        if (VerboseLogs) Debug.Log($"[USB] Table: {L:F3}×{W:F3} @ {Y:F3}m");
                    }
                }
            }
            if (nl < 0) break;
            lineStart += nl + 1;
        }

        if (_havePockets)
        {
            if (_pocketGOs == null || _pocketGOs.Length != 6) { SpawnOrEnsureMarkers(); }
            UpdatePocketMarkers();
        }
    }
}