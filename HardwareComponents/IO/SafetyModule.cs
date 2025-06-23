using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.IO
{
	/// <summary>
	/// Safety module for critical safety functions and emergency stopping
	/// </summary>
	public class SafetyModule : DeviceBase
	{
		public override DeviceType Type => DeviceType.IOModule;

		public int InputCount { get; private set; }
		public int OutputCount { get; private set; }
		public SafetyIntegrityLevel SIL { get; private set; }
		public bool[] SafetyInputs { get; private set; }
		public bool[] SafetyOutputs { get; private set; }

		// Internal state and configuration
		private SafetyModuleState _moduleState;
		private bool _requiresReset;
		private double _responseTime; // in milliseconds
		private bool _hasDiagnosticCapabilities;
		private SafetyModuleArchitecture _architecture;

		// For internal simulation
		private bool[] _faultDetected;
		private bool[] _inputDiscrepancy; // For dual-channel inputs
		private bool[] _forceEnabled;
		private bool[] _forcedInputState;
		private bool _masterFaultPresent;
		private DateTime _lastDiagnosticTest;
		private TimeSpan _diagnosticInterval;

		/// <summary>
		/// Event raised when safety state changes
		/// </summary>
		public event EventHandler<SafetyStateChangeEventArgs> SafetyStateChanged;

		public SafetyModule(
			string deviceId,
			string name,
			int inputCount = 8,
			int outputCount = 4,
			SafetyIntegrityLevel sil = SafetyIntegrityLevel.SIL3,
			SafetyModuleArchitecture architecture = SafetyModuleArchitecture.OneOutOfTwo,
			bool requiresReset = true)
			: base(deviceId, name)
		{
			InputCount = inputCount;
			OutputCount = outputCount;
			SIL = sil;
			_architecture = architecture;
			_requiresReset = requiresReset;

			// Set default parameters based on SIL level
			switch (SIL)
			{
				case SafetyIntegrityLevel.SIL1:
					_responseTime = 20.0;
					_hasDiagnosticCapabilities = false;
					_diagnosticInterval = TimeSpan.FromHours(24);
					break;
				case SafetyIntegrityLevel.SIL2:
					_responseTime = 10.0;
					_hasDiagnosticCapabilities = true;
					_diagnosticInterval = TimeSpan.FromHours(12);
					break;
				case SafetyIntegrityLevel.SIL3:
					_responseTime = 5.0;
					_hasDiagnosticCapabilities = true;
					_diagnosticInterval = TimeSpan.FromHours(8);
					break;
				case SafetyIntegrityLevel.SIL4:
					_responseTime = 1.0;
					_hasDiagnosticCapabilities = true;
					_diagnosticInterval = TimeSpan.FromHours(4);
					break;
			}

			// Initialize arrays
			SafetyInputs = new bool[InputCount];
			SafetyOutputs = new bool[OutputCount];
			_faultDetected = new bool[InputCount];
			_inputDiscrepancy = new bool[InputCount];
			_forceEnabled = new bool[InputCount];
			_forcedInputState = new bool[InputCount];

			// Initial state
			_moduleState = SafetyModuleState.NotReady;
			_masterFaultPresent = false;
			_lastDiagnosticTest = DateTime.Now;

			// Add diagnostic data
			DiagnosticData["InputCount"] = InputCount;
			DiagnosticData["OutputCount"] = OutputCount;
			DiagnosticData["SIL"] = SIL.ToString();
			DiagnosticData["Architecture"] = _architecture.ToString();
			DiagnosticData["RequiresReset"] = _requiresReset;
			DiagnosticData["ResponseTime"] = _responseTime;
			DiagnosticData["HasDiagnostics"] = _hasDiagnosticCapabilities;
			DiagnosticData["ModuleState"] = _moduleState.ToString();
		}

		public override void Initialize()
		{
			base.Initialize();

			// Safety modules start in a safe state with all outputs off
			for (int i = 0; i < OutputCount; i++)
			{
				SafetyOutputs[i] = false;
			}

			// Reset all fault and force flags
			for (int i = 0; i < InputCount; i++)
			{
				_faultDetected[i] = false;
				_inputDiscrepancy[i] = false;
				_forceEnabled[i] = false;
			}

			_moduleState = SafetyModuleState.ReadyToRun;
			_masterFaultPresent = false;

			// Update state in diagnostics
			DiagnosticData["ModuleState"] = _moduleState.ToString();

			// If the module has diagnostic capabilities, perform initial diagnostic test
			if (_hasDiagnosticCapabilities)
			{
				PerformDiagnosticTest();
			}
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Check if it's time for a diagnostic test
			if (_hasDiagnosticCapabilities && (DateTime.Now - _lastDiagnosticTest) > _diagnosticInterval)
			{
				PerformDiagnosticTest();
			}

			// Process safety logic based on module state
			switch (_moduleState)
			{
				case SafetyModuleState.Running:
					ProcessSafetyLogic();
					break;

				case SafetyModuleState.Faulted:
					// In faulted state, all outputs are driven to the safe state (off)
					for (int i = 0; i < OutputCount; i++)
					{
						SafetyOutputs[i] = false;
					}
					break;

				case SafetyModuleState.AwaitingReset:
					// Waiting for reset signal, outputs remain in safe state
					for (int i = 0; i < OutputCount; i++)
					{
						SafetyOutputs[i] = false;
					}
					break;

				case SafetyModuleState.ReadyToRun:
					// Ready to run, but outputs remain in safe state until explicitly started
					break;
			}

			// Update diagnostic data
			DiagnosticData["ModuleState"] = _moduleState.ToString();
			DiagnosticData["MasterFault"] = _masterFaultPresent;

			// Simulated response time
			DiagnosticData["ActualResponseTime"] = _responseTime * (0.9 + Random.NextDouble() * 0.2);
		}

		/// <summary>
		/// Process the safety logic based on inputs
		/// </summary>
		private void ProcessSafetyLogic()
		{
			// Safety logic is simple in this simulation:
			// Any safety input that is 'false' (indicating a safety condition)
			// will cause all outputs to go to their safe state (off)

			bool safetyCondition = true; // Assume safe unless proven otherwise

			for (int i = 0; i < InputCount; i++)
			{
				bool actualInput;

				// Apply input forcing if enabled
				if (_forceEnabled[i])
				{
					actualInput = _forcedInputState[i];
				}
				else
				{
					actualInput = SafetyInputs[i];
				}

				// For dual-channel architecture, check for discrepancies
				if (_architecture == SafetyModuleArchitecture.OneOutOfTwo && i % 2 == 0 && i + 1 < InputCount)
				{
					bool pairInput;

					if (_forceEnabled[i + 1])
					{
						pairInput = _forcedInputState[i + 1];
					}
					else
					{
						pairInput = SafetyInputs[i + 1];
					}

					// Check for discrepancy between paired inputs
					if (actualInput != pairInput)
					{
						_inputDiscrepancy[i] = true;
						AddAlarm($"INPUT_DISCREPANCY_{i}", $"Discrepancy detected on dual-channel input {i}/{i + 1}", AlarmSeverity.Major);

						// In 1oo2 architecture, both channels must be safe to be considered safe
						// So if either is unsafe, the safety condition becomes false
						if (!actualInput || !pairInput)
						{
							safetyCondition = false;
						}
					}
					else
					{
						_inputDiscrepancy[i] = false;

						// Both channels agree, use the common value
						if (!actualInput) // If the input is false (safety condition)
						{
							safetyCondition = false;
						}
					}

					// Skip the second channel in the pair since we've already processed it
					i++;
				}
				else
				{
					// Standard single-channel logic
					if (!actualInput) // If the input is false (safety condition)
					{
						safetyCondition = false;
					}
				}

				// Check for detected faults
				if (_faultDetected[i])
				{
					// In a real safety system, a fault might trigger a safe state
					// depending on the fault reaction configuration
					AddAlarm($"INPUT_FAULT_{i}", $"Fault detected on safety input {i}", AlarmSeverity.Major);
					safetyCondition = false;
				}
			}

			// If master fault is present, safety condition is always false
			if (_masterFaultPresent)
			{
				safetyCondition = false;
			}

			// Apply safety condition to all outputs
			bool previousOutputState = SafetyOutputs[0];

			for (int i = 0; i < OutputCount; i++)
			{
				SafetyOutputs[i] = safetyCondition;
			}

			// Notify if safety state changed
			if (SafetyOutputs[0] != previousOutputState)
			{
				OnSafetyStateChanged(safetyCondition);

				if (!safetyCondition)
				{
					AddAlarm("SAFETY_TRIP", "Safety function activated", AlarmSeverity.Major);

					// If reset is required, transition to awaiting reset state
					if (_requiresReset)
					{
						_moduleState = SafetyModuleState.AwaitingReset;
					}
				}
			}
		}

		/// <summary>
		/// Perform diagnostic test of the safety module
		/// </summary>
		private void PerformDiagnosticTest()
		{
			_lastDiagnosticTest = DateTime.Now;

			// Log diagnostic test execution
			DiagnosticData["LastDiagnosticTest"] = _lastDiagnosticTest;
			AddAlarm("DIAGNOSTIC_TEST", "Safety module diagnostic test running", AlarmSeverity.Information);

			// Simulate diagnostic test result (rarely fails)
			if (Random.NextDouble() < 0.005) // 0.5% failure rate
			{
				_masterFaultPresent = true;
				_moduleState = SafetyModuleState.Faulted;
				AddAlarm("DIAGNOSTIC_FAILURE", "Safety module diagnostic test failed", AlarmSeverity.Critical);
			}
			else
			{
				AddAlarm("DIAGNOSTIC_PASSED", "Safety module diagnostic test passed", AlarmSeverity.Information);
			}
		}

		/// <summary>
		/// Set a safety input state
		/// </summary>
		public void SetInput(int channel, bool value)
		{
			if (channel >= 0 && channel < InputCount)
			{
				SafetyInputs[channel] = value;
				DiagnosticData[$"Input_{channel}"] = value;
			}
		}

		/// <summary>
		/// Get a safety output state
		/// </summary>
		public bool GetOutput(int channel)
		{
			if (channel >= 0 && channel < OutputCount)
			{
				return SafetyOutputs[channel];
			}
			return false; // Default to safe state
		}

		/// <summary>
		/// Reset the safety module after a trip
		/// </summary>
		/// <returns>True if reset was successful</returns>
		public bool Reset()
		{
			if (_moduleState == SafetyModuleState.AwaitingReset || _moduleState == SafetyModuleState.Faulted)
			{
				// Check if there's a master fault - can't reset in that case
				if (_masterFaultPresent)
				{
					AddAlarm("RESET_FAILED", "Cannot reset safety module due to internal fault", AlarmSeverity.Major);
					return false;
				}

				// Check if any safety inputs are still in the safety condition state
				for (int i = 0; i < InputCount; i++)
				{
					if (!SafetyInputs[i])
					{
						AddAlarm("RESET_FAILED", "Cannot reset safety module while safety condition active", AlarmSeverity.Warning);
						return false;
					}
				}

				// Clear any non-critical faults
				for (int i = 0; i < InputCount; i++)
				{
					_faultDetected[i] = false;
					_inputDiscrepancy[i] = false;
				}

				_moduleState = SafetyModuleState.ReadyToRun;
				DiagnosticData["ModuleState"] = _moduleState.ToString();
				AddAlarm("MODULE_RESET", "Safety module successfully reset", AlarmSeverity.Information);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Start safety operations after initialization or reset
		/// </summary>
		public bool Start()
		{
			if (_moduleState == SafetyModuleState.ReadyToRun)
			{
				_moduleState = SafetyModuleState.Running;
				DiagnosticData["ModuleState"] = _moduleState.ToString();
				AddAlarm("MODULE_STARTED", "Safety module started normal operation", AlarmSeverity.Information);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Force an input to a specific state (for testing and commissioning)
		/// </summary>
		public bool ForceInput(int channel, bool value)
		{
			if (channel >= 0 && channel < InputCount)
			{
				// Force enable this input
				_forceEnabled[channel] = true;
				_forcedInputState[channel] = value;
				DiagnosticData[$"Force_{channel}"] = true;
				DiagnosticData[$"ForceValue_{channel}"] = value;
				AddAlarm($"INPUT_FORCED_{channel}", $"Safety input {channel} forced to {value}", AlarmSeverity.Warning);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Remove forcing from an input
		/// </summary>
		public bool UnforceInput(int channel)
		{
			if (channel >= 0 && channel < InputCount && _forceEnabled[channel])
			{
				_forceEnabled[channel] = false;
				DiagnosticData[$"Force_{channel}"] = false;
				AddAlarm($"FORCE_REMOVED_{channel}", $"Force removed from safety input {channel}", AlarmSeverity.Information);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Simulate an input fault
		/// </summary>
		public void SimulateInputFault(int channel, bool hasFault)
		{
			if (channel >= 0 && channel < InputCount)
			{
				_faultDetected[channel] = hasFault;

				if (hasFault)
				{
					AddAlarm($"INPUT_FAULT_{channel}", $"Fault detected on safety input {channel}", AlarmSeverity.Major);
				}
			}
		}

		protected virtual void OnSafetyStateChanged(bool safeState)
		{
			SafetyStateChanged?.Invoke(this, new SafetyStateChangeEventArgs(safeState));
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Module internal fault
					_masterFaultPresent = true;
					_moduleState = SafetyModuleState.Faulted;
					AddAlarm("INTERNAL_FAULT", "Safety module internal fault detected", AlarmSeverity.Critical);
					Status = DeviceStatus.Fault;
					break;

				case 1: // Channel fault
					int channel = Random.Next(InputCount);
					SimulateInputFault(channel, true);
					break;

				case 2: // Communication fault
					AddAlarm("COMM_FAULT", "Safety communication fault detected", AlarmSeverity.Major);
					// In a safety system, communication faults typically result in safe state
					_moduleState = SafetyModuleState.Faulted;
					break;
			}
		}
	}

	public enum SafetyIntegrityLevel
	{
		SIL1,
		SIL2,
		SIL3,
		SIL4
	}

	public enum SafetyModuleState
	{
		NotReady,       // Initial state before initialization
		ReadyToRun,     // Initialized but not yet running
		Running,        // Normal operation
		AwaitingReset,  // Safety condition occurred, waiting for reset
		Faulted         // Internal fault detected
	}

	public enum SafetyModuleArchitecture
	{
		Single,         // Single channel
		OneOutOfTwo,    // Redundant 1oo2 (one-out-of-two) voting
		TwoOutOfThree   // 2oo3 (two-out-of-three) voting
	}

	public class SafetyStateChangeEventArgs : EventArgs
	{
		public bool SafeState { get; private set; }

		public SafetyStateChangeEventArgs(bool safeState)
		{
			SafeState = safeState;
		}
	}
}