using UnityEngine;
using System.Collections.Generic;

namespace Plinko
{
    public class BoardManager : MonoBehaviour
    {
        [Header("Board Configuration")]
        [SerializeField, Range(8, 20)] private int pegRows = 12;
        [SerializeField] private float pegSpacing = 0.6f;
        [SerializeField] private float rowSpacing = 0.5f;

        [Header("Prefabs")]
        [SerializeField] private GameObject pegPrefab;
        [SerializeField] private GameObject slotPrefab;
        [SerializeField] private GameObject wallPrefab;

        [Header("Object Pool")]
        [SerializeField] private Transform pegPoolParent;
        [SerializeField] private Transform slotPoolParent;

        private List<GameObject> activePegs;
        private List<Slot> activeSlots;
        private Queue<GameObject> pegPool;
        private Queue<GameObject> slotPool;
        private GameObject leftWall;
        private GameObject rightWall;
        private float boardWidth;
        private float boardHeight;

        // Cached for GC optimization
        private Vector3 positionCache;
        private float[] multiplierCache;

        public int PegRows => pegRows;
        public float PegSpacing => pegSpacing;
        public float RowSpacing => rowSpacing;
        // Slots fit between pegs in the last row (lastRowPegs - 1 gaps)
        public int SlotCount => 3 + pegRows - 2;
        public float BoardWidth => boardWidth;
        public List<Slot> Slots => activeSlots;

        // Multipliers matching reference image (symmetric)
        private static readonly float[] baseMultipliers = { 15f, 6f, 2f, 1.3f, 1.2f, 1.1f, 1.1f, 1f, 0.5f };

        private void Awake()
        {
            // Pre-allocate with estimated capacity
            int estimatedPegs = (3 + pegRows) * pegRows / 2;
            activePegs = new List<GameObject>(estimatedPegs);
            activeSlots = new List<Slot>(SlotCount);
            pegPool = new Queue<GameObject>(estimatedPegs);
            slotPool = new Queue<GameObject>(SlotCount);
            multiplierCache = new float[SlotCount];
        }

        public void GenerateBoard()
        {
            ClearBoard();
            GeneratePegs();
            GenerateSlots();
            GenerateWalls();
        }

        private void ClearBoard()
        {
            foreach (var peg in activePegs)
            {
                ReturnPegToPool(peg);
            }
            activePegs.Clear();

            foreach (var slot in activeSlots)
            {
                ReturnSlotToPool(slot.gameObject);
            }
            activeSlots.Clear();
        }

        private GameObject GetPegFromPool()
        {
            GameObject peg;
            if (pegPool.Count > 0)
            {
                peg = pegPool.Dequeue();
                peg.SetActive(true);
            }
            else
            {
                peg = Instantiate(pegPrefab, transform);
            }
            return peg;
        }

        private void ReturnPegToPool(GameObject peg)
        {
            peg.SetActive(false);
            peg.transform.SetParent(pegPoolParent);
            pegPool.Enqueue(peg);
        }

        private GameObject GetSlotFromPool()
        {
            GameObject slot;
            if (slotPool.Count > 0)
            {
                slot = slotPool.Dequeue();
                slot.SetActive(true);
            }
            else
            {
                slot = Instantiate(slotPrefab, transform);
            }
            return slot;
        }

        private void ReturnSlotToPool(GameObject slot)
        {
            slot.SetActive(false);
            slot.transform.SetParent(slotPoolParent);
            slotPool.Enqueue(slot);
        }

