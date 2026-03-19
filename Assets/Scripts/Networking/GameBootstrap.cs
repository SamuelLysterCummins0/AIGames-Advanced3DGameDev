using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Semester2
{
    /// <summary>
    /// Manages the Fusion session lifecycle with a lobby room list.
    /// On Start the runner joins the Photon lobby so available sessions are received
    /// via OnSessionListUpdated. Players can create a named room (host) or join one
    /// from the list (client). Attach to the GameBootstrap GameObject in CA2_NetworkTest.
    /// </summary>
    public class GameBootstrap : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Fusion")]
        [SerializeField] private NetworkRunner _runner;

        [Header("Spawn")]
        [SerializeField] private NetworkObject _pickupPrefab;
        [SerializeField] private Transform     _pickupSpawnPoint;

        [Header("Lobby UI")]
        [SerializeField] private GameObject     _lobbyPanel;
        [SerializeField] private GameObject     _gamePanel;
        [SerializeField] private TMP_InputField _roomNameInput;
        [SerializeField] private Button         _createRoomButton;
        [SerializeField] private Transform      _roomListContainer;
        [SerializeField] private GameObject     _roomEntryPrefab;

        [Header("Shared")]
        [SerializeField] private TMP_Text _statusText;

        private bool _pickupSpawned;

        private void Awake()
        {
            if (_runner == null)
                _runner = GetComponent<NetworkRunner>();

            _createRoomButton.onClick.AddListener(CreateRoom);
        }

        private void Start()
        {
            JoinLobby();
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
                GameMode     = GameMode.Host,
                SessionName  = name,
                Scene        = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            var result = await _runner.StartGame(args);

            if (result.Ok)
                ShowGamePanel($"Hosting: {name}");
            else
            {
                SetStatus($"Create failed: {result.ShutdownReason}");
                _createRoomButton.interactable = true;
            }
        }

        /// <summary>
        /// Called by a RoomEntryUI Join button. Joins an existing session as client.
        /// </summary>
        public async void JoinRoom(string sessionName)
        {
            SetStatus($"Joining \"{sessionName}\"...");
            _createRoomButton.interactable = false;

            var args = new StartGameArgs
            {
                GameMode     = GameMode.Client,
                SessionName  = sessionName,
                Scene        = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
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
            if (runner.IsServer && !_pickupSpawned)
            {
                Vector3 spawnPos = _pickupSpawnPoint != null
                    ? _pickupSpawnPoint.position
                    : Vector3.zero;

                runner.Spawn(_pickupPrefab, spawnPos, Quaternion.identity);
                _pickupSpawned = true;

                Debug.Log("[GameBootstrap] <color=cyan>Pickup spawned</color>");
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
            Debug.Log("[GameBootstrap] Session ended: " + shutdownReason);
            SetStatus("Session ended: " + shutdownReason);
            _pickupSpawned = false;

            _gamePanel.SetActive(false);
            _lobbyPanel.SetActive(true);
            _createRoomButton.interactable = true;
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

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ShowGamePanel(string status)
        {
            _lobbyPanel.SetActive(false);
            _gamePanel.SetActive(true);
            SetStatus(status);
        }

        private void SetStatus(string msg)
        {
            if (_statusText != null)
                _statusText.text = msg;
        }
    }
}
