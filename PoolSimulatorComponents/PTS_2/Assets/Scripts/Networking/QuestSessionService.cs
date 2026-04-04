using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class QuestPeerRuntimeState
{
    public string IpAddress;
    public DeviceInformation Role;
    public float LastSeenRealtimeSec;

    public bool IsAuthoritative => Role == DeviceInformation.PrimaryQuest;

    public QuestPeerRuntimeState(string ipAddress, DeviceInformation role, float lastSeenRealtimeSec)
    {
        IpAddress = ipAddress;
        Role = role;
        LastSeenRealtimeSec = lastSeenRealtimeSec;
    }

    public QuestPeerRuntimeState Clone() => new(IpAddress, Role, LastSeenRealtimeSec);
}

public class QuestSessionService : MonoBehaviour
{
    public static QuestSessionService Instance { get; private set; }

    [SerializeField] private bool VerboseLogs = false;
    [SerializeField] private float PeerTimeoutSec = 10f; // e.g. stale peer timeout

    private readonly Dictionary<string, QuestPeerRuntimeState> _peersByIp = new(StringComparer.Ordinal);

    public int ActivePeerCount => GetPeersSnapshot(includeExpired: false).Count;
    public bool HasAuthoritativePeer => TryGetAuthoritativePeer(out _);
    public string AuthoritativePeerIp => TryGetAuthoritativePeer(out QuestPeerRuntimeState peer) ? peer.IpAddress : null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update() => PruneExpiredPeers();

    public void ApplyPeerSnapshot(IReadOnlyList<QuestPeerRuntimeState> peerSnapshot)
    {
        float now = Time.unscaledTime;
        HashSet<string> incomingIps = new(StringComparer.Ordinal);

        if (peerSnapshot != null)
        {
            for (int i = 0; i < peerSnapshot.Count; i++)
            {
                QuestPeerRuntimeState incoming = peerSnapshot[i];
                if (incoming == null || string.IsNullOrWhiteSpace(incoming.IpAddress))
                    continue;

                incoming.LastSeenRealtimeSec = now; // UPDATED: receiver refreshes peer liveness when q arrives.
                incomingIps.Add(incoming.IpAddress);
                _peersByIp[incoming.IpAddress] = incoming.Clone();
            }
        }

        List<string> removedKeys = null;
        foreach (string existingKey in _peersByIp.Keys)
        {
            if (incomingIps.Contains(existingKey))
                continue;

            removedKeys ??= new List<string>();
            removedKeys.Add(existingKey);
        }

        if (removedKeys != null)
        {
            for (int i = 0; i < removedKeys.Count; i++)
                _peersByIp.Remove(removedKeys[i]);
        }

        if (VerboseLogs)
        {
            Debug.Log(
                "[QuestSessionService] Applied peer snapshot. " +
                $"Active={_peersByIp.Count}, Authoritative={AuthoritativePeerIp ?? "<none>"}");
        }
    }

    public List<QuestPeerRuntimeState> GetPeersSnapshot(bool includeExpired = false)
    {
        float now = Time.unscaledTime;
        List<QuestPeerRuntimeState> snapshot = new(_peersByIp.Count);

        foreach (QuestPeerRuntimeState peer in _peersByIp.Values)
        {
            if (!includeExpired && (now - peer.LastSeenRealtimeSec) > PeerTimeoutSec)
                continue;

            snapshot.Add(peer.Clone());
        }

        snapshot.Sort((a, b) => string.CompareOrdinal(a.IpAddress, b.IpAddress));
        return snapshot;
    }

    public bool TryGetAuthoritativePeer(out QuestPeerRuntimeState peerState)
    {
        float now = Time.unscaledTime;

        foreach (QuestPeerRuntimeState peer in _peersByIp.Values)
        {
            if ((now - peer.LastSeenRealtimeSec) > PeerTimeoutSec)
                continue;

            if (peer.Role != DeviceInformation.PrimaryQuest)
                continue;

            peerState = peer.Clone();
            return true;
        }

        peerState = null;
        return false;
    }

    private void PruneExpiredPeers()
    {
        float now = Time.unscaledTime;
        List<string> expiredKeys = null;

        foreach (KeyValuePair<string, QuestPeerRuntimeState> pair in _peersByIp)
        {
            if ((now - pair.Value.LastSeenRealtimeSec) <= PeerTimeoutSec)
                continue;

            expiredKeys ??= new List<string>();
            expiredKeys.Add(pair.Key);
        }

        if (expiredKeys == null)
            return;

        for (int i = 0; i < expiredKeys.Count; i++)
            _peersByIp.Remove(expiredKeys[i]);

        if (VerboseLogs)
            Debug.Log($"[QuestSessionService] Pruned expired peers. Active={_peersByIp.Count}");
    }
}