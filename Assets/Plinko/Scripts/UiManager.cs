using UnityEngine;
using UnityEngine.UI;
using System.Text;
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
        private readonly StringBuilder resultsBuilder = new StringBuilder(512);
        private readonly StringBuilder tempBuilder = new StringBuilder(32);

        private void Start()
        {
            Debug.Log("[PLINKO] UiManager Start");
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
            Debug.Log("[PLINKO] UiManager initialization complete");
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
            Debug.Log("[PLINKO] UI: Drop button clicked");
            if (gameManager != null)
            {
                int ballCount = GetSelectedBallCount();
                Debug.Log($"[PLINKO] UI: Dropping {ballCount} ball(s)");
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
            Debug.Log("[PLINKO] UI: Set targets button clicked");
            if (targetInputField == null || gameManager == null) return;

            string input = targetInputField.text;
            Debug.Log($"[PLINKO] UI: Parsing targets from input: '{input}'");
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

            Debug.Log($"[PLINKO] UI: Parsed {targets.Length} targets: [{string.Join(", ", targets)}]");
            gameManager.SetTargetSlots(targets);
            resultsBuilder.Clear();
            UpdateUI();
        }

        private void OnControlledToggleChanged(bool isOn)
        {
            Debug.Log($"[PLINKO] UI: Controlled toggle changed to: {isOn}");
            if (gameManager != null)
            {
                gameManager.SetControlledOutcome(isOn);
                UpdateUI();
            }
        }

        private void HandleBallResult(int slotId, float multiplier)
        {
            Debug.Log($"[PLINKO] UI: Received ball result - Slot {slotId}, Multiplier x{multiplier}");
            // Build result line without allocation
            tempBuilder.Clear();
            tempBuilder.Append("Slot ").Append(slotId).Append(" (x").Append(multiplier).Append(")\n");

            // Insert at beginning
            resultsBuilder.Insert(0, tempBuilder.ToString());

            // Limit results display
            if (resultsBuilder.Length > 500)
            {
                resultsBuilder.Length = 500;
            }

            UpdateUI();
        }

        private void HandleBallStateChanged(bool inPlay)
        {
            Debug.Log($"[PLINKO] UI: Ball state changed - In play: {inPlay}");
            bool canDrop = !inPlay && (gameManager == null || !gameManager.IsMultiDropInProgress);
            if (dropButton != null)
            {
                dropButton.interactable = canDrop;
            }
            UpdateUI();
        }

        private void HandleTargetsCompleted()
        {
            Debug.Log("[PLINKO] UI: All targets completed");
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
            if (resultsText != null)
            {
                resultsText.text = resultsBuilder.ToString();
            }

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
