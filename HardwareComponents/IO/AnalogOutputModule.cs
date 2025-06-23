using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaVax.HardwareComponents.IO
{
	/// <summary>
	/// Analog output module that handles multiple analog output points
	/// </summary>
	public class AnalogOutputModule : DeviceBase
	{
		public override DeviceType Type => DeviceType.IOModule;

		public int ChannelCount { get; private set; }
		public double Resolution { get; private set; } // in bits
		public double UpdateRate { get; private set; } // in milliseconds per channel
		public double[] Outputs { get; private set; }
		public double[] ActualOutputs { get; private set; } // For simulating real-world differences

		// Module configuration
		private AnalogOutputType _outputType;
		private double _rangeMin;
		private double _rangeMax;

		// Diagnostic-related
		private bool[] _shortCircuitDetected;
		private bool[] _openCircuitDetected;
		private DateTime[] _lastUpdateTime;

		/// <summary>
		/// Event raised when an output is changed
		/// </summary>
		public event EventHandler<AnalogOutputChangeEventArgs> OutputChanged;

		public AnalogOutputModule(
			string deviceId,
			string name,
			int channelCount = 8,
			AnalogOutputType outputType = AnalogOutputType.Current4_20mA,
			double resolution = 16,
			double updateRate = 5.0)
			: base(deviceId, name)
		{
			ChannelCount = channelCount;
			_outputType = outputType;
			Resolution = resolution;
			UpdateRate = updateRate;

			// Set range based on output type
			switch (_outputType)
			{
				case AnalogOutputType.Current4_20mA:
					_rangeMin = 4.0;
					_rangeMax = 20.0;
					break;
				case AnalogOutputType.Voltage0_10V:
					_rangeMin = 0.0;
					_rangeMax = 10.0;
					break;
				case AnalogOutputType.VoltagePN10V:
					_rangeMin = -10.0;
					_rangeMax = 10.0;
					break;
				default:
					_rangeMin = 0.0;
					_rangeMax = 100.0;
					break;
			}

			// Initialize arrays
			Outputs = new double[ChannelCount];
			ActualOutputs = new double[ChannelCount];
			_shortCircuitDetected = new bool[ChannelCount];
			_openCircuitDetected = new bool[ChannelCount];
			_lastUpdateTime = new DateTime[ChannelCount];

			// Add diagnostic data
			DiagnosticData["ChannelCount"] = ChannelCount;
			DiagnosticData["OutputType"] = _outputType.ToString();
			DiagnosticData["Resolution"] = Resolution;
			DiagnosticData["UpdateRate"] = UpdateRate;
			DiagnosticData["RangeMin"] = _rangeMin;
			DiagnosticData["RangeMax"] = _rangeMax;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize outputs to minimum value
			for (int i = 0; i < ChannelCount; i++)
			{
				Outputs[i] = _rangeMin;
				ActualOutputs[i] = _rangeMin;
				_shortCircuitDetected[i] = false;
				_openCircuitDetected[i] = false;
				_lastUpdateTime[i] = DateTime.Now;
			}
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Calculate how many channels we can update this cycle
			// This simulates the module's update rate
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

				// Calculate actual output value with slight error
				double error = (Random.NextDouble() * 2 - 1) * (_rangeMax - _rangeMin) * 0.001; // ±0.1% error
				ActualOutputs[channel] = Outputs[channel] + error;

				// Clamp to valid range
				ActualOutputs[channel] = Math.Max(_rangeMin, Math.Min(_rangeMax, ActualOutputs[channel]));

				// Check for diagnostics
				if (_shortCircuitDetected[channel])
				{
					AddAlarm($"SHORT_CIRCUIT_{channel}", $"Channel {channel} short circuit detected", AlarmSeverity.Minor);
					ActualOutputs[channel] = 0; // Short circuit typically reads as 0V or 0mA
				}

				if (_openCircuitDetected[channel])
				{
					AddAlarm($"OPEN_CIRCUIT_{channel}", $"Channel {channel} open circuit detected", AlarmSeverity.Minor);
					// For open circuit, voltage outputs will read as max voltage
					// Current outputs will read as 0mA
					if (_outputType == AnalogOutputType.Current4_20mA)
					{
						ActualOutputs[channel] = 0;
					}
					else
					{
						ActualOutputs[channel] = _rangeMax;
					}
				}

				// Update diagnostics
				DiagnosticData[$"Output_{channel}"] = Outputs[channel];
				DiagnosticData[$"Actual_{channel}"] = ActualOutputs[channel];
				DiagnosticData[$"LastUpdate_{channel}"] = _lastUpdateTime[channel];
			}
		}

		/// <summary>
		/// Set output value for a channel
		/// </summary>
		public void SetOutput(int channel, double value)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				// Apply resolution limitations
				double steps = Math.Pow(2, Resolution);
				double stepSize = (_rangeMax - _rangeMin) / steps;
				double quantizedValue = Math.Round(value / stepSize) * stepSize;

				// Clamp to valid range
				double clampedValue = Math.Max(_rangeMin, Math.Min(_rangeMax, quantizedValue));

				// Check if value is different from current
				if (Math.Abs(Outputs[channel] - clampedValue) > stepSize * 0.5)
				{
					Outputs[channel] = clampedValue;

					// Notify subscribers
					OnOutputChanged(channel, clampedValue);
				}
			}
		}

		/// <summary>
		/// Get output value for a channel
		/// </summary>
		public double GetOutput(int channel)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				return Outputs[channel];
			}
			return _rangeMin;
		}

		/// <summary>
		/// Get actual output value (including simulated errors)
		/// </summary>
		public double GetActualOutput(int channel)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				return ActualOutputs[channel];
			}
			return _rangeMin;
		}

		/// <summary>
		/// Simulate a short circuit on a channel
		/// </summary>
		public void SimulateShortCircuit(int channel, bool isShorted)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				_shortCircuitDetected[channel] = isShorted;

				if (isShorted)
				{
					AddAlarm($"SHORT_CIRCUIT_{channel}", $"Channel {channel} short circuit detected", AlarmSeverity.Minor);
				}
			}
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
					AddAlarm($"OPEN_CIRCUIT_{channel}", $"Channel {channel} open circuit detected", AlarmSeverity.Minor);
				}
			}
		}

		protected virtual void OnOutputChanged(int channel, double value)
		{
			OutputChanged?.Invoke(this, new AnalogOutputChangeEventArgs(channel, value));
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
					AddAlarm($"CHANNEL_FAULT_{channel}", $"Channel {channel} output fault", AlarmSeverity.Minor);
					break;

				case 2: // Output drift
					channel = Random.Next(ChannelCount);
					double drift = (_rangeMax - _rangeMin) * 0.05; // 5% drift
					ActualOutputs[channel] = Outputs[channel] + drift;
					AddAlarm($"OUTPUT_DRIFT_{channel}", $"Channel {channel} output drift detected", AlarmSeverity.Warning);
					break;
			}
		}
	}

	public enum AnalogOutputType
	{
		Current4_20mA,
		Voltage0_10V,
		VoltagePN10V,
		Custom
	}

	public class AnalogOutputChangeEventArgs : EventArgs
	{
		public int Channel { get; private set; }
		public double Value { get; private set; }

		public AnalogOutputChangeEventArgs(int channel, double value)
		{
			Channel = channel;
			Value = value;
		}
	}
}