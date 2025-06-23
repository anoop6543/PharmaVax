using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
	public class PressureTransmitter : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		public double Pressure { get; private set; } // Current pressure in bar
		public double MinPressure { get; private set; } // Minimum range in bar
		public double MaxPressure { get; private set; } // Maximum range in bar
		public double Accuracy { get; private set; } // Accuracy in % of span
		public bool HasHartSupport { get; private set; } // Whether the device supports HART protocol
		public string SensorType { get; private set; } // Type of sensor (e.g., "Sanitary", "Differential", "Standard")

		// Internal process variables
		private double _actualPressure; // The "real" pressure (for simulation)
		private double _damping; // Damping in seconds
		private double _drift; // Sensor drift per day in % of span
		private double _driftAccumulated; // Accumulated drift since calibration
		private DateTime _lastCalibration;
		private bool _isDifferential;
		private double _secondaryPressure; // For differential pressure sensors

		public PressureTransmitter(
			string deviceId,
			string name,
			double minPressure,
			double maxPressure,
			string sensorType = "Standard",
			double accuracy = 0.1,
			bool hasHartSupport = true,
			bool isDifferential = false)
			: base(deviceId, name)
		{
			MinPressure = minPressure;
			MaxPressure = maxPressure;
			SensorType = sensorType;
			Accuracy = accuracy;
			HasHartSupport = hasHartSupport;
			_isDifferential = isDifferential;

			// Initialize with ambient pressure by default
			_actualPressure = 1.01325; // Standard atmospheric pressure in bar
			Pressure = _actualPressure;

			// Initial settings
			_damping = 0.5; // 0.5 seconds damping
			_drift = 0.01; // 0.01% drift per day
			_driftAccumulated = 0;
			_lastCalibration = DateTime.Now;
			_secondaryPressure = 0; // For differential pressure sensors

			DiagnosticData["Range"] = $"{MinPressure} to {MaxPressure} bar";
			DiagnosticData["Accuracy"] = $"±{Accuracy}% of span";
			DiagnosticData["SensorType"] = SensorType;
			DiagnosticData["HasHartSupport"] = HasHartSupport;
			DiagnosticData["IsDifferential"] = _isDifferential;
			DiagnosticData["Damping"] = _damping;
			DiagnosticData["LastCalibration"] = _lastCalibration;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Calculate time-based drift
			double daysSinceCalibration = (DateTime.Now - _lastCalibration).TotalDays;
			_driftAccumulated = _drift * daysSinceCalibration;

			// Apply damping to pressure changes
			double span = MaxPressure - MinPressure;
			double dampingFactor = 1.0 - Math.Exp(-elapsedTime.TotalSeconds / _damping);

			double targetPressure = _actualPressure;
			if (_isDifferential)
			{
				targetPressure = _actualPressure - _secondaryPressure;
			}

			// Apply damping
			double dampedPressure = Pressure + (targetPressure - Pressure) * dampingFactor;

			// Add noise and accuracy error
			double accuracyError = (Random.NextDouble() * 2 - 1) * (Accuracy / 100.0) * span;
			double noise = (Random.NextDouble() * 2 - 1) * 0.0001 * span; // 0.01% noise

			// Add drift
			double drift = (_driftAccumulated / 100.0) * span;

			// Calculate final pressure value
			Pressure = dampedPressure + accuracyError + noise + drift;

			// Ensure within range limits
			Pressure = Math.Max(MinPressure, Math.Min(MaxPressure, Pressure));

			// Update diagnostic data
			DiagnosticData["Pressure"] = Pressure;
			DiagnosticData["DriftAccumulated"] = _driftAccumulated;

			// Check for abnormal conditions
			if (Pressure <= MinPressure + (span * 0.02))
			{
				AddAlarm("LOW_PRESSURE", "Pressure at or below minimum range", AlarmSeverity.Warning);
			}
			else if (Pressure >= MaxPressure - (span * 0.02))
			{
				AddAlarm("HIGH_PRESSURE", "Pressure at or above maximum range", AlarmSeverity.Warning);
			}

			// Check for drift beyond acceptable limit
			if (Math.Abs(_driftAccumulated) > 0.5) // 0.5% drift is concerning
			{
				AddAlarm("EXCESSIVE_DRIFT", "Excessive sensor drift detected", AlarmSeverity.Minor);
			}
		}

		/// <summary>
		/// Set the actual process pressure (for simulation)
		/// </summary>
		public void SetProcessPressure(double pressure)
		{
			_actualPressure = pressure;
		}

		/// <summary>
		/// For differential pressure sensors, set the secondary pressure
		/// </summary>
		public void SetSecondaryPressure(double pressure)
		{
			if (_isDifferential)
			{
				_secondaryPressure = pressure;
			}
		}

		/// <summary>
		/// Set the damping time in seconds
		/// </summary>
		public void SetDamping(double seconds)
		{
			_damping = Math.Max(0, Math.Min(60, seconds)); // Limit to 0-60 seconds range
			DiagnosticData["Damping"] = _damping;
		}

		/// <summary>
		/// Recalibrate the sensor (reset drift)
		/// </summary>
		public void Calibrate()
		{
			_lastCalibration = DateTime.Now;
			_driftAccumulated = 0;
			DiagnosticData["LastCalibration"] = _lastCalibration;
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(4);

			switch (faultType)
			{
				case 0: // Sensor disconnection
					AddAlarm("SENSOR_DISCONNECT", "Pressure transmitter disconnected", AlarmSeverity.Major);
					Pressure = MinPressure - 1; // Below range
					break;

				case 1: // Impulse line blockage (affects reading)
					AddAlarm("IMPULSE_LINE_BLOCK", "Impulse line blockage suspected", AlarmSeverity.Minor);
					// Pressure reading gets "stuck"
					break;

				case 2: // Large pressure spike
					AddAlarm("PRESSURE_SPIKE", "Abnormal pressure spike detected", AlarmSeverity.Warning);
					Pressure = MaxPressure * 0.95;
					break;

				case 3: // HART communication error
					if (HasHartSupport)
					{
						AddAlarm("HART_COMM_ERROR", "HART communication failure", AlarmSeverity.Warning);
						// Pressure reading continues but no HART data
					}
					break;
			}
		}
	}
}