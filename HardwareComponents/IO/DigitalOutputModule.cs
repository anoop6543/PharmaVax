using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaVax.HardwareComponents.IO
{
	/// <summary>
	/// Digital output module that handles multiple digital output points
	/// </summary>
	public class DigitalOutputModule : DeviceBase
	{
		public override DeviceType Type => DeviceType.IOModule;

		public int ChannelCount { get; private set; }
		public double SwitchingTime { get; private set; } // in milliseconds
		public bool HasDiagnostics { get; private set; }
		public double CurrentRating { get; private set; } // in amperes
		public bool[] Outputs { get; private set; }

		// Output diagnostics (if supported)
		private bool[] _outputShortCircuit;
		private bool[] _outputOpenLoad;

		/// <summary>
		/// Event raised when an output state is changed by the module
		/// </summary>
		public event EventHandler<OutputChangeEventArgs> OutputChanged;

		public DigitalOutputModule(
			string deviceId,
			string name,
			int channelCount = 16,
			double switchingTime = 0.5,
			double currentRating = 0.5,
			bool hasDiagnostics = false)
			: base(deviceId, name)
		{
			ChannelCount = channelCount;
			SwitchingTime = switchingTime;
			CurrentRating = currentRating;
			HasDiagnostics = hasDiagnostics;

			// Initialize arrays
			Outputs = new bool[ChannelCount];
			_outputShortCircuit = new bool[ChannelCount];
			_outputOpenLoad = new bool[ChannelCount];

			// Add diagnostic data
			DiagnosticData["ChannelCount"] = ChannelCount;
			DiagnosticData["SwitchingTime"] = SwitchingTime;
			DiagnosticData["CurrentRating"] = CurrentRating;
			DiagnosticData["HasDiagnostics"] = HasDiagnostics;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize outputs to false
			for (int i = 0; i < ChannelCount; i++)
			{
				Outputs[i] = false;
				_outputShortCircuit[i] = false;
				_outputOpenLoad[i] = false;
			}
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Check for any output overloads or faults (for diagnostic-capable modules)
			if (HasDiagnostics)
			{
				for (int i = 0; i < ChannelCount; i++)
				{
					// If output is on, check for short circuit
					if (Outputs[i] && _outputShortCircuit[i])
					{
						AddAlarm($"OUTPUT_SHORT_{i}", $"Output {i} short circuit detected", AlarmSeverity.Minor);
					}

					// If output is on, check for open load (only if diagnostics supported)
					if (Outputs[i] && _outputOpenLoad[i])
					{
						AddAlarm($"OUTPUT_OPEN_{i}", $"Output {i} open load detected", AlarmSeverity.Warning);
					}
				}
			}

			// Add simulated switching time delay (no actual delay, just for modeling)
			DiagnosticData["SwitchingDelay"] = SwitchingTime * (0.9 + Random.NextDouble() * 0.2);
		}

		/// <summary>
		/// Set output value
		/// </summary>
		public void SetOutput(int channel, bool value)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				if (Outputs[channel] != value)
				{
					// Short circuit protection - if short-circuit detected, don't turn on
					if (value && _outputShortCircuit[channel])
					{
						if (HasDiagnostics)
						{
							AddAlarm($"OUTPUT_SHORT_{channel}", $"Cannot turn on output {channel} - short circuit", AlarmSeverity.Minor);
						}
						return;
					}

					// Update output
					Outputs[channel] = value;

					// For diagnostic-capable modules, update our diagnostics
					if (HasDiagnostics)
					{
						DiagnosticData[$"Output_{channel}"] = value;

						// If turning on, might detect open load
						if (value && _outputOpenLoad[channel])
						{
							AddAlarm($"OUTPUT_OPEN_{channel}", $"Output {channel} open load detected", AlarmSeverity.Warning);
						}
					}

					// Notify subscribers
					OnOutputChanged(channel, value);
				}
			}
		}

		/// <summary>
		/// Get output value
		/// </summary>
		public bool GetOutput(int channel)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				return Outputs[channel];
			}
			return false;
		}

		/// <summary>
		/// Simulate a short circuit on an output channel (for testing fault handling)
		/// </summary>
		public void SimulateShortCircuit(int channel, bool isShorted)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				_outputShortCircuit[channel] = isShorted;

				// If output is currently on and we have a short, generate alarm
				if (isShorted && Outputs[channel] && HasDiagnostics)
				{
					AddAlarm($"OUTPUT_SHORT_{channel}", $"Output {channel} short circuit detected", AlarmSeverity.Minor);
				}
			}
		}

		/// <summary>
		/// Simulate an open load on an output channel (for testing fault handling)
		/// </summary>
		public void SimulateOpenLoad(int channel, bool isOpen)
		{
			if (channel >= 0 && channel < ChannelCount && HasDiagnostics)
			{
				_outputOpenLoad[channel] = isOpen;

				// If output is currently on and we have an open load, generate alarm
				if (isOpen && Outputs[channel])
				{
					AddAlarm($"OUTPUT_OPEN_{channel}", $"Output {channel} open load detected", AlarmSeverity.Warning);
				}
			}
		}

		protected virtual void OnOutputChanged(int channel, bool value)
		{
			OutputChanged?.Invoke(this, new OutputChangeEventArgs(channel, value));
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Module communication failure
					AddAlarm("MODULE_COMM_ERROR", "Module communication failure", AlarmSeverity.Major);
					Status = DeviceStatus.Fault;
					break;

				case 1: // Output stuck
					int channel = Random.Next(ChannelCount);
					// Output stuck at current state
					AddAlarm($"OUTPUT_STUCK_{channel}", $"Output channel {channel} stuck at {Outputs[channel]}", AlarmSeverity.Minor);
					break;

				case 2: // Overheating
					AddAlarm("MODULE_OVERTEMP", "Module temperature too high", AlarmSeverity.Warning);
					break;
			}
		}
	}

	public class OutputChangeEventArgs : EventArgs
	{
		public int Channel { get; private set; }
		public bool Value { get; private set; }

		public OutputChangeEventArgs(int channel, bool value)
		{
			Channel = channel;
			Value = value;
		}
	}
}