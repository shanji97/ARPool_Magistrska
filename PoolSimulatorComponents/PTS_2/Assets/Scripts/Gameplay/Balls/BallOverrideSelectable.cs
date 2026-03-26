using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Attach to: BallView_new prefab root used by TableService in PoolSetup scene
// Branch: ISSUE-84
// Issue: #84 manual ball override UI entry-point visibility
public class BallOverrideSelectable : MonoBehaviour
{
    [Header("Selection Visuals")]
    [SerializeField] private GameObject selectionHighlightRoot; // e.g. ring / outline object
    [SerializeField] private Transform menuAnchor; // e.g. empty child above the ball
    [SerializeField] private TMP_Text debugLabel; // optional

    [Header("Entry Button Visuals")]
    [SerializeField] private GameObject entryButtonRoot; // UPDATED: e.g. SelectButtonCanvas child with the "E" button
    [SerializeField] private TMP_Text entryButtonLabel; // UPDATED: optional label inside the entry button

    public Ball RuntimeBall { get; private set; }

    public Transform MenuAnchor => menuAnchor != null ? menuAnchor : transform;

    // UPDATED: global registry of all currently active spawned ball views.
    private static readonly HashSet<BallOverrideSelectable> ActiveSelectables = new();

    // UPDATED: global toggle state for all "E" entry buttons.
    public static bool AreEntryButtonsGloballyVisible { get; private set; } = false;

    // UPDATED: environment status menu and ball menu can react to global visibility changes.
    public static event Action<bool> EntryButtonsVisibilityChanged;

    public static int RegisteredSelectableCount => ActiveSelectables.Count;

    private void Awake()
    {
        AutoResolveReferences();
    }

    private void OnValidate()
    {
        AutoResolveReferences();
    }

    private void OnEnable()
    {
        ActiveSelectables.Add(this); // UPDATED: register live spawned/selectable ball.

        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged += HandleSelectedBallChanged;

        RefreshVisualState();
    }

    private void OnDisable()
    {
        ActiveSelectables.Remove(this); // UPDATED: unregister when hidden/destroyed.

        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged -= HandleSelectedBallChanged;
    }

    public void Bind(Ball runtimeBall)
    {
        RuntimeBall = runtimeBall;
        RefreshVisualState();
    }

    /// <summary>
    /// Wire this to the "E" button click event.
    /// </summary>
    public void SelectThisBall()
    {
        if (RuntimeBall == null)
            return;

        // UPDATED: selection is only valid while the global entry buttons are visible.
        if (!AreEntryButtonsGloballyVisible)
            return;

        ManualBallOverrideService.Instance?.SelectBall(this);
    }

    public void RefreshVisualState()
    {
        bool isSelected =
            ManualBallOverrideService.Instance != null &&
            ManualBallOverrideService.Instance.SelectedSelectable == this;

        if (selectionHighlightRoot != null)
            selectionHighlightRoot.SetActive(isSelected);

        RefreshEntryButtonVisibility();

        if (entryButtonLabel != null)
            entryButtonLabel.text = "E";

        if (debugLabel != null && RuntimeBall != null)
        {
            string ignoredText = RuntimeBall.IsIgnoredByUser() ? " [IGNORED]" : string.Empty;
            string overridesText = RuntimeBall.UserOverrides == UserOverrides.None
                ? string.Empty
                : $" [{RuntimeBall.UserOverrides}]";

            debugLabel.text = $"{RuntimeBall.BallType} #{RuntimeBall.BallId}{ignoredText}{overridesText}";
        }

        BallVisualView visualView = GetComponent<BallVisualView>();
        if (visualView != null && RuntimeBall != null)
            visualView.Bind(RuntimeBall);
    }

    public void RefreshEntryButtonVisibility()
    {
        if (entryButtonRoot == null)
            return;

        bool shouldShow =
            RuntimeBall != null &&
            !RuntimeBall.IsIgnoredByUser() &&
            AreEntryButtonsGloballyVisible;

        if (entryButtonRoot.activeSelf != shouldShow)
            entryButtonRoot.SetActive(shouldShow);
    }

    public static void SetGlobalEntryButtonsVisible(bool isVisible)
    {
        if (AreEntryButtonsGloballyVisible == isVisible)
        {
            RefreshAllEntryButtons();
            return;
        }

        AreEntryButtonsGloballyVisible = isVisible; // UPDATED: shared global gate for all live ball views.
        RefreshAllEntryButtons();
        EntryButtonsVisibilityChanged?.Invoke(isVisible);
    }

    public static void RefreshAllEntryButtons()
    {
        foreach (BallOverrideSelectable selectable in ActiveSelectables)
            selectable?.RefreshEntryButtonVisibility();
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable _)
    {
        RefreshVisualState();
    }

    private void AutoResolveReferences()
    {
        // UPDATED: keep prefab setup resilient to missed Inspector assignments.
        if (entryButtonRoot == null)
        {
            Transform entryRoot = transform.Find("SelectButtonCanvas");
            if (entryRoot != null)
                entryButtonRoot = entryRoot.gameObject;
        }

        if (menuAnchor == null)
        {
            Transform anchor = transform.Find("MenuAnchor");
            if (anchor != null)
                menuAnchor = anchor;
        }

        if (entryButtonLabel == null && entryButtonRoot != null)
            entryButtonLabel = entryButtonRoot.GetComponentInChildren<TMP_Text>(includeInactive: true);
    }
}