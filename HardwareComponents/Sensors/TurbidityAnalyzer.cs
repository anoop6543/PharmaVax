using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
	/// <summary>
	/// Simulates a turbidity analyzer for measuring suspended particles in liquid
	/// </summary>
	public class TurbidityAnalyzer : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		// Primary measurement values
		public double Turbidity { get; private set; } // Current turbidity in NTU
		public double ActualTurbidity { get; private set; } // Actual process turbidity (for simulation)
		public double Temperature { get; private set; } // Sample temperature in Celsius

		// Sensor specifications
		public TurbidityMeasurementMethod MeasurementMethod { get; private set; }
		public double MinTurbidity { get; private set; }
		public double MaxTurbidity { get; private set; }
		public double Accuracy { get; private set; } // ±% of reading or ±NTU absolute
		public bool IsAccuracyRelative { get; private set; } // true = % of reading, false = NTU absolute

		// Internal parameters
		private double _lightSourceIntensity; // 0-100% of original intensity
		private double _opticalSurfaceClarity; // 0-100%
		private double _driftRate; // NTU per month
		private double _responseTime; // in seconds
		private DateTime _lastCalibration;
		private DateTime _lastCleaningDate;
		private bool _hasAutoBubbleRejection;
		private double _bubbleProbability; // 0-1, probability of bubbles affecting reading

		// Calibration parameters
		private double _zeroOffset; // Zero offset in NTU
		private double _slopeError; // Slope error as multiplier

		public TurbidityAnalyzer(
			string deviceId,
			string name,
			TurbidityMeasurementMethod method = TurbidityMeasurementMethod.Nephelometric,
			double minTurbidity = 0.0,
			double maxTurbidity = 1000.0,
			double accuracy = 2.0,
			bool isAccuracyRelative = true)
			: base(deviceId, name)
		{
			MeasurementMethod = method;
			MinTurbidity = minTurbidity;
			MaxTurbidity = maxTurbidity;
			Accuracy = accuracy;
			IsAccuracyRelative = isAccuracyRelative;

			// Initialize to low turbidity (typical for pharmaceutical water)
			Turbidity = 0.5;
			ActualTurbidity = 0.5;
			Temperature = 25.0;

			// Set initial parameters
			_lightSourceIntensity = 100.0; // New light source
			_opticalSurfaceClarity = 100.0; // Clean optics
			_lastCalibration = DateTime.Now;
			_lastCleaningDate = DateTime.Now;

			// Set parameters based on measurement method
			switch (method)
			{
				case TurbidityMeasurementMethod.Nephelometric:
					_responseTime = 3.0; // 3 seconds
					_driftRate = 0.05; // 0.05 NTU per month
					_hasAutoBubbleRejection = true;
					break;

				case TurbidityMeasurementMethod.Transmission:
					_responseTime = 2.0; // 2 seconds
					_driftRate = 0.1; // 0.1 NTU per month
					_hasAutoBubbleRejection = false;
					break;

				case TurbidityMeasurementMethod.RatioBased:
					_responseTime = 4.0; // 4 seconds
					_driftRate = 0.02; // 0.02 NTU per month - most stable
					_hasAutoBubbleRejection = true;
					break;
			}

			_bubbleProbability = 0.01; // 1% chance of bubbles per update

			// Calibration parameters
			_zeroOffset = 0.0;
			_slopeError = 1.0; // Perfect slope

			// Update diagnostic data
			DiagnosticData["MeasurementMethod"] = method.ToString();
			DiagnosticData["Range"] = $"{MinTurbidity} to {MaxTurbidity} NTU";
			DiagnosticData["Accuracy"] = IsAccuracyRelative ?
				$"±{Accuracy}% of reading" : $"±{Accuracy} NTU";
			DiagnosticData["LightSourceIntensity"] = _lightSourceIntensity;
			DiagnosticData["OpticalSurfaceClarity"] = _opticalSurfaceClarity;
			DiagnosticData["LastCalibration"] = _lastCalibration;
			DiagnosticData["LastCleaning"] = _lastCleaningDate;
			DiagnosticData["HasAutoBubbleRejection"] = _hasAutoBubbleRejection;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Calculate months since calibration for drift calculation
			double monthsSinceCalibration = (DateTime.Now - _lastCalibration).TotalDays / 30.0;
			double drift = _driftRate * monthsSinceCalibration;

			// Apply response time (first-order lag)
			double responseRate = 1.0 - Math.Exp(-elapsedTime.TotalSeconds / _responseTime);
			double filteredTurbidity = Turbidity + (ActualTurbidity - Turbidity) * responseRate;

			// Apply light source aging effect (more pronounced in transmission method)
			double lightSourceEffect = 0;
			if (MeasurementMethod == TurbidityMeasurementMethod.Transmission)
			{
				// Transmission method is more affected by light intensity
				lightSourceEffect = (100.0 - _lightSourceIntensity) / 100.0 * 0.2 * MaxTurbidity;
			}
			else
			{
				// Nephelometric and ratio are less affected
				lightSourceEffect = (100.0 - _lightSourceIntensity) / 100.0 * 0.05 * MaxTurbidity;
			}

			// Apply optical surface fouling (all methods affected)
			double opticalFoulingEffect = (100.0 - _opticalSurfaceClarity) / 100.0;

			// Different methods are affected differently by optical fouling
			switch (MeasurementMethod)
			{
				case TurbidityMeasurementMethod.Nephelometric:
					opticalFoulingEffect *= 0.3 * MaxTurbidity; // Up to 30% of range for severe fouling
					break;
				case TurbidityMeasurementMethod.Transmission:
					opticalFoulingEffect *= 0.5 * MaxTurbidity; // Up to 50% of range
					break;
				case TurbidityMeasurementMethod.RatioBased:
					// Ratio method is least affected by fouling due to internal compensation
					opticalFoulingEffect *= 0.1 * MaxTurbidity; // Only up to 10% of range
					break;
			}

			// Simulate bubble effects (random bubble occurrence)
			double bubbleEffect = 0;
			if (Random.NextDouble() < _bubbleProbability * elapsedTime.TotalSeconds)
			{
				// Bubble detected
				if (_hasAutoBubbleRejection)
				{
					// Auto rejection minimizes but doesn't eliminate the effect
					bubbleEffect = Random.NextDouble() * 0.5; // Small effect
				}
				else
				{
					// No auto rejection causes significant spikes
					bubbleEffect = Random.NextDouble() * 5.0 + 1.0; // Large effect
				}
			}

			// Apply calibration errors
			double calibratedTurbidity = (filteredTurbidity + _zeroOffset) * _slopeError;

			// Calculate random noise based on accuracy
			double noiseAmplitude;
			if (IsAccuracyRelative)
			{
				// Noise based on % of reading
				noiseAmplitude = (Accuracy / 100.0) * calibratedTurbidity;
			}
			else
			{
				// Noise based on absolute NTU value
				noiseAmplitude = Accuracy;
			}

			double noise = (Random.NextDouble() * 2 - 1) * noiseAmplitude;

			// Calculate final turbidity reading
			Turbidity = calibratedTurbidity + drift + lightSourceEffect + opticalFoulingEffect + bubbleEffect + noise;

			// Ensure value is within valid range
			Turbidity = Math.Max(MinTurbidity, Math.Min(MaxTurbidity, Turbidity));

			// Update diagnostics
			DiagnosticData["Turbidity"] = Turbidity;
			DiagnosticData["Temperature"] = Temperature;
			DiagnosticData["Drift"] = drift;
			DiagnosticData["LightSourceIntensity"] = _lightSourceIntensity;
			DiagnosticData["OpticalSurfaceClarity"] = _opticalSurfaceClarity;

			// Gradually degrade light source and optical clarity
			_lightSourceIntensity -= 0.001 * elapsedTime.TotalHours; // 0.1% degradation per 100 hours
			_opticalSurfaceClarity -= 0.005 * elapsedTime.TotalHours; // 0.5% fouling per 100 hours

			// Keep values within bounds
			_lightSourceIntensity = Math.Max(0, _lightSourceIntensity);
			_opticalSurfaceClarity = Math.Max(0, _opticalSurfaceClarity);

			// Check for maintenance conditions
			if (_lightSourceIntensity < 50)
			{
				AddAlarm("LIGHT_SOURCE_LOW", "Turbidity analyzer light source intensity low", AlarmSeverity.Warning);
			}

			if (_opticalSurfaceClarity < 70)
			{
				AddAlarm("OPTICAL_FOULING", "Turbidity analyzer optical surfaces fouled", AlarmSeverity.Minor);
			}

			if (monthsSinceCalibration > 3)
			{
				AddAlarm("CALIBRATION_DUE", "Turbidity analyzer calibration overdue", AlarmSeverity.Warning);
			}
		}

		/// <summary>
		/// Set the actual process turbidity (for simulation)
		/// </summary>
		public void SetProcessTurbidity(double turbidity)
		{
			ActualTurbidity = Math.Max(MinTurbidity, Math.Min(MaxTurbidity, turbidity));
		}

		/// <summary>
		/// Set the sample temperature
		/// </summary>
		public void SetTemperature(double temperature)
		{
			Temperature = Math.Max(0, Math.Min(100, temperature));
			DiagnosticData["Temperature"] = Temperature;
		}

		/// <summary>
		/// Calibrate the turbidity analyzer
		/// </summary>
		public void Calibrate(double zeroStandard, double spanStandard)
		{
			_lastCalibration = DateTime.Now;

			// Calculate new calibration parameters
			_zeroOffset = (Random.NextDouble() * 2 - 1) * 0.1; // ±0.1 NTU zero offset
			_slopeError = 0.98 + (Random.NextDouble() * 0.04); // 98-102% of ideal slope

			DiagnosticData["LastCalibration"] = _lastCalibration;
			DiagnosticData["ZeroOffset"] = _zeroOffset;
			DiagnosticData["SlopeError"] = _slopeError;

			AddAlarm("CALIBRATION", "Turbidity analyzer calibrated", AlarmSeverity.Information);
		}

		/// <summary>
		/// Clean the optical surfaces
		/// </summary>
		public void CleanOptics()
		{
			_lastCleaningDate = DateTime.Now;
			_opticalSurfaceClarity = 95 + Random.NextDouble() * 5; // 95-100% clean

			DiagnosticData["LastCleaning"] = _lastCleaningDate;
			DiagnosticData["OpticalSurfaceClarity"] = _opticalSurfaceClarity;

			AddAlarm("OPTICS_CLEANED", "Turbidity analyzer optics cleaned", AlarmSeverity.Information);
		}

		/// <summary>
		/// Replace the light source
		/// </summary>
		public void ReplaceLightSource()
		{
			_lightSourceIntensity = 95 + Random.NextDouble() * 5; // 95-100% intensity

			DiagnosticData["LightSourceIntensity"] = _lightSourceIntensity;
			DiagnosticData["LightSourceReplacement"] = DateTime.Now;

			AddAlarm("LIGHT_SOURCE_REPLACED", "Turbidity analyzer light source replaced", AlarmSeverity.Information);
		}

		/// <summary>
		/// Enable or disable auto bubble rejection
		/// </summary>
		public void SetAutoBubbleRejection(bool enabled)
		{
			_hasAutoBubbleRejection = enabled;
			DiagnosticData["HasAutoBubbleRejection"] = enabled;
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(5);

			switch (faultType)
			{
				case 0: // Light source failure
					AddAlarm("LIGHT_FAILURE", "Light source failure", AlarmSeverity.Critical);
					_lightSourceIntensity = 5.0; // Almost no light
					break;

				case 1: // Severe optical fouling
					AddAlarm("SEVERE_FOULING", "Severe optical fouling detected", AlarmSeverity.Major);
					_opticalSurfaceClarity = 10.0; // Heavily fouled
					break;

				case 2: // Air bubble stuck in flow cell
					AddAlarm("STUCK_BUBBLE", "Air bubble trapped in flow cell", AlarmSeverity.Minor);
					Turbidity = ActualTurbidity * 3.5; // Reading much higher than actual
					_bubbleProbability = 0.8; // High probability of bubbles
					break;

				case 3: // Calibration drift
					AddAlarm("CALIBRATION_DRIFT", "Significant calibration drift detected", AlarmSeverity.Warning);
					_zeroOffset = 2.0; // Significant zero offset
					_slopeError = 1.2; // 20% slope error
					break;

				case 4: // Flow cell crack or leak
					AddAlarm("FLOW_CELL_LEAK", "Possible flow cell leak detected", AlarmSeverity.Major);
					Turbidity = Random.NextDouble() * MaxTurbidity; // Erratic readings
					break;
			}
		}
	}

	public enum TurbidityMeasurementMethod
	{
		Nephelometric, // 90-degree scatter measurement (most common)
		Transmission,  // Direct light attenuation through sample
		RatioBased     // Combines multiple angles for greater accuracy
	}
}