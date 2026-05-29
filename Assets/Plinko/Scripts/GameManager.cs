
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace Plinko
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [FormerlySerializedAs("board")]
        [Header("References")]
        [SerializeField] private BoardManager boardManager;
        [FormerlySerializedAs("plinkoUI")] [SerializeField] private UiManager uiManager;
        [SerializeField] private GameObject ballPrefab;

        [Header("Target Slots")]
        [SerializeField] private int[] targetSlots = { 0, 5, 8, 15, 3 };
        [SerializeField] private bool useControlledOutcome = true;

        [Header("Drop Settings")]
        [SerializeField] private float SpawnDelay = 0.5f;
        [SerializeField] private float dropInterval = 1.0f;

        [Header("Object Pool")]
        [SerializeField] private int poolSize = 10;
        [SerializeField] private Transform poolParent;

        [Header("Betting")]
        public BettingManager bettingManager;

        private int currentTargetIndex;
        private bool isBallInPlay;
        private bool isMultiDropInProgress;
        private int ballsInPlayCount;
        private float currentDropBetAmount;
        private List<BallResult> results;
        private PlinkoBall currentBall;
        private List<PlinkoBall> activeBalls;
        private Queue<PlinkoBall> ballPool;

        // Cached for GC optimization
        private WaitForSeconds spawnWait;
        private WaitForSeconds intervalWait;

        public event Action<int, float> OnBallResult;
        public event Action<bool> OnBallStateChanged;
        public event Action OnTargetsCompleted;

        public bool IsBallInPlay => isBallInPlay;
        public bool IsMultiDropInProgress => isMultiDropInProgress;
        public int CurrentTargetSlot => useControlledOutcome && currentTargetIndex < targetSlots.Length
            ? targetSlots[currentTargetIndex] : -1;
        public int RemainingTargets => Mathf.Max(0, targetSlots.Length - currentTargetIndex);
        public List<BallResult> Results => results;
        public bool UseControlledOutcome => useControlledOutcome;

        public void SetControlledOutcome(bool controlled)
        {
            useControlledOutcome = controlled;
        }

        [Serializable]
        public struct BallResult
        {
            public int targetSlot;
            public int actualSlot;
            public float multiplier;
            public bool wasControlled;
            public bool hitTarget;
            public float winAmount;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate GameManager detected, destroying");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Pre-allocate collections
            results = new List<BallResult>(targetSlots.Length);
            ballPool = new Queue<PlinkoBall>(poolSize);
            activeBalls = new List<PlinkoBall>(10);
            spawnWait = new WaitForSeconds(SpawnDelay);
            intervalWait = new WaitForSeconds(dropInterval);
        }

        private void Start()
        {
            bettingManager ??= BettingManager.Instance;
            InitializePool();
            boardManager.GenerateBoard();
            Slot.OnBallLanded += HandleBallLanded;
        }

        private void OnDestroy()
        {
            Slot.OnBallLanded -= HandleBallLanded;
        }

        private void InitializePool()
        {
            for (int i = 0; i < poolSize; i++)
            {
                PlinkoBall ball = CreateBallForPool();
                ballPool.Enqueue(ball);
            }
        }

        private PlinkoBall CreateBallForPool()
        {
            GameObject ballObj = Instantiate(ballPrefab, poolParent);
            ballObj.TryGetComponent(out PlinkoBall ball);
            ballObj.SetActive(false);
            return ball;
        }

        private PlinkoBall GetBallFromPool()
        {
            PlinkoBall ball;

            if (ballPool.Count > 0)
            {
                ball = ballPool.Dequeue();
            }
            else
            {
                ball = CreateBallForPool();
            }

            ball.gameObject.SetActive(true);
            return ball;
        }

        public void ReturnBallToPool(PlinkoBall ball)
        {
            if (ball == null) return;

            ball.ResetBall();
            ball.transform.SetParent(poolParent);
            ball.gameObject.SetActive(false);
            ballPool.Enqueue(ball);
        }

        public void SetTargetSlots(int[] slots)
        {
            targetSlots = slots;
            currentTargetIndex = 0;
            results.Clear();
        }

        public void DropBall()
        {
            if (isBallInPlay)
            {
                Debug.LogWarning("Ball already in play, ignoring drop request");
                return;
            }

            if (useControlledOutcome && currentTargetIndex >= targetSlots.Length)
            {
                Debug.Log("All targets completed");
                OnTargetsCompleted?.Invoke();
                return;
            }

            if (bettingManager != null && !TryDeductBets(1))
            {
                uiManager?.ShowInsufficientBalanceMessage();
                return;
            }

            StartCoroutine(DropBallCoroutine());
        }

        public void DropBalls(int count)
        {
            if (isMultiDropInProgress)
            {
                Debug.LogWarning("Multi-drop already in progress, ignoring request");
                return;
            }

            if (isBallInPlay)
            {
                Debug.LogWarning("Ball already in play, ignoring drop request");
                return;
            }

            if (bettingManager != null && !TryDeductBets(count))
            {
                uiManager?.ShowInsufficientBalanceMessage();
                return;
            }

            if (useControlledOutcome && currentTargetIndex >= targetSlots.Length)
            {
                Debug.Log("All targets completed");
                OnTargetsCompleted?.Invoke();
                return;
            }

            StartCoroutine(DropMultipleBallsCoroutine(count));
        }

        private IEnumerator DropBallCoroutine()
        {
            isBallInPlay = true;
            OnBallStateChanged?.Invoke(true);

            yield return spawnWait;

            SpawnBall();
        }

        public void Drop10Balls()
        {
            DropBalls(10);
        }

        private IEnumerator DropMultipleBallsCoroutine(int count)
        {
            isMultiDropInProgress = true;
            isBallInPlay = true;
            OnBallStateChanged?.Invoke(true);

            for (int i = 0; i < count; i++)
            {
                yield return spawnWait;

                SpawnBallForMultiDrop();
                ballsInPlayCount++;

                if (i < count - 1)
                {
                    yield return intervalWait;
                }
            }

            while (ballsInPlayCount > 0)
            {
                yield return null;
            }

            isMultiDropInProgress = false;
            isBallInPlay = false;
            OnBallStateChanged?.Invoke(false);
        }

        private void SpawnBallForMultiDrop()
        {
            Vector3 dropPos = boardManager.GetDropPosition();
            dropPos.x = 0f;

            PlinkoBall ball = GetBallFromPool();
            ball.transform.SetParent(null);
            ball.transform.position = dropPos;
            ball.transform.rotation = Quaternion.identity;

            activeBalls.Add(ball);

            float slotY = boardManager.GetSlotYPosition();
            float pegSpace = boardManager.PegSpacing;
            float rowSpace = boardManager.RowSpacing;

            if (useControlledOutcome && currentTargetIndex < targetSlots.Length)
            {
                int targetSlot = targetSlots[currentTargetIndex];
                int maxSlot = boardManager.SlotCount - 1;

                if (targetSlot < 0 || targetSlot > maxSlot)
                {
                    Debug.LogWarning($"Target slot {targetSlot} out of range, clamping to valid range [0, {maxSlot}]");
                    targetSlot = targetSlot < 0 ? 0 : maxSlot;
                }

                float slotX = boardManager.GetSlotXPosition(targetSlot);
                Slot slot = boardManager.Slots[targetSlot];
                ball.Initialize(targetSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
                currentTargetIndex++;
            }
            else
            {
                int randomSlot = UnityEngine.Random.Range(0, boardManager.SlotCount);
                float slotX = boardManager.GetSlotXPosition(randomSlot);
                Slot slot = boardManager.Slots[randomSlot];
                ball.Initialize(randomSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
            }
        }

        private void SpawnBall()
        {
            Vector3 dropPos = boardManager.GetDropPosition();
            dropPos.x = 0f;

            currentBall = GetBallFromPool();
            currentBall.transform.SetParent(null);
            currentBall.transform.position = dropPos;
            currentBall.transform.rotation = Quaternion.identity;

            float slotY = boardManager.GetSlotYPosition();
            float pegSpace = boardManager.PegSpacing;
            float rowSpace = boardManager.RowSpacing;

            if (useControlledOutcome && currentTargetIndex < targetSlots.Length)
            {
                int targetSlot = targetSlots[currentTargetIndex];
                int maxSlot = boardManager.SlotCount - 1;

                if (targetSlot < 0 || targetSlot > maxSlot)
                {
                    Debug.LogWarning($"Target slot {targetSlot} out of range, clamping to valid range [0, {maxSlot}]");
                    targetSlot = targetSlot < 0 ? 0 : maxSlot;
                }

                float slotX = boardManager.GetSlotXPosition(targetSlot);
                Slot slot = boardManager.Slots[targetSlot];
                currentBall.Initialize(targetSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
            }
            else
            {
                int randomSlot = UnityEngine.Random.Range(0, boardManager.SlotCount);
                float slotX = boardManager.GetSlotXPosition(randomSlot);
                Slot slot = boardManager.Slots[randomSlot];
                currentBall.Initialize(randomSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
            }
        }

        private void HandleBallLanded(int slotId, float multiplier, PlinkoBall landedBall)
        {
            float winAmount = 0f;
            if (bettingManager != null)
            {
                winAmount = currentDropBetAmount * multiplier;
                bettingManager.AddWin(winAmount);
            }

            Debug.Log($"Ball landed in slot {slotId} (x{multiplier})");

            var result = new BallResult
            {
                targetSlot = -1,
                actualSlot = slotId,
                multiplier = multiplier,
                wasControlled = useControlledOutcome,
                hitTarget = false,
                winAmount = winAmount
            };
            results.Add(result);

            OnBallResult?.Invoke(slotId, multiplier);

            if (isMultiDropInProgress)
            {
                ballsInPlayCount--;

                if (landedBall != null && activeBalls.Contains(landedBall))
                {
                    activeBalls.Remove(landedBall);
                    ReturnBallToPool(landedBall);
                }
                else if (landedBall != null)
                {
                    Debug.LogWarning($"Ball {landedBall.name} landed but was not in activeBalls list - returning anyway");
                    ReturnBallToPool(landedBall);
                }
                else
                {
                    Debug.LogWarning("landedBall was null! Using fallback removal");
                    if (activeBalls.Count > 0)
                    {
                        PlinkoBall fallbackBall = activeBalls[0];
                        activeBalls.RemoveAt(0);
                        ReturnBallToPool(fallbackBall);
                    }
                }
            }
            else
            {
                if (useControlledOutcome)
                {
                    currentTargetIndex++;
                }

                ReturnBallToPool(landedBall != null ? landedBall : currentBall);
                currentBall = null;

                isBallInPlay = false;
                OnBallStateChanged?.Invoke(false);

                if (useControlledOutcome && currentTargetIndex >= targetSlots.Length)
                {
                    Debug.Log("All targets completed!");
                    OnTargetsCompleted?.Invoke();
                }
            }
        }

        public void ResetGame()
        {
            StopAllCoroutines();
            currentTargetIndex = 0;
            results.Clear();
            isBallInPlay = false;
            isMultiDropInProgress = false;
            ballsInPlayCount = 0;

            if (currentBall != null)
            {
                ReturnBallToPool(currentBall);
                currentBall = null;
            }

            foreach (var ball in activeBalls)
            {
                ReturnBallToPool(ball);
            }
            activeBalls.Clear();
        }

        private bool TryDeductBets(int ballCount)
        {
            float betPerBall = bettingManager.CurrentBetAmount;
            float totalBet = betPerBall * ballCount;

            if (bettingManager.CanDeductBet(totalBet))
            {
                currentDropBetAmount = betPerBall;
                bettingManager.DeductBet(totalBet);
                return true;
            }
            else
            {
                Debug.LogWarning($"Insufficient balance! Need ${totalBet:F2}, have ${bettingManager.CurrentBalance:F2}");
                return false;
            }
        }
    }
}