        private void GeneratePegs()
        {
            // Pyramid layout: starts with 3 pegs, expands each row
            const int startPegs = 3;
            float startY = pegRows * rowSpacing * 0.5f;
            Transform parent = pegPoolParent != null ? pegPoolParent : transform;

            for (int row = 0; row < pegRows; row++)
            {
                int pegsInRow = startPegs + row;
                float rowWidth = (pegsInRow - 1) * pegSpacing;
                float startX = -rowWidth * 0.5f;
                float y = startY - (row * rowSpacing);

                for (int col = 0; col < pegsInRow; col++)
                {
                    positionCache.x = startX + (col * pegSpacing);
                    positionCache.y = y;
                    positionCache.z = 0f;

                    GameObject peg = GetPegFromPool();
                    peg.transform.SetParent(parent);
                    peg.transform.position = positionCache;
                    activePegs.Add(peg);
                }
            }

            // Calculate board dimensions
            int lastRowPegs = startPegs + pegRows - 1;
            boardWidth = (lastRowPegs - 1) * pegSpacing + pegSpacing;
            boardHeight = pegRows * rowSpacing;
        }

        private void GenerateSlots()
        {
            int slotCount = SlotCount;

            // Position slots between pegs of the last row
            int lastRowPegs = 3 + pegRows - 1;
            float lastRowWidth = (lastRowPegs - 1) * pegSpacing;
            float lastRowStartX = -lastRowWidth * 0.5f;

            // Slot width matches peg spacing, positioned at midpoints between pegs
            float startX = lastRowStartX + pegSpacing * 0.5f;

            // Position just below the last row of pegs
            float lastRowY = (pegRows * rowSpacing * 0.5f) - ((pegRows - 1) * rowSpacing);
            positionCache.y = lastRowY - rowSpacing * 0.8f;
            positionCache.z = 0f;

            // Generate multipliers (symmetric) - reuse cached array
            GenerateMultipliers(slotCount);
            Transform parent = slotPoolParent != null ? slotPoolParent : transform;

            for (int i = 0; i < slotCount; i++)
            {
                positionCache.x = startX + (i * pegSpacing);

                GameObject slotObj = GetSlotFromPool();
                slotObj.transform.SetParent(parent);
                slotObj.transform.position = positionCache;

                if (slotObj.TryGetComponent(out Slot slot))
                {
                    slot.Initialize(i, multiplierCache[i]);
                    activeSlots.Add(slot);
                }
            }
        }

        private void GenerateMultipliers(int count)
        {
            // Resize cache if needed
            if (multiplierCache == null || multiplierCache.Length < count)
            {
                multiplierCache = new float[count];
            }

            int maxIndex = baseMultipliers.Length - 1;
            for (int i = 0; i < count; i++)
            {
                // Edge slots get highest multipliers, center gets lowest
                int edgeDist = i < count - 1 - i ? i : count - 1 - i;
                multiplierCache[i] = edgeDist < baseMultipliers.Length
                    ? baseMultipliers[edgeDist]
                    : baseMultipliers[maxIndex];
            }
        }

        private void GenerateWalls()
        {
            // Position walls just outside the slot area
            int lastRowPegs = 3 + pegRows - 1;
            float lastRowWidth = (lastRowPegs - 1) * pegSpacing;
            float wallX = lastRowWidth * 0.5f;

            // Reuse existing walls or create new ones
            if (leftWall == null)
            {
                leftWall = Instantiate(wallPrefab, transform);
            }
            positionCache.x = -wallX;
            positionCache.y = 0f;
            positionCache.z = 0f;
            leftWall.transform.position = positionCache;

            if (rightWall == null)
            {
                rightWall = Instantiate(wallPrefab, transform);
            }
            positionCache.x = wallX;
            rightWall.transform.position = positionCache;
        }

        public float GetSlotXPosition(int slotId)
        {
            if (slotId >= 0 && slotId < activeSlots.Count)
            {
                return activeSlots[slotId].transform.position.x;
            }
            return 0f;
        }

        public float GetSlotYPosition()
        {
            if (activeSlots.Count > 0)
            {
                return activeSlots[0].transform.position.y;
            }
            // Fallback calculation
            float lastRowY = (pegRows * rowSpacing * 0.5f) - ((pegRows - 1) * rowSpacing);
            return lastRowY - rowSpacing * 0.8f;
        }

        public Vector3 GetDropPosition()
        {
            positionCache.x = 0f;
            positionCache.y = (pegRows * rowSpacing * 0.5f) + 1f;
            positionCache.z = 0f;
            return positionCache;
        }
    }
}
