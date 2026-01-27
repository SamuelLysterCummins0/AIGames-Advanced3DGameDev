using System;
using System.Collections.Generic;
using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Interface that all states must implement.
    /// Defines the lifecycle methods for a state in the FSM.
    /// </summary>
    public interface IState
    {
        /// <summary>
        /// Called once when transitioning INTO this state.
        /// Use this to initialize state-specific variables or start animations.
        /// </summary>
        void OnEnter();

        /// <summary>
        /// Called every frame while this state is active.
        /// Use this for ongoing state behavior and logic.
        /// </summary>
        void OnUpdate();

        /// <summary>
        /// Called once when transitioning OUT OF this state.
        /// Use this to clean up or reset state-specific variables.
        /// </summary>
        void OnExit();

        /// <summary>
        /// Provides the state with a reference to its FSM.
        /// This allows states to trigger their own transitions.
        /// </summary>
        /// <param name="fsm">The FSM managing this state</param>
        void SetFSM(MinimalisticFSM fsm);
    }

    /// <summary>
    /// A minimalistic Finite State Machine (FSM) implementation.
    /// Manages state transitions and ensures only one state is active at a time.
    /// </summary>
    public class MinimalisticFSM
    {
        /// <summary>
        /// Singleton instance for global FSM usage (e.g., game flow states).
        /// For individual entities (NPCs, players), create separate instances using 'new MinimalisticFSM()'.
        /// </summary>
        public static MinimalisticFSM Instance { get; } = new MinimalisticFSM();

        /// <summary>
        /// The currently active state. Can be null if no state is set.
        /// </summary>
        private IState currentState;

        /// <summary>
        /// Dictionary storing all registered states, indexed by their Type.
        /// This allows fast lookup when changing states.
        /// </summary>
        private Dictionary<Type, IState> states = new Dictionary<Type, IState>();

        /// <summary>
        /// Public read-only access to the current state.
        /// </summary>
        public IState CurrentState => currentState;

        /// <summary>
        /// Registers a new state to the FSM.
        /// Each state type can only be added once.
        /// </summary>
        /// <typeparam name="T">The type of state to add (must implement IState)</typeparam>
        /// <param name="state">The state instance to register</param>
        public void AddState<T>(T state) where T : IState
        {
            Type stateType = typeof(T);
            if (!states.ContainsKey(stateType))
            {
                states.Add(stateType, state);
                // Give the state a reference to this FSM
                state.SetFSM(this);
            }
        }

        /// <summary>
        /// Removes a state from the FSM.
        /// Note: Cannot remove the currently active state.
        /// </summary>
        /// <typeparam name="T">The type of state to remove</typeparam>
        public void RemoveState<T>() where T : IState
        {
            Type stateType = typeof(T);
            if (states.ContainsKey(stateType))
            {
                states.Remove(stateType);
            }
        }

        /// <summary>
        /// Transitions from the current state to a new state.
        /// Automatically calls OnExit() on the old state and OnEnter() on the new state.
        /// </summary>
        /// <typeparam name="T">The type of state to transition to</typeparam>
        public void ChangeState<T>() where T : IState
        {
            Type stateType = typeof(T);
            
            // Check if the requested state exists in our dictionary
            if (!states.ContainsKey(stateType))
            {
                Debug.LogError($"State {stateType} not found in FSM!");
                return;
            }

            // Exit the current state (if one exists)
            currentState?.OnExit();

            // Switch to the new state
            currentState = states[stateType];

            // Enter the new state
            currentState?.OnEnter();
        }

        /// <summary>
        /// Updates the current state. Call this every frame (typically in Update()).
        /// </summary>
        public void Update()
        {
            currentState?.OnUpdate();
        }

        /// <summary>
        /// Checks if the FSM is currently in a specific state.
        /// </summary>
        /// <typeparam name="T">The state type to check</typeparam>
        /// <returns>True if currently in the specified state, false otherwise</returns>
        public bool IsInState<T>() where T : IState
        {
            return currentState != null && currentState.GetType() == typeof(T);
        }

        /// <summary>
        /// Clears all states and resets the FSM.
        /// Calls OnExit() on the current state before clearing.
        /// </summary>
        public void Clear()
        {
            currentState?.OnExit();
            currentState = null;
            states.Clear();
        }
    }
}
