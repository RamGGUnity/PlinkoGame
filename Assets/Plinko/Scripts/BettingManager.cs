// BettingManager.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace Plinko
{
    public class BettingManager : MonoBehaviour
    {
        public static BettingManager Instance { get; private set; }

        [Header("Balance Settings")]
        [SerializeField] private float startingBalance = 100f;
        [SerializeField] private float minBet = 0.04f;
        [SerializeField] private float maxBet = 100f;

        [Header("UI References")]
        [SerializeField] private TMP_Text balanceText;
        [SerializeField] private TMP_Text betAmountText;
        [SerializeField] private TMP_Text winAmountText;
        [SerializeField] private Button increaseBetButton;
        [SerializeField] private Button decreaseBetButton;
    
        [SerializeField] private Button[] presetButtons; // 0.5, 1, 2, 7
        [SerializeField] private TMP_Text[] presetButtonTexts;
        [SerializeField] private TMP_InputField betInputField;

        [Header("Quick Amounts")]
        [SerializeField] private float[] presetAmounts = { 0.5f, 1f, 2f, 7f };

        private float currentBalance;
        private float currentBetAmount = 1f;
        private float pendingWinAmount;
        private float currentSessionTotalWin;

        public event Action<float> OnBalanceChanged;
        public event Action<float> OnBetAmountChanged;
        public event Action<float> OnWinRecorded;

        public float CurrentBalance => currentBalance;
        public float CurrentBetAmount => currentBetAmount;
        public float MinBet => minBet;
        public float MaxBet => maxBet;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            currentBalance = startingBalance;
        }

        private void Start()
        {
            SetupUI();
            UpdateBalanceDisplay();
            UpdateBetDisplay();
        }

        private void SetupUI()
        {
            // Setup buttons
            if (increaseBetButton != null)
                increaseBetButton.onClick.AddListener(IncreaseBet);

            if (decreaseBetButton != null)
                decreaseBetButton.onClick.AddListener(DecreaseBet);


            // Setup preset buttons
            for (int i = 0; i < presetButtons.Length && i < presetAmounts.Length; i++)
            {
                int index = i;
                if (presetButtons[i] != null)
                {
                    presetButtons[i].onClick.AddListener(() => SetBetAmount(presetAmounts[index]));
                    if (presetButtonTexts[i] != null)
                        presetButtonTexts[i].text = presetAmounts[i].ToString("0.##");
                }
            }


            // Setup input field
            if (betInputField != null)
            {
                betInputField.onEndEdit.AddListener(OnBetInputChanged);
                betInputField.text = currentBetAmount.ToString("0.##");
            }
        }

        private void OnSliderValueChanged(float value)
        {
            SetBetAmount(value);
        }

        private void OnBetInputChanged(string value)
        {
            if (float.TryParse(value, out float newBet))
            {
                SetBetAmount(newBet);
            }
            else
            {
                betInputField.text = currentBetAmount.ToString("0.##");
            }
        }

        public void SetBetAmount(float amount)
        {
            float newAmount = Mathf.Clamp(amount, minBet, maxBet);
            newAmount = Mathf.Round(newAmount * 100f) / 100f;
            
            if (Mathf.Abs(currentBetAmount - newAmount) > 0.01f)
            {
                currentBetAmount = newAmount;
                UpdateBetDisplay();
                OnBetAmountChanged?.Invoke(currentBetAmount);
            }
        }

        public void IncreaseBet()
        {
            float newBet = currentBetAmount + 0.5f;
            SetBetAmount(newBet);
        }

        public void DecreaseBet()
        {
            float newBet = currentBetAmount - 0.5f;
            SetBetAmount(newBet);
        }

        public void SetMaxBet()
        {
            SetBetAmount(Mathf.Min(maxBet, currentBalance));
        }

        public void SetMinBet()
        {
            SetBetAmount(minBet);
        }

        private void UpdateBetDisplay()
        {
            if (betAmountText != null)
                betAmountText.text = $"${currentBetAmount:F2}";
            
            if (betInputField != null && betInputField.text != currentBetAmount.ToString("0.##"))
                betInputField.text = currentBetAmount.ToString("0.##");
            
        
        }

        private void UpdateBalanceDisplay()
        {
            if (balanceText != null)
                balanceText.text = $"${currentBalance:F2}";
            
            OnBalanceChanged?.Invoke(currentBalance);
        }

        public bool CanDeductBet(float amount)
        {
            return currentBalance >= amount;
        }

        public bool DeductBet(float amount)
        {
            if (currentBalance >= amount)
            {
                currentBalance -= amount;
                UpdateBalanceDisplay();
                return true;
            }
            return false;
        }

        public void AddWin(float winAmount)
        {
            currentBalance += winAmount;
            pendingWinAmount = winAmount;
            currentSessionTotalWin += winAmount;
            
            UpdateBalanceDisplay();
            
            // Show win amount popup
            if (winAmountText != null)
            {
                winAmountText.text = $"+${winAmount:F2}";
                winAmountText.gameObject.SetActive(true);
                // Hide after 2 seconds
                Invoke(nameof(HideWinAmount), 2f);
            }
            
            OnWinRecorded?.Invoke(winAmount);
            Debug.Log($"[BETTING] Won: ${winAmount:F2}, New Balance: ${currentBalance:F2}");
        }

        private void HideWinAmount()
        {
            if (winAmountText != null)
                winAmountText.gameObject.SetActive(false);
        }

        public void ResetBalance()
        {
            currentBalance = startingBalance;
            currentSessionTotalWin = 0;
            UpdateBalanceDisplay();
            Debug.Log($"[BETTING] Balance reset to ${currentBalance:F2}");
        }

        public void AddBalance(float amount)
        {
            currentBalance += amount;
            UpdateBalanceDisplay();
        }
    }
}