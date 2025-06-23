using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.IO
{
	/// <summary>
	/// Module for RTD (Resistance Temperature Detector) temperature measurement
	/// </summary>
	public class RTDModule : DeviceBase
	{
		public override DeviceType Type => DeviceType.IOModule;

		public int ChannelCount { get; private set; }
		public RTDSensorType SensorType { get; private set; }
		public double[] Temperatures { get; private set; } // Temperature values in Celsius
		public double[] Resistances { get; private set; } // Raw resistance values in Ohms

		// Module configuration
		private double _minTemperature;
		private double _maxTemperature;
		private double _accuracy; // Accuracy in °C
		private double _resolution; // Resolution in bits
		private double _updateRate; // Update rate in milliseconds per channel
		private bool _hasLeadWireCompensation;
		private ConnectionType _connectionType;

		// Internal state
		private double[] _filteredResistances;
		private bool[] _sensorBreakDetected;
		private bool[] _sensorShortDetected;
		private bool[] _leadWireBreakDetected;
		private DateTime[] _lastUpdateTime;

		/// <summary>
		/// Event raised when a temperature changes beyond a specified dead band
		/// </summary>
		public event EventHandler<TemperatureChangeEventArgs> TemperatureChanged;

		public RTDModule(
			string deviceId,
			string name,
			int channelCount = 4,
			RTDSensorType sensorType = RTDSensorType.PT100,
			ConnectionType connectionType = ConnectionType.ThreeWire,
			double minTemperature = -200,
			double maxTemperature = 850,
			double accuracy = 0.1,
			double resolution = 16,
			double updateRate = 100.0)
			: base(deviceId, name)
		{
			ChannelCount = channelCount;
			SensorType = sensorType;
			_connectionType = connectionType;
			_minTemperature = minTemperature;
			_maxTemperature = maxTemperature;
			_accuracy = accuracy;
			_resolution = resolution;
			_updateRate = updateRate;

			_hasLeadWireCompensation = connectionType != ConnectionType.TwoWire;

			// Initialize arrays
			Temperatures = new double[ChannelCount];
			Resistances = new double[ChannelCount];
			_filteredResistances = new double[ChannelCount];
			_sensorBreakDetected = new bool[ChannelCount];
			_sensorShortDetected = new bool[ChannelCount];
			_leadWireBreakDetected = new bool[ChannelCount];
			_lastUpdateTime = new DateTime[ChannelCount];

			// Set nominal resistance based on sensor type
			double nominalResistance = GetNominalResistance(sensorType);

			// Initialize all channels to 25°C
			for (int i = 0; i < ChannelCount; i++)
			{
				Temperatures[i] = 25.0;
				// For PT100, resistance at 25°C is approximately 109.73 ohms
				Resistances[i] = CalculateResistance(25.0, sensorType);
				_filteredResistances[i] = Resistances[i];
				_lastUpdateTime[i] = DateTime.Now;
			}

			// Add diagnostic data
			DiagnosticData["ChannelCount"] = ChannelCount;
			DiagnosticData["SensorType"] = SensorType.ToString();
			DiagnosticData["ConnectionType"] = _connectionType.ToString();
			DiagnosticData["Range"] = $"{_minTemperature}°C to {_maxTemperature}°C";
			DiagnosticData["Accuracy"] = $"±{_accuracy}°C";
			DiagnosticData["Resolution"] = _resolution;
			DiagnosticData["UpdateRate"] = _updateRate;
			DiagnosticData["LeadWireCompensation"] = _hasLeadWireCompensation;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Reset fault conditions
			for (int i = 0; i < ChannelCount; i++)
			{
				_sensorBreakDetected[i] = false;
				_sensorShortDetected[i] = false;
				_leadWireBreakDetected[i] = false;
			}
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Calculate how many channels we can update this cycle
			double totalUpdateTime = elapsedTime.TotalMilliseconds;
			int channelsToUpdate = (int)Math.Floor(totalUpdateTime / _updateRate);

			// Update at least one channel
			channelsToUpdate = Math.Max(1, channelsToUpdate);

			// Update channels in sequence, wrapping around
			int startChannel = Random.Next(ChannelCount); // Random start to avoid bias

			for (int i = 0; i < Math.Min(channelsToUpdate, ChannelCount); i++)
			{
				int channel = (startChannel + i) % ChannelCount;

				// Skip channels with detected breaks or shorts
				if (_sensorBreakDetected[channel] || _sensorShortDetected[channel])
					continue;

				// Simulate update timing
				_lastUpdateTime[channel] = DateTime.Now;

				// Apply filtering to resistance value (simple low-pass filter)
				_filteredResistances[channel] = 0.8 * _filteredResistances[channel] + 0.2 * Resistances[channel];

				// Apply resolution limitations (quantization)
				double steps = Math.Pow(2, _resolution);
				double maxResistance = GetMaxResistance(SensorType);
				double stepSize = maxResistance / steps;
				double quantizedResistance = Math.Round(_filteredResistances[channel] / stepSize) * stepSize;

				// Apply lead wire resistance effects
				double leadWireEffect = 0;
				if (_connectionType == ConnectionType.TwoWire)
				{
					// Two-wire connections have uncorrected lead wire resistance
					leadWireEffect = Random.NextDouble() * 0.5; // 0-0.5 ohm lead resistance
				}
				else if (_connectionType == ConnectionType.ThreeWire && _leadWireBreakDetected[channel])
				{
					// Three-wire with one lead broken falls back to two-wire behavior
					leadWireEffect = Random.NextDouble() * 0.5;
				}

				// Apply accuracy effects
				double accuracyInOhms = _accuracy * 0.385; // ~0.385 ohms/°C for PT100
				double accuracyEffect = (Random.NextDouble() * 2 - 1) * accuracyInOhms;

				// Calculate final resistance with all effects
				double measuredResistance = quantizedResistance + leadWireEffect + accuracyEffect;

				// Convert resistance to temperature
				double oldTemperature = Temperatures[channel];

				try
				{
					Temperatures[channel] = CalculateTemperature(measuredResistance, SensorType);
				}
				catch (Exception)
				{
					// Handle out of range resistance values
					if (measuredResistance < 18.5) // Less than -200°C for PT100
					{
						_sensorShortDetected[channel] = true;
						AddAlarm($"SENSOR_SHORT_{channel}", $"RTD channel {channel} short circuit detected", AlarmSeverity.Minor);
					}
					else
					{
						_sensorBreakDetected[channel] = true;
						AddAlarm($"SENSOR_BREAK_{channel}", $"RTD channel {channel} break detected", AlarmSeverity.Minor);
					}

					// Set to error value
					Temperatures[channel] = double.NaN;
				}

				// Check for significant change and raise event
				double deadBand = 0.1; // 0.1°C deadband
				if (!double.IsNaN(oldTemperature) && !double.IsNaN(Temperatures[channel]) &&
					Math.Abs(Temperatures[channel] - oldTemperature) > deadBand)
				{
					OnTemperatureChanged(channel, Temperatures[channel]);
				}

				// Update diagnostics
				DiagnosticData[$"Temperature_{channel}"] = Temperatures[channel];
				DiagnosticData[$"Resistance_{channel}"] = measuredResistance;
				DiagnosticData[$"LastUpdate_{channel}"] = _lastUpdateTime[channel];
			}

			// Add occasional diagnostics
			if (Random.NextDouble() < 0.001) // 0.1% chance
			{
				// Check for sensor degradation (increasing resistance due to contamination)
				int channel = Random.Next(ChannelCount);
				if (!_sensorBreakDetected[channel] && !_sensorShortDetected[channel])
				{
					double degradationFactor = 1.002; // 0.2% increase in resistance
					Resistances[channel] *= degradationFactor;
					DiagnosticData[$"SensorContamination_{channel}"] = "Slight increase in base resistance detected";
				}
			}
		}

		/// <summary>
		/// Set the process temperature for a channel (for simulation)
		/// </summary>
		public void SetTemperature(int channel, double temperature)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				// Clamp temperature to valid range
				temperature = Math.Max(_minTemperature, Math.Min(_maxTemperature, temperature));

				// Calculate the corresponding resistance for this temperature
				Resistances[channel] = CalculateResistance(temperature, SensorType);

				// Clear any fault conditions
				_sensorBreakDetected[channel] = false;
				_sensorShortDetected[channel] = false;
			}
		}

		/// <summary>
		/// Simulate a sensor break on a specified channel
		/// </summary>
		public void SimulateSensorBreak(int channel, bool isBroken)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				_sensorBreakDetected[channel] = isBroken;

				if (isBroken)
				{
					AddAlarm($"SENSOR_BREAK_{channel}", $"RTD channel {channel} break detected", AlarmSeverity.Minor);
					Temperatures[channel] = double.NaN;
					DiagnosticData[$"Temperature_{channel}"] = "OPEN";
				}
			}
		}

		/// <summary>
		/// Simulate a sensor short on a specified channel
		/// </summary>
		public void SimulateSensorShort(int channel, bool isShorted)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				_sensorShortDetected[channel] = isShorted;

				if (isShorted)
				{
					AddAlarm($"SENSOR_SHORT_{channel}", $"RTD channel {channel} short circuit detected", AlarmSeverity.Minor);
					Temperatures[channel] = double.NaN;
					DiagnosticData[$"Temperature_{channel}"] = "SHORT";
				}
			}
		}

		/// <summary>
		/// Simulate a lead wire break on a specified channel
		/// </summary>
		public void SimulateLeadWireBreak(int channel, bool isBroken)
		{
			if (channel >= 0 && channel < ChannelCount)
			{
				_leadWireBreakDetected[channel] = isBroken;

				if (isBroken && _connectionType != ConnectionType.TwoWire)
				{
					AddAlarm($"LEAD_BREAK_{channel}", $"RTD channel {channel} lead wire break detected", AlarmSeverity.Warning);
				}
			}
		}

		private double GetNominalResistance(RTDSensorType sensorType)
		{
			switch (sensorType)
			{
				case RTDSensorType.PT100:
					return 100.0;
				case RTDSensorType.PT1000:
					return 1000.0;
				case RTDSensorType.PT500:
					return 500.0;
				case RTDSensorType.NI100:
					return 100.0;
				case RTDSensorType.NI1000:
					return 1000.0;
				default:
					return 100.0;
			}
		}

		private double GetMaxResistance(RTDSensorType sensorType)
		{
			// Maximum resistance for the full temperature range
			switch (sensorType)
			{
				case RTDSensorType.PT100:
					return 400.0; // ~400 ohms at 850°C
				case RTDSensorType.PT1000:
					return 4000.0;
				case RTDSensorType.PT500:
					return 2000.0;
				case RTDSensorType.NI100:
					return 350.0;
				case RTDSensorType.NI1000:
					return 3500.0;
				default:
					return 400.0;
			}
		}

		private double CalculateResistance(double temperature, RTDSensorType sensorType)
		{
			double r0 = GetNominalResistance(sensorType);

			// Different calculations based on sensor type
			if (sensorType == RTDSensorType.PT100 || sensorType == RTDSensorType.PT500 || sensorType == RTDSensorType.PT1000)
			{
				// Standard PT100 calculation (also applies to PT500 and PT1000 with different R0)
				// IEC 60751 standard: R(t) = R0 * (1 + At + Bt²) for t ≥ 0°C
				// R(t) = R0 * (1 + At + Bt² + C(t-100)t³) for t < 0°C

				double A = 3.9083e-3;
				double B = -5.775e-7;
				double C = -4.183e-12; // Only used for temperatures below 0°C

				if (temperature >= 0)
				{
					return r0 * (1 + A * temperature + B * temperature * temperature);
				}
				else
				{
					return r0 * (1 + A * temperature + B * temperature * temperature +
						C * (temperature - 100) * temperature * temperature * temperature);
				}
			}
			else if (sensorType == RTDSensorType.NI100 || sensorType == RTDSensorType.NI1000)
			{
				// Nickel RTD calculation
				// Nickel has a non-linear relationship (simplified here)
				double alpha = 0.00617; // Nickel temperature coefficient
				return r0 * (1 + alpha * temperature);
			}

			// Default to linear approximation if unknown type
			return r0 * (1 + 0.00385 * temperature);
		}

		private double CalculateTemperature(double resistance, RTDSensorType sensorType)
		{
			double r0 = GetNominalResistance(sensorType);

			// Normalized resistance
			double ratio = resistance / r0;

			if (sensorType == RTDSensorType.PT100 || sensorType == RTDSensorType.PT500 || sensorType == RTDSensorType.PT1000)
			{
				// Handle as PT100/PT500/PT1000
				// This is a simplified approach that works reasonably well for the normal range

				double A = 3.9083e-3;
				double B = -5.775e-7;

				if (ratio >= 1.0)
				{
					// Temperature > 0°C (simplified quadratic formula)
					double temp = (-A + Math.Sqrt(A * A - 4 * B * (1 - ratio))) / (2 * B);
					return temp;
				}
				else
				{
					// Temperature < 0°C (approximation)
					// This is a simplified approximation that's reasonably accurate for normal range
					return (ratio - 1) / A;
				}
			}
			else if (sensorType == RTDSensorType.NI100 || sensorType == RTDSensorType.NI1000)
			{
				// Nickel RTD calculation
				double alpha = 0.00617;
				return (ratio - 1) / alpha;
			}

			// Default linear approximation
			return (ratio - 1) / 0.00385;
		}

		protected virtual void OnTemperatureChanged(int channel, double temperature)
		{
			TemperatureChanged?.Invoke(this, new TemperatureChangeEventArgs(channel, temperature));
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

				case 1: // Single channel failure
					int channel = Random.Next(ChannelCount);
					int failureType = Random.Next(3);
					switch (failureType)
					{
						case 0:
							SimulateSensorBreak(channel, true);
							break;
						case 1:
							SimulateSensorShort(channel, true);
							break;
						case 2:
							SimulateLeadWireBreak(channel, true);
							break;
					}
					break;

				case 2: // Noise issue
					AddAlarm("RTD_NOISE", "Excessive noise on RTD channels", AlarmSeverity.Warning);
					// Add temporary noise to all channels
					for (int i = 0; i < ChannelCount; i++)
					{
						if (!_sensorBreakDetected[i] && !_sensorShortDetected[i])
						{
							Resistances[i] += (Random.NextDouble() * 2 - 1) * 0.5;
						}
					}
					break;
			}
		}
	}

	public enum RTDSensorType
	{
		PT100,  // Platinum 100 ohm (most common)
		PT500,  // Platinum 500 ohm
		PT1000, // Platinum 1000 ohm
		NI100,  // Nickel 100 ohm
		NI1000  // Nickel 1000 ohm
	}

	public enum ConnectionType
	{
		TwoWire,    // Least accurate, no lead wire compensation
		ThreeWire,  // Common, provides lead wire compensation
		FourWire    // Most accurate, eliminates lead wire resistance
	}

	public class TemperatureChangeEventArgs : EventArgs
	{
		public int Channel { get; private set; }
		public double Temperature { get; private set; }

		public TemperatureChangeEventArgs(int channel, double temperature)
		{
			Channel = channel;
			Temperature = temperature;
		}
	}
}