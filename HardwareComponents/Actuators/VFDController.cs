using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Actuators
{
	public class VFDController : DeviceBase
	{
		public override DeviceType Type => DeviceType.Actuator;

		public double Speed { get; private set; } // Current speed in percentage (0-100%)
		public double TargetSpeed { get; private set; } // Target speed in percentage
		public double RampRate { get; private set; } // Acceleration/deceleration rate in %/second
		public double PowerConsumption { get; private set; } // Power in kW
		public double Current { get; private set; } // Current in Amps
		public double MaxPower { get; private set; } // Maximum power in kW
		public double MaxSpeed { get; private set; } // Maximum speed in Hz
		public MotorControlMode ControlMode { get; private set; }

		private double _actualSpeed; // Physical speed (for simulation)
		private double _loadFactor = 1.0; // Load factor (1.0 = nominal)
		private double _efficiency = 0.95; // Motor efficiency
		private double _temperatureFactor = 1.0; // Temperature impact on efficiency

		public VFDController(
			string deviceId,
			string name,
			double maxPower,
			double maxSpeed = 60, // Default 60Hz
			double rampRate = 20.0, // Default 20%/second
			MotorControlMode controlMode = MotorControlMode.VHz)
			: base(deviceId, name)
		{
			MaxPower = maxPower;
			MaxSpeed = maxSpeed;
			RampRate = rampRate;
			ControlMode = controlMode;

			// Initialize at stopped position
			Speed = 0;
			_actualSpeed = 0;
			TargetSpeed = 0;
			PowerConsumption = 0;
			Current = 0;

			DiagnosticData["MaxPower"] = MaxPower;
			DiagnosticData["MaxSpeed"] = MaxSpeed;
			DiagnosticData["RampRate"] = RampRate;
			DiagnosticData["ControlMode"] = ControlMode.ToString();
			DiagnosticData["Efficiency"] = _efficiency * 100;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Calculate speed change based on ramp rate
			double speedDifference = TargetSpeed - _actualSpeed;
			double maxChange = RampRate * elapsedTime.TotalSeconds;
			double actualChange = Math.Sign(speedDifference) * Math.Min(Math.Abs(speedDifference), maxChange);

			// Update actual speed
			_actualSpeed += actualChange;

			// Apply small random fluctuations to simulate real motor behavior
			double fluctuation = (Random.NextDouble() * 2 - 1) * 0.5;
			Speed = Math.Min(Math.Max(_actualSpeed + fluctuation, 0), 100);

			// Calculate power consumption based on speed, efficiency, and load
			double speedFactor = _actualSpeed / 100.0;

			// Power curve depends on control mode
			double powerFactor;
			switch (ControlMode)
			{
				case MotorControlMode.VHz:
					// V/Hz has power roughly proportional to speed cubed (fan/pump law)
					powerFactor = Math.Pow(speedFactor, 3);
					break;
				case MotorControlMode.VectorControl:
					// Vector control is more linear in power vs speed
					powerFactor = speedFactor;
					break;
				default:
					powerFactor = Math.Pow(speedFactor, 2);
					break;
			}

			PowerConsumption = MaxPower * powerFactor * _loadFactor / (_efficiency * _temperatureFactor);

			// Calculate motor current (simplified)
			// P = V * I * PF * sqrt(3) for three-phase, where PF is power factor
			double voltage = 480; // Assume 480V
			double powerFactor = 0.85;
			Current = PowerConsumption * 1000 / (voltage * powerFactor * Math.Sqrt(3));

			// Update diagnostic data
			DiagnosticData["Speed"] = Speed;
			DiagnosticData["TargetSpeed"] = TargetSpeed;
			DiagnosticData["PowerConsumption"] = PowerConsumption;
			DiagnosticData["Current"] = Current;
			DiagnosticData["LoadFactor"] = _loadFactor;
			DiagnosticData["ActualFrequency"] = MaxSpeed * speedFactor;

			// Check for overload conditions
			if (PowerConsumption > MaxPower * 1.1)
			{
				AddAlarm("VFD_OVERLOAD", $"VFD power overload: {PowerConsumption:F2}kW", AlarmSeverity.Warning);
				_temperatureFactor = Math.Max(_temperatureFactor - 0.01 * elapsedTime.TotalSeconds, 0.8);
			}
			else
			{
				_temperatureFactor = Math.Min(_temperatureFactor + 0.005 * elapsedTime.TotalSeconds, 1.0);
			}

			// Check for current anomalies
			if (Current > 0 && Speed < 1.0)
			{
				AddAlarm("VFD_STALL", "Motor stall detected", AlarmSeverity.Major);
			}
		}

		public void SetSpeed(double targetSpeed)
		{
			// Clamp value between 0-100%
			TargetSpeed = Math.Min(Math.Max(targetSpeed, 0), 100);
		}

		public void SetLoadFactor(double loadFactor)
		{
			_loadFactor = Math.Max(loadFactor, 0);
		}

		public void EmergencyStop()
		{
			TargetSpeed = 0;
			RampRate *= 3; // Faster deceleration for emergency
			AddAlarm("VFD_EMERGENCY_STOP", "Emergency stop activated", AlarmSeverity.Major);
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(4);

			switch (faultType)
			{
				case 0: // Overheating
					_temperatureFactor = 0.7;
					AddAlarm("VFD_OVERHEAT", "VFD overheating detected", AlarmSeverity.Minor);
					break;
				case 1: // Phase loss
					AddAlarm("VFD_PHASE_LOSS", "Input phase loss detected", AlarmSeverity.Major);
					PowerConsumption *= 1.5;
					Current *= 1.7;
					break;
				case 2: // Ground fault
					AddAlarm("VFD_GROUND_FAULT", "Ground fault detected", AlarmSeverity.Critical);
					Status = DeviceStatus.Fault;
					TargetSpeed = 0;
					break;
				case 3: // Communication error
					AddAlarm("VFD_COMM_ERROR", "Communication error", AlarmSeverity.Warning);
					// The VFD continues to run at its last setpoint
					break;
			}
		}
	}

	public enum MotorControlMode
	{
		VHz,                // Simple V/Hz control
		VectorControl,      // Vector control (sensorless or with encoder)
		TorqueControl,      // Direct torque control
		ServoDrive          // Servo control (position, velocity, torque)
	}
}