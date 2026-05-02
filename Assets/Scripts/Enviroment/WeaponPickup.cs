using Fusion;
using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Networked weapon pickup. Works whether spawned via Fusion (GameBootstrap) or via
    /// plain Instantiate (WeaponSpawner) so both setups are supported during the transition.
    ///
    /// Networked path: RPC → StateAuthority sets IsPickedUp → ChangeDetector → Despawn.
    /// Local fallback: direct Destroy when Runner is null (non-networked object).
    /// </summary>
    public class WeaponPickup : NetworkBehaviour
    {
        [SerializeField] private float      pickupRange = 2.5f;
        [SerializeField] private KeyCode    pickupKey   = KeyCode.E;
        [SerializeField] private GameObject _model;

        [Networked] private NetworkBool IsPickedUp { get; set; }
        [Networked] private PlayerRef   PickedUpBy { get; set; }

        private ChangeDetector _changeDetector;
        private Transform      _localPlayer;
        private GameObject     _promptUI;

        // ── Fusion path ────────────────────────────────────────────────────────

        public override void Spawned()
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            if (_model != null) _model.SetActive(true);
            TryFindLocalPlayer();
            TryCachePromptUI();
            if (_promptUI != null) _promptUI.SetActive(false);

            Debug.Log($"[WeaponPickup] Spawned — localPlayer={((_localPlayer != null) ? _localPlayer.name : "NULL")} promptUI={(_promptUI != null ? _promptUI.name : "NULL")} HasStateAuthority={Object.HasStateAuthority}");
        }

        // ── Non-networked fallback (WeaponSpawner / plain Instantiate) ─────────

        private void Start()
        {
            // Only runs when not already initialised by Spawned()
            if (_localPlayer == null) TryFindLocalPlayer();
            TryCachePromptUI();
            if (_promptUI != null) _promptUI.SetActive(false);
        }

        // ── Shared update ──────────────────────────────────────────────────────

        private void Update()
        {
            if (_localPlayer == null)
            {
                TryFindLocalPlayer();
                if (_localPlayer != null)
                    Debug.Log($"[WeaponPickup] Found local player in Update: {_localPlayer.name}");
                return;
            }

            // Runner == null means the object was not spawned via Fusion
            bool isNetworked = Runner != null && Object != null && Object.IsValid;
            if (isNetworked && IsPickedUp) return;

            float dist = Vector3.Distance(transform.position, _localPlayer.position);
            bool inRange = dist <= pickupRange;

            // Log distance every ~2 seconds so we can see if the player ever gets close
            if (Time.frameCount % 120 == 0)
                Debug.Log($"[WeaponPickup] dist={dist:F2} pickupRange={pickupRange} inRange={inRange} weaponPos={transform.position} playerPos={_localPlayer.position}");

            if (_promptUI != null)
                _promptUI.SetActive(inRange);

            if (inRange && Input.GetKeyDown(pickupKey))
            {
                Debug.Log($"[WeaponPickup] E pressed in range. isNetworked={isNetworked} Runner={Runner != null} ObjectValid={Object != null && Object.IsValid}");
                if (isNetworked)
                {
                    RPC_RequestPickup(Runner.LocalPlayer);

                    // Fire the local game-state change immediately. We can't rely on the
                    // ChangeDetector path on the client because the host despawns this object
                    // in the same tick it sets IsPickedUp, and the despawn can reach the client
                    // before the IsPickedUp change does — the ChangeDetector then never fires.
                    if (GameManager.Instance != null
                        && GameManager.Instance.State == GameState.FindWeapon)
                    {
                        GameManager.Instance.OnWeaponPickedUp();
                    }
                }
                else
                {
                    PickupLocally();
                }
            }
        }

        // ── Networked pickup ───────────────────────────────────────────────────

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPickup(PlayerRef requester, RpcInfo info = default)
        {
            if (!IsPickedUp)
            {
                IsPickedUp = true;
                PickedUpBy = requester;
                Debug.Log($"[WeaponPickup] <color=green>Picked up by {requester}</color>");
            }
        }

        public override void Render()
        {
            if (_changeDetector == null) return;
            foreach (var change in _changeDetector.DetectChanges(this))
            {
                if (change == nameof(IsPickedUp))
                    HandleNetworkPickup();
            }
        }

        private void HandleNetworkPickup()
        {
            if (!IsPickedUp) return;
            if (_model    != null) _model.SetActive(false);
            if (_promptUI != null) _promptUI.SetActive(false);

            if (PickedUpBy == Runner.LocalPlayer && GameManager.Instance != null)
                GameManager.Instance.OnWeaponPickedUp();

            if (Object.HasStateAuthority)
                Runner.Despawn(Object);
        }

        // ── Non-networked pickup ───────────────────────────────────────────────

        private void PickupLocally()
        {
            if (_model    != null) _model.SetActive(false);
            if (_promptUI != null) _promptUI.SetActive(false);
            if (GameManager.Instance != null)
                GameManager.Instance.OnWeaponPickedUp();
            Destroy(gameObject);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void TryFindLocalPlayer()
        {
            // In networked mode, the NetworkObject (and HasInputAuthority) lives on the player
            // root, but the CharacterController — and therefore actual world position — lives on
            // a child capsule. We must track the capsule's transform or distance never changes.
            if (Runner != null && Object != null && Object.IsValid)
            {
                foreach (GameObject obj in GameObject.FindGameObjectsWithTag("Player"))
                {
                    var no = obj.GetComponent<NetworkObject>();
                    if (no != null && no.HasInputAuthority)
                    {
                        var cc = no.GetComponentInChildren<CharacterController>();
                        _localPlayer = cc != null ? cc.transform : obj.transform;
                        Debug.Log($"[WeaponPickup] Local player found: root={obj.name} tracking={(cc != null ? cc.name : obj.name)}");
                        return;
                    }
                }
                return; // local player not spawned yet — Update() will retry
            }

            // Non-networked fallback
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                var cc = playerObj.GetComponentInChildren<CharacterController>();
                _localPlayer = cc != null ? cc.transform : playerObj.transform;
            }
        }

        private void TryCachePromptUI()
        {
            if (_promptUI == null)
            {
                if (GameManager.Instance != null)
                {
                    _promptUI = GameManager.Instance.weaponPromptUI;
                    Debug.Log($"[WeaponPickup] TryCachePromptUI — GameManager found, weaponPromptUI={((_promptUI != null) ? _promptUI.name : "NULL (not assigned in Inspector?)")}");
                }
                else
                {
                    Debug.Log("[WeaponPickup] TryCachePromptUI — GameManager.Instance is NULL");
                }
            }
        }

        private void OnDestroy()
        {
            if (_promptUI != null) _promptUI.SetActive(false);
        }
    }
}
