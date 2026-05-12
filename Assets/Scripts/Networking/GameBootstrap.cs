using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using Fusion.Photon.Realtime;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Semester2
{
    /// <summary>
    /// Manages the Fusion session lifecycle with a lobby room list.
    ///
    /// Authentication flow (CA3 requirement):
    ///   1. UnityServices.InitializeAsync() — sets up the Unity Gaming Services SDK.
    ///   2. AuthenticationService.Instance.SignInAnonymouslyAsync() — obtains a session token.
    ///   3. The access token is sent to the Azure authentication proxy via Fusion 2
    ///      Custom Authorization. The proxy validates it against the Unity Auth service
    ///      and returns allow/deny. Unauthenticated clients are refused at the Fusion level,
    ///      not merely hidden behind a UI gate.
    ///
    /// All clients use GameMode.Shared (CA3 requirement — replaces Host/Client from CA2).
    /// In Shared mode there is no single authoritative host; StateAuthority is distributed
    /// per-object, which requires all NetworkObjects to declare their own authority strategy.
    /// </summary>
    public class GameBootstrap : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Fusion")]
        [SerializeField] private NetworkRunner _runner;

        [Header("Spawn")]
        [SerializeField] private NetworkObject[] _pickupPrefabs;
        [SerializeField] private Transform[]     _pickupSpawnPoints;

        [Header("Player")]
        [SerializeField] private GameObject  _playerPrefab;
        [SerializeField] private Transform[] _playerSpawnPoints;

        [Header("Weapon")]
        [SerializeField] private NetworkObject _weaponPrefab;
        [SerializeField] private Transform[]   _weaponSpawnPoints;

        [Header("Lobby UI")]
        [SerializeField] private GameObject     _lobbyPanel;
        [SerializeField] private GameObject     _gamePanel;
        [SerializeField] private TMP_InputField _roomNameInput;
        [SerializeField] private Button         _createRoomButton;
        [SerializeField] private Transform      _roomListContainer;
        [SerializeField] private GameObject     _roomEntryPrefab;

        [Header("Shared")]
        [SerializeField] private TMP_Text    _statusText;

        [Header("Game UI (hidden until session joined)")]
        [SerializeField] private GameObject[] _gameUIElements;
        [SerializeField] private Camera       _lobbyCamera;

        private bool          _pickupSpawned;
        private bool          _weaponSpawned;
        private bool          _authenticated;
        private bool          _sessionActive;
        private int           _spawnIndex;
        private NetworkObject _localPlayerObject;

        private void Awake()
        {
            if (_runner == null)
                _runner = GetComponent<NetworkRunner>();

            _createRoomButton.onClick.AddListener(CreateRoom);
            _createRoomButton.interactable = false;

            SetLobbyCursor();

            // Hide all in-game UI until a session is joined
            if (_gameUIElements != null)
                foreach (GameObject ui in _gameUIElements)
                    if (ui != null) ui.SetActive(false);
        }

        private async void Start()
        {
            //await SignInAsync();
            JoinLobby();
        }

        // ── Unity Authentication ──────────────────────────────────────────────

        /// <summary>
        /// Initialises Unity Gaming Services and signs in anonymously.
        /// Anonymous sign-in is the minimum required by the CA3 spec — a named
        /// identity provider is not required. The resulting access token is used
        /// by the Azure authentication proxy to verify the client before Fusion
        /// allows the session connection.
        ///
        /// Why server-side validation matters: if the token were validated on the
        /// client, a malicious client could bypass the check by modifying local
        /// code. The Azure proxy receives the token, calls the Unity Auth service
        /// to confirm it is valid and unexpired, then tells Fusion to allow or
        /// refuse the connection — the client never controls this decision.
        /// </summary>
        private async Task SignInAsync()
        {
            SetStatus("Signing in...");
            try
            {
                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                _authenticated = true;
                string playerId = AuthenticationService.Instance.PlayerId;
                Debug.Log($"[GameBootstrap] <color=green>Signed in. PlayerID: {playerId}</color>");
                SetStatus("Signed in — connecting to lobby...");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameBootstrap] Sign-in failed: {e.Message}");
                SetStatus($"Sign-in failed: {e.Message}");
                // Allow proceeding without auth so the session can still be tested
                // locally, but Fusion Custom Authorization will refuse the connection
                // once the Azure proxy is configured.
            }
        }

        // ── Lobby ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Connect to the Photon lobby so we can see available sessions.
        /// Does NOT start a game session — just listens for the room list.
        /// </summary>
        private async void JoinLobby()
        {
            SetStatus("Connecting to lobby...");
            _createRoomButton.interactable = false;

            _runner.ProvideInput = true;
            await _runner.JoinSessionLobby(SessionLobby.ClientServer);

            SetStatus("Ready — create a room or join one below");
            _createRoomButton.interactable = true;
        }

        // ── Room creation / joining ────────────────────────────────────────────

        /// <summary>
        /// Called by the Create Room button. Uses the input field text as session name,
        /// or generates a random name if left blank.
        ///
        /// Uses GameMode.Shared (CA3 requirement). In Shared mode all peers call
        /// StartGame with the same GameMode — Fusion creates the session if it doesn't
        /// exist, or joins it if it does. There is no separate Host vs Client path.
        /// </summary>
        public async void CreateRoom()
        {
            string name = string.IsNullOrWhiteSpace(_roomNameInput.text)
                ? "Room_" + Random.Range(1000, 9999)
                : _roomNameInput.text.Trim();

            SetStatus($"Creating \"{name}\"...");
            _createRoomButton.interactable = false;

            var args = new StartGameArgs
            {
                GameMode     = GameMode.Shared,
                SessionName  = name,
                Scene        = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
                AuthValues   = BuildAuthValues()
            };

            var result = await _runner.StartGame(args);

            if (result.Ok)
                ShowGamePanel($"Session: {name}");
            else
            {
                SetStatus($"Create failed: {result.ShutdownReason}");
                _createRoomButton.interactable = true;
            }
        }

        /// <summary>
        /// Called by a RoomEntryUI Join button. Joins an existing Shared-mode session.
        /// In Shared mode the join path is identical to create — same GameMode.Shared args.
        /// </summary>
        public async void JoinRoom(string sessionName)
        {
            SetStatus($"Joining \"{sessionName}\"...");
            _createRoomButton.interactable = false;

            var args = new StartGameArgs
            {
                GameMode     = GameMode.Shared,
                SessionName  = sessionName,
                Scene        = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
                AuthValues   = BuildAuthValues()
            };

            var result = await _runner.StartGame(args);

            if (result.Ok)
                ShowGamePanel($"Joined: {sessionName}");
            else
            {
                SetStatus($"Join failed: {result.ShutdownReason}");
                _createRoomButton.interactable = true;
            }
        }

        // ── INetworkRunnerCallbacks ────────────────────────────────────────────

        /// <summary>
        /// Fired by Fusion whenever the list of available sessions changes.
        /// Rebuilds the room list UI from scratch each time.
        /// </summary>
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            foreach (Transform child in _roomListContainer)
                Destroy(child.gameObject);

            if (sessionList.Count == 0)
            {
                SetStatus("No rooms found — create one!");
                return;
            }

            foreach (SessionInfo session in sessionList)
            {
                GameObject entry = Instantiate(_roomEntryPrefab, _roomListContainer);
                string     name  = session.Name;

                entry.GetComponent<RoomEntryUI>().Setup(
                    name,
                    session.PlayerCount,
                    session.MaxPlayers,
                    () => JoinRoom(name));
            }

            SetStatus($"{sessionList.Count} room(s) available");
        }

        /// <summary>
        /// Called by Fusion each tick to collect local input.
        /// </summary>
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new NetworkInputData
            {
                PickupPressed = Input.GetKey(KeyCode.E)
            };
            input.Set(data);
        }

        /// <summary>
        /// Called on the host when any player joins. Spawns the pickup once.
        /// </summary>
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[GameBootstrap] OnPlayerJoined — player={player} localPlayer={runner.LocalPlayer} prefabNull={_playerPrefab == null}");

            // Each client spawns only their own player (runner.LocalPlayer == player).
            // InputAuthority is assigned so only this client can control their character.
            if (runner.LocalPlayer == player && _playerPrefab != null)
            {
                int     idx      = _spawnIndex % Mathf.Max(1, _playerSpawnPoints?.Length ?? 0);
                Vector3 spawnPos = (_playerSpawnPoints != null && _playerSpawnPoints.Length > 0 && _playerSpawnPoints[idx] != null)
                    ? _playerSpawnPoints[idx].position
                    : Vector3.zero;
                _spawnIndex++;

                Debug.Log($"[GameBootstrap] Spawning player at {spawnPos} (spawnPoints count={_playerSpawnPoints?.Length ?? 0})");

                var no = _playerPrefab.GetComponent<NetworkObject>();
                if (no == null) { Debug.LogError("[GameBootstrap] Player prefab has no NetworkObject on root!"); return; }

                _localPlayerObject = runner.Spawn(no, spawnPos, Quaternion.identity, player);
                Debug.Log($"[GameBootstrap] <color=cyan>Local player spawned: {_localPlayerObject != null}</color>");
            }

            // Master client spawns scene pickups once
            if (runner.IsSharedModeMasterClient && !_pickupSpawned)
            {
                for (int i = 0; i < _pickupPrefabs.Length; i++)
                {
                    Vector3 spawnPos = (i < _pickupSpawnPoints.Length && _pickupSpawnPoints[i] != null)
                        ? _pickupSpawnPoints[i].position
                        : Vector3.zero;

                    runner.Spawn(_pickupPrefabs[i], spawnPos, Quaternion.identity);
                }

                _pickupSpawned = true;
                Debug.Log("[GameBootstrap] <color=cyan>Pickups spawned: " + _pickupPrefabs.Length + "</color>");
            }

            // Spawn the weapon at a random spawn point — master client only, once per session
            if (runner.IsSharedModeMasterClient && !_weaponSpawned && _weaponPrefab != null)
            {
                Vector3 weaponPos = Vector3.zero;
                if (_weaponSpawnPoints != null && _weaponSpawnPoints.Length > 0)
                {
                    Transform chosen = _weaponSpawnPoints[Random.Range(0, _weaponSpawnPoints.Length)];
                    if (chosen != null) weaponPos = chosen.position;
                }
                runner.Spawn(_weaponPrefab, weaponPos, Quaternion.identity);
                _weaponSpawned = true;
                Debug.Log($"[GameBootstrap] <color=cyan>Weapon spawned at {weaponPos}</color>");
            }

            SetStatus(runner.IsSharedModeMasterClient ? "Player joined — pickup active" : "Joined session");
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log("[GameBootstrap] Player left: " + player);
            SetStatus("A player disconnected");
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log("[GameBootstrap] Session ended: " + shutdownReason);
            SetStatus("Session ended: " + shutdownReason);
            _pickupSpawned = false;
            _weaponSpawned = false;
            _spawnIndex    = 0;

            _gamePanel.SetActive(false);
            _lobbyPanel.SetActive(true);
            _createRoomButton.interactable = true;

            // Hide game UI and restore cursor for lobby
            if (_gameUIElements != null)
                foreach (GameObject ui in _gameUIElements)
                    if (ui != null) ui.SetActive(false);

            _sessionActive = false;
            SetLobbyCursor();

            if (_lobbyCamera != null)
                _lobbyCamera.gameObject.SetActive(true);
        }

        public void OnConnectedToServer(NetworkRunner runner)                                                                      { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)                                      { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)          { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)                  { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)                                     { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)                           { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)                                    { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)                 { }
        public void OnSceneLoadDone(NetworkRunner runner)                                                                           { }
        public void OnSceneLoadStart(NetworkRunner runner)                                                                          { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)                                      { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)                                     { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)                                      { }

        // ── Authentication helpers ────────────────────────────────────────────

        /// <summary>
        /// Builds Photon AuthenticationValues containing the Unity Auth access token.
        /// Photon forwards these to the Azure proxy endpoint configured in the dashboard.
        /// The proxy validates the token server-side — the client cannot bypass this
        /// because the decision is made by the proxy, not by any code running locally.
        /// </summary>
        private AuthenticationValues BuildAuthValues()
        {
            var auth = new AuthenticationValues();
            auth.AuthType = CustomAuthenticationType.Custom;

            if (_authenticated && AuthenticationService.Instance.IsSignedIn)
            {
                string token = AuthenticationService.Instance.AccessToken;
                auth.AddAuthParameter("token", token);
                Debug.Log("[GameBootstrap] <color=cyan>Auth token attached to StartGameArgs.</color>");
            }
            else
            {
                // No valid token — the Azure proxy will reject this connection.
                // This is the expected behaviour for unauthenticated clients.
                Debug.LogWarning("[GameBootstrap] No auth token — connection will be refused by proxy.");
            }

            return auth;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ShowGamePanel(string status)
        {
            _lobbyPanel.SetActive(false);
            _gamePanel.SetActive(true);
            SetStatus(status);

            // Show in-game UI now that we're in a session
            if (_gameUIElements != null)
                foreach (GameObject ui in _gameUIElements)
                    if (ui != null) ui.SetActive(true);

            // Disable lobby camera so the spawned player's camera takes over
            if (_lobbyCamera != null)
                _lobbyCamera.gameObject.SetActive(false);

            _sessionActive = true;
            SetGameplayCursor();
        }

        /// <summary>
        /// Despawns the local player and respawns them at spawn point 0.
        /// Waits one frame between despawn and spawn so Fusion can process the
        /// despawn before the new object is created — spawning on the same frame
        /// as despawn causes Fusion to misplace the new object.
        /// </summary>
        public void RespawnLocalPlayer()
        {
            if (_runner == null || !_runner.IsRunning) return;
            StartCoroutine(DoRespawn());
        }

        private System.Collections.IEnumerator DoRespawn()
        {
            if (_localPlayerObject != null)
            {
                _runner.Despawn(_localPlayerObject);
                _localPlayerObject = null;
                yield return null; // let Fusion process the despawn before spawning
            }

            var prefabNo = _playerPrefab?.GetComponent<NetworkObject>();
            if (prefabNo == null) yield break;

            // Always respawn at index 0 — _spawnIndex was already incremented on first
            // join, so using it here would pick a different (potentially unassigned) slot.
            Vector3 spawnPos = (_playerSpawnPoints != null && _playerSpawnPoints.Length > 0 && _playerSpawnPoints[0] != null)
                ? _playerSpawnPoints[0].position
                : Vector3.zero;

            _localPlayerObject = _runner.Spawn(prefabNo, spawnPos, Quaternion.identity, _runner.LocalPlayer);
            SetGameplayCursor();
        }

        // StarterAssetsInputs.OnApplicationFocus locks the cursor whenever the window
        // gains focus (cursorLocked defaults to true). Re-assert the correct state here
        // so the lobby cursor stays visible even when the editor Game view is clicked.
        // Also force the lobby cursor while the game is over (Win/Defeat) so the player
        // can actually click the restart button — otherwise this method snatches the
        // cursor back from GameManager.ShowCursor on every focus event.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;

            // Default to NOT locking the cursor when GameManager isn't available — better
            // a stuck-visible cursor than a stuck-locked one that traps the dead player.
            bool gameOver = GameManager.Instance == null
                            || GameManager.Instance.State == GameState.Win
                            || GameManager.Instance.State == GameState.Defeat;

            if (_sessionActive && !gameOver)
                SetGameplayCursor();
            else
                SetLobbyCursor();
        }

        private void SetLobbyCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        private void SetGameplayCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        private void SetStatus(string msg)
        {
            if (_statusText != null)
                _statusText.text = msg;
        }
    }
}
