using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Plays footstep sounds on the NPC based on its NavMeshAgent speed.
    /// Attach to the same GameObject as the NavMeshAgent and add an AudioSource.
    /// </summary>
    public class NpcFootstepAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] footstepClips;

        [Tooltip("Time between steps when walking.")]
        [SerializeField] private float walkStepInterval = 0.55f;

        [Tooltip("Time between steps when running.")]
        [SerializeField] private float runStepInterval = 0.32f;

        [Tooltip("Agent speed above which the run interval is used.")]
        [SerializeField] private float runSpeedThreshold = 4f;

        [Tooltip("Minimum agent speed before footsteps play at all.")]
        [SerializeField] private float moveThreshold = 0.5f;

        [SerializeField] [Range(0f, 1f)] private float volume = 0.6f;

        private NavMeshAgent _agent;
        private float _stepTimer;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
        }

        void Update()
        {
            if (_agent == null || footstepClips == null || footstepClips.Length == 0) return;

            float speed = _agent.velocity.magnitude;

            // Reset timer when the NPC stops so the first step after moving isn't delayed
            if (speed < moveThreshold)
            {
                _stepTimer = 0f;
                return;
            }

            float interval = speed >= runSpeedThreshold ? runStepInterval : walkStepInterval;
            _stepTimer -= Time.deltaTime;

            if (_stepTimer <= 0f)
            {
                AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
                audioSource.PlayOneShot(clip, volume);
                _stepTimer = interval;
            }
        }
    }
}
