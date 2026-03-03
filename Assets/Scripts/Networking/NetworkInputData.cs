using Fusion;

namespace Semester2
{
    /// <summary>
    /// Input struct polled by the NetworkRunner each tick via INetworkRunnerCallbacks.OnInput.
    /// Must implement INetworkInput so Fusion can serialize it across the network.
    /// </summary>
    public struct NetworkInputData : INetworkInput
    {
        /// <summary>True on the tick the player presses E to pick up an object.</summary>
        public NetworkBool PickupPressed;
    }
}
