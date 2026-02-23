using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class ForkLiftMover : MonoBehaviour
    {
        [Header("Waypoints")]
        [Tooltip("The center waypoint - forklift always returns here between each destination")]
        [SerializeField] private Transform centerPoint;

        [Tooltip("The 4 outer waypoints the forklift visits one at a time")]
        [SerializeField] private Transform[] outerPoints;

        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float waypointReachedThreshold = 0.5f;

        [Header("Barrel Settings")]
        [Tooltip("The barrel sitting on the forks")]
        [SerializeField] private GameObject barrelOnForks;

        [Tooltip("Prefab to spawn when barrel rolls off")]
        [SerializeField] private GameObject barrelPrefab;

        [Tooltip("How often a barrel rolls off (seconds)")]
        [SerializeField] private float barrelDropInterval = 5f;

        [Tooltip("Maximum number of barrels allowed at once - oldest dissolves when limit is exceeded")]
        [SerializeField] private int maxBarrels = 5;

        [Tooltip("Direction forklift faces when idle at a waypoint (degrees)")]
        [SerializeField] private float idleRotationY = 0f;

        // Movement state
        private int currentOuterIndex = 0;
        private bool headingToCenter = false;
        private Transform currentTarget;
        private NavMeshAgent navMeshAgent;

        // Barrel timing
        private float barrelDropTimer = 0f;

        // Barrel queue - tracks spawned barrels oldest to newest
        private Queue<DroppedBarrel> spawnedBarrels = new Queue<DroppedBarrel>();

        IEnumerator Start()
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
            navMeshAgent.speed = moveSpeed;

            if (centerPoint == null || outerPoints.Length == 0)
            {
                Debug.LogWarning($"[{name}] ForkLiftMover: assign centerPoint and outerPoints!");
                enabled = false;
                yield break;
            }

            // Wait one frame for the NavMeshAgent to be fully placed on the NavMesh
            yield return null;

            // Start by heading to first outer point
            currentTarget = outerPoints[currentOuterIndex];
            navMeshAgent.SetDestination(currentTarget.position);
        }

        void Update()
        {
            MoveToTarget();
            UpdateBarrelDrop();
        }

        private void MoveToTarget()
        {
            if (currentTarget == null || navMeshAgent == null) return;
            if (!navMeshAgent.isOnNavMesh) return;
            if (navMeshAgent.pathPending) return;

            // Check if the NavMeshAgent has arrived at the target
            if (navMeshAgent.remainingDistance <= waypointReachedThreshold)
            {
                OnReachedTarget();
            }
        }

        private void OnReachedTarget()
        {
            if (headingToCenter)
            {
                // Just arrived at center - move to next outer point
                headingToCenter = false;
                currentOuterIndex = (currentOuterIndex + 1) % outerPoints.Length;
                currentTarget = outerPoints[currentOuterIndex];
                Debug.Log($"[{name}] Heading to outer point {currentOuterIndex + 1}");
            }
            else
            {
                // Just arrived at outer point - return to center
                headingToCenter = true;
                currentTarget = centerPoint;
                // Face idle direction when arriving at a point
                transform.rotation = Quaternion.Euler(0f, idleRotationY, 0f);
                Debug.Log($"[{name}] Returning to center");
            }

            navMeshAgent.SetDestination(currentTarget.position);
        }

        private void UpdateBarrelDrop()
        {
            if (barrelOnForks == null || !barrelOnForks.activeSelf) return;

            barrelDropTimer += Time.deltaTime;

            if (barrelDropTimer >= barrelDropInterval)
            {
                barrelDropTimer = 0f;
                DropBarrel();
            }
        }

        private void DropBarrel()
        {
            if (barrelPrefab == null) return;

            // Clear out any nulls (barrels destroyed externally)
            while (spawnedBarrels.Count > 0 && spawnedBarrels.Peek() == null)
                spawnedBarrels.Dequeue();

            // If at the limit, dissolve the oldest barrel before spawning a new one
            if (spawnedBarrels.Count >= maxBarrels)
            {
                DroppedBarrel oldest = spawnedBarrels.Dequeue();
                if (oldest != null)
                {
                    oldest.DissolveAndDestroy();
                    Debug.Log($"[{name}] Max barrels reached - dissolving oldest");
                }
            }

            // Spawn the new barrel and track it
            Vector3 spawnPos = transform.position;
            GameObject newBarrelObj = Instantiate(barrelPrefab, spawnPos, Quaternion.identity);
            DroppedBarrel newBarrel = newBarrelObj.GetComponent<DroppedBarrel>();
            if (newBarrel != null)
                spawnedBarrels.Enqueue(newBarrel);

            Debug.Log($"[{name}] Barrel dropped at {spawnPos} ({spawnedBarrels.Count}/{maxBarrels})");
        }
    }
}