using UnityEngine;
using TMPro;
using System;

namespace Plinko
{
    public class Slot : MonoBehaviour
    {
        [SerializeField] private int slotId;
        [SerializeField] private float multiplier;
        [SerializeField] private TMP_Text multiplierText;

        public int SlotId => slotId;
        public float Multiplier => multiplier;

        public static event Action<int, float, PlinkoBall> OnBallLanded;

        public void Initialize(int id, float mult)
        {
            slotId = id;
            multiplier = mult;
#if UNITY_EDITOR
            gameObject.name = $"Slot_{id}";
#endif
            UpdateText();
        }

        private void UpdateText()
        {
            multiplierText.text = $"x{multiplier}";
        }

        public void TriggerBallLanded(PlinkoBall ball = null)
        {
            OnBallLanded?.Invoke(slotId, multiplier, ball);
        }

    }
}
