using System.Collections;
using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Plays a looping radio chatter clip on the NPC with a configurable on/off cycle.
    /// Default: plays for 7 seconds, silent for ~20 seconds, then repeats.
    /// Call StopRadio() when the NPC dies so the sound cuts out cleanly.
    /// </summary>
    public class NpcRadioAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip   radioClip;

        [Tooltip("How long the radio plays before cutting off.")]
        [SerializeField] private float playDuration = 7f;

        [Tooltip("Base silence time between bursts.")]
        [SerializeField] private float silenceDuration = 20f;

        [Tooltip("Random seconds added or removed from silence so multiple NPCs don't sync.")]
        [SerializeField] private float silenceVariance = 5f;

        [Tooltip("Delay before the very first broadcast. Stagger this per NPC.")]
        [SerializeField] private float startDelay = 2f;

        void Start()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
            {
                Debug.LogWarning($"[NpcRadioAudio] No AudioSource found on {gameObject.name}. Add one and assign it.");
                return;
            }
            if (radioClip == null)
            {
                Debug.LogWarning($"[NpcRadioAudio] No radio clip assigned on {gameObject.name}. Drag a clip into the Radio Clip field.");
                return;
            }

            StartCoroutine(RadioLoop());
        }

        private IEnumerator RadioLoop()
        {
            yield return new WaitForSeconds(startDelay);

            while (true)
            {
                // PlayOneShot bypasses the AudioSource's internal clip/loop state — the same
                // pattern used by GunAudio, proven to work reliably in 3D.
                Debug.Log($"[NpcRadioAudio] <color=cyan>Radio burst playing on {gameObject.name}</color>");
                audioSource.PlayOneShot(radioClip);

                // Wait for the clip to finish naturally, then the silence gap before the next burst.
                // Using radioClip.length instead of a hard Play/Stop so the timing is self-contained.
                float clipTime = Mathf.Min(radioClip.length, playDuration);
                float pause    = clipTime + silenceDuration + Random.Range(-silenceVariance, silenceVariance);
                yield return new WaitForSeconds(Mathf.Max(1f, pause));
            }
        }

        /// <summary>Called by NpcController when the NPC is taken down so the radio cuts out.</summary>
        public void StopRadio()
        {
            StopAllCoroutines();
            if (audioSource != null) audioSource.Stop();
        }
    }
}
