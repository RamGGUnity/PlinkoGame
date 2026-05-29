using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

namespace Plinko
{
    public class UiManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button dropButton;
        [SerializeField] private TMP_Text targetText;
        [SerializeField] private TMP_Text resultsText;
        [SerializeField] private TMP_InputField targetInputField;
        [SerializeField] private Button setTargetsButton;
        [SerializeField] private Toggle controlledToggle;
        [SerializeField] private TMP_Dropdown ballCountDropdown;

        [Header("Betting UI")]
        [SerializeField] private BettingManager bettingManager;
        [SerializeField] private TMP_Text balanceText;
        [SerializeField] private TMP_Text totalWinText;
        [SerializeField] private GameObject insufficientBalancePanel;
        private float sessionTotalWin = 0f;

        private GameManager gameManager;
        private readonly List<string> resultsLines = new List<string>(5);

        private void Start()
        {
            gameManager = GameManager.Instance;

            dropButton.onClick.AddListener(OnDropClicked);
            setTargetsButton.onClick.AddListener(OnSetTargetsClicked);
            controlledToggle.onValueChanged.AddListener(OnControlledToggleChanged);

            // Setup dropdown options
            if (ballCountDropdown != null)
            {
                ballCountDropdown.ClearOptions();
                ballCountDropdown.AddOptions(new System.Collections.Generic.List<string> { "1 Ball", "2 Balls", "3 Balls", "5 Balls", "10 Balls" });
                ballCountDropdown.value = 0; // Default to 1 ball
            }

            // Set initial toggle value from GameManager
            if (gameManager != null && controlledToggle != null)
            {
                controlledToggle.isOn = gameManager.UseControlledOutcome;
            }
            if (bettingManager != null)
            {
                bettingManager.OnBalanceChanged += UpdateBalanceDisplay;
                bettingManager.OnWinRecorded += OnWinRecorded;
                UpdateBalanceDisplay(bettingManager.CurrentBalance);
            }

            BindEvents();
            UpdateUI();
        }

        private void OnDestroy()
        {
            UnbindEvents();
            if (bettingManager != null)
            {
                bettingManager.OnBalanceChanged -= UpdateBalanceDisplay;
                bettingManager.OnWinRecorded -= OnWinRecorded;
            }
        }

        private void BindEvents()
        {
            if (gameManager != null)
            {
                gameManager.OnBallResult += HandleBallResult;
                gameManager.OnBallStateChanged += HandleBallStateChanged;
                gameManager.OnTargetsCompleted += HandleTargetsCompleted;
            }
        }

        private void UnbindEvents()
        {
            if (gameManager != null)
            {
                gameManager.OnBallResult -= HandleBallResult;
                gameManager.OnBallStateChanged -= HandleBallStateChanged;
                gameManager.OnTargetsCompleted -= HandleTargetsCompleted;
            }
        }

        private void OnDropClicked()
        {
            if (gameManager != null)
            {
                int ballCount = GetSelectedBallCount();
                gameManager.DropBalls(ballCount);
            }
        }

        private int GetSelectedBallCount()
        {
            if (ballCountDropdown == null) return 1;

            switch (ballCountDropdown.value)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 3;
                case 3: return 5;
                case 4: return 10;
                default: return 1;
            }
        }

        private void OnSetTargetsClicked()
        {
            if (targetInputField == null || gameManager == null) return;

            string input = targetInputField.text;
            string[] parts = input.Split(',');

            int[] targets = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i].Trim(), out int slot))
                {
                    targets[i] = slot;
                }
                else
                {
                    targets[i] = 0;
                }
            }

            gameManager.SetTargetSlots(targets);
            resultsLines.Clear();
            UpdateUI();
        }

        private void OnControlledToggleChanged(bool isOn)
        {
            if (gameManager != null)
            {
                gameManager.SetControlledOutcome(isOn);
                UpdateUI();
            }
        }

        private void HandleBallResult(int slotId, float multiplier)
        {
            resultsLines.Insert(0, $"Slot {slotId} (x{multiplier})");
            if (resultsLines.Count > 5)
                resultsLines.RemoveAt(5);

            UpdateUI();
        }

        private void HandleBallStateChanged(bool inPlay)
        {
            bool canDrop = !inPlay && (gameManager == null || !gameManager.IsMultiDropInProgress);
            if (dropButton != null)
            {
                dropButton.interactable = canDrop;
            }
            UpdateUI();
        }

        private void HandleTargetsCompleted()
        {
            if (targetText != null)
            {
                targetText.text = "All targets complete!";
            }
        }

        private void UpdateUI()
        {
            if (gameManager == null) return;

            // Update target text
            if (targetText != null)
            {
                int target = gameManager.CurrentTargetSlot;
                targetText.text = target >= 0 ? $"Next Target: Slot {target}" : "Mode: Random";
            }

            // Update results text
            if (resultsText is not null)
                resultsText.text = string.Join("\n", resultsLines);

            // Update drop button state
            bool canDrop = !gameManager.IsBallInPlay && !gameManager.IsMultiDropInProgress;
            if (dropButton != null)
            {
                dropButton.interactable = canDrop;
            }
        }

        private void UpdateBalanceDisplay(float balance)
        {
            if (balanceText != null)
                balanceText.text = $"${balance:F2}";
        }

        private void OnWinRecorded(float winAmount)
        {
            sessionTotalWin += winAmount;
            if (totalWinText != null)
                totalWinText.text = $"+${sessionTotalWin:F2}";
        }

        public void ShowInsufficientBalanceMessage()
        {
            if (insufficientBalancePanel != null)
            {
                insufficientBalancePanel.SetActive(true);
                Invoke(nameof(HideInsufficientBalanceMessage), 2f);
            }
        }

        private void HideInsufficientBalanceMessage()
        {
            if (insufficientBalancePanel != null)
                insufficientBalancePanel.SetActive(false);
        }

        public void ResetSession()
        {
            sessionTotalWin = 0f;
            if (totalWinText != null)
                totalWinText.text = "+$0.00";
        }
    }
}
