using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
	public class TemperatureSensor : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		public double Temperature { get; private set; } // Current temperature in Celsius
		public double SetTemperature { get; private set; } // Target temperature (for simulation)
		public double MinTemperature { get; private set; }
		public double MaxTemperature { get; private set; }
		public double Accuracy { get; private set; } // Accuracy in °C
		public double Drift { get; private set; } // Drift per hour in °C
		public double Noise { get; private set; } // Random noise factor

		private double _rawTemperature; // Actual physical temperature (simulated)
		private double _driftOffset = 0; // Accumulated drift
		private DateTime _lastCalibration;

		public TemperatureSensor(
			string deviceId,
			string name,
			double minTemperature,
			double maxTemperature,
			double accuracy = 0.1,
			double drift = 0.01,
			double noise = 0.05)
			: base(deviceId, name)
		{
			MinTemperature = minTemperature;
			MaxTemperature = maxTemperature;
			Accuracy = accuracy;
			Drift = drift;
			Noise = noise;

			// Initialize with room temperature by default
			_rawTemperature = 22.0;
			Temperature = _rawTemperature;
			SetTemperature = _rawTemperature;

			_lastCalibration = DateTime.Now;

			DiagnosticData["Range"] = $"{MinTemperature}°C to {MaxTemperature}°C";
			DiagnosticData["Accuracy"] = $"±{Accuracy}°C";
			DiagnosticData["LastCalibration"] = _lastCalibration;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Simulate movement of physical temperature toward setpoint
			double diffToSetpoint = SetTemperature - _rawTemperature;
			_rawTemperature += diffToSetpoint * 0.1 * elapsedTime.TotalSeconds;

			// Calculate time-based drift
			double hoursSinceCalibration = (DateTime.Now - _lastCalibration).TotalHours;
			_driftOffset = Drift * hoursSinceCalibration;

			// Add noise and drift to the raw temperature
			double noiseComponent = (Random.NextDouble() * 2 - 1) * Noise;
			Temperature = _rawTemperature + _driftOffset + noiseComponent;

			// Enforce min/max range
			Temperature = Math.Min(Math.Max(Temperature, MinTemperature), MaxTemperature);

			// Update diagnostic data
			DiagnosticData["CurrentTemperature"] = Temperature;
			DiagnosticData["SetTemperature"] = SetTemperature;
			DiagnosticData["DriftOffset"] = _driftOffset;

			// Check for out-of-range conditions
			if (Math.Abs(Temperature - SetTemperature) > 5.0)
			{
				AddAlarm("TEMP_DEVIATION",
					$"Temperature deviation: {Temperature:F2}°C vs setpoint {SetTemperature:F2}°C",
					AlarmSeverity.Warning);
			}
		}

		public void SetTargetTemperature(double temperature)
		{
			SetTemperature = Math.Min(Math.Max(temperature, MinTemperature), MaxTemperature);
		}

		public void Calibrate()
		{
			_lastCalibration = DateTime.Now;
			_driftOffset = 0;
			DiagnosticData["LastCalibration"] = _lastCalibration;

			// Simulate improved accuracy after calibration
			Temperature = _rawTemperature + (Random.NextDouble() * 2 - 1) * (Accuracy / 2);
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Sensor disconnect
					AddAlarm("SENSOR_DISCONNECT", "Temperature sensor disconnected", AlarmSeverity.Major);
					Temperature = 0;
					break;
				case 1: // Reading stuck
					AddAlarm("SENSOR_FROZEN", "Temperature reading not changing", AlarmSeverity.Minor);
					// Temperature remains the same - "stuck"
					break;
				case 2: // Wild reading
					AddAlarm("SENSOR_ERRATIC", "Erratic temperature reading", AlarmSeverity.Warning);
					Temperature = _rawTemperature + (Random.NextDouble() * 20 - 10);
					break;
			}
		}
	}
}