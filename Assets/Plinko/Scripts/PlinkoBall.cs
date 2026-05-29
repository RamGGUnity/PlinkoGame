using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Plinko
{
    [RequireComponent(typeof(CircleCollider2D))]
    public class PlinkoBall : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private float minBounceDuration = 0.18f;
        [SerializeField] private float maxBounceDuration = 0.25f;
        [SerializeField] private float finalDropDuration = 0.3f;
        [SerializeField] private float slotDetectionRadius = 0.2f;

        private int targetSlotId = -1;
        private float targetSlotX;
        private System.Action<PlinkoBall> onLanded;
        private bool hasLanded;

        private float pegSpacing;
        private float rowSpacing;
        private int totalRows;
        private float endY;

        // Pre-allocated for GC optimization
        private readonly List<Vector2> bouncePath = new List<Vector2>(24);
        private readonly Collider2D[] hitBuffer = new Collider2D[4];
        private ContactFilter2D contactFilter;
        private Vector3 positionCache;
        private CircleCollider2D ballCollider;

        public int TargetSlotId => targetSlotId;
        public float TargetSlotX => targetSlotX;

        private void Awake()
        {
            contactFilter = new ContactFilter2D();
            contactFilter.NoFilter();
            ballCollider = GetComponent<CircleCollider2D>();
        }

        public void Initialize(int targetSlot, float slotX, int rows, float slotY, float pegSpace, float rowSpace, System.Action<PlinkoBall> landedCallback = null)
        {
            targetSlotId = targetSlot;
            targetSlotX = slotX;
            totalRows = rows;
            pegSpacing = pegSpace;
            rowSpacing = rowSpace;
            endY = slotY;
            onLanded = landedCallback;

            StartCoroutine(AnimateBounce());
        }

        private IEnumerator AnimateBounce()
        {
            // Disable collider during animation to prevent premature slot triggers
            if (ballCollider != null)
            {
                ballCollider.enabled = false;
            }

            CalculateZigZagPath();

            int pathCount = bouncePath.Count;
            for (int i = 0; i < pathCount - 1; i++)
            {
                yield return AnimateSegment(bouncePath[i], bouncePath[i + 1]);
            }

            // Final settle into slot
            Vector2 lastPos = bouncePath[pathCount - 1];
            yield return AnimateFinalDrop(lastPos.y, targetSlotX, endY - 0.25f);

            DetectSlotCollision();
        }

        private IEnumerator AnimateSegment(Vector2 start, Vector2 end)
        {
            float horizontalDist = Mathf.Abs(end.x - start.x);
            float duration = minBounceDuration + Random.Range(0f, maxBounceDuration - minBounceDuration);
            float sideArc = horizontalDist * 0.15f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = t * t;

                float y = Mathf.Lerp(start.y, end.y, easedT);
                float x = Mathf.Lerp(start.x, end.x, t);

                // Small wobble at impact
                if (t < 0.3f)
                {
                    float wobbleT = t * 3.333f; // t / 0.3f optimized
                    x += Mathf.Sin(wobbleT * Mathf.PI) * sideArc * (1f - wobbleT);
                }

                positionCache.x = x;
                positionCache.y = y;
                positionCache.z = 0f;
                transform.position = positionCache;
                yield return null;
            }

            positionCache.x = end.x;
            positionCache.y = end.y;
            positionCache.z = 0f;
            transform.position = positionCache;
        }

        private IEnumerator AnimateFinalDrop(float startY, float endX, float finalY)
        {
            // Snap X to slot position immediately, then fall straight down
            positionCache.x = endX;
            positionCache.y = startY;
            positionCache.z = 0f;
            transform.position = positionCache;

            float elapsed = 0f;

            while (elapsed < finalDropDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / finalDropDuration);

                // Gravity acceleration (faster as it falls)
                float gravityT = t * t;

                positionCache.x = endX;
                positionCache.y = Mathf.Lerp(startY, finalY, gravityT);
                positionCache.z = 0f;
                transform.position = positionCache;
                yield return null;
            }

            positionCache.x = endX;
            positionCache.y = finalY;
            positionCache.z = 0f;
            transform.position = positionCache;
        }

        private void CalculateZigZagPath()
        {
            bouncePath.Clear();

            float startX = transform.position.x;
            float startY = transform.position.y;
            float boardTopY = totalRows * rowSpacing * 0.5f;

            bouncePath.Add(new Vector2(startX, startY));

            float currentX = startX;
            float halfPegSpacing = pegSpacing * 0.5f;

            for (int row = 0; row < totalRows; row++)
            {
                int pegsInRow = 3 + row;
                float rowWidth = (pegsInRow - 1) * pegSpacing;
                float rowStartX = -rowWidth * 0.5f;
                float rowY = boardTopY - row * rowSpacing;

                // Find nearest peg
                float relativeX = currentX - rowStartX;
                int nearestCol = Mathf.Clamp(Mathf.RoundToInt(relativeX / pegSpacing), 0, pegsInRow - 1);
                float nearestPegX = rowStartX + nearestCol * pegSpacing;

                // Always calculate direction from CURRENT position to target
                float distanceToTarget = targetSlotX - currentX;
                int neededDir = distanceToTarget > 0 ? 1 : -1;

                // How many rows left to reach target
                int rowsRemaining = totalRows - row;
                float maxReachableDistance = rowsRemaining * halfPegSpacing;
                float absDistanceToTarget = Mathf.Abs(distanceToTarget);

                int bounceDir;
                if (absDistanceToTarget < 0.01f)
                {
                    // Already at target X, alternate to look natural
                    bounceDir = (row & 1) == 0 ? 1 : -1;
                }
                else if (absDistanceToTarget >= maxReachableDistance * 0.7f)
                {
                    // Must go toward target to reach it in time
                    bounceDir = neededDir;
                }
                else
                {
                    // Some slack - add randomness but bias toward target
                    float biasProbability = 0.5f + (absDistanceToTarget / maxReachableDistance) * 0.4f;
                    bounceDir = Random.value < biasProbability ? neededDir : -neededDir;
                }

                float postBounceX = nearestPegX + halfPegSpacing * bounceDir;
                float boardEdge = rowWidth * 0.5f + pegSpacing * 0.25f;
                postBounceX = Mathf.Clamp(postBounceX, -boardEdge, boardEdge);

                bouncePath.Add(new Vector2(postBounceX, rowY));
                currentX = postBounceX;
            }

            // Ensure final position is at target slot X
            if (bouncePath.Count > 1)
            {
                int lastIndex = bouncePath.Count - 1;
                Vector2 lastPoint = bouncePath[lastIndex];
                bouncePath[lastIndex] = new Vector2(targetSlotX, lastPoint.y);
            }
        }

        private void DetectSlotCollision()
        {
            if (hasLanded) return;
            hasLanded = true;

            // Use direct callback (set at Initialize time) — avoids physics detection entirely
            if (onLanded != null)
            {
                onLanded(this);
                return;
            }

            // Physics fallback for cases where no callback was provided
            Vector2 detectionPos = new Vector2(targetSlotX, endY);
            int hitCount = Physics2D.OverlapCircle(detectionPos, slotDetectionRadius, contactFilter, hitBuffer);

            for (int i = 0; i < hitCount; i++)
            {
                if (hitBuffer[i].TryGetComponent(out Slot slot))
                {
                    slot.TriggerBallLanded(this);
                    return;
                }
            }

            Debug.LogError($"No slot detected! Position: {transform.position}, Target slot: {targetSlotId}, Target X: {targetSlotX}");
        }

        public void ResetBall()
        {
            targetSlotId = -1;
            onLanded = null;
            hasLanded = false;
            StopAllCoroutines();

            // Ensure collider is re-enabled when ball is reset
            if (ballCollider != null)
            {
                ballCollider.enabled = true;
            }
        }
    }
}
