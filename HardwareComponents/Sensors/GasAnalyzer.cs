using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
	/// <summary>
	/// Simulates a process gas analyzer for measuring multiple gas concentrations
	/// </summary>
	public class GasAnalyzer : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		// Primary measurement values
		public Dictionary<GasType, double> GasConcentrations { get; private set; } // Measured gas concentrations
		public Dictionary<GasType, double> ActualGasConcentrations { get; private set; } // Actual concentrations (for simulation)
		public double Temperature { get; private set; } // Sample temperature in Celsius
		public double FlowRate { get; private set; } // Sample flow rate in L/min
		public double Pressure { get; private set; } // Sample pressure in bar

		// Analyzer specifications
		public GasAnalyzerType AnalyzerType { get; private set; }
		public Dictionary<GasType, double> MinRanges { get; private set; } // Minimum detectable concentration
		public Dictionary<GasType, double> MaxRanges { get; private set; } // Maximum detectable concentration
		public Dictionary<GasType, double> Accuracies { get; private set; } // % of full scale

		// Internal parameters
		private Dictionary<GasType, double> _driftRates; // % of full scale per month
		private Dictionary<GasType, double> _responseTimesSeconds; // in seconds
		private Dictionary<GasType, double> _sensorAges; // 0-100%
		private Dictionary<GasType, double> _crossSensitivities; // % interference from other gases
		private Dictionary<GasType, double> _temperatureCoefficients; // % change per °C
		private Dictionary<GasType, DateTime> _lastCalibrations;

		// Calibration parameters
		private Dictionary<GasType, double> _zeroOffsets; // Zero offset in concentration units
		private Dictionary<GasType, double> _slopeErrors; // Slope error as multipliers

		// Maintenance parameters
		private double _filterCondition; // 0-100%
		private DateTime _lastFilterReplacement;
		private DateTime _lastMaintenanceDate;

		public GasAnalyzer(
			string deviceId,
			string name,
			GasAnalyzerType analyzerType = GasAnalyzerType.MultiGas,
			List<GasType> gasTypes = null)
			: base(deviceId, name)
		{
			AnalyzerType = analyzerType;

			// Initialize dictionaries
			GasConcentrations = new Dictionary<GasType, double>();
			ActualGasConcentrations = new Dictionary<GasType, double>();
			MinRanges = new Dictionary<GasType, double>();
			MaxRanges = new Dictionary<GasType, double>();
			Accuracies = new Dictionary<GasType, double>();
			_driftRates = new Dictionary<GasType, double>();
			_responseTimesSeconds = new Dictionary<GasType, double>();
			_sensorAges = new Dictionary<GasType, double>();
			_crossSensitivities = new Dictionary<GasType, double>();
			_temperatureCoefficients = new Dictionary<GasType, double>();
			_lastCalibrations = new Dictionary<GasType, DateTime>();
			_zeroOffsets = new Dictionary<GasType, double>();
			_slopeErrors = new Dictionary<GasType, double>();

			// Set default gas types if none provided
			if (gasTypes == null || gasTypes.Count == 0)
			{
				gasTypes = new List<GasType> { GasType.O2, GasType.CO2 };
			}

			// Set up default parameters for each gas
			foreach (var gas in gasTypes)
			{
				// Default concentration is atmospheric for O2, small for others
				double defaultConcentration = gas == GasType.O2 ? 20.9 : 0.5;
				GasConcentrations[gas] = defaultConcentration;
				ActualGasConcentrations[gas] = defaultConcentration;

				// Set ranges and accuracy based on gas type
				switch (gas)
				{
					case GasType.O2:
						MinRanges[gas] = 0.0;
						MaxRanges[gas] = 25.0; // %
						Accuracies[gas] = 0.2; // ±0.2% absolute
						_driftRates[gas] = 0.05; // 0.05% per month
						_responseTimesSeconds[gas] = 10.0; // 10 seconds
						break;

					case GasType.CO2:
						MinRanges[gas] = 0.0;
						MaxRanges[gas] = 20.0; // %
						Accuracies[gas] = 0.1; // ±0.1% absolute
						_driftRates[gas] = 0.1; // 0.1% per month
						_responseTimesSeconds[gas] = 15.0; // 15 seconds
						break;

					case GasType.CO:
						MinRanges[gas] = 0.0;
						MaxRanges[gas] = 1000.0; // ppm
						Accuracies[gas] = 5.0; // ±5 ppm
						_driftRates[gas] = 1.0; // 1 ppm per month
						_responseTimesSeconds[gas] = 20.0; // 20 seconds
						break;

					case GasType.CH4:
						MinRanges[gas] = 0.0;
						MaxRanges[gas] = 5.0; // %
						Accuracies[gas] = 0.05; // ±0.05%
						_driftRates[gas] = 0.02; // 0.02% per month
						_responseTimesSeconds[gas] = 15.0; // 15 seconds
						break;

					case GasType.H2:
						MinRanges[gas] = 0.0;
						MaxRanges[gas] = 4.0; // %
						Accuracies[gas] = 0.05; // ±0.05%
						_driftRates[gas] = 0.03; // 0.03% per month
						_responseTimesSeconds[gas] = 12.0; // 12 seconds
						break;

					default:
						MinRanges[gas] = 0.0;
						MaxRanges[gas] = 1.0; // %
						Accuracies[gas] = 0.01; // ±0.01%
						_driftRates[gas] = 0.01; // 0.01% per month
						_responseTimesSeconds[gas] = 20.0; // 20 seconds
						break;
				}

				// Initialize other parameters
				_sensorAges[gas] = 0.0; // New sensors
				_crossSensitivities[gas] = 0.0; // No cross sensitivity yet
				_temperatureCoefficients[gas] = 0.01; // 0.01% per °C
				_lastCalibrations[gas] = DateTime.Now;
				_zeroOffsets[gas] = 0.0;
				_slopeErrors[gas] = 1.0; // Perfect slope
			}

			// Initialize common parameters
			Temperature = 25.0;
			FlowRate = 1.0; // 1 L/min
			Pressure = 1.013; // 1.013 bar (atmospheric)
			_filterCondition = 100.0; // New filter
			_lastFilterReplacement = DateTime.Now;
			_lastMaintenanceDate = DateTime.Now;

			// Update diagnostic data
			DiagnosticData["AnalyzerType"] = AnalyzerType.ToString();
			DiagnosticData["GasCount"] = gasTypes.Count;
			DiagnosticData["Temperature"] = Temperature;
			DiagnosticData["FlowRate"] = FlowRate;
			DiagnosticData["Pressure"] = Pressure;
			DiagnosticData["FilterCondition"] = _filterCondition;
			DiagnosticData["LastFilterReplacement"] = _lastFilterReplacement;
			DiagnosticData["LastMaintenance"] = _lastMaintenanceDate;

			// Add gas-specific diagnostics
			foreach (var gas in gasTypes)
			{
				DiagnosticData[$"{gas}_Range"] = $"{MinRanges[gas]} - {MaxRanges[gas]}";
				DiagnosticData[$"{gas}_Accuracy"] = $"±{Accuracies[gas]}";
				DiagnosticData[$"{gas}_LastCalibration"] = _lastCalibrations[gas];
				DiagnosticData[$"{gas}_SensorAge"] = _sensorAges[gas];
			}
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Update filter condition
			_filterCondition -= 0.001 * elapsedTime.TotalHours; // 0.1% degradation per 100 hours
			_filterCondition = Math.Max(0, _filterCondition);

			// Update each gas measurement
			foreach (var gas in GasConcentrations.Keys.ToArray())
			{
				// Calculate months since calibration for drift calculation
				double monthsSinceCalibration = (DateTime.Now - _lastCalibrations[gas]).TotalDays / 30.0;
				double drift = _driftRates[gas] * monthsSinceCalibration;

				// Apply response time (first-order lag)
				double responseRate = 1.0 - Math.Exp(-elapsedTime.TotalSeconds / _responseTimesSeconds[gas]);
				double filteredConcentration = GasConcentrations[gas] + (ActualGasConcentrations[gas] - GasConcentrations[gas]) * responseRate;

				// Apply temperature effects
				double temperatureDeviation = Temperature - 25.0; // Deviation from standard 25°C
				double temperatureEffect = filteredConcentration * (_temperatureCoefficients[gas] / 100.0) * temperatureDeviation;

				// Apply sensor aging effects
				double agingEffect = (_sensorAges[gas] / 100.0) * 0.05 * MaxRanges[gas]; // Up to 5% of range for old sensor

				// Apply cross sensitivity effects (simplified)
				double crossSensitivityEffect = _crossSensitivities[gas] * 0.02 * MaxRanges[gas]; // Up to 2% of range

				// Apply flow and pressure effects
				double flowEffect = 0;
				double pressureEffect = 0;

				// Non-optimal flow can affect readings
				if (FlowRate < 0.5 || FlowRate > 2.0)
				{
					flowEffect = (Math.Abs(FlowRate - 1.0) / 0.5) * 0.02 * MaxRanges[gas]; // Up to 2% of range
				}

				// Pressure affects some analyzers more than others
				if (AnalyzerType == GasAnalyzerType.Infrared || AnalyzerType == GasAnalyzerType.Thermal)
				{
					pressureEffect = (Pressure - 1.013) * 0.05 * MaxRanges[gas]; // Up to 5% of range per bar
				}

				// Apply filter condition effect (dirty filter reduces sample flow/quality)
				double filterEffect = (100.0 - _filterCondition) / 100.0 * 0.03 * MaxRanges[gas]; // Up to 3% of range

				// Apply calibration errors
				double calibratedConcentration = (filteredConcentration + _zeroOffsets[gas]) * _slopeErrors[gas];

				// Calculate random noise based on accuracy
				double noise = (Random.NextDouble() * 2 - 1) * Accuracies[gas];

				// Calculate final gas concentration reading
				double newConcentration = calibratedConcentration + drift + temperatureEffect + agingEffect +
										 crossSensitivityEffect + flowEffect + pressureEffect + filterEffect + noise;

				// Ensure value is within valid range
				newConcentration = Math.Max(MinRanges[gas], Math.Min(MaxRanges[gas], newConcentration));
				GasConcentrations[gas] = newConcentration;

				// Update diagnostics for this gas
				DiagnosticData[$"{gas}_Concentration"] = newConcentration;
				DiagnosticData[$"{gas}_Drift"] = drift;

				// Slowly age the sensor over time
				_sensorAges[gas] += 0.001 * elapsedTime.TotalHours; // 0.1% aging per 100 hours
				DiagnosticData[$"{gas}_SensorAge"] = _sensorAges[gas];

				// Check for sensor conditions
				if (_sensorAges[gas] > 80)
				{
					AddAlarm($"{gas}_SENSOR_OLD", $"{gas} sensor nearing end of life", AlarmSeverity.Warning);
				}

				if (monthsSinceCalibration > 6)
				{
					AddAlarm($"{gas}_CAL_DUE", $"{gas} sensor calibration overdue", AlarmSeverity.Warning);
				}
			}

			// Update filter condition diagnostics
			DiagnosticData["FilterCondition"] = _filterCondition;

			// Check for filter condition
			if (_filterCondition < 30)
			{
				AddAlarm("FILTER_DIRTY", "Gas analyzer filter needs replacement", AlarmSeverity.Warning);
			}
		}

		/// <summary>
		/// Set the actual process gas concentration (for simulation)
		/// </summary>
		public void SetProcessGasConcentration(GasType gas, double concentration)
		{
			if (ActualGasConcentrations.ContainsKey(gas))
			{
				ActualGasConcentrations[gas] = Math.Max(MinRanges[gas], Math.Min(MaxRanges[gas], concentration));
			}
		}

		/// <summary>
		/// Set the sample temperature
		/// </summary>
		public void SetTemperature(double temperature)
		{
			Temperature = Math.Max(0, Math.Min(80, temperature));
			DiagnosticData["Temperature"] = Temperature;
		}

		/// <summary>
		/// Set the sample flow rate
		/// </summary>
		public void SetFlowRate(double flowRate)
		{
			FlowRate = Math.Max(0, Math.Min(5, flowRate));
			DiagnosticData["FlowRate"] = FlowRate;
		}

		/// <summary>
		/// Set the sample pressure
		/// </summary>
		public void SetPressure(double pressure)
		{
			Pressure = Math.Max(0.8, Math.Min(1.5, pressure));
			DiagnosticData["Pressure"] = Pressure;
		}

		/// <summary>
		/// Calibrate a specific gas sensor
		/// </summary>
		public void CalibrateGas(GasType gas, double zeroGas, double spanGas)
		{
			if (!GasConcentrations.ContainsKey(gas))
				return;

			_lastCalibrations[gas] = DateTime.Now;

			// Calculate new calibration parameters
			_zeroOffsets[gas] = (Random.NextDouble() * 2 - 1) * 0.005 * MaxRanges[gas]; // Small zero offset
			_slopeErrors[gas] = 0.98 + (Random.NextDouble() * 0.04); // 98-102% of ideal slope

			DiagnosticData[$"{gas}_LastCalibration"] = _lastCalibrations[gas];
			DiagnosticData[$"{gas}_ZeroOffset"] = _zeroOffsets[gas];
			DiagnosticData[$"{gas}_SlopeError"] = _slopeErrors[gas];

			AddAlarm($"{gas}_CALIBRATION", $"{gas} sensor calibrated", AlarmSeverity.Information);
		}

		/// <summary>
		/// Replace a specific gas sensor
		/// </summary>
		public void ReplaceGasSensor(GasType gas)
		{
			if (!GasConcentrations.ContainsKey(gas))
				return;

			_sensorAges[gas] = 0.0; // New sensor
			_lastCalibrations[gas] = DateTime.Now;

			// New sensors typically have values close to ideal
			_zeroOffsets[gas] = (Random.NextDouble() * 2 - 1) * 0.002 * MaxRanges[gas]; // Very small zero offset
			_slopeErrors[gas] = 0.99 + (Random.NextDouble() * 0.02); // 99-101% of ideal slope

			DiagnosticData[$"{gas}_SensorAge"] = _sensorAges[gas];
			DiagnosticData[$"{gas}_LastCalibration"] = _lastCalibrations[gas];
			DiagnosticData[$"{gas}_ZeroOffset"] = _zeroOffsets[gas];
			DiagnosticData[$"{gas}_SlopeError"] = _slopeErrors[gas];

			AddAlarm($"{gas}_SENSOR_REPLACED", $"{gas} sensor replaced", AlarmSeverity.Information);
		}

		/// <summary>
		/// Replace the sample filter
		/// </summary>
		public void ReplaceFilter()
		{
			_filterCondition = 100.0;
			_lastFilterReplacement = DateTime.Now;

			DiagnosticData["FilterCondition"] = _filterCondition;
			DiagnosticData["LastFilterReplacement"] = _lastFilterReplacement;

			AddAlarm("FILTER_REPLACED", "Gas analyzer filter replaced", AlarmSeverity.Information);
		}

		/// <summary>
		/// Perform routine maintenance
		/// </summary>
		public void PerformMaintenance()
		{
			_lastMaintenanceDate = DateTime.Now;

			// Maintenance improves some parameters slightly
			foreach (var gas in GasConcentrations.Keys)
			{
				// Small improvement in cross-sensitivities
				_crossSensitivities[gas] *= 0.9;

				// Small improvement in drift rates
				_driftRates[gas] *= 0.95;
			}

			DiagnosticData["LastMaintenance"] = _lastMaintenanceDate;

			AddAlarm("MAINTENANCE_DONE", "Gas analyzer maintenance performed", AlarmSeverity.Information);
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(6);

			switch (faultType)
			{
				case 0: // Sample flow issue
					AddAlarm("FLOW_BLOCKED", "Sample flow blocked or restricted", AlarmSeverity.Major);
					FlowRate = 0.1; // Almost no flow
					break;

				case 1: // Condensation in sample lines
					AddAlarm("CONDENSATION", "Condensation in sample line detected", AlarmSeverity.Minor);
					// Affect all gases
					foreach (var gas in GasConcentrations.Keys)
					{
						GasConcentrations[gas] = ActualGasConcentrations[gas] * 0.7; // Reading 30% low
					}
					break;

				case 2: // Specific sensor failure
						// Pick a random gas to fail
					GasType failedGas = GasConcentrations.Keys.ElementAt(Random.Next(GasConcentrations.Count));
					AddAlarm($"{failedGas}_FAILURE", $"{failedGas} sensor failure", AlarmSeverity.Critical);
					GasConcentrations[failedGas] = 0; // Reading zero
					break;

				case 3: // Cross-sensitivity issue
						// Pick two random gases for cross-sensitivity
					if (GasConcentrations.Count >= 2)
					{
						int idx1 = Random.Next(GasConcentrations.Count);
						int idx2 = (idx1 + 1) % GasConcentrations.Count;

						GasType gas1 = GasConcentrations.Keys.ElementAt(idx1);
						GasType gas2 = GasConcentrations.Keys.ElementAt(idx2);

						AddAlarm("CROSS_SENSITIVITY", $"Cross-sensitivity between {gas1} and {gas2} detected", AlarmSeverity.Warning);

						// Increase cross-sensitivity
						_crossSensitivities[gas1] = 0.05;
						_crossSensitivities[gas2] = 0.05;
					}
					else
					{
						// Fall back to something else if we don't have enough gases
						AddAlarm("CALIBRATION_FAULT", "Calibration fault detected", AlarmSeverity.Warning);
						foreach (var gas in GasConcentrations.Keys)
						{
							_zeroOffsets[gas] = MaxRanges[gas] * 0.05; // 5% of range zero offset
						}
					}
					break;

				case 4: // Power supply issues
					AddAlarm("POWER_FLUCTUATION", "Power supply fluctuation detected", AlarmSeverity.Minor);
					// Cause all readings to fluctuate
					foreach (var gas in GasConcentrations.Keys)
					{
						GasConcentrations[gas] = ActualGasConcentrations[gas] * (0.8 + Random.NextDouble() * 0.4); // ±20% of actual
					}
					break;

				case 5: // Pressure regulator issue
					AddAlarm("PRESSURE_REGULATOR", "Sample pressure regulator failure", AlarmSeverity.Major);
					Pressure = 1.4; // High pressure
					break;
			}
		}
	}

	public enum GasAnalyzerType
	{
		Infrared,         // NDIR for CO2, CH4, etc.
		Paramagnetic,     // For O2
		Electrochemical,  // For O2, CO, etc.
		Thermal,          // Thermal conductivity
		MultiGas          // Combined technologies
	}

	public enum GasType
	{
		O2,   // Oxygen
		CO2,  // Carbon dioxide
		CO,   // Carbon monoxide
		CH4,  // Methane
		H2,   // Hydrogen
		N2,   // Nitrogen
		Ar,   // Argon
		He    // Helium
	}
}