using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
	/// <summary>
	/// Simulates a process conductivity sensor for measuring electrical conductivity of solutions
	/// </summary>
	public class ConductivitySensor : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		// Primary measurement values
		public double Conductivity { get; private set; } // Current conductivity in μS/cm or mS/cm
		public double Temperature { get; private set; } // Temperature in Celsius
		public double ActualConductivity { get; private set; } // Actual process conductivity (for simulation)
		public bool UseMilliSiemens { get; private set; } // true = mS/cm, false = μS/cm

		// Sensor specifications
		public double MinConductivity { get; private set; }
		public double MaxConductivity { get; private set; }
		public double Accuracy { get; private set; } // % of reading accuracy

		// Internal parameters
		private ConductivitySensorType _sensorType;
		private double _cellConstant; // K-factor (cm⁻¹)
		private double _driftRate; // % per month
		private double _temperatureCoefficient; // %/°C
		private double _responseTime; // in seconds
		private bool _hasAutomaticTemperatureCompensation;
		private DateTime _lastCalibration;
		private DateTime _lastCleaning;

		// Sensor health and status
		private double _electrodeAge; // 0-100%
		private double _foulingLevel; // 0-100%
		private double _polarizationLevel; // 0-100% (for contacting sensors)

		// Calibration parameters
		private double _zeroOffset; // Zero offset in μS/cm or mS/cm
		private double _slopeError; // Slope error as a multiplier

		public ConductivitySensor(
			string deviceId,
			string name,
			ConductivitySensorType sensorType = ConductivitySensorType.ContactingCell,
			double minConductivity = 0.0,
			double maxConductivity = 20.0,
			bool useMilliSiemens = true,
			double accuracy = 1.0)
			: base(deviceId, name)
		{
			_sensorType = sensorType;
			MinConductivity = minConductivity;
			MaxConductivity = maxConductivity;
			UseMilliSiemens = useMilliSiemens;
			Accuracy = accuracy;

			// Initialize to typical values
			Conductivity = 1.0;
			ActualConductivity = 1.0;
			Temperature = 25.0;

			// Set parameters based on sensor type
			if (sensorType == ConductivitySensorType.ContactingCell)
			{
				_cellConstant = 1.0; // Common K=1.0/cm cell constant
				_driftRate = 0.5; // 0.5% per month
				_responseTime = 2.0; // 2 seconds
				_temperatureCoefficient = 2.1; // 2.1%/°C (approximately for pure water)
			}
			else if (sensorType == ConductivitySensorType.InductiveToroidal)
			{
				_cellConstant = 2.0; // Typical for toroidal sensors
				_driftRate = 0.2; // 0.2% per month - more stable
				_responseTime = 5.0; // 5 seconds - typically slower
				_temperatureCoefficient = 2.1; // 2.1%/°C
			}
			else // Multifrequency
			{
				_cellConstant = 1.0;
				_driftRate = 0.1; // 0.1% per month - most stable
				_responseTime = 3.0; // 3 seconds
				_temperatureCoefficient = 2.1; // 2.1%/°C
			}

			_hasAutomaticTemperatureCompensation = true;
			_lastCalibration = DateTime.Now;
			_lastCleaning = DateTime.Now;

			// Set initial conditions
			_electrodeAge = 0.0; // New sensor
			_foulingLevel = 0.0; // No fouling
			_polarizationLevel = 0.0; // No polarization

			// Calibration parameters
			_zeroOffset = 0.0;
			_slopeError = 1.0; // Perfect slope

			// Update diagnostic data
			DiagnosticData["SensorType"] = _sensorType.ToString();
			DiagnosticData["Range"] = UseMilliSiemens ?
				$"{MinConductivity} to {MaxConductivity} mS/cm" :
				$"{MinConductivity} to {MaxConductivity} μS/cm";
			DiagnosticData["Accuracy"] = $"±{Accuracy}% of reading";
			DiagnosticData["CellConstant"] = _cellConstant;
			DiagnosticData["LastCalibration"] = _lastCalibration;
			DiagnosticData["AutomaticTemperatureCompensation"] = _hasAutomaticTemperatureCompensation;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Calculate months since calibration for drift calculation
			double monthsSinceCalibration = (DateTime.Now - _lastCalibration).TotalDays / 30.0;
			double drift = _driftRate * monthsSinceCalibration / 100.0 * ActualConductivity;

			// Apply response time (first-order lag)
			double responseRate = 1.0 - Math.Exp(-elapsedTime.TotalSeconds / _responseTime);
			double filteredConductivity = Conductivity + (ActualConductivity - Conductivity) * responseRate;

			// Apply temperature effects
			double temperatureDeviation = Temperature - 25.0; // Deviation from standard 25°C
			double temperatureEffect = 0;

			if (!_hasAutomaticTemperatureCompensation)
			{
				// Without temperature compensation, readings change with temperature
				temperatureEffect = filteredConductivity * (_temperatureCoefficient / 100.0) * temperatureDeviation;
			}

			// Apply aging and condition effects
			double agingEffect = (_electrodeAge / 100.0) * 0.05 * filteredConductivity; // Up to 5% error for old electrode
			double foulingEffect = _foulingLevel / 100.0;

			// Fouling has different effects based on sensor type
			if (_sensorType == ConductivitySensorType.ContactingCell)
			{
				// Contacting cells typically read low when fouled
				foulingEffect *= -0.2 * filteredConductivity; // Up to -20% for heavy fouling
			}
			else if (_sensorType == ConductivitySensorType.InductiveToroidal)
			{
				// Toroidal sensors are less affected by fouling but still impacted
				foulingEffect *= -0.05 * filteredConductivity; // Up to -5% for heavy fouling
			}
			else // Multifrequency
			{
				// Multifrequency can compensate somewhat
				foulingEffect *= -0.02 * filteredConductivity; // Up to -2% for heavy fouling
			}

			// Polarization only affects contacting cells
			double polarizationEffect = 0;
			if (_sensorType == ConductivitySensorType.ContactingCell)
			{
				polarizationEffect = (_polarizationLevel / 100.0) * 0.1 * filteredConductivity; // Up to 10% error
			}

			// Calculate random noise (more noise with age and fouling)
			double conditionFactor = 1.0 + ((_electrodeAge / 100.0) + (_foulingLevel / 100.0)) * 2.0;
			double noise = (Random.NextDouble() * 2 - 1) * (Accuracy / 100.0) * filteredConductivity * conditionFactor;

			// Apply calibration errors
			double calibratedConductivity = (filteredConductivity + _zeroOffset) * _slopeError;

			// Calculate final conductivity reading
			Conductivity = calibratedConductivity + drift + temperatureEffect + agingEffect +
						   foulingEffect + polarizationEffect + noise;

			// Ensure value is within valid range
			Conductivity = Math.Max(MinConductivity, Math.Min(MaxConductivity, Conductivity));

			// Update diagnostics
			DiagnosticData["Conductivity"] = Conductivity;
			DiagnosticData["Units"] = UseMilliSiemens ? "mS/cm" : "μS/cm";
			DiagnosticData["Temperature"] = Temperature;
			DiagnosticData["Drift"] = drift;
			DiagnosticData["ElectrodeAge"] = _electrodeAge;
			DiagnosticData["FoulingLevel"] = _foulingLevel;

			// Slowly age the sensor over time
			_electrodeAge += 0.001 * elapsedTime.TotalHours; // 0.1% aging per 100 hours
			_foulingLevel += 0.005 * elapsedTime.TotalHours; // 0.5% fouling per 100 hours

			if (_sensorType == ConductivitySensorType.ContactingCell)
			{
				_polarizationLevel += 0.002 * elapsedTime.TotalHours; // 0.2% polarization per 100 hours
				DiagnosticData["PolarizationLevel"] = _polarizationLevel;
			}

			// Add alarms for sensor condition
			if (_electrodeAge > 80)
			{
				AddAlarm("ELECTRODE_OLD", "Conductivity sensor nearing end of life", AlarmSeverity.Warning);
			}

			if (_foulingLevel > 50)
			{
				AddAlarm("SENSOR_FOULING", "Conductivity sensor fouling detected", AlarmSeverity.Minor);
			}

			if (_sensorType == ConductivitySensorType.ContactingCell && _polarizationLevel > 60)
			{
				AddAlarm("POLARIZATION", "Electrode polarization detected", AlarmSeverity.Minor);
			}

			// Check calibration age
			if (monthsSinceCalibration > 6)
			{
				AddAlarm("CALIBRATION_DUE", "Conductivity calibration overdue", AlarmSeverity.Warning);
			}
		}

		/// <summary>
		/// Set the actual process conductivity (for simulation)
		/// </summary>
		public void SetProcessConductivity(double conductivity)
		{
			ActualConductivity = Math.Max(MinConductivity, Math.Min(MaxConductivity, conductivity));
		}

		/// <summary>
		/// Set the process temperature
		/// </summary>
		public void SetTemperature(double temperature)
		{
			Temperature = Math.Max(-10, Math.Min(150, temperature));
			DiagnosticData["Temperature"] = Temperature;
		}

		/// <summary>
		/// Enable or disable automatic temperature compensation
		/// </summary>
		public void SetTemperatureCompensation(bool enabled)
		{
			_hasAutomaticTemperatureCompensation = enabled;
			DiagnosticData["AutomaticTemperatureCompensation"] = enabled;
		}

		/// <summary>
		/// Calibrate the conductivity sensor
		/// </summary>
		public void Calibrate(double standardValue)
		{
			_lastCalibration = DateTime.Now;

			// Calculate new calibration parameters (simplified)
			_zeroOffset = (Random.NextDouble() * 2 - 1) * 0.01 * MaxConductivity; // Small zero offset
			_slopeError = 0.98 + (Random.NextDouble() * 0.04); // 98-102% of ideal slope

			DiagnosticData["LastCalibration"] = _lastCalibration;
			DiagnosticData["ZeroOffset"] = _zeroOffset;
			DiagnosticData["SlopeError"] = _slopeError;

			AddAlarm("CALIBRATION", "Conductivity sensor calibrated", AlarmSeverity.Information);
		}

		/// <summary>
		/// Clean the sensor to remove fouling and reset polarization
		/// </summary>
		public void CleanSensor()
		{
			_lastCleaning = DateTime.Now;
			_foulingLevel = Math.Max(0, _foulingLevel - 95); // Remove 95% of fouling
			_polarizationLevel = Math.Max(0, _polarizationLevel - 90); // Remove 90% of polarization

			DiagnosticData["LastCleaning"] = _lastCleaning;
			DiagnosticData["FoulingLevel"] = _foulingLevel;

			if (_sensorType == ConductivitySensorType.ContactingCell)
			{
				DiagnosticData["PolarizationLevel"] = _polarizationLevel;
			}

			AddAlarm("SENSOR_CLEANED", "Conductivity sensor cleaned", AlarmSeverity.Information);
		}

		/// <summary>
		/// Replace sensor with a new one
		/// </summary>
		public void ReplaceSensor()
		{
			_lastCalibration = DateTime.Now;
			_lastCleaning = DateTime.Now;

			_electrodeAge = 0.0;
			_foulingLevel = 0.0;
			_polarizationLevel = 0.0;

			// New sensors typically have values close to ideal
			_zeroOffset = (Random.NextDouble() * 2 - 1) * 0.005 * MaxConductivity; // Very small zero offset
			_slopeError = 0.99 + (Random.NextDouble() * 0.02); // 99-101% of ideal slope

			DiagnosticData["ElectrodeAge"] = _electrodeAge;
			DiagnosticData["FoulingLevel"] = _foulingLevel;
			DiagnosticData["PolarizationLevel"] = _polarizationLevel;
			DiagnosticData["ZeroOffset"] = _zeroOffset;
			DiagnosticData["SlopeError"] = _slopeError;

			AddAlarm("SENSOR_REPLACED", "Conductivity sensor replaced", AlarmSeverity.Information);
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(5);

			switch (faultType)
			{
				case 0: // Cable/connection issue
					AddAlarm("CONNECTION_ISSUE", "Conductivity sensor connection issue", AlarmSeverity.Major);
					Conductivity = 0; // Reading zero
					break;

				case 1: // Severe fouling
					AddAlarm("SEVERE_FOULING", "Severe fouling detected", AlarmSeverity.Major);
					_foulingLevel = 90;
					break;

				case 2: // Short circuit
					if (_sensorType == ConductivitySensorType.ContactingCell)
					{
						AddAlarm("SHORT_CIRCUIT", "Electrode short circuit detected", AlarmSeverity.Critical);
						Conductivity = MaxConductivity; // Reading maximum
					}
					else
					{
						AddAlarm("COIL_FAULT", "Toroid coil fault detected", AlarmSeverity.Critical);
						Conductivity = Random.NextDouble() * MaxConductivity;
					}
					break;

				case 3: // Bubbles
					AddAlarm("BUBBLE_INTERFERENCE", "Air bubbles affecting measurement", AlarmSeverity.Minor);
					Conductivity = ActualConductivity * (0.7 + Random.NextDouble() * 0.2); // Reading 70-90% of actual
					break;

				case 4: // Ground loop
					AddAlarm("GROUND_LOOP", "Electrical interference detected", AlarmSeverity.Minor);
					Conductivity += (ActualConductivity * 0.2); // Reading too high by ~20%
					break;
			}
		}
	}

	public enum ConductivitySensorType
	{
		ContactingCell,    // Standard 2-electrode or 4-electrode cell
		InductiveToroidal, // Non-contact toroidal sensor
		Multifrequency     // Advanced multi-frequency sensor
	}
}