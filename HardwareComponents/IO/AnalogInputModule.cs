using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaVax.HardwareComponents.IO
{
	/// <summary>
	/// Analog input module that handles multiple analog input points
	/// </summary>
	public class AnalogInputModule : DeviceBase
	{
		public override DeviceType Type => DeviceType.IOModule;

		public int ChannelCount { get; private set; }
		public double Resolution { get; private set; } // in bits
		public double UpdateRate { get; private set; } // in milliseconds per channel
		public bool HasHartSupport { get; private set; }
		public double[] Inputs { get; private set; } // Scaled values
		public double[] RawInputs { get; private set; } // Raw ADC values

		// Module configuration
		private AnalogInputType _inputType;
		private double _rangeMin;
		private double _rangeMax;

		// Filtering and smoothing
		private double _filterCoefficient;
		private double[] _filteredValues;

		// Diagnostic-related
		private bool[] _openCircuitDetected;
		private bool[] _outOfRangeDetected;
		private DateTime[] _lastUpdateTime;

		/// <summary>
		/// Event raised when an input changes beyond a specified dead band
		/// </summary>
		public event EventHandler<AnalogInputChangeEventArgs> InputChanged;

		public AnalogInputModule(
			string deviceId,
			string name,
			int channelCount = 8,
			AnalogInputType inputType = AnalogInputType.Current4_20mA,
			double resolution = 16,
			double updateRate = 10.0,
			bool hasHartSupport = false)
			: base(deviceId, name)
		{
			ChannelCount = channelCount;
			_inputType = inputType;
			Resolution = resolution;
			UpdateRate = updateRate;
			HasHartSupport = hasHartSupport;

			// Set range based on input type
			switch (_inputType)
			{
				case AnalogInputType.Current4_20mA:
					_rangeMin = 4.0;
					_rangeMax = 20.0;
					break;
				case AnalogInputType.Voltage0_10V:
					_rangeMin = 0.0;
					_rangeMax = 10.0;
					break;
				case AnalogInputType.VoltagePN10V:
					_rangeMin = -10.0;
					_rangeMax = 10.0;
					break;
				case AnalogInputType.Resistance:
					_rangeMin = 0.0;
					_rangeMax = 6000.0; // Typical for RTD (PT100/PT1000)
					break;
				default:
					_rangeMin = 0.0;
					_rangeMax = 100.0;
					break;
			}

			// Initialize arrays
			Inputs = new double[ChannelCount];
			RawInputs = new double[ChannelCount];
			_filteredValues = new double[ChannelCount];
			_openCircuitDetected = new bool[ChannelCount];
			_outOfRangeDetected = new bool[ChannelCount];
			_lastUpdateTime = new DateTime[ChannelCount];

			// Default filter coefficient (0 = no filtering, 1 = infinite filtering)
			_filterCoefficient = 0.8;

			// Add diagnostic data
			DiagnosticData["ChannelCount"] = ChannelCount;
			DiagnosticData["InputType"] = _inputType.ToString();
			DiagnosticData["Resolution"] = Resolution;
			DiagnosticData["UpdateRate"] = UpdateRate;
			DiagnosticData["HasHartSupport"] = HasHartSupport;
			DiagnosticData["RangeMin"] = _rangeMin;
			DiagnosticData["RangeMax"] = _rangeMax;
			DiagnosticData["FilterCoefficient"] = _filterCoefficient;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize inputs to zero
			for (int i = 0; i < ChannelCount; i++)
			{
				Inputs[i] = 0;
				RawInputs[i] = 0;
				_filteredValues[i] = 0;
				_openCircuitDetected[i] = false;
				_outOfRangeDetected[i] = false;
				_lastUpdateTime[i] = DateTime.Now;
			}
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Calculate how many channels we can update this cycle
			// This simulates the module's scan rate
			double totalUpdateTime = elapsedTime.TotalMilliseconds;
			int channelsToUpdate = (int)Math.Floor(totalUpdateTime / UpdateRate);

			// Update at least one channel
			channelsToUpdate = Math.Max(1, channelsToUpdate);

			// Update channels in sequence, wrapping around
			int startChannel = Random.Next(ChannelCount); // Random start to avoid bias

			for (int i = 0; i < Math.Min(channelsToUpdate, ChannelCount); i++)
			{
				int channel = (startChannel + i) % ChannelCount;

				// Simulate update timing
				_lastUpdateTime[channel] = DateTime.Now;

				// Apply filter to raw input value
				if (_filterCoefficient > 0)
				{
					_filteredValues[channel] = _filterCoefficient * _filteredValues[channel] +
											 (1 - _filterCoefficient) * RawInputs[channel];
				}
				else
				{
					_filteredValues[channel] = RawInputs[channel];
				}

				// Apply resolution limitations (quantization)
				double steps = Math.Pow(2, Resolution);
				double stepSize = (_rangeMax - _rangeMin) / steps;
				double quantizedValue = Math.Round(_filteredValues[channel] / stepSize) * stepSize;

				// Store the processed value
				double oldValue = Inputs[channel];
				Inputs[channel] = quantizedValue;

				// Check for significant change and raise event
				double deadBand = stepSize * 2; // 2 counts of deadband
				if (Math.Abs(Inputs[channel] - oldValue) > deadBand)
				{
					OnInputChanged(channel, Inputs[channel]);
				}

				// Check for diagnostics
				if (_openCircuitDetected[channel])
				{
					AddAlarm($"OPEN_CIRCUIT_{channel}", $"Channel {channel} open circuit detected", AlarmSeverity.Minor);
				}

				if (_outOfRangeDetected[channel])
				{
					AddAlarm($"OUT_OF_RANGE_{channel}", $"Channel {channel} out of range", AlarmSeverity.Warning);
				}

				// Update diagnostics
				DiagnosticData[$"Input_{channel}"] = Inputs[channel];
				DiagnosticData[$"Raw_{channel}"] = RawInputs[channel];
				DiagnosticData[$"LastUpdate_{channel}"] = _lastUpdateTime[channel];
			}

			// Add common mode rejection simulation
			if (_inputType == AnalogInputType.Voltage0_10V || _inputType == AnalogInputType.VoltagePN10V)
			{
				// Simulate common mode noise rejection (better for expensive modules)
				double commonModeNoise = Random.NextDouble() * 0.001; // 0.1% noise
				DiagnosticData["CommonModeNoise"] = commonModeNoise;
			}
		}

		/// <summary>
		/// Set raw input value for a channel (simulates field signal)
		/// </summary>
		public void SetInput(int channel, double value)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				RawInputs[channel] = value;

				// Check if value is within range
				if (value < _rangeMin || value > _rangeMax)
				{
					_outOfRangeDetected[channel] = true;
				}
				else
				{
					_outOfRangeDetected[channel] = false;
				}
			}
		}

		/// <summary>
		/// Get processed input value for a channel
		/// </summary>
		public double GetInput(int channel)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				return Inputs[channel];
			}
			return 0;
		}

		/// <summary>
		/// Set filtering coefficient (0 = no filtering, 1 = infinite filtering)
		/// </summary>
		public void SetFilterCoefficient(double coefficient)
		{
			_filterCoefficient = Math.Max(0, Math.Min(0.99, coefficient));
			DiagnosticData["FilterCoefficient"] = _filterCoefficient;
		}

		/// <summary>
		/// Simulate an open circuit on a channel
		/// </summary>
		public void SimulateOpenCircuit(int channel, bool isOpen)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				_openCircuitDetected[channel] = isOpen;

				if (isOpen)
				{
					// For 4-20mA, open circuit usually reads as 0mA
					if (_inputType == AnalogInputType.Current4_20mA)
					{
						RawInputs[channel] = 0;
					}
				}
			}
		}

		protected virtual void OnInputChanged(int channel, double value)
		{
			InputChanged?.Invoke(this, new AnalogInputChangeEventArgs(channel, value));
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

				case 1: // Channel fault
					int channel = Random.Next(ChannelCount);
					AddAlarm($"CHANNEL_FAULT_{channel}", $"Channel {channel} measurement fault", AlarmSeverity.Minor);
					break;

				case 2: // Noise/interference
					AddAlarm("SIGNAL_NOISE", "Excessive noise on analog inputs", AlarmSeverity.Warning);

					// Add noise to all channels temporarily
					for (int i = 0; i < ChannelCount; i++)
					{
						RawInputs[i] += (Random.NextDouble() * 2 - 1) * (_rangeMax - _rangeMin) * 0.05; // ±5% noise
					}
					break;
			}
		}
	}

	public enum AnalogInputType
	{
		Current4_20mA,
		Voltage0_10V,
		VoltagePN10V,
		Resistance,
		Custom
	}

	public class AnalogInputChangeEventArgs : EventArgs
	{
		public int Channel { get; private set; }
		public double Value { get; private set; }

		public AnalogInputChangeEventArgs(int channel, double value)
		{
			Channel = channel;
			Value = value;
		}
	}
}