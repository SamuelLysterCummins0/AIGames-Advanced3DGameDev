using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Picks one spawn point at random on Start and instantiates the weapon prefab there.
    /// Add multiple child GameObjects as spawn points and assign them in the Inspector.
    /// </summary>
    public class WeaponSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject weaponPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [Tooltip("Offset applied to the spawn position. Adjust if the knife floats, clips into the floor, or is misaligned.")]
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;

        void Start()
        {
            if (weaponPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogWarning("[WeaponSpawner] No prefab or spawn points assigned.");
                return;
            }

            Transform chosen = spawnPoints[Random.Range(0, spawnPoints.Length)];
            Vector3 spawnPos = chosen.position + spawnOffset;
            Instantiate(weaponPrefab, spawnPos, chosen.rotation);
            Debug.Log($"[WeaponSpawner] Weapon spawned at {chosen.name}.");
        }
    }
}
