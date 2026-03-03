using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Semester2
{
    /// <summary>
    /// Manages the Fusion session lifecycle (start as Host or Client) and
    /// handles spawning the pickup object when players join.
    /// Attach this to the GameBootstrap GameObject in the CA2_NetworkTest scene.
    /// Also requires a NetworkRunner component on the same GameObject.
    /// </summary>
    public class GameBootstrap : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Fusion")]
        [SerializeField] private NetworkRunner _runner;

        [Header("Spawn")]
        [SerializeField] private NetworkObject _pickupPrefab;
        [SerializeField] private Transform _pickupSpawnPoint;

        [Header("UI")]
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _clientButton;
        [SerializeField] private TMP_Text _statusText;

        private bool _pickupSpawned;

        private void Awake()
        {
            if (_runner == null)
                _runner = GetComponent<NetworkRunner>();

            _hostButton.onClick.AddListener(StartHost);
            _clientButton.onClick.AddListener(StartClient);
        }

        /// <summary>Start a Fusion session in Host mode (this client is host + player).</summary>
        public async void StartHost()
        {
            SetStatus("Starting host...");
            SetButtonsInteractable(false);

            var args = new StartGameArgs
            {
                GameMode   = GameMode.Host,
                SessionName = "CA2Session",
                Scene      = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            var result = await _runner.StartGame(args);

            if (result.Ok)
                SetStatus("Hosting — waiting for client...");
            else
                SetStatus($"Host failed: {result.ShutdownReason}");
        }

        /// <summary>Join an existing Fusion session as a client.</summary>
        public async void StartClient()
        {
            SetStatus("Connecting as client...");
            SetButtonsInteractable(false);

            var args = new StartGameArgs
            {
                GameMode   = GameMode.Client,
                SessionName = "CA2Session",
                Scene      = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            var result = await _runner.StartGame(args);

            if (result.Ok)
                SetStatus("Connected as client");
            else
                SetStatus($"Connect failed: {result.ShutdownReason}");
        }

        // --- INetworkRunnerCallbacks ---

        /// <summary>
        /// Called by Fusion each tick to collect local input.
        /// Only runs on the machine that owns InputAuthority.
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
        /// Called on the Host when any player (including the host itself) joins.
        /// We spawn the pickup here and assign InputAuthority to the joining player.
        /// Only runs on the host — Fusion will replicate the spawned object to all clients.
        /// </summary>
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Only spawn the pickup once (when the first non-host player joins),
            // and only from the host (HasStateAuthority over spawned objects).
            if (runner.IsServer && !_pickupSpawned)
            {
                Vector3 spawnPos = _pickupSpawnPoint != null
                    ? _pickupSpawnPoint.position
                    : Vector3.zero;

                // No InputAuthority assigned — any client can request pickup via RPC.
                runner.Spawn(_pickupPrefab, spawnPos, Quaternion.identity);
                _pickupSpawned = true;

                Debug.Log("[GameBootstrap] <color=cyan>Pickup spawned for player: </color>" + player);
            }

            SetStatus(runner.IsServer ? "Player joined — pickup active" : "Joined session");
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log("[GameBootstrap] Player left: " + player);
            SetStatus("A player disconnected");
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log("[GameBootstrap] Session shut down: " + shutdownReason);
            SetStatus("Session ended: " + shutdownReason);
            SetButtonsInteractable(true);
            _pickupSpawned = false;
        }

        public void OnConnectedToServer(NetworkRunner runner)          { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner)               { }
        public void OnSceneLoadStart(NetworkRunner runner)              { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

        // --- Helpers ---

        private void SetStatus(string msg)
        {
            if (_statusText != null)
                _statusText.text = msg;
        }

        private void SetButtonsInteractable(bool state)
        {
            _hostButton.interactable  = state;
            _clientButton.interactable = state;
        }
    }
}
