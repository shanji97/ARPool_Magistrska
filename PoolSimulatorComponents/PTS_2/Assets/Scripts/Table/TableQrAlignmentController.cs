using Meta.XR.MRUtilityKit;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TableQrAlignmentController : MonoBehaviour
{
    private const string DefaultMandatoryPayload01 = "ARPOOL_MARKER_01";
    private const string DefaultMandatoryPayload02 = "ARPOOL_MARKER_02";
    private const string DefaultMandatoryPayload03 = "ARPOOL_MARKER_03";
    private const string DefaultMandatoryPayload04 = "ARPOOL_MARKER_04";
    private const string DefaultOptionalPayload05 = "ARPOOL_MARKER_05";
    private const string DefaultOptionalPayload06 = "ARPOOL_MARKER_06";

    [Header("Dependencies")]
    public TableService TableServiceOverride;

    [Header("Workflow")]
    public bool EnableQrTrackingWorkflow = true;

    [Min(0.05f)]
    public float ScanIntervalSeconds = 0.25f;

    [Min(0f)]
    public float RescanCooldownSeconds = 0f;

    [Tooltip("Lets you continue with fewer QR markers. Set this to 2 for the current fallback workflow.")]
    [Range(2, 4)]
    public int MinimumRequiredMandatoryMarkers = 2;

    [Tooltip("When only 2 mandatory markers are required, they must form a diagonal pair.")]
    public bool RequireDiagonalPairWhenUsingTwoMarkers = true;

    public bool UseMiddleQrMarkersWhenPresent = true;

    [Min(0.001f)]
    public float PoseChangePositionThresholdM = 0.01f;

    [Min(0.1f)]
    public float PoseChangeRotationThresholdDeg = 4f;

    public bool VerboseQrLogs = true;

    [Tooltip("After the first successful QR lock, stop scanning and keep the locked alignment for the rest of the current setup session.")]
    public bool FreezeQrScanningAfterSuccessfulLock = true;

    [Header("QR Payload Configuration")]
    [SerializeField]
    private string[] mandatoryCornerPayloads =
    {
        DefaultMandatoryPayload01,
        DefaultMandatoryPayload02,
        DefaultMandatoryPayload03,
        DefaultMandatoryPayload04
    };

    [SerializeField] private string optionalReferencePayload05 = DefaultOptionalPayload05;
    [SerializeField] private string optionalReferencePayload06 = DefaultOptionalPayload06;

    [SerializeField, Min(0.05f)]
    private float fallbackQrPaperSizeM = 0.16f;

    [Header("Live QR Monitor Visuals")]
    public GameObject LiveQrMarkerPrefab;
    public Transform LiveQrMarkersParent;
    [Min(0.0005f)] public float LiveQrMarkerThicknessM = 0.002f;
    public Color LiveQrMarkerColor = new(1f, 0.75f, 0f, 0.9f);

    [Header("Locked QR Monitor Visuals")]
    public GameObject LockedQrMarkerPrefab;
    public Transform LockedQrMarkersParent;
    [Min(0.0005f)] public float LockedQrMarkerThicknessM = 0.003f;
    public Color LockedQrMarkerColor = Color.green;

    private readonly Dictionary<string, Pose> _liveDetections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Pose> _lockedDetections = new(StringComparer.Ordinal);

    private readonly Dictionary<string, GameObject> _liveMarkerVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameObject> _lockedMarkerVisuals = new(StringComparer.Ordinal);

    private TableService _tableService;
    private float _nextScanTime;
    private float _lastSuccessfulLockTime = -999f;
    private bool _hasAppliedPocketPlacementFromQr;
    private bool _qrAlignmentLockedForSession;

    public bool IsQrAlignmentLockedForSession => _qrAlignmentLockedForSession;

    public int LiveDetectedCount => _liveDetections.Count;
    public int LockedDetectedCount => _lockedDetections.Count;

    public int LiveDetectedMandatoryCount => CountMandatoryMarkers(_liveDetections.Keys);
    public int LockedDetectedMandatoryCount => CountMandatoryMarkers(_lockedDetections.Keys);

    public int LiveDetectedOptionalCount => CountOptionalMarkers(_liveDetections.Keys);
    public int LockedDetectedOptionalCount => CountOptionalMarkers(_lockedDetections.Keys);

    public bool HasAnyLiveDetections => LiveDetectedCount > 0;
    public bool HasAnyLockedDetections => LockedDetectedCount > 0;

    public bool HasAllMandatoryLiveDetections => CanSatisfyMandatoryRequirement(_liveDetections);
    public bool HasAllMandatoryLockedDetections => CanSatisfyMandatoryRequirement(_lockedDetections);

    public bool HasAppliedPocketPlacementFromQr => _hasAppliedPocketPlacementFromQr;

    public bool IsReadyToPlacePockets =>
        EnableQrTrackingWorkflow &&
        _hasAppliedPocketPlacementFromQr &&
        CanResolveLockedCornerSet();

    public bool HasPendingLockUpdate { get; private set; }

    public string LockButtonLabel =>
        _hasAppliedPocketPlacementFromQr
            ? "Re-lock QR alignment"
            : "Lock current QR code";

    public IReadOnlyDictionary<string, Pose> LockedDetections => _lockedDetections;

    private void Awake() => ResolveDependencies();

    private void OnEnable()
    {
        ResolveDependencies();
        RefreshLiveQrDetections(forceLog: true);
        TryAutoApplyLockedQrPlacementIfReady();
    }

    private void Update()
    {
        if (!EnableQrTrackingWorkflow)
            return;

        if (_qrAlignmentLockedForSession && FreezeQrScanningAfterSuccessfulLock)
        {
            if (!_hasAppliedPocketPlacementFromQr)
                TryAutoApplyLockedQrPlacementIfReady();

            return;
        }

        if (Time.unscaledTime >= _nextScanTime)
        {
            _nextScanTime = Time.unscaledTime + ScanIntervalSeconds;
            RefreshLiveQrDetections(forceLog: false);
        }

        TryAutoApplyLockedQrPlacementIfReady();
    }


    public string BuildStatusMessage()
    {
        ResolveDependencies();

        if (!EnableQrTrackingWorkflow)
            return "QR workflow disabled.";

        bool environmentParsed = _tableService != null && _tableService.CanApplyQrAlignedPocketLayout();
        bool liveSolvable = CanResolveLiveCornerSet();
        bool lockedSolvable = CanResolveLockedCornerSet();

        string liveSummary = BuildPayloadSummary(_liveDetections.Keys);
        string lockedSummary = BuildPayloadSummary(_lockedDetections.Keys);
        string solveModes = BuildSupportedSolveModesText();
        float cooldownRemaining = GetRemainingRescanCooldownSeconds();

        if (!HasAnyLiveDetections && !HasAnyLockedDetections && !_qrAlignmentLockedForSession)
            return $"No QR code detected. Show a solvable QR set ({solveModes}).";

        if (_qrAlignmentLockedForSession && _hasAppliedPocketPlacementFromQr)
        {
            return
                $"QR alignment locked for this setup session. " +
                $"Locked: {lockedSummary}. " +
                $"QR scanning is paused and the current pocket placement will not be recalculated. " +
                $"You can now manually drag the pocket markers to fine-tune them before confirming pockets.";
        }

        if (!_hasAppliedPocketPlacementFromQr)
        {
            if (cooldownRemaining > 0f && HasAnyLockedDetections)
            {
                return
                    $"QR snapshot stored. " +
                    $"Locked: {lockedSummary}. " +
                    $"Wait {cooldownRemaining:F1}s before relocking.";
            }

            if (!liveSolvable)
            {
                if (HasAnyLiveDetections)
                {
                    return
                        $"QR codes detected, but the current live set cannot solve the table yet. " +
                        $"Live detected: {liveSummary}. " +
                        $"Supported solve modes: {solveModes}.";
                }

                if (lockedSolvable)
                {
                    return
                        $"A locked QR snapshot is already stored. " +
                        $"Locked: {lockedSummary}. " +
                        $"Waiting to apply pocket alignment when the environment is ready.";
                }

                return $"No solvable QR set detected yet. Supported solve modes: {solveModes}.";
            }

            if (!HasAnyLockedDetections)
            {
                if (!environmentParsed)
                {
                    return
                        $"Current QR set can solve table alignment. " +
                        $"Live detected: {liveSummary}. " +
                        $"You can lock now; pockets will be applied when the environment is ready.";
                }

                return
                    $"Current QR set can solve table alignment. " +
                    $"Live detected: {liveSummary}. " +
                    $"Press 'Lock current QR code' to align the pockets to the real table.";
            }

            if (!environmentParsed)
            {
                return
                    $"Locked QR snapshot can solve table alignment. " +
                    $"Locked: {lockedSummary}. " +
                    $"Waiting for environment parsing before pockets are applied.";
            }

            return
                $"Locked QR snapshot can solve table alignment. " +
                $"Locked: {lockedSummary}. " +
                $"Waiting to align the pockets automatically.";
        }

        if (!environmentParsed)
        {
            return
                $"QR alignment was stored successfully, but the environment is not ready yet. " +
                $"Locked: {lockedSummary}.";
        }

        if (liveSolvable && HasAnyNewOrChangedLiveDetection())
        {
            return
                $"QR alignment applied. " +
                $"Locked: {lockedSummary}. " +
                $"Live detected: {liveSummary}. " +
                $"The current live QR set can refine the placement. Press 'Refine QR alignment' if the table moved.";
        }

        if (HasAnyLiveDetections && !liveSolvable)
        {
            return
                $"QR alignment applied from the locked snapshot. " +
                $"Locked: {lockedSummary}. " +
                $"Current live detections are incomplete, but the existing pocket placement remains valid.";
        }

        return
            $"QR alignment applied. " +
            $"Locked: {lockedSummary}. " +
            $"Optional QR markers can still refine the fit, and manual pocket correction is still allowed before confirmation.";
    }

    public Color GetSuggestedStatusColor()
    {
        ResolveDependencies();

        if (!EnableQrTrackingWorkflow)
            return Color.white;

        bool environmentParsed = _tableService != null && _tableService.CanApplyQrAlignedPocketLayout();
        bool liveSolvable = CanResolveLiveCornerSet();

        if (!HasAnyLiveDetections && !HasAnyLockedDetections)
            return Color.red;

        if (!_hasAppliedPocketPlacementFromQr)
        {
            if (liveSolvable)
                return new Color(1f, 0.8f, 0f, 1f);

            return HasAnyLiveDetections || HasAnyLockedDetections
                ? new Color(1f, 0.8f, 0f, 1f)
                : Color.red;
        }

        if (_qrAlignmentLockedForSession && _hasAppliedPocketPlacementFromQr)
            return Color.green;

        if (!environmentParsed)
            return new Color(1f, 0.8f, 0f, 1f);

        return liveSolvable && HasAnyNewOrChangedLiveDetection()
            ? new Color(1f, 0.8f, 0f, 1f)
            : Color.green;
    }



    public string BuildPocketPlacementGuidanceMessage()
    {
        ResolveDependencies();

        bool environmentParsed = _tableService != null && _tableService.CanApplyQrAlignedPocketLayout();

        if (!environmentParsed)
            return "Pocket data received. Waiting for environment parsing before QR alignment can be applied.";

        if (_qrAlignmentLockedForSession && _hasAppliedPocketPlacementFromQr)
            return "QR alignment is locked for this setup session. The pocket markers now stay in their current QR-aligned frame, and you can manually fine-tune them before confirming pockets.";

        if (!_hasAppliedPocketPlacementFromQr)
        {
            if (CanResolveLiveCornerSet())
                return "Pocket data received. The current QR set can align the pockets to the real table. Press 'Lock current QR code' to place them automatically.";

            if (CanResolveLockedCornerSet())
                return "Pocket data received. A locked QR snapshot is stored and can align the table as soon as it is applied.";

            return $"Pocket data received. Show a solvable QR set to align the pockets automatically ({BuildSupportedSolveModesText()}).";
        }

        return "Placed pockets based on QR code detections. QR scanning can still refine the fit until you lock the session, then you can make final manual adjustments and confirm pockets.";
    }

    public bool CanLockCurrentDetections
    {
        get
        {
            ResolveDependencies();

            if (!EnableQrTrackingWorkflow)
                return false;

            if (_tableService != null && _tableService.LockFinalized)
                return false;

            if (_qrAlignmentLockedForSession)
                return false;

            if (GetRemainingRescanCooldownSeconds() > 0f)
                return false;

            if (!CanResolveLiveCornerSet())
                return false;

            if (!HasAnyLockedDetections)
                return true;

            if (!CanResolveLockedCornerSet())
                return true;

            return HasPendingLockUpdate || !_hasAppliedPocketPlacementFromQr;
        }
    }

    public string BuildLockButtonStatusLabel()
    {
        if (!EnableQrTrackingWorkflow)
            return "QR disabled";

        if (_tableService != null && _tableService.LockFinalized)
            return "Pockets confirmed";

        if (_qrAlignmentLockedForSession)
            return "QR locked for session";

        float cooldownRemaining = GetRemainingRescanCooldownSeconds();
        if (cooldownRemaining > 0f)
            return $"Re-lock in {cooldownRemaining:F1}s";

        if (!HasAnyLiveDetections)
            return "Show QR codes";

        if (!CanResolveLiveCornerSet())
            return "Need solvable QR set";

        if (_hasAppliedPocketPlacementFromQr && !HasPendingLockUpdate)
            return "Alignment up to date";

        return LockButtonLabel;
    }



    public bool LockCurrentDetectionsAndApplyPockets()
    {
        ResolveDependencies();

        if (!EnableQrTrackingWorkflow)
        {
            Debug.LogWarning("[TableQrAlignmentController] QR lock ignored because the workflow is disabled.");
            return false;
        }

        if (!CanLockCurrentDetections)
        {
            Debug.LogWarning($"[TableQrAlignmentController] QR lock ignored. {BuildCannotLockReason()}");
            return false;
        }

        ReplaceLockedSnapshotWithCurrentLiveDetections();

        _qrAlignmentLockedForSession = true;
        _lastSuccessfulLockTime = Time.unscaledTime;
        HasPendingLockUpdate = ComputePendingLockUpdate();

        bool applied = TryAutoApplyLockedQrPlacementIfReady();

        if (applied)
            _tableService?.SetExternalPocketUpdatesSuppressed(true);

        if (VerboseQrLogs)
        {
            Debug.Log(
                $"[TableQrAlignmentController] QR snapshot stored. " +
                $"Locked solve mode={GetSolveModeLabel(_lockedDetections)}, " +
                $"Locked mandatory={LockedDetectedMandatoryCount}/{GetMandatoryPayloads().Length}, " +
                $"Locked optional={LockedDetectedOptionalCount}/2, " +
                $"PocketPlacementApplied={applied}");
        }

        return applied;
    }

    public float GetRemainingRescanCooldownSeconds()
    {
        float remaining = (_lastSuccessfulLockTime + RescanCooldownSeconds) - Time.unscaledTime;
        return Mathf.Max(0f, remaining);
    }

    private bool CanResolveLiveCornerSet() => TryResolveCornerSetFromDetections(_liveDetections, out _);

    private bool CanResolveLockedCornerSet() => TryResolveCornerSetFromDetections(_lockedDetections, out _);

    private string BuildCannotLockReason()
    {
        if (!EnableQrTrackingWorkflow)
            return "The QR workflow is disabled.";

        if (_tableService != null && _tableService.LockFinalized)
            return "Pocket placement is already finalized.";

        if (_qrAlignmentLockedForSession)
            return "QR alignment is already locked for this setup session.";

        float cooldownRemaining = GetRemainingRescanCooldownSeconds();
        if (cooldownRemaining > 0f)
            return $"Re-lock is on cooldown for another {cooldownRemaining:F1}s.";

        if (!HasAnyLiveDetections)
            return "No live QR detections are currently visible.";

        if (!CanResolveLiveCornerSet())
        {
            return
                $"The current live QR set cannot solve the table yet. " +
                $"Live detected: {BuildPayloadSummary(_liveDetections.Keys)}. " +
                $"Supported solve modes: {BuildSupportedSolveModesText()}.";
        }

        if (HasAnyLockedDetections && CanResolveLockedCornerSet() && !HasPendingLockUpdate && _hasAppliedPocketPlacementFromQr)
            return "The current live QR snapshot matches the locked placement, so there is nothing new to relock.";

        return "The current QR state is not lockable yet.";
    }

    private string BuildSupportedSolveModesText() =>
        "2 diagonal corner markers, 4 corner markers, or 4 corner markers plus 2 middle refiners";

    private string GetSolveModeLabel(IReadOnlyDictionary<string, Pose> detections)
    {
        List<NamedPose> mandatory = GetMandatoryNamedPoses(detections);
        if (mandatory.Count <= 0)
            return "none";

        bool has01 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload01, out _);
        bool has02 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload02, out _);
        bool has03 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload03, out _);
        bool has04 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload04, out _);

        bool hasAllFourCorners = has01 && has02 && has03 && has04;
        bool hasOptional =
            detections.ContainsKey(optionalReferencePayload05) ||
            detections.ContainsKey(optionalReferencePayload06);

        if (hasAllFourCorners)
            return hasOptional ? "4 corners + middle refinement" : "4 corners";

        if (TryGetBestDiagonalPair(mandatory, out NamedPose diagonalA, out NamedPose diagonalB))
            return $"diagonal fallback ({ShortMarkerName(diagonalA.Payload)} + {ShortMarkerName(diagonalB.Payload)})";

        return $"{mandatory.Count} corner markers, but no solvable diagonal pair yet";
    }


    public void ClearAllLockedMarkers()
    {
        _lockedDetections.Clear();
        _hasAppliedPocketPlacementFromQr = false;
        _qrAlignmentLockedForSession = false;
        _tableService?.SetExternalPocketUpdatesSuppressed(false);
        HasPendingLockUpdate = ComputePendingLockUpdate();

        foreach (KeyValuePair<string, GameObject> entry in _lockedMarkerVisuals)
        {
            if (entry.Value == null)
                continue;

            if (Application.isPlaying)
                Destroy(entry.Value);
            else
                DestroyImmediate(entry.Value);
        }

        _lockedMarkerVisuals.Clear();
    }

    private void ResolveDependencies() =>
        _tableService = TableServiceOverride != null ? TableServiceOverride : TableService.Instance;

    private void ReplaceLockedSnapshotWithCurrentLiveDetections()
    {
        ClearAllLockedMarkers();

        foreach (KeyValuePair<string, Pose> entry in _liveDetections)
        {
            if (!IsAllowedPayload(entry.Key))
                continue;

            _lockedDetections[entry.Key] = entry.Value;
            UpdateOrCreateLockedMarkerVisual(entry.Key, entry.Value);
        }
    }

    private bool TryAutoApplyLockedQrPlacementIfReady()
    {
        ResolveDependencies();

        if (_tableService == null)
            return false;

        if (_tableService.LockFinalized)
            return _hasAppliedPocketPlacementFromQr;

        if (_qrAlignmentLockedForSession && _hasAppliedPocketPlacementFromQr)
            return true;

        if (!CanResolveLockedCornerSet())
            return false;

        if (!_tableService.CanApplyQrAlignedPocketLayout())
            return false;

        bool applied = TryApplyPocketLayoutFromLockedMarkers();
        _hasAppliedPocketPlacementFromQr = applied;

        if (applied)
            _tableService.SetExternalPocketUpdatesSuppressed(true);

        HasPendingLockUpdate = ComputePendingLockUpdate();
        return applied;
    }

    private void RefreshLiveQrDetections(bool forceLog)
    {
        ResolveDependencies();

        Dictionary<string, Pose> nextLiveDetections = new(StringComparer.Ordinal);
        Dictionary<string, int> payloadCounts = new(StringComparer.Ordinal);

        MRUKTrackable[] trackables = FindObjectsByType<MRUKTrackable>(FindObjectsSortMode.None);
        int qrTrackableCount = 0;

        for (int i = 0; i < trackables.Length; i++)
        {
            MRUKTrackable trackable = trackables[i];
            if (trackable == null)
                continue;

            if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
                continue;

            qrTrackableCount++;

            string payload = trackable.MarkerPayloadString;
            if (string.IsNullOrWhiteSpace(payload))
            {
                Debug.LogWarning("[TableQrAlignmentController] Detected a QR trackable with an empty payload.");
                continue;
            }

            if (!payloadCounts.ContainsKey(payload))
                payloadCounts[payload] = 0;

            payloadCounts[payload]++;

            if (!IsAllowedPayload(payload))
            {
                Debug.LogWarning($"[TableQrAlignmentController] Ignoring detected QR payload '{payload}' because it is not part of the configured marker set.");
                continue;
            }

            nextLiveDetections[payload] = new Pose(trackable.transform.position, trackable.transform.rotation);
        }

        bool liveSetChanged = HaveDictionariesChanged(_liveDetections, nextLiveDetections);

        _liveDetections.Clear();
        foreach (KeyValuePair<string, Pose> entry in nextLiveDetections)
            _liveDetections[entry.Key] = entry.Value;

        RefreshLiveMarkerVisuals();
        HasPendingLockUpdate = ComputePendingLockUpdate();

        if ((forceLog || liveSetChanged) && VerboseQrLogs)
        {
            Debug.Log(
                $"[TableQrAlignmentController] MRUK QR trackables total={qrTrackableCount}, " +
                $"unique allowed payloads kept={_liveDetections.Count}, " +
                $"payload summary={BuildPayloadSummary(_liveDetections.Keys)}");
        }

        foreach (KeyValuePair<string, int> entry in payloadCounts)
        {
            if (entry.Value > 1)
            {
                Debug.LogWarning(
                    $"[TableQrAlignmentController] Duplicate QR payload detected: '{entry.Key}' appeared {entry.Value} times. " +
                    $"Because detections are keyed by payload string, duplicates collapse into one entry.");
            }
        }
    }

    private void RefreshLiveMarkerVisuals()
    {
        foreach (KeyValuePair<string, Pose> entry in _liveDetections)
            UpdateOrCreateLiveMarkerVisual(entry.Key, entry.Value);

        List<string> keysToRemove = new();

        foreach (KeyValuePair<string, GameObject> entry in _liveMarkerVisuals)
        {
            if (_liveDetections.ContainsKey(entry.Key))
                continue;

            if (entry.Value != null)
            {
                if (Application.isPlaying)
                    Destroy(entry.Value);
                else
                    DestroyImmediate(entry.Value);
            }

            keysToRemove.Add(entry.Key);
        }

        for (int i = 0; i < keysToRemove.Count; i++)
            _liveMarkerVisuals.Remove(keysToRemove[i]);
    }

    private bool TryApplyPocketLayoutFromLockedMarkers()
    {
        if (_tableService == null)
        {
            Debug.LogWarning("[TableQrAlignmentController] Pocket initialization failed because TableService is missing.");
            return false;
        }

        if (!_tableService.CanApplyQrAlignedPocketLayout())
        {
            Debug.LogWarning("[TableQrAlignmentController] Pocket initialization failed because the table plane is not ready yet.");
            return false;
        }

        if (!TryResolveCornerSetFromDetections(_lockedDetections, out ResolvedCornerSet corners))
        {
            Debug.LogWarning("[TableQrAlignmentController] Pocket initialization failed because the locked QR corner set could not be resolved.");
            return false;
        }

        ValidateCornerMarkerDimensions(corners);

        float cornerOffsetM = GetConfiguredCornerMarkerToCornerPocketOffsetM();
        float middleOffsetM = GetConfiguredMiddleMarkerToRailOffsetM();

        Vector3 topLeftPocketSeed = corners.TopLeft - (corners.ShortAxisBottomToTop * cornerOffsetM);
        Vector3 topRightPocketSeed = corners.TopRight - (corners.ShortAxisBottomToTop * cornerOffsetM);
        Vector3 bottomLeftPocketSeed = corners.BottomLeft + (corners.ShortAxisBottomToTop * cornerOffsetM);
        Vector3 bottomRightPocketSeed = corners.BottomRight + (corners.ShortAxisBottomToTop * cornerOffsetM);

        float leftLong = 0.5f * (
            Vector3.Dot(topLeftPocketSeed - corners.Center, corners.LongAxisLeftToRight) +
            Vector3.Dot(bottomLeftPocketSeed - corners.Center, corners.LongAxisLeftToRight));

        float rightLong = 0.5f * (
            Vector3.Dot(topRightPocketSeed - corners.Center, corners.LongAxisLeftToRight) +
            Vector3.Dot(bottomRightPocketSeed - corners.Center, corners.LongAxisLeftToRight));

        List<float> topShortSamples = new()
        {
            Vector3.Dot(topLeftPocketSeed - corners.Center, corners.ShortAxisBottomToTop),
            Vector3.Dot(topRightPocketSeed - corners.Center, corners.ShortAxisBottomToTop)
        };

        List<float> bottomShortSamples = new()
        {
            Vector3.Dot(bottomLeftPocketSeed - corners.Center, corners.ShortAxisBottomToTop),
            Vector3.Dot(bottomRightPocketSeed - corners.Center, corners.ShortAxisBottomToTop)
        };

        if (UseMiddleQrMarkersWhenPresent && _tableService.QR_USE_MIDDLE_MARKERS_WHEN_PRESENT)
            AddOptionalRailSamples(corners, middleOffsetM, topShortSamples, bottomShortSamples);

        float topShort = Average(topShortSamples);
        float bottomShort = Average(bottomShortSamples);
        float centerLong = 0.5f * (leftLong + rightLong);

        Vector3 topLeft = corners.Center + (corners.LongAxisLeftToRight * leftLong) + (corners.ShortAxisBottomToTop * topShort);
        Vector3 topRight = corners.Center + (corners.LongAxisLeftToRight * rightLong) + (corners.ShortAxisBottomToTop * topShort);
        Vector3 bottomLeft = corners.Center + (corners.LongAxisLeftToRight * leftLong) + (corners.ShortAxisBottomToTop * bottomShort);
        Vector3 bottomRight = corners.Center + (corners.LongAxisLeftToRight * rightLong) + (corners.ShortAxisBottomToTop * bottomShort);
        Vector3 bottomMiddle = corners.Center + (corners.LongAxisLeftToRight * centerLong) + (corners.ShortAxisBottomToTop * bottomShort);
        Vector3 topMiddle = corners.Center + (corners.LongAxisLeftToRight * centerLong) + (corners.ShortAxisBottomToTop * topShort);

        (float x, float z)[] pocketXZ = new (float x, float z)[_tableService.MAX_POCKET_COUNT];

        // TableService order: TL, TR, ML(bottom-middle), MR(top-middle), BL, BR
        pocketXZ[0] = (topLeft.x, topLeft.z);
        pocketXZ[1] = (topRight.x, topRight.z);
        pocketXZ[2] = (bottomMiddle.x, bottomMiddle.z);
        pocketXZ[3] = (topMiddle.x, topMiddle.z);
        pocketXZ[4] = (bottomLeft.x, bottomLeft.z);
        pocketXZ[5] = (bottomRight.x, bottomRight.z);

        _tableService.SetPocketsXZ(pocketXZ);

        if (VerboseQrLogs)
        {
            Debug.Log(
                "[TableQrAlignmentController] Pocket positions initialized from locked QR markers. " +
                $"leftLong={leftLong:F3}, rightLong={rightLong:F3}, topShort={topShort:F3}, bottomShort={bottomShort:F3}");
        }

        return _tableService.HasAllPockets();
    }

    private bool TryResolveCornerSetFromDetections(IReadOnlyDictionary<string, Pose> detections, out ResolvedCornerSet corners)
    {
        List<NamedPose> mandatory = GetMandatoryNamedPoses(detections);

        if (mandatory.Count >= 4 && TryResolveCornerSetFromFullMandatoryDetections(mandatory, out corners))
            return true;

        return TryResolveCornerSetFromPartialMandatoryDetections(mandatory, out corners);
    }

    private static bool TryGetNamedPoseByPayload(List<NamedPose> mandatory, string payload, out NamedPose result)
    {
        if (mandatory != null)
        {
            for (int i = 0; i < mandatory.Count; i++)
            {
                if (string.Equals(mandatory[i].Payload, payload, StringComparison.Ordinal))
                {
                    result = mandatory[i];
                    return true;
                }
            }
        }

        result = default;
        return false;
    }


    private bool TryResolveCornerSetFromFullMandatoryDetections(List<NamedPose> mandatory, out ResolvedCornerSet corners)
    {
        corners = default;

        if (!TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload01, out NamedPose topLeftPose) ||
            !TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload02, out NamedPose topRightPose) ||
            !TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload03, out NamedPose bottomLeftPose) ||
            !TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload04, out NamedPose bottomRightPose))
        {
            return false;
        }

        Vector3 topLeft = Flatten(topLeftPose.Pose.position);
        Vector3 topRight = Flatten(topRightPose.Pose.position);
        Vector3 bottomLeft = Flatten(bottomLeftPose.Pose.position);
        Vector3 bottomRight = Flatten(bottomRightPose.Pose.position);

        Vector3 center = Average(new List<Vector3> { topLeft, topRight, bottomLeft, bottomRight });

        Vector3 longAxisLeftToRight = Flatten(((topRight - topLeft) + (bottomRight - bottomLeft)) * 0.5f).normalized;
        Vector3 shortAxisBottomToTop = Flatten(((topLeft - bottomLeft) + (topRight - bottomRight)) * 0.5f).normalized;

        if (longAxisLeftToRight == Vector3.zero || shortAxisBottomToTop == Vector3.zero)
            return false;

        corners = new ResolvedCornerSet
        {
            Center = center,
            LongAxisLeftToRight = longAxisLeftToRight,
            ShortAxisBottomToTop = shortAxisBottomToTop,
            TopLeft = topLeft,
            TopRight = topRight,
            BottomLeft = bottomLeft,
            BottomRight = bottomRight
        };

        return true;
    }

    private bool TryResolveCornerSetFromPartialMandatoryDetections(List<NamedPose> mandatory, out ResolvedCornerSet corners)
    {
        corners = default;

        if (mandatory.Count < 2)
            return false;

        if (!TryGetBestDiagonalPair(mandatory, out NamedPose diagonalA, out NamedPose diagonalB))
            return false;

        Vector3 pointA = Flatten(diagonalA.Pose.position);
        Vector3 pointB = Flatten(diagonalB.Pose.position);

        Vector3 diagonal = pointB - pointA;
        float diagonalLength = diagonal.magnitude;
        if (diagonalLength <= 0.0001f)
            return false;

        Vector3 center = 0.5f * (pointA + pointB);
        Vector3 diagonalDirection = diagonal / diagonalLength;

        float expectedWidth = GetExpectedCornerCenterLeftRightM();
        float expectedHeight = GetExpectedCornerCenterTopBottomM();
        float diagonalAngleDeg = Mathf.Rad2Deg * Mathf.Atan2(expectedHeight, expectedWidth);

        Vector3 candidateLongAxisA = RotateOnPlane(diagonalDirection, -diagonalAngleDeg).normalized;
        Vector3 candidateShortAxisA = new(-candidateLongAxisA.z, 0f, candidateLongAxisA.x);

        Vector3 candidateLongAxisB = RotateOnPlane(diagonalDirection, diagonalAngleDeg).normalized;
        Vector3 candidateShortAxisB = new(-candidateLongAxisB.z, 0f, candidateLongAxisB.x);

        List<Vector3> observedPoints = mandatory.Select(p => Flatten(p.Pose.position)).ToList();

        float scoreA = ScoreCandidateRectangle(center, candidateLongAxisA, candidateShortAxisA, expectedWidth, expectedHeight, observedPoints);
        float scoreB = ScoreCandidateRectangle(center, candidateLongAxisB, candidateShortAxisB, expectedWidth, expectedHeight, observedPoints);

        Vector3 longAxis = scoreA <= scoreB ? candidateLongAxisA : candidateLongAxisB;
        Vector3 shortAxis = scoreA <= scoreB ? candidateShortAxisA : candidateShortAxisB;

        OrientAxes(ref longAxis, ref shortAxis, center);

        corners = BuildResolvedCornerSet(center, longAxis, shortAxis, expectedWidth, expectedHeight);
        return true;
    }

    private bool TryGetBestDiagonalPair(List<NamedPose> mandatory, out NamedPose diagonalA, out NamedPose diagonalB)
    {
        diagonalA = default;
        diagonalB = default;

        if (mandatory == null || mandatory.Count < 2)
            return false;

        float expectedDiagonal = GetExpectedCornerDiagonalM();
        float diagonalTolerance = GetConfiguredDiagonalToleranceM();

        NamedPose marker01 = default;
        NamedPose marker02 = default;
        NamedPose marker03 = default;
        NamedPose marker04 = default;

        bool has01 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload01, out marker01);
        bool has02 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload02, out marker02);
        bool has03 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload03, out marker03);
        bool has04 = TryGetNamedPoseByPayload(mandatory, DefaultMandatoryPayload04, out marker04);

        bool pair14Exists = has01 && has04;
        bool pair23Exists = has02 && has03;

        if (!pair14Exists && !pair23Exists)
            return false;

        float pair14Delta = float.MaxValue;
        float pair23Delta = float.MaxValue;

        if (pair14Exists)
        {
            pair14Delta = Mathf.Abs(
                DistanceXZ(Flatten(marker01.Pose.position), Flatten(marker04.Pose.position)) - expectedDiagonal);
        }

        if (pair23Exists)
        {
            pair23Delta = Mathf.Abs(
                DistanceXZ(Flatten(marker02.Pose.position), Flatten(marker03.Pose.position)) - expectedDiagonal);
        }

        bool use14 = pair14Exists && (!pair23Exists || pair14Delta <= pair23Delta);

        diagonalA = use14 ? marker01 : marker02;
        diagonalB = use14 ? marker04 : marker03;

        float bestDelta = use14 ? pair14Delta : pair23Delta;

        if (RequireDiagonalPairWhenUsingTwoMarkers && bestDelta > diagonalTolerance)
        {
            if (VerboseQrLogs)
            {
                Debug.LogWarning(
                    $"[TableQrAlignmentController] The detected mandatory QR pair does not match the expected diagonal spacing. " +
                    $"Diagonal delta={bestDelta:F3} m, tolerance={diagonalTolerance:F3} m.");
            }

            return false;
        }

        return true;
    }

    private void ValidateCornerMarkerDimensions(ResolvedCornerSet corners)
    {
        float topWidth = DistanceXZ(corners.TopLeft, corners.TopRight);
        float bottomWidth = DistanceXZ(corners.BottomLeft, corners.BottomRight);
        float leftHeight = DistanceXZ(corners.TopLeft, corners.BottomLeft);
        float rightHeight = DistanceXZ(corners.TopRight, corners.BottomRight);

        float averageWidth = 0.5f * (topWidth + bottomWidth);
        float averageHeight = 0.5f * (leftHeight + rightHeight);

        float expectedWidth = GetExpectedCornerCenterLeftRightM();
        float expectedHeight = GetExpectedCornerCenterTopBottomM();
        float tolerance = GetConfiguredCornerSpanToleranceM();

        float widthDelta = Mathf.Abs(averageWidth - expectedWidth);
        float heightDelta = Mathf.Abs(averageHeight - expectedHeight);

        if (VerboseQrLogs)
        {
            Debug.Log(
                $"[TableQrAlignmentController] Corner QR span validation: " +
                $"avgWidth={averageWidth:F3}m (expected {expectedWidth:F3}m), " +
                $"avgHeight={averageHeight:F3}m (expected {expectedHeight:F3}m), " +
                $"tolerance={tolerance:F3}m");
        }

        if (widthDelta > tolerance || heightDelta > tolerance)
        {
            Debug.LogWarning(
                $"[TableQrAlignmentController] Locked QR spacing differs from the configured setup. " +
                $"Width delta={widthDelta:F3}m, Height delta={heightDelta:F3}m.");
        }
    }

    private void AddOptionalRailSamples(
        ResolvedCornerSet corners,
        float middleOffsetM,
        List<float> topShortSamples,
        List<float> bottomShortSamples)
    {
        TryAddSingleOptionalRailSample(optionalReferencePayload05, corners, middleOffsetM, topShortSamples, bottomShortSamples);
        TryAddSingleOptionalRailSample(optionalReferencePayload06, corners, middleOffsetM, topShortSamples, bottomShortSamples);
    }

    private void TryAddSingleOptionalRailSample(
        string payload,
        ResolvedCornerSet corners,
        float middleOffsetM,
        List<float> topShortSamples,
        List<float> bottomShortSamples)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        if (!_lockedDetections.TryGetValue(payload, out Pose pose))
            return;

        Vector3 flat = Flatten(pose.position);
        float shortCoord = Vector3.Dot(flat - corners.Center, corners.ShortAxisBottomToTop);

        if (shortCoord >= 0f)
        {
            Vector3 seeded = flat - (corners.ShortAxisBottomToTop * middleOffsetM);
            topShortSamples.Add(Vector3.Dot(seeded - corners.Center, corners.ShortAxisBottomToTop));
        }
        else
        {
            Vector3 seeded = flat + (corners.ShortAxisBottomToTop * middleOffsetM);
            bottomShortSamples.Add(Vector3.Dot(seeded - corners.Center, corners.ShortAxisBottomToTop));
        }
    }

    private void OrientAxes(ref Vector3 longAxis, ref Vector3 shortAxis, Vector3 center)
    {
        bool usedOptionalOrientation = false;

        if (_lockedDetections.TryGetValue(optionalReferencePayload05, out Pose opt05))
        {
            float shortProjection05 = Vector3.Dot(Flatten(opt05.position) - center, shortAxis);
            if (shortProjection05 < 0f)
                shortAxis = -shortAxis;

            usedOptionalOrientation = true;
        }

        if (!usedOptionalOrientation && _lockedDetections.TryGetValue(optionalReferencePayload06, out Pose opt06))
        {
            float shortProjection06 = Vector3.Dot(Flatten(opt06.position) - center, shortAxis);
            if (shortProjection06 > 0f)
                shortAxis = -shortAxis;

            usedOptionalOrientation = true;
        }

        if (!usedOptionalOrientation && Vector3.Dot(shortAxis, Vector3.forward) < 0f)
            shortAxis = -shortAxis;

        if (Vector3.Dot(longAxis, Vector3.right) < 0f)
        {
            longAxis = -longAxis;
            shortAxis = -shortAxis;
        }
    }

    private float ScoreCandidateRectangle(
        Vector3 center,
        Vector3 longAxis,
        Vector3 shortAxis,
        float width,
        float height,
        List<Vector3> observedPoints)
    {
        ResolvedCornerSet candidate = BuildResolvedCornerSet(center, longAxis, shortAxis, width, height);
        Vector3[] corners =
        {
            candidate.TopLeft,
            candidate.TopRight,
            candidate.BottomLeft,
            candidate.BottomRight
        };

        float score = 0f;

        for (int i = 0; i < observedPoints.Count; i++)
        {
            float best = float.MaxValue;

            for (int j = 0; j < corners.Length; j++)
                best = Mathf.Min(best, DistanceXZ(observedPoints[i], corners[j]));

            score += best;
        }

        return score;
    }

    private ResolvedCornerSet BuildResolvedCornerSet(Vector3 center, Vector3 longAxis, Vector3 shortAxis, float width, float height)
    {
        Vector3 halfLong = longAxis * (width * 0.5f);
        Vector3 halfShort = shortAxis * (height * 0.5f);

        return new ResolvedCornerSet
        {
            Center = center,
            LongAxisLeftToRight = longAxis,
            ShortAxisBottomToTop = shortAxis,
            TopLeft = center - halfLong + halfShort,
            TopRight = center + halfLong + halfShort,
            BottomLeft = center - halfLong - halfShort,
            BottomRight = center + halfLong - halfShort
        };
    }

    private void UpdateOrCreateLiveMarkerVisual(string payload, Pose pose)
    {
        if (!_liveMarkerVisuals.TryGetValue(payload, out GameObject marker) || marker == null)
        {
            marker = CreateMarkerVisual(
                payload,
                liveVisual: true,
                LiveQrMarkersParent != null ? LiveQrMarkersParent : transform,
                LiveQrMarkerPrefab,
                LiveQrMarkerColor,
                LiveQrMarkerThicknessM);

            _liveMarkerVisuals[payload] = marker;
        }

        marker.transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    private void UpdateOrCreateLockedMarkerVisual(string payload, Pose pose)
    {
        if (!_lockedMarkerVisuals.TryGetValue(payload, out GameObject marker) || marker == null)
        {
            marker = CreateMarkerVisual(
                payload,
                liveVisual: false,
                LockedQrMarkersParent != null ? LockedQrMarkersParent : transform,
                LockedQrMarkerPrefab,
                LockedQrMarkerColor,
                LockedQrMarkerThicknessM);

            _lockedMarkerVisuals[payload] = marker;
        }

        marker.transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    private GameObject CreateMarkerVisual(
        string payload,
        bool liveVisual,
        Transform parent,
        GameObject prefab,
        Color color,
        float thickness)
    {
        GameObject marker;

        if (prefab != null)
        {
            marker = Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.transform.SetParent(parent, worldPositionStays: true);

            if (marker.TryGetComponent<Collider>(out Collider collider))
            {
                if (Application.isPlaying)
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }

            float paperSizeM = GetConfiguredQrPaperSizeM();
            marker.transform.localScale = new Vector3(paperSizeM, thickness, paperSizeM);

            if (marker.TryGetComponent<Renderer>(out Renderer renderer))
                renderer.material.color = color;
        }

        marker.name = $"{(liveVisual ? "Live" : "Locked")}QrMarker_{payload}";
        return marker;
    }

    private bool ComputePendingLockUpdate()
    {
        if (_qrAlignmentLockedForSession && FreezeQrScanningAfterSuccessfulLock)
            return false;

        if (_liveDetections.Count == 0)
            return false;

        if (_lockedDetections.Count == 0)
            return true;

        return HasAnyNewOrChangedLiveDetection();
    }

    private bool HasAnyNewOrChangedLiveDetection()
    {
        foreach (KeyValuePair<string, Pose> liveEntry in _liveDetections)
        {
            if (!_lockedDetections.TryGetValue(liveEntry.Key, out Pose lockedPose))
                return true;

            if (!AreSamePoseWithinThreshold(lockedPose, liveEntry.Value))
                return true;
        }

        return false;
    }

    private bool HaveDictionariesChanged(Dictionary<string, Pose> previous, Dictionary<string, Pose> current)
    {
        if (previous.Count != current.Count)
            return true;

        foreach (KeyValuePair<string, Pose> entry in current)
        {
            if (!previous.TryGetValue(entry.Key, out Pose previousPose))
                return true;

            if (!AreSamePoseWithinThreshold(previousPose, entry.Value))
                return true;
        }

        return false;
    }

    private bool AreSamePoseWithinThreshold(Pose a, Pose b)
    {
        float positionDelta = Vector3.Distance(a.position, b.position);
        if (positionDelta > PoseChangePositionThresholdM)
            return false;

        float rotationDelta = Quaternion.Angle(a.rotation, b.rotation);
        return rotationDelta <= PoseChangeRotationThresholdDeg;
    }


    private bool CanSatisfyMandatoryRequirement(IReadOnlyDictionary<string, Pose> detections) =>
        TryResolveCornerSetFromDetections(detections, out _);

    private List<NamedPose> GetMandatoryNamedPoses(IReadOnlyDictionary<string, Pose> detections)

    {
        string[] mandatoryPayloads = GetMandatoryPayloads();
        List<NamedPose> result = new();

        for (int i = 0; i < mandatoryPayloads.Length; i++)
        {
            if (detections.TryGetValue(mandatoryPayloads[i], out Pose pose))
                result.Add(new NamedPose(mandatoryPayloads[i], pose));
        }

        return result;
    }

    private bool IsAllowedPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        if (GetMandatoryPayloads().Any(p => string.Equals(p, payload, StringComparison.Ordinal)))
            return true;

        if (string.Equals(payload, optionalReferencePayload05, StringComparison.Ordinal))
            return true;

        if (string.Equals(payload, optionalReferencePayload06, StringComparison.Ordinal))
            return true;

        return false;
    }

    private IEnumerable<string> GetMissingMandatoryPayloadsFromLive()
    {
        string[] mandatory = GetMandatoryPayloads();

        for (int i = 0; i < mandatory.Length; i++)
        {
            if (!_liveDetections.ContainsKey(mandatory[i]))
                yield return mandatory[i];
        }
    }

    private IEnumerable<string> GetMissingOptionalPayloadsFromLocked()
    {
        if (!string.IsNullOrWhiteSpace(optionalReferencePayload05) && !_lockedDetections.ContainsKey(optionalReferencePayload05))
            yield return optionalReferencePayload05;

        if (!string.IsNullOrWhiteSpace(optionalReferencePayload06) && !_lockedDetections.ContainsKey(optionalReferencePayload06))
            yield return optionalReferencePayload06;
    }

    private int CountMandatoryMarkers(IEnumerable<string> payloads)
    {
        string[] mandatory = GetMandatoryPayloads();
        return payloads.Count(p => mandatory.Any(m => string.Equals(m, p, StringComparison.Ordinal)));
    }

    private int CountOptionalMarkers(IEnumerable<string> payloads)
    {
        return payloads.Count(p =>
            string.Equals(p, optionalReferencePayload05, StringComparison.Ordinal) ||
            string.Equals(p, optionalReferencePayload06, StringComparison.Ordinal));
    }

    private string[] GetMandatoryPayloads() =>
        mandatoryCornerPayloads
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private int GetRequiredMandatoryMarkerCount() =>
        Mathf.Clamp(MinimumRequiredMandatoryMarkers, 2, Mathf.Max(2, GetMandatoryPayloads().Length));

    private string GetRequiredMarkerText(bool lowercaseFirst = false)
    {
        string text = GetRequiredMandatoryMarkerCount() == 2 && RequireDiagonalPairWhenUsingTwoMarkers
            ? "2 diagonal mandatory QR code markers"
            : $"{GetRequiredMandatoryMarkerCount()} mandatory QR code markers";

        return lowercaseFirst && !string.IsNullOrEmpty(text)
            ? char.ToLowerInvariant(text[0]) + text[1..]
            : text;
    }

    private float GetConfiguredQrPaperSizeM() =>
        _tableService != null && _tableService.QR_CODE_WHOLE_PAPER_SIZE_M > 0f
            ? _tableService.QR_CODE_WHOLE_PAPER_SIZE_M
            : fallbackQrPaperSizeM;

    private float GetExpectedCornerCenterTopBottomM()
    {
        if (_tableService == null)
            return GetConfiguredQrPaperSizeM();

        if (_tableService.QR_USE_DIRECT_CENTER_SPAN_VALUES)
            return _tableService.QR_CORNER_CENTER_TOP_BOTTOM_M;

        return _tableService.QR_CORNER_PAPER_GAP_TOP_BOTTOM_M
             + GetConfiguredQrPaperSizeM()
             + _tableService.QR_CORNER_CENTER_EXTRA_TOP_BOTTOM_M;
    }

    private float GetExpectedCornerCenterLeftRightM()
    {
        if (_tableService == null)
            return GetConfiguredQrPaperSizeM();

        if (_tableService.QR_USE_DIRECT_CENTER_SPAN_VALUES)
            return _tableService.QR_CORNER_CENTER_LEFT_RIGHT_M;

        return _tableService.QR_CORNER_PAPER_GAP_LEFT_RIGHT_M
             + GetConfiguredQrPaperSizeM()
             + _tableService.QR_CORNER_CENTER_EXTRA_LEFT_RIGHT_M;
    }

    private float GetExpectedCornerDiagonalM()
    {
        float width = GetExpectedCornerCenterLeftRightM();
        float height = GetExpectedCornerCenterTopBottomM();
        return Mathf.Sqrt((width * width) + (height * height));
    }

    private float GetConfiguredCornerSpanToleranceM() =>
        _tableService != null
            ? Mathf.Max(0f, _tableService.QR_CORNER_SPAN_TOLERANCE_M)
            : 0.10f;

    private float GetConfiguredDiagonalToleranceM() =>
        Mathf.Max(GetConfiguredCornerSpanToleranceM() * 1.5f, 0.12f);

    private float GetConfiguredCornerMarkerToCornerPocketOffsetM() =>
        _tableService != null
            ? Mathf.Max(0f, _tableService.QR_CORNER_MARKER_TO_CORNER_POCKET_OFFSET_M)
            : 0.14f;

    private float GetConfiguredMiddleMarkerToRailOffsetM() =>
        _tableService != null
            ? Mathf.Max(0f, _tableService.QR_MIDDLE_MARKER_TO_RAIL_OFFSET_M)
            : 0.14f;

    private static string BuildPayloadSummary(IEnumerable<string> payloads)
    {
        if (payloads == null)
            return "-";

        List<string> ordered = payloads
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(ShortMarkerName)
            .ToList();

        return ordered.Count == 0 ? "-" : string.Join(", ", ordered);
    }

    private static string ShortMarkerName(string payload)
    {
        const string prefix = "ARPOOL_MARKER_";

        if (string.IsNullOrWhiteSpace(payload))
            return "-";

        return payload.StartsWith(prefix, StringComparison.Ordinal)
            ? payload[prefix.Length..]
            : payload;
    }

    private static Vector3 Flatten(Vector3 value) => new(value.x, 0f, value.z);

    private static Vector3 RotateOnPlane(Vector3 vector, float angleDeg)
    {
        float radians = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        return new Vector3(
            (vector.x * cos) - (vector.z * sin),
            0f,
            (vector.x * sin) + (vector.z * cos));
    }

    private static Vector3 Average(List<Vector3> values)
    {
        if (values == null || values.Count == 0)
            return Vector3.zero;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < values.Count; i++)
            sum += values[i];

        return sum / values.Count;
    }

    private static float Average(List<float> values) => values.Count > 0 ? values.Average() : 0f;

    private static float DistanceXZ(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    private readonly struct NamedPose
    {
        public readonly string Payload;
        public readonly Pose Pose;

        public NamedPose(string payload, Pose pose)
        {
            Payload = payload;
            Pose = pose;
        }
    }

}