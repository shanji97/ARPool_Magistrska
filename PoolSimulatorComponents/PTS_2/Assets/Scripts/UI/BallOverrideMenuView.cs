using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BallOverrideMenuView : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot; // child object that is shown/hidden, keep this script object active
    [SerializeField] private TMP_Text selectedBallText;

    [Header("Controls")]
    [SerializeField] private TMP_Dropdown typeDropdown;
    [SerializeField] private TMP_Dropdown ballIdDropdown;

    [Header("Placement")]
    [SerializeField] private bool followSelectedBall = true;
    [SerializeField] private Vector3 worldOffset = new(0f, 0.15f, 0f);
    [SerializeField] private bool faceMainCamera = true;

    private void Awake()
    {
        BuildTypeOptions();

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged += HandleSelectedBallChanged;
    }

    private void OnDisable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged -= HandleSelectedBallChanged;
    }

    private void LateUpdate()
    {
        if (panelRoot == null || !panelRoot.activeSelf)
            return;

        BallOverrideSelectable selected = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedSelectable
            : null;

        if (selected == null)
            return;

        if (followSelectedBall)
        {
            Transform anchor = selected.MenuAnchor != null ? selected.MenuAnchor : selected.transform;
            transform.position = anchor.position + worldOffset;
        }

        if (faceMainCamera && Camera.main != null)
        {
            Vector3 toCamera = transform.position - Camera.main.transform.position;
            if (toCamera.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }
    }

    public void ApplyOverrideFromCurrentSelection()
    {
        if (ManualBallOverrideService.Instance == null)
            return;

        BallType selectedType = GetBallTypeForDropdownIndex(typeDropdown.value);

        int selectedBallId = 0;
        if (ballIdDropdown.options.Count > 0)
            int.TryParse(ballIdDropdown.options[ballIdDropdown.value].text, out selectedBallId);

        ManualBallOverrideService.Instance.ApplySelectedTypeOverride(selectedType);
        ManualBallOverrideService.Instance.ApplySelectedBallIdOverride(selectedBallId);

        SyncFromSelectedBall();
    }

    public void ResetSelectedBallOverride()
    {
        ManualBallOverrideService.Instance?.ResetSelectedOverrides();
        SyncFromSelectedBall();
    }

    public void CloseMenu()
    {
        ManualBallOverrideService.Instance?.ClearSelection();
    }

    public void OnTypeDropdownValueChanged(int _)
    {
        BallType selectedType = GetBallTypeForDropdownIndex(typeDropdown.value);
        BuildBallIdOptions(selectedType, GetDefaultBallIdForType(selectedType));
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable selected)
    {
        bool hasSelection = selected != null && selected.RuntimeBall != null;

        if (panelRoot != null)
            panelRoot.SetActive(hasSelection);

        if (!hasSelection)
            return;

        SyncFromSelectedBall();
    }

    private void SyncFromSelectedBall()
    {
        Ball ball = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

        if (ball == null)
            return;

        typeDropdown.SetValueWithoutNotify(GetDropdownIndexForBallType(ball.BallType));
        BuildBallIdOptions(ball.BallType, ball.BallId);

        if (selectedBallText != null)
        {
            selectedBallText.text =
                $"Editing: {ball.BallType} #{ball.BallId}\n" +
                $"Type locked: {ball.IsTypeUserOverriden()}\n" +
                $"Number locked: {ball.IsBallIdUserOverriden()}";
        }
    }

    private void BuildTypeOptions()
    {
        if (typeDropdown == null)
            return;

        typeDropdown.ClearOptions();

        List<TMP_Dropdown.OptionData> options = new()
        {
            new TMP_Dropdown.OptionData("Cue"),
            new TMP_Dropdown.OptionData("Solid"),
            new TMP_Dropdown.OptionData("Stripe"),
            new TMP_Dropdown.OptionData("Eight")
        };

        typeDropdown.AddOptions(options);
    }

    private void BuildBallIdOptions(BallType ballType, int selectedBallId)
    {
        if (ballIdDropdown == null)
            return;

        ballIdDropdown.ClearOptions();

        List<TMP_Dropdown.OptionData> options = new();

        switch (ballType)
        {
            case BallType.Cue:
                options.Add(new TMP_Dropdown.OptionData("0"));
                break;

            case BallType.Solid:
                for (int i = 1; i <= 7; i++)
                    options.Add(new TMP_Dropdown.OptionData(i.ToString()));
                break;

            case BallType.Eight:
                options.Add(new TMP_Dropdown.OptionData("8"));
                break;

            case BallType.Stripe:
                for (int i = 9; i <= 15; i++)
                    options.Add(new TMP_Dropdown.OptionData(i.ToString()));
                break;
        }

        ballIdDropdown.AddOptions(options);

        int selectedIndex = 0;
        for (int i = 0; i < ballIdDropdown.options.Count; i++)
        {
            if (ballIdDropdown.options[i].text == selectedBallId.ToString())
            {
                selectedIndex = i;
                break;
            }
        }

        ballIdDropdown.SetValueWithoutNotify(selectedIndex);
    }

    private static int GetDropdownIndexForBallType(BallType ballType)
    {
        return ballType switch
        {
            BallType.Cue => 0,
            BallType.Solid => 1,
            BallType.Stripe => 2,
            BallType.Eight => 3,
            _ => 0
        };
    }

    private static BallType GetBallTypeForDropdownIndex(int dropdownIndex)
    {
        return dropdownIndex switch
        {
            0 => BallType.Cue,
            1 => BallType.Solid,
            2 => BallType.Stripe,
            3 => BallType.Eight,
            _ => BallType.Cue
        };
    }

    private static int GetDefaultBallIdForType(BallType ballType)
    {
        return ballType switch
        {
            BallType.Cue => 0,
            BallType.Solid => 1,
            BallType.Stripe => 9,
            BallType.Eight => 8,
            _ => 0
        };
    }
}