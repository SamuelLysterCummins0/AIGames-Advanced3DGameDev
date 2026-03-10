using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Attach to the player's weapon GameObject.
    /// Call PlayShot() from your shooting script whenever a shot fires.
    /// Supports multiple clips so each shot sounds slightly different.
    /// </summary>
    public class GunAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] gunshotClips;

        [Tooltip("Small pitch variation so repeated shots don't sound identical.")]
        [SerializeField] private float minPitch = 0.95f;
        [SerializeField] private float maxPitch = 1.05f;

        void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
        }

        /// <summary>Call this from your shooting script when a shot is fired.</summary>
        public void PlayShot()
        {
            if (gunshotClips == null || gunshotClips.Length == 0) return;

            audioSource.pitch = Random.Range(minPitch, maxPitch);
            audioSource.PlayOneShot(gunshotClips[Random.Range(0, gunshotClips.Length)]);
        }
    }
}
