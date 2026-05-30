using UnityEngine;
using System.Collections;

namespace Plinko
{
    public class Peg : MonoBehaviour
    {
        [SerializeField] private float pulseScale = 1.8f;
        [SerializeField] private float pulseDuration = 0.22f;
        [SerializeField] private Color pulseColor = Color.white;

        private Vector3 originalScale;
        private SpriteRenderer sr;
        private Color originalColor;
        private Coroutine pulseCoroutine;

        private void Awake()
        {
            originalScale = transform.localScale;
            sr = GetComponent<SpriteRenderer>();
            if (sr != null) originalColor = sr.color;
        }

        public void Pulse()
        {
            if (pulseCoroutine != null) StopCoroutine(pulseCoroutine);
            pulseCoroutine = StartCoroutine(DoPulse());
        }

        private IEnumerator DoPulse()
        {
            float half = pulseDuration * 0.5f;
            float elapsed = 0f;

            // Scale up + flash to white
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / half);
                transform.localScale = Vector3.Lerp(originalScale, originalScale * pulseScale, t);
                if (sr != null) sr.color = Color.Lerp(originalColor, pulseColor, t);
                yield return null;
            }

            // Scale back to normal
            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / half);
                transform.localScale = Vector3.Lerp(originalScale * pulseScale, originalScale, t);
                if (sr != null) sr.color = Color.Lerp(pulseColor, originalColor, t);
                yield return null;
            }

            transform.localScale = originalScale;
            if (sr != null) sr.color = originalColor;
            pulseCoroutine = null;
        }
    }
}
