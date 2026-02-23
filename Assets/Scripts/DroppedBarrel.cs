using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    [RequireComponent(typeof(NavMeshObstacle))]
    [RequireComponent(typeof(Rigidbody))]
    public class DroppedBarrel : MonoBehaviour
    {
        [Header("Roll Settings")]
        [SerializeField] private float rollForce = 3f;

        [Header("NavMesh Settings")]
        [Tooltip("How long after stopping before becoming a NavMesh obstacle")]
        [SerializeField] private float carveDelay = 1f;

        [Header("Dissolve Settings")]
        [Tooltip("How long the dissolve takes when removed")]
        [SerializeField] private float dissolveDuration = 2f;

        private Rigidbody rb;
        private NavMeshObstacle navObstacle;
        private DissolveEffect dissolveEffect;
        private bool hasSettled = false;
        private float settleTimer = 0f;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            navObstacle = GetComponent<NavMeshObstacle>();
            dissolveEffect = GetComponent<DissolveEffect>();

            // Keep obstacle disabled until barrel settles - prevents NavMesh spam
            navObstacle.enabled = false;

            // Random roll direction
            Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            rb.AddForce(randomDir * rollForce, ForceMode.Impulse);
        }

        void Update()
        {
            if (hasSettled) return;

            if (rb.linearVelocity.magnitude < 0.1f)
            {
                settleTimer += Time.deltaTime;

                if (settleTimer >= carveDelay)
                {
                    SettleBarrel();
                }
            }
            else
            {
                settleTimer = 0f;
            }
        }

        private void SettleBarrel()
        {
            hasSettled = true;
            rb.isKinematic = true;

            // Only enable NavMeshObstacle once fully stopped
            navObstacle.enabled = true;
            navObstacle.carving = true;
            navObstacle.carveOnlyStationary = true;

            Debug.Log($"[{name}] Barrel settled - now a NavMesh obstacle");
        }

        /// <summary>
        /// Called by ForkLiftMover when this barrel needs to be removed.
        /// Triggers the dissolve shader then destroys the object.
        /// </summary>
        public void DissolveAndDestroy()
        {
            // Disable as nav obstacle so NPC doesn't path around a disappearing barrel
            navObstacle.enabled = false;

            if (dissolveEffect != null)
            {
                dissolveEffect.Play();
                Destroy(gameObject, dissolveDuration);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}