using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaVax.HardwareComponents.IO
{
	/// <summary>
	/// Digital input module that handles multiple digital input points
	/// </summary>
	public class DigitalInputModule : DeviceBase
	{
		public override DeviceType Type => DeviceType.IOModule;

		public int ChannelCount { get; private set; }
		public double ResponseTime { get; private set; } // in milliseconds
		public bool HasDiagnostics { get; private set; }
		public bool[] Inputs { get; private set; }
		public bool[] PreviousInputs { get; private set; }

		// Input signal filtering
		private double _filterTime; // in milliseconds
		private DateTime[] _lastChangeTime;
		private bool[] _inputFiltered;
		private bool[] _inputRaw;

		/// <summary>
		/// Event raised when an input changes state after filtering
		/// </summary>
		public event EventHandler<InputChangeEventArgs> InputChanged;

		public DigitalInputModule(
			string deviceId,
			string name,
			int channelCount = 16,
			double responseTime = 3.0,
			double filterTime = 10.0,
			bool hasDiagnostics = false)
			: base(deviceId, name)
		{
			ChannelCount = channelCount;
			ResponseTime = responseTime;
			HasDiagnostics = hasDiagnostics;
			_filterTime = filterTime;

			// Initialize arrays
			Inputs = new bool[ChannelCount];
			PreviousInputs = new bool[ChannelCount];
			_lastChangeTime = new DateTime[ChannelCount];
			_inputFiltered = new bool[ChannelCount];
			_inputRaw = new bool[ChannelCount];

			// Add diagnostic data
			DiagnosticData["ChannelCount"] = ChannelCount;
			DiagnosticData["ResponseTime"] = ResponseTime;
			DiagnosticData["HasDiagnostics"] = HasDiagnostics;
			DiagnosticData["FilterTime"] = _filterTime;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize inputs to false
			for (int i = 0; i < ChannelCount; i++)
			{
				Inputs[i] = false;
				PreviousInputs[i] = false;
				_inputFiltered[i] = false;
				_inputRaw[i] = false;
				_lastChangeTime[i] = DateTime.Now;
			}
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Copy current inputs to previous inputs
			Array.Copy(Inputs, PreviousInputs, ChannelCount);

			// Apply input filtering
			for (int i = 0; i < ChannelCount; i++)
			{
				if (_inputRaw[i] != _inputFiltered[i])
				{
					// If the raw input is different from the filtered input
					// check if enough time has passed to change the filtered input
					if ((DateTime.Now - _lastChangeTime[i]).TotalMilliseconds > _filterTime)
					{
						_inputFiltered[i] = _inputRaw[i];
						Inputs[i] = _inputFiltered[i];

						// Raise event if the input has changed
						if (Inputs[i] != PreviousInputs[i])
						{
							OnInputChanged(i, Inputs[i]);
						}
					}
				}
			}

			// Add simulated response time delay (no actual delay, just for modeling)
			DiagnosticData["ResponseDelay"] = ResponseTime * (0.9 + Random.NextDouble() * 0.2);

			// Update diagnostics
			if (HasDiagnostics)
			{
				// In a real module with diagnostics, we might detect broken wires, shorts, etc.
				// Here we'll just occasionally simulate a diagnostic issue
				if (Random.NextDouble() < 0.0001) // Very rare
				{
					int channel = Random.Next(ChannelCount);
					AddAlarm($"INPUT_FAULT_{channel}", $"Input channel {channel} fault detected", AlarmSeverity.Minor);
				}
			}
		}

		/// <summary>
		/// Set raw input value (simulates field signal)
		/// </summary>
		public void SetInput(int channel, bool value)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				if (_inputRaw[channel] != value)
				{
					_inputRaw[channel] = value;
					_lastChangeTime[channel] = DateTime.Now;
				}
			}
		}

		/// <summary>
		/// Get input value (after filtering)
		/// </summary>
		public bool GetInput(int channel)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				return Inputs[channel];
			}
			return false;
		}

		/// <summary>
		/// Set filter time for all channels
		/// </summary>
		public void SetFilterTime(double milliseconds)
		{
			_filterTime = Math.Max(0, milliseconds);
			DiagnosticData["FilterTime"] = _filterTime;
		}

		protected virtual void OnInputChanged(int channel, bool value)
		{
			InputChanged?.Invoke(this, new InputChangeEventArgs(channel, value));
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

				case 1: // Input stuck
					int channel = Random.Next(ChannelCount);
					// Input stuck at current state
					AddAlarm($"INPUT_STUCK_{channel}", $"Input channel {channel} stuck at {Inputs[channel]}", AlarmSeverity.Minor);
					break;

				case 2: // Random input noise
					if (HasDiagnostics) // Only modules with diagnostics would detect this
					{
						AddAlarm("INPUT_NOISE", "Electrical noise detected on input channels", AlarmSeverity.Warning);
					}
					break;
			}
		}
	}

	public class InputChangeEventArgs : EventArgs
	{
		public int Channel { get; private set; }
		public bool Value { get; private set; }

		public InputChangeEventArgs(int channel, bool value)
		{
			Channel = channel;
			Value = value;
		}
	}
}