
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
            Debug.Log($"[PLINKO] Controlled outcome set to: {controlled}");
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
            Debug.Log("[PLINKO] GameManager Awake");
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PLINKO] Duplicate GameManager detected, destroying");
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
            Debug.Log("[PLINKO] GameManager Start - Initializing game");
            bettingManager ??= BettingManager.Instance;
            Debug.Log($"[PLINKO] BettingManager: {(bettingManager != null ? "assigned" : "NULL - assign in Inspector")}");
            InitializePool();

            boardManager.GenerateBoard();
            Debug.Log($"[PLINKO] Board generated with {boardManager.PegRows} rows and {boardManager.SlotCount} slots");

            Slot.OnBallLanded += HandleBallLanded;
            Debug.Log("[PLINKO] GameManager initialization complete");
        }

        private void OnDestroy()
        {
            Slot.OnBallLanded -= HandleBallLanded;
        }

        private void InitializePool()
        {
            Debug.Log($"[PLINKO] Initializing ball pool with {poolSize} balls");
            for (int i = 0; i < poolSize; i++)
            {
                PlinkoBall ball = CreateBallForPool();
                ballPool.Enqueue(ball);
            }
            Debug.Log($"[PLINKO] Ball pool initialized: {ballPool.Count} balls ready");
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
            Debug.Log($"[PLINKO] Setting target slots: [{string.Join(", ", slots)}]");
            targetSlots = slots;
            currentTargetIndex = 0;
            results.Clear();
        }

        public void DropBall()
        {
            Debug.Log("[PLINKO] DropBall called");
            if (isBallInPlay)
            {
                Debug.LogWarning("[PLINKO] Ball already in play, ignoring drop request");
                return;
            }

            if (useControlledOutcome && currentTargetIndex >= targetSlots.Length)
            {
                Debug.Log("[PLINKO] All targets completed");
                OnTargetsCompleted?.Invoke();
                return;
            }

            if (bettingManager != null && !TryDeductBets(1))
            {
                uiManager?.ShowInsufficientBalanceMessage();
                return;
            }

            Debug.Log($"[PLINKO] Starting ball drop (target index: {currentTargetIndex}, controlled: {useControlledOutcome})");
            StartCoroutine(DropBallCoroutine());
        }

        public void DropBalls(int count)
        {
            Debug.Log($"[PLINKO] DropBalls called with count: {count}");
    
            if (isMultiDropInProgress)
            {
                Debug.LogWarning("[PLINKO] Multi-drop already in progress, ignoring request");
                return;
            }

            if (isBallInPlay)
            {
                Debug.LogWarning("[PLINKO] Ball already in play, ignoring drop request");
                return;
            }

            if (bettingManager != null && !TryDeductBets(count))
            {
                uiManager?.ShowInsufficientBalanceMessage();
                return;
            }

            if (useControlledOutcome && currentTargetIndex >= targetSlots.Length)
            {
                Debug.Log("[PLINKO] All targets completed");
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
            Debug.Log("[PLINKO] Drop10Balls called");
            DropBalls(10);
        }

        private IEnumerator Drop10BallsCoroutine()
        {
            isMultiDropInProgress = true;
            isBallInPlay = true;
            OnBallStateChanged?.Invoke(true);
            Debug.Log("[PLINKO] Starting multi-drop sequence: 10 balls");

            // Drop all 10 balls with interval delay
            for (int i = 0; i < 10; i++)
            {
                Debug.Log($"[PLINKO] Dropping ball {i + 1}/10");

                yield return spawnWait;

                SpawnBallForMultiDrop();
                ballsInPlayCount++;

                // Wait for interval before dropping next ball
                if (i < 9)
                {
                    yield return intervalWait;
                }
            }

            Debug.Log("[PLINKO] All 10 balls dropped, waiting for them to land...");

            // Wait for all balls to land
            while (ballsInPlayCount > 0)
            {
                yield return null;
            }

            isMultiDropInProgress = false;
            isBallInPlay = false;
            OnBallStateChanged?.Invoke(false);
            Debug.Log("[PLINKO] Multi-drop sequence completed");
        }

        private IEnumerator DropMultipleBallsCoroutine(int count)
        {
            isMultiDropInProgress = true;
            isBallInPlay = true;
            OnBallStateChanged?.Invoke(true);
            Debug.Log($"[PLINKO] Starting multi-drop sequence: {count} balls");

            // Drop all balls with interval delay
            for (int i = 0; i < count; i++)
            {
                Debug.Log($"[PLINKO] Dropping ball {i + 1}/{count}");

                yield return spawnWait;

                SpawnBallForMultiDrop();
                ballsInPlayCount++;

                // Wait for interval before dropping next ball
                if (i < count - 1)
                {
                    yield return intervalWait;
                }
            }

            Debug.Log($"[PLINKO] All {count} balls dropped, waiting for them to land...");

            // Wait for all balls to land
            while (ballsInPlayCount > 0)
            {
                yield return null;
            }

            isMultiDropInProgress = false;
            isBallInPlay = false;
            OnBallStateChanged?.Invoke(false);
            Debug.Log("[PLINKO] Multi-drop sequence completed");
        }

        private void SpawnBallForMultiDrop()
        {
            Vector3 dropPos = boardManager.GetDropPosition();
            dropPos.x = 0f;
            Debug.Log($"[PLINKO] Spawning ball at position: {dropPos}");

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
                    Debug.LogWarning($"[PLINKO] Target slot {targetSlot} out of range, clamping to valid range [0, {maxSlot}]");
                    targetSlot = targetSlot < 0 ? 0 : maxSlot;
                }

                float slotX = boardManager.GetSlotXPosition(targetSlot);
                Slot slot = boardManager.Slots[targetSlot];
                Debug.Log($"[PLINKO] Controlled drop targeting slot {targetSlot} at X: {slotX}");
                ball.Initialize(targetSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
                currentTargetIndex++;
            }
            else
            {
                int randomSlot = UnityEngine.Random.Range(0, boardManager.SlotCount);
                float slotX = boardManager.GetSlotXPosition(randomSlot);
                Slot slot = boardManager.Slots[randomSlot];
                Debug.Log("[PLINKO] Random drop initiated");
                ball.Initialize(randomSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
            }
        }

        private void SpawnBall()
        {
            // Always drop from center (above middle top peg)
            Vector3 dropPos = boardManager.GetDropPosition();
            dropPos.x = 0f;
            Debug.Log($"[PLINKO] Spawning ball at position: {dropPos}");

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
                    Debug.LogWarning($"[PLINKO] Target slot {targetSlot} out of range, clamping to valid range [0, {maxSlot}]");
                    targetSlot = targetSlot < 0 ? 0 : maxSlot;
                }

                float slotX = boardManager.GetSlotXPosition(targetSlot);
                Slot slot = boardManager.Slots[targetSlot];
                Debug.Log($"[PLINKO] Controlled drop targeting slot {targetSlot} at X: {slotX}");
                currentBall.Initialize(targetSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
            }
            else
            {
                int randomSlot = UnityEngine.Random.Range(0, boardManager.SlotCount);
                float slotX = boardManager.GetSlotXPosition(randomSlot);
                Slot slot = boardManager.Slots[randomSlot];
                Debug.Log("[PLINKO] Random drop initiated");
                currentBall.Initialize(randomSlot, slotX, boardManager.PegRows, slotY, pegSpace, rowSpace, b => slot.TriggerBallLanded(b));
            }
        }

        private void HandleBallLanded(int slotId, float multiplier, PlinkoBall landedBall)
        {
            // Calculate win using the bet that was locked in at drop time
            float winAmount = 0f;
            if (bettingManager != null)
            {
                winAmount = currentDropBetAmount * multiplier;
                bettingManager.AddWin(winAmount);
                Debug.Log("Adding Win Amount");
            }
            else
            {
                if (bettingManager == null)
                {
                    Debug.Log("BettingManager null");
                }
                else
                {
                     Debug.Log("BettingManager not null");
                }
            }
            Debug.Log($"[PLINKO] Ball landed in slot {slotId} (x{multiplier})");

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
            Debug.Log($"[PLINKO] Total results: {results.Count}");

            OnBallResult?.Invoke(slotId, multiplier);

            // Handle multi-drop mode
            if (isMultiDropInProgress)
            {
                ballsInPlayCount--;
                Debug.Log($"[PLINKO] Balls still in play: {ballsInPlayCount}, landedBall: {(landedBall != null ? landedBall.name : "null")}, activeBalls count: {activeBalls.Count}");

                // Return the specific ball that landed
                if (landedBall != null && activeBalls.Contains(landedBall))
                {
                    Debug.Log($"[PLINKO] Returning landed ball to pool: {landedBall.name}");
                    activeBalls.Remove(landedBall);
                    ReturnBallToPool(landedBall);
                }
                else if (landedBall != null)
                {
                    // Ball landed but wasn't in activeBalls - still return it
                    Debug.LogWarning($"[PLINKO] Ball {landedBall.name} landed but was not in activeBalls list - returning anyway");
                    ReturnBallToPool(landedBall);
                }
                else
                {
                    // Fallback: remove first active ball if landedBall is null
                    Debug.LogWarning("[PLINKO] landedBall was null! Using fallback removal");
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
                // Single ball mode
                if (useControlledOutcome)
                {
                    currentTargetIndex++;
                    Debug.Log($"[PLINKO] Target index advanced to {currentTargetIndex}/{targetSlots.Length}");
                }

                ReturnBallToPool(landedBall != null ? landedBall : currentBall);
                currentBall = null;

                isBallInPlay = false;
                OnBallStateChanged?.Invoke(false);

                if (useControlledOutcome && currentTargetIndex >= targetSlots.Length)
                {
                    Debug.Log("[PLINKO] All targets completed!");
                    OnTargetsCompleted?.Invoke();
                }
            }
        }

        public void ResetGame()
        {
            Debug.Log("[PLINKO] Game reset");
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

            // Return all active balls to pool
            foreach (var ball in activeBalls)
            {
                ReturnBallToPool(ball);
            }
            activeBalls.Clear();

            Debug.Log($"[PLINKO] Reset complete - {targetSlots.Length} targets remaining");
        }
        
        
        private bool TryDeductBets(int ballCount)
        {
            float betPerBall = bettingManager.CurrentBetAmount;
            float totalBet = betPerBall * ballCount;

            if (bettingManager.CanDeductBet(totalBet))
            {
                currentDropBetAmount = betPerBall;
                bettingManager.DeductBet(totalBet);
                Debug.Log($"[PLINKO] Deducted ${totalBet:F2} for {ballCount} balls (${betPerBall:F2} each)");
                return true;
            }
            else
            {
                Debug.LogWarning($"[PLINKO] Insufficient balance! Need ${totalBet:F2}, have ${bettingManager.CurrentBalance:F2}");
                return false;
            }
        }
    }
    
}
