using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.ProcessEquipment
{
	/// <summary>
	/// Simulates a centrifugation system used for cell separation in biopharmaceutical manufacturing
	/// </summary>
	public class CentrifugationSystem : DeviceBase
	{
		public override DeviceType Type => DeviceType.ProcessEquipment;

		// Core parameters
		public double CurrentSpeed { get; private set; } // Current RPM
		public double TargetSpeed { get; private set; } // Target RPM
		public double MaximumSpeed { get; private set; } // Maximum RPM
		public double CurrentGForce { get; private set; } // Current G-force
		public double Temperature { get; private set; } // Temperature in Celsius
		public double VibrationLevel { get; private set; } // Vibration level (0-100%)
		public double ImbalanceLevel { get; private set; } // Imbalance level (0-100%)
		public double SeparationEfficiency { get; private set; } // Separation efficiency (0-100%)

		// Rotor parameters
		public CentrifugeRotorType RotorType { get; private set; }
		public double RotorCapacity { get; private set; } // In liters
		public double RotorRadius { get; private set; } // In centimeters
		public double RotorWearLevel { get; private set; } // 0-100%
		public DateTime RotorInstallationDate { get; private set; }
		public int RotorCycleCount { get; private set; }

		// Process parameters
		public double CurrentVolume { get; private set; } // Current volume in rotor
		public double ProcessTime { get; private set; } // Current process time in minutes
		public double TargetProcessTime { get; private set; } // Target process time in minutes
		public CentrifugePhase CurrentPhase { get; private set; }
		public CentrifugeOperationMode OperationMode { get; private set; }

		// Connected systems
		private TemperatureSensor _temperatureSensor;
		private VibrationSensor _vibrationSensor;
		private PressureSensor _pressureSensor;
		private MotorController _driveMotor;
		private ValveController _feedValve;
		private ValveController _dischargeValve;
		private ValveController _wasteValve;
		private FlowMeter _feedFlowMeter;
		private FlowMeter _dischargeFlowMeter;

		// Internal state tracking
		private double _accelerationRate; // RPM per second
		private double _decelerationRate; // RPM per second
		private double _temperatureRiseRate; // °C per minute at max speed
		private double _imbalanceIncreaseRate; // % increase per minute
		private bool _doorLocked;
		private bool _brakesEngaged;
		private bool _coolingSytemActive;
		private double _maintenanceThreshold; // Hours before maintenance

		// Batch information
		private double _batchVolume;
		private double _feedDensity;
		private double _productDensity;
		private double _contaminantDensity;
		private double _initialContaminationLevel; // % of feed
		private double _processedVolume;

		public CentrifugationSystem(
			string deviceId,
			string name,
			CentrifugeRotorType rotorType,
			double maximumSpeed,
			double rotorCapacity,
			MotorController driveMotor = null,
			TemperatureSensor temperatureSensor = null,
			VibrationSensor vibrationSensor = null)
			: base(deviceId, name)
		{
			// Initialize rotor parameters
			RotorType = rotorType;
			MaximumSpeed = maximumSpeed;
			RotorCapacity = rotorCapacity;
			RotorInstallationDate = DateTime.Now;
			RotorCycleCount = 0;
			RotorWearLevel = 0;

			// Set rotor-specific parameters
			InitializeRotorParameters();

			// Initialize process parameters
			CurrentSpeed = 0;
			TargetSpeed = 0;
			CurrentGForce = 0;
			Temperature = 22.0; // Room temperature
			VibrationLevel = 0;
			ImbalanceLevel = 0;
			SeparationEfficiency = 0;
			CurrentVolume = 0;
			ProcessTime = 0;
			TargetProcessTime = 0;
			CurrentPhase = CentrifugePhase.Idle;
			OperationMode = CentrifugeOperationMode.Batch;

			// Initialize internal state
			_accelerationRate = 100; // RPM per second
			_decelerationRate = 50;  // RPM per second
			_temperatureRiseRate = 0.2; // °C per minute at max speed
			_imbalanceIncreaseRate = 0.05; // % increase per minute
			_doorLocked = false;
			_brakesEngaged = true;
			_coolingSytemActive = false;
			_maintenanceThreshold = 1000; // Hours

			// Connect external systems
			_driveMotor = driveMotor;
			_temperatureSensor = temperatureSensor;
			_vibrationSensor = vibrationSensor;

			// Set diagnostic data
			DiagnosticData["RotorType"] = RotorType.ToString();
			DiagnosticData["MaximumSpeed"] = MaximumSpeed;
			DiagnosticData["RotorCapacity"] = RotorCapacity;
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["RotorWearLevel"] = RotorWearLevel;
			DiagnosticData["RotorCycleCount"] = RotorCycleCount;
			DiagnosticData["OperationMode"] = OperationMode.ToString();
		}

		private void InitializeRotorParameters()
		{
			switch (RotorType)
			{
				case CentrifugeRotorType.FixedAngle:
					RotorRadius = 15.0; // cm
					_accelerationRate = 120; // RPM per second
					_decelerationRate = 60;  // RPM per second
					_temperatureRiseRate = 0.25; // °C per minute
					break;

				case CentrifugeRotorType.SwingingBucket:
					RotorRadius = 20.0; // cm
					_accelerationRate = 90; // RPM per second
					_decelerationRate = 45; // RPM per second
					_temperatureRiseRate = 0.2; // °C per minute
					break;

				case CentrifugeRotorType.ContinuousFlow:
					RotorRadius = 25.0; // cm
					_accelerationRate = 80; // RPM per second
					_decelerationRate = 40; // RPM per second
					_temperatureRiseRate = 0.15; // °C per minute
					break;

				case CentrifugeRotorType.ZonalRotor:
					RotorRadius = 22.0; // cm
					_accelerationRate = 100; // RPM per second
					_decelerationRate = 50; // RPM per second
					_temperatureRiseRate = 0.18; // °C per minute
					break;

				case CentrifugeRotorType.DensityGradient:
					RotorRadius = 18.0; // cm
					_accelerationRate = 110; // RPM per second
					_decelerationRate = 55; // RPM per second
					_temperatureRiseRate = 0.22; // °C per minute
					break;

				default:
					RotorRadius = 15.0; // cm
					_accelerationRate = 100; // RPM per second
					_decelerationRate = 50; // RPM per second
					_temperatureRiseRate = 0.2; // °C per minute
					break;
			}
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize connected systems
			_driveMotor?.Initialize();
			_temperatureSensor?.Initialize();
			_vibrationSensor?.Initialize();
			_pressureSensor?.Initialize();

			if (_feedValve != null) _feedValve.Initialize();
			if (_dischargeValve != null) _dischargeValve.Initialize();
			if (_wasteValve != null) _wasteValve.Initialize();

			if (_feedFlowMeter != null) _feedFlowMeter.Initialize();
			if (_dischargeFlowMeter != null) _dischargeFlowMeter.Initialize();

			// Reset current state
			CurrentSpeed = 0;
			TargetSpeed = 0;
			CurrentGForce = 0;
			Temperature = 22.0; // Room temperature
			VibrationLevel = 0;
			ImbalanceLevel = 0;
			SeparationEfficiency = 0;
			CurrentVolume = 0;
			ProcessTime = 0;

			// Set initial state
			CurrentPhase = CentrifugePhase.Idle;
			_doorLocked = false;
			_brakesEngaged = true;
			_coolingSytemActive = false;

			// Update diagnostic data
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["CurrentSpeed"] = CurrentSpeed;
			DiagnosticData["Temperature"] = Temperature;
			DiagnosticData["DoorLocked"] = _doorLocked;
			DiagnosticData["BrakesEngaged"] = _brakesEngaged;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Update process time
			ProcessTime += elapsedTime.TotalMinutes;

			// Get readings from sensors if available
			UpdateSensorReadings();

			// Update rotor speed based on current phase
			UpdateRotorSpeed(elapsedTime);

			// Calculate G-force based on speed
			CurrentGForce = CalculateGForce(CurrentSpeed);

			// Update temperature based on operation
			UpdateTemperature(elapsedTime);

			// Update vibration and imbalance levels
			UpdateVibrationAndImbalance(elapsedTime);

			// Update separation efficiency
			UpdateSeparationEfficiency();

			// Process based on current phase
			ProcessCurrentPhase(elapsedTime);

			// Check for phase completion
			CheckPhaseCompletion();

			// Check for alarms
			CheckAlarmConditions();

			// Update rotor wear
			UpdateRotorWear(elapsedTime);

			// Update diagnostic data
			UpdateDiagnostics();
		}

		private void UpdateSensorReadings()
		{
			// Get temperature from sensor if available
			if (_temperatureSensor != null && _temperatureSensor.Status == DeviceStatus.Running)
			{
				Temperature = _temperatureSensor.Temperature;
			}

			// Get vibration level from sensor if available
			if (_vibrationSensor != null && _vibrationSensor.Status == DeviceStatus.Running)
			{
				VibrationLevel = _vibrationSensor.VibrationLevel;
			}
		}

		private void UpdateRotorSpeed(TimeSpan elapsedTime)
		{
			double speedDifference = TargetSpeed - CurrentSpeed;
			double maxSpeedChange;

			if (speedDifference > 0)
			{
				// Accelerating
				maxSpeedChange = _accelerationRate * elapsedTime.TotalSeconds;
				CurrentSpeed += Math.Min(speedDifference, maxSpeedChange);
			}
			else if (speedDifference < 0)
			{
				// Decelerating
				maxSpeedChange = _decelerationRate * elapsedTime.TotalSeconds;
				CurrentSpeed -= Math.Min(-speedDifference, maxSpeedChange);

				// If brakes are engaged, deceleration is faster
				if (_brakesEngaged && CurrentSpeed > 0)
				{
					CurrentSpeed -= _decelerationRate * 1.5 * elapsedTime.TotalSeconds;
					CurrentSpeed = Math.Max(0, CurrentSpeed);
				}
			}

			// Ensure speed is within limits
			CurrentSpeed = Math.Max(0, Math.Min(CurrentSpeed, MaximumSpeed));

			// Update drive motor if available
			if (_driveMotor != null && _driveMotor.Status == DeviceStatus.Running)
			{
				double speedPercentage = (CurrentSpeed / MaximumSpeed) * 100.0;
				_driveMotor.SetSpeed(speedPercentage);
			}
		}

		private double CalculateGForce(double speedRPM)
		{
			// G-force = 1.118 × 10^-5 × r × (RPM)²
			// where r is radius in centimeters
			return 1.118e-5 * RotorRadius * Math.Pow(speedRPM, 2);
		}

		private void UpdateTemperature(TimeSpan elapsedTime)
		{
			if (_temperatureSensor != null && _temperatureSensor.Status == DeviceStatus.Running)
			{
				// Temperature is already updated from sensor
				return;
			}

			// Temperature rises based on speed and time
			double speedFactor = Math.Pow(CurrentSpeed / MaximumSpeed, 2); // Quadratic relation
			double temperatureIncrease = _temperatureRiseRate * speedFactor * elapsedTime.TotalMinutes;

			// If cooling system is active, counteract temperature rise
			if (_coolingSytemActive && CurrentSpeed > 0)
			{
				temperatureIncrease -= _temperatureRiseRate * 1.2 * elapsedTime.TotalMinutes;
			}
			else if (_coolingSytemActive)
			{
				// Active cooling when not running
				temperatureIncrease = -0.5 * elapsedTime.TotalMinutes;
			}
			else if (CurrentSpeed == 0)
			{
				// Natural cooling when stopped
				temperatureIncrease = -0.1 * elapsedTime.TotalMinutes;
			}

			// Update temperature
			Temperature += temperatureIncrease;

			// Ensure temperature is within reasonable limits
			Temperature = Math.Max(4.0, Math.Min(Temperature, 40.0));
		}

		private void UpdateVibrationAndImbalance(TimeSpan elapsedTime)
		{
			if (_vibrationSensor != null && _vibrationSensor.Status == DeviceStatus.Running)
			{
				// Vibration is already updated from sensor
				VibrationLevel = _vibrationSensor.VibrationLevel;
			}
			else
			{
				// Calculate vibration based on speed, imbalance and wear
				double speedFactor = Math.Pow(CurrentSpeed / MaximumSpeed, 1.5);
				double wearFactor = 1.0 + (RotorWearLevel / 100.0);
				double imbalanceFactor = 1.0 + (ImbalanceLevel / 20.0);

				// Base vibration increases with speed
				VibrationLevel = 5.0 * speedFactor * wearFactor * imbalanceFactor;

				// Add random vibration component
				VibrationLevel += Random.NextDouble() * 2.0 * speedFactor;

				// Ensure vibration is within limits (0-100%)
				VibrationLevel = Math.Max(0, Math.Min(VibrationLevel, 100));
			}

			// Imbalance slowly increases during operation depending on phase
			if (CurrentPhase == CentrifugePhase.Processing && CurrentSpeed > 0)
			{
				// Imbalance increases more at higher speeds
				double speedFactor = Math.Pow(CurrentSpeed / MaximumSpeed, 2);
				ImbalanceLevel += _imbalanceIncreaseRate * speedFactor * elapsedTime.TotalMinutes;
				ImbalanceLevel = Math.Min(ImbalanceLevel, 100);
			}
		}

		private void UpdateSeparationEfficiency()
		{
			// Only calculate during processing phase
			if (CurrentPhase != CentrifugePhase.Processing)
			{
				SeparationEfficiency = 0;
				return;
			}

			// Factors affecting separation efficiency
			double gForceFactor = Math.Min(1.0, CurrentGForce / 10000.0); // Maximum efficiency at 10,000g
			double timeFactor = Math.Min(1.0, ProcessTime / TargetProcessTime); // Time factor
			double tempFactor = CalculateTemperatureFactor(); // Temperature factor
			double imbalancePenalty = ImbalanceLevel / 200.0; // Imbalance reduces efficiency
			double rotorWearPenalty = RotorWearLevel / 200.0; // Rotor wear reduces efficiency

			// Calculate base efficiency based on factors
			double baseEfficiency = 95.0 * gForceFactor * timeFactor * tempFactor;

			// Apply penalties
			SeparationEfficiency = baseEfficiency * (1.0 - imbalancePenalty - rotorWearPenalty);

			// Add small random variation
			SeparationEfficiency += (Random.NextDouble() * 2.0 - 1.0);

			// Ensure within limits
			SeparationEfficiency = Math.Max(0, Math.Min(SeparationEfficiency, 100));
		}

		private double CalculateTemperatureFactor()
		{
			// Temperature factor depends on rotor type and process
			switch (RotorType)
			{
				case CentrifugeRotorType.FixedAngle:
					// Optimal around 15-25°C
					return 1.0 - Math.Abs(Temperature - 20.0) / 30.0;

				case CentrifugeRotorType.SwingingBucket:
					// Optimal around 15-25°C
					return 1.0 - Math.Abs(Temperature - 20.0) / 30.0;

				case CentrifugeRotorType.ContinuousFlow:
					// More sensitive to temperature
					return 1.0 - Math.Abs(Temperature - 18.0) / 25.0;

				case CentrifugeRotorType.ZonalRotor:
					// Usually temperature-controlled tightly
					return 1.0 - Math.Abs(Temperature - 15.0) / 20.0;

				case CentrifugeRotorType.DensityGradient:
					// Very sensitive to temperature
					return 1.0 - Math.Abs(Temperature - 12.0) / 15.0;

				default:
					return 1.0 - Math.Abs(Temperature - 20.0) / 30.0;
			}
		}

		private void ProcessCurrentPhase(TimeSpan elapsedTime)
		{
			switch (CurrentPhase)
			{
				case CentrifugePhase.Loading:
					ProcessLoadingPhase(elapsedTime);
					break;

				case CentrifugePhase.Acceleration:
					// Handled by UpdateRotorSpeed
					if (Math.Abs(CurrentSpeed - TargetSpeed) < 50)
					{
						CurrentPhase = CentrifugePhase.Processing;
						DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
					}
					break;

				case CentrifugePhase.Processing:
					ProcessProcessingPhase(elapsedTime);
					break;

				case CentrifugePhase.Deceleration:
					// Handled by UpdateRotorSpeed
					if (CurrentSpeed < 50)
					{
						CurrentPhase = CentrifugePhase.Unloading;
						TargetSpeed = 0;
						DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
					}
					break;

				case CentrifugePhase.Unloading:
					ProcessUnloadingPhase(elapsedTime);
					break;

				case CentrifugePhase.Cleaning:
					ProcessCleaningPhase(elapsedTime);
					break;
			}
		}

		private void ProcessLoadingPhase(TimeSpan elapsedTime)
		{
			// Simulate feed flow into the system
			if (_feedFlowMeter != null && _feedFlowMeter.Status == DeviceStatus.Running)
			{
				double volumeAdded = _feedFlowMeter.FlowRate * elapsedTime.TotalHours; // L/hr * hr
				CurrentVolume += volumeAdded;
			}
			else
			{
				// If no flowmeter, simulate loading at a fixed rate
				double loadingRate = RotorCapacity / 5.0; // Full load in ~5 minutes
				double volumeAdded = loadingRate * elapsedTime.TotalMinutes;
				CurrentVolume += volumeAdded;
			}

			// Check if loading is complete
			if (CurrentVolume >= _batchVolume || CurrentVolume >= RotorCapacity)
			{
				CurrentVolume = Math.Min(_batchVolume, RotorCapacity);

				// Close feed valve and prepare for acceleration
				if (_feedValve != null)
				{
					_feedValve.SetPosition(0); // Close valve
				}

				// Lock door for safety
				_doorLocked = true;

				// Start acceleration
				CurrentPhase = CentrifugePhase.Acceleration;
				DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
				DiagnosticData["DoorLocked"] = _doorLocked;
			}
		}

		private void ProcessProcessingPhase(TimeSpan elapsedTime)
		{
			// Processing handles the centrifugation
			// Most of the work is done in UpdateSeparationEfficiency()

			// For continuous flow, handle ongoing feed and discharge
			if (OperationMode == CentrifugeOperationMode.Continuous)
			{
				ProcessContinuousFlowOperation(elapsedTime);
			}

			// Calculate how much material we've processed
			_processedVolume = CurrentVolume * (SeparationEfficiency / 100.0);
		}

		private void ProcessContinuousFlowOperation(TimeSpan elapsedTime)
		{
			if (_feedFlowMeter != null && _feedFlowMeter.Status == DeviceStatus.Running)
			{
				// Add incoming material
				double volumeAdded = _feedFlowMeter.FlowRate * elapsedTime.TotalHours;
				_processedVolume += volumeAdded * (SeparationEfficiency / 100.0);
			}

			if (_dischargeFlowMeter != null && _dischargeFlowMeter.Status == DeviceStatus.Running)
			{
				// Process outgoing material
				double volumeRemoved = _dischargeFlowMeter.FlowRate * elapsedTime.TotalHours;
				// No action needed for simulation purposes
			}
		}

		private void ProcessUnloadingPhase(TimeSpan elapsedTime)
		{
			// Ensure rotor has fully stopped
			if (CurrentSpeed > 0)
			{
				return;
			}

			// Unlock door if it's still locked
			if (_doorLocked)
			{
				_doorLocked = false;
				DiagnosticData["DoorLocked"] = _doorLocked;
			}

			// Open discharge valve if available
			if (_dischargeValve != null && _dischargeValve.Status == DeviceStatus.Running)
			{
				_dischargeValve.SetPosition(100); // Fully open
			}

			// Simulate discharge flow
			double dischargeRate = RotorCapacity / 2.0; // Empty in ~2 minutes
			double volumeRemoved = dischargeRate * elapsedTime.TotalMinutes;
			CurrentVolume -= volumeRemoved;
			CurrentVolume = Math.Max(0, CurrentVolume);

			// Check if unloading is complete
			if (CurrentVolume <= 0.1) // Allow for some residual volume
			{
				CurrentVolume = 0;

				// Close discharge valve
				if (_dischargeValve != null)
				{
					_dischargeValve.SetPosition(0);
				}

				// Move to cleaning phase if configured, otherwise back to idle
				if (OperationMode == CentrifugeOperationMode.BatchWithCleaning)
				{
					CurrentPhase = CentrifugePhase.Cleaning;
				}
				else
				{
					CurrentPhase = CentrifugePhase.Idle;
				}

				DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();

				// Increment rotor cycle count
				RotorCycleCount++;
				DiagnosticData["RotorCycleCount"] = RotorCycleCount;
			}
		}

		private void ProcessCleaningPhase(TimeSpan elapsedTime)
		{
			// Simulate automatic cleaning cycle
			double cleaningProgress = (ProcessTime - _cleaningStartTime) / _cleaningDuration;

			if (cleaningProgress >= 1.0)
			{
				// Cleaning complete, return to idle
				CurrentPhase = CentrifugePhase.Idle;
				DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
				ImbalanceLevel = 0; // Reset imbalance after cleaning
				DiagnosticData["ImbalanceLevel"] = ImbalanceLevel;
			}
		}

		private double _cleaningStartTime;
		private double _cleaningDuration = 5.0; // 5 minutes

		private void CheckPhaseCompletion()
		{
			// Process completion check for batch mode
			if (CurrentPhase == CentrifugePhase.Processing && OperationMode == CentrifugeOperationMode.Batch)
			{
				if (ProcessTime >= TargetProcessTime)
				{
					// Start deceleration
					CurrentPhase = CentrifugePhase.Deceleration;
					TargetSpeed = 0;
					_brakesEngaged = true;
					DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
					DiagnosticData["BrakesEngaged"] = _brakesEngaged;
				}
			}

			// No completion check needed for continuous mode - it's controlled externally
		}

		private void CheckAlarmConditions()
		{
			// Check for high temperature
			if (Temperature > 35)
			{
				AddAlarm("HIGH_TEMPERATURE", $"High temperature detected: {Temperature:F1}°C", AlarmSeverity.Warning);
			}

			// Check for critical temperature
			if (Temperature > 38)
			{
				AddAlarm("CRITICAL_TEMPERATURE", $"Critical temperature detected: {Temperature:F1}°C", AlarmSeverity.Critical);
				EmergencyStop("Critical temperature limit exceeded");
			}

			// Check for high vibration
			if (VibrationLevel > 70)
			{
				AddAlarm("HIGH_VIBRATION", $"High vibration detected: {VibrationLevel:F1}%", AlarmSeverity.Warning);
			}

			// Check for critical vibration
			if (VibrationLevel > 90)
			{
				AddAlarm("CRITICAL_VIBRATION", $"Critical vibration detected: {VibrationLevel:F1}%", AlarmSeverity.Critical);
				EmergencyStop("Critical vibration limit exceeded");
			}

			// Check for high imbalance
			if (ImbalanceLevel > 50)
			{
				AddAlarm("HIGH_IMBALANCE", $"High rotor imbalance detected: {ImbalanceLevel:F1}%", AlarmSeverity.Warning);
			}

			// Check for critical imbalance
			if (ImbalanceLevel > 80)
			{
				AddAlarm("CRITICAL_IMBALANCE", $"Critical rotor imbalance: {ImbalanceLevel:F1}%", AlarmSeverity.Critical);
				EmergencyStop("Critical imbalance detected");
			}

			// Check for rotor wear
			if (RotorWearLevel > 70)
			{
				AddAlarm("ROTOR_WEAR", $"Rotor approaching end of life: {RotorWearLevel:F1}%", AlarmSeverity.Warning);
			}

			// Check for rotor cycle count
			if (RotorCycleCount > 900)
			{
				AddAlarm("ROTOR_CYCLES", $"Rotor approaching maximum cycle count: {RotorCycleCount}/1000", AlarmSeverity.Warning);
			}
		}

		private void EmergencyStop(string reason)
		{
			// Emergency stop procedure
			TargetSpeed = 0;
			_brakesEngaged = true;
			CurrentPhase = CentrifugePhase.Deceleration;
			Status = DeviceStatus.Fault;

			// Update diagnostic data
			DiagnosticData["EmergencyStopReason"] = reason;
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["BrakesEngaged"] = _brakesEngaged;

			AddAlarm("EMERGENCY_STOP", $"Emergency stop: {reason}", AlarmSeverity.Critical);
		}

		private void UpdateRotorWear(TimeSpan elapsedTime)
		{
			// Rotor wear depends on speed, time, and vibration
			if (CurrentSpeed > 0)
			{
				double speedFactor = Math.Pow(CurrentSpeed / MaximumSpeed, 2);
				double vibrationFactor = 1.0 + (VibrationLevel / 50.0);

				// Calculate wear increase
				double wearIncrease = 0.001 * speedFactor * vibrationFactor * elapsedTime.TotalMinutes;
				RotorWearLevel += wearIncrease;

				// Limit to 100%
				RotorWearLevel = Math.Min(RotorWearLevel, 100);

				// Update diagnostic data
				DiagnosticData["RotorWearLevel"] = RotorWearLevel;
			}
		}

		private void UpdateDiagnostics()
		{
			DiagnosticData["CurrentSpeed"] = CurrentSpeed;
			DiagnosticData["TargetSpeed"] = TargetSpeed;
			DiagnosticData["CurrentGForce"] = CurrentGForce;
			DiagnosticData["Temperature"] = Temperature;
			DiagnosticData["VibrationLevel"] = VibrationLevel;
			DiagnosticData["ImbalanceLevel"] = ImbalanceLevel;
			DiagnosticData["SeparationEfficiency"] = SeparationEfficiency;
			DiagnosticData["CurrentVolume"] = CurrentVolume;
			DiagnosticData["ProcessTime"] = ProcessTime;
			DiagnosticData["CoolingSytemActive"] = _coolingSytemActive;
		}

		#region Public Control Methods

		/// <summary>
		/// Starts the centrifugation process with the specified parameters
		/// </summary>
		/// <param name="targetSpeed">Target speed in RPM</param>
		/// <param name="processTime">Process time in minutes</param>
		/// <param name="batchVolume">Volume to process in liters</param>
		/// <param name="feedDensity">Feed material density</param>
		/// <param name="contaminationLevel">Initial contamination level (0-100%)</param>
		/// <returns>True if process started successfully</returns>
		public bool StartBatchProcess(double targetSpeed, double processTime, double batchVolume,
									 double feedDensity = 1.05, double contaminationLevel = 10.0)
		{
			// Check if system is ready
			if (Status != DeviceStatus.Ready || CurrentPhase != CentrifugePhase.Idle)
			{
				AddAlarm("START_FAILED", "Cannot start: System not in ready state", AlarmSeverity.Warning);
				return false;
			}

			// Validate parameters
			if (targetSpeed > MaximumSpeed)
			{
				AddAlarm("INVALID_SPEED", $"Speed exceeds maximum: {targetSpeed} > {MaximumSpeed} RPM", AlarmSeverity.Warning);
				return false;
			}

			if (batchVolume > RotorCapacity)
			{
				AddAlarm("VOLUME_EXCEEDED", $"Volume exceeds capacity: {batchVolume} > {RotorCapacity} liters", AlarmSeverity.Warning);
				return false;
			}

			// Set batch parameters
			TargetSpeed = targetSpeed;
			TargetProcessTime = processTime;
			_batchVolume = batchVolume;
			_feedDensity = feedDensity;
			_initialContaminationLevel = contaminationLevel;
			_productDensity = feedDensity * 1.2; // Arbitrary for simulation
			_contaminantDensity = feedDensity * 0.8; // Arbitrary for simulation

			// Reset process parameters
			ProcessTime = 0;
			CurrentVolume = 0;
			SeparationEfficiency = 0;
			OperationMode = CentrifugeOperationMode.Batch;

			// Start the process
			CurrentPhase = CentrifugePhase.Loading;
			Status = DeviceStatus.Running;

			// Open feed valve if available
			if (_feedValve != null && _feedValve.Status == DeviceStatus.Ready)
			{
				_feedValve.Start();
				_feedValve.SetPosition(100); // Fully open
			}

			// Start motor if available
			if (_driveMotor != null)
			{
				_driveMotor.Start();
			}

			// Update diagnostic data
			DiagnosticData["TargetSpeed"] = TargetSpeed;
			DiagnosticData["TargetProcessTime"] = TargetProcessTime;
			DiagnosticData["BatchVolume"] = _batchVolume;
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["OperationMode"] = OperationMode.ToString();

			AddAlarm("BATCH_STARTED", "Centrifugation batch process started", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Starts continuous flow centrifugation process
		/// </summary>
		/// <param name="targetSpeed">Target speed in RPM</param>
		/// <param name="feedRate">Feed rate in liters/hour</param>
		/// <returns>True if process started successfully</returns>
		public bool StartContinuousProcess(double targetSpeed, double feedRate)
		{
			// Check if system is ready
			if (Status != DeviceStatus.Ready || CurrentPhase != CentrifugePhase.Idle)
			{
				AddAlarm("START_FAILED", "Cannot start: System not in ready state", AlarmSeverity.Warning);
				return false;
			}

			// Validate parameters
			if (targetSpeed > MaximumSpeed)
			{
				AddAlarm("INVALID_SPEED", $"Speed exceeds maximum: {targetSpeed} > {MaximumSpeed} RPM", AlarmSeverity.Warning);
				return false;
			}

			// Set parameters
			TargetSpeed = targetSpeed;
			OperationMode = CentrifugeOperationMode.Continuous;
			ProcessTime = 0;

			// Start rotor directly (no loading phase for continuous)
			CurrentPhase = CentrifugePhase.Acceleration;
			Status = DeviceStatus.Running;
			_doorLocked = true;
			_brakesEngaged = false;

			// Start motor if available
			if (_driveMotor != null)
			{
				_driveMotor.Start();
			}

			// Configure flow meters if available
			if (_feedFlowMeter != null)
			{
				_feedFlowMeter.SetFlowRate(feedRate);
			}

			// Open feed valve if available
			if (_feedValve != null && _feedValve.Status == DeviceStatus.Ready)
			{
				_feedValve.Start();
				_feedValve.SetPosition(100); // Fully open
			}

			// Open discharge valve if available
			if (_dischargeValve != null && _dischargeValve.Status == DeviceStatus.Ready)
			{
				_dischargeValve.Start();
				_dischargeValve.SetPosition(100); // Fully open
			}

			// Update diagnostic data
			DiagnosticData["TargetSpeed"] = TargetSpeed;
			DiagnosticData["FeedRate"] = feedRate;
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["OperationMode"] = OperationMode.ToString();
			DiagnosticData["DoorLocked"] = _doorLocked;

			AddAlarm("CONTINUOUS_STARTED", "Continuous centrifugation process started", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Stops the current process
		/// </summary>
		public void StopProcess()
		{
			if (CurrentPhase == CentrifugePhase.Idle)
			{
				return;
			}

			// Start deceleration
			TargetSpeed = 0;
			_brakesEngaged = true;
			CurrentPhase = CentrifugePhase.Deceleration;

			// Close feed valve if available
			if (_feedValve != null)
			{
				_feedValve.SetPosition(0); // Close valve
			}

			// Close discharge valve if continous operation
			if (OperationMode == CentrifugeOperationMode.Continuous && _dischargeValve != null)
			{
				_dischargeValve.SetPosition(0); // Close valve
			}

			// Update diagnostic data
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["BrakesEngaged"] = _brakesEngaged;

			AddAlarm("PROCESS_STOPPING", "Centrifugation process stopping", AlarmSeverity.Information);
		}

		/// <summary>
		/// Turns the cooling system on or off
		/// </summary>
		public void SetCooling(bool enabled)
		{
			_coolingSytemActive = enabled;
			DiagnosticData["CoolingSytemActive"] = _coolingSytemActive;
		}

		/// <summary>
		/// Replaces the rotor with a new one
		/// </summary>
		public void ReplaceRotor()
		{
			// Check if it's safe to replace rotor
			if (CurrentPhase != CentrifugePhase.Idle || Status != DeviceStatus.Ready)
			{
				AddAlarm("REPLACE_FAILED", "Cannot replace rotor while system is operating", AlarmSeverity.Warning);
				return;
			}

			// Reset rotor parameters
			RotorWearLevel = 0;
			RotorCycleCount = 0;
			RotorInstallationDate = DateTime.Now;

			// Update diagnostic data
			DiagnosticData["RotorWearLevel"] = RotorWearLevel;
			DiagnosticData["RotorCycleCount"] = RotorCycleCount;
			DiagnosticData["RotorInstallationDate"] = RotorInstallationDate;

			AddAlarm("ROTOR_REPLACED", "Centrifuge rotor replaced", AlarmSeverity.Information);
		}

		/// <summary>
		/// Performs a special cleaning cycle
		/// </summary>
		public void PerformCleaningCycle()
		{
			// Check if it's safe to clean
			if (CurrentPhase != CentrifugePhase.Idle || Status != DeviceStatus.Ready)
			{
				AddAlarm("CLEANING_FAILED", "Cannot start cleaning while system is operating", AlarmSeverity.Warning);
				return;
			}

			// Start cleaning cycle
			CurrentPhase = CentrifugePhase.Cleaning;
			_cleaningStartTime = ProcessTime;

			// Update diagnostic data
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["CleaningStartTime"] = _cleaningStartTime;

			AddAlarm("CLEANING_STARTED", "Centrifuge cleaning cycle started", AlarmSeverity.Information);
		}

		/// <summary>
		/// Rebalances the rotor to fix imbalance issues
		/// </summary>
		public void RebalanceRotor()
		{
			// Check if it's safe to rebalance
			if (CurrentPhase != CentrifugePhase.Idle || Status != DeviceStatus.Ready)
			{
				AddAlarm("REBALANCE_FAILED", "Cannot rebalance while system is operating", AlarmSeverity.Warning);
				return;
			}

			// Reduce imbalance level
			ImbalanceLevel = Math.Max(0, ImbalanceLevel - 80); // Remove 80% of imbalance

			// Update diagnostic data
			DiagnosticData["ImbalanceLevel"] = ImbalanceLevel;

			AddAlarm("REBALANCE_COMPLETE", "Centrifuge rotor rebalanced", AlarmSeverity.Information);
		}

		/// <summary>
		/// Changes the rotor type
		/// </summary>
		public void ChangeRotorType(CentrifugeRotorType rotorType, double rotorCapacity)
		{
			// Check if it's safe to change rotor
			if (CurrentPhase != CentrifugePhase.Idle || Status != DeviceStatus.Ready)
			{
				AddAlarm("CHANGE_FAILED", "Cannot change rotor while system is operating", AlarmSeverity.Warning);
				return;
			}

			// Change rotor parameters
			RotorType = rotorType;
			RotorCapacity = rotorCapacity;
			RotorWearLevel = 0;
			RotorCycleCount = 0;
			RotorInstallationDate = DateTime.Now;

			// Update rotor-specific parameters
			InitializeRotorParameters();

			// Update diagnostic data
			DiagnosticData["RotorType"] = RotorType.ToString();
			DiagnosticData["RotorCapacity"] = RotorCapacity;
			DiagnosticData["RotorWearLevel"] = RotorWearLevel;
			DiagnosticData["RotorCycleCount"] = RotorCycleCount;
			DiagnosticData["RotorInstallationDate"] = RotorInstallationDate;

			AddAlarm("ROTOR_CHANGED", $"Centrifuge rotor changed to {rotorType}", AlarmSeverity.Information);
		}

		/// <summary>
		/// Connect feed and discharge valves to the centrifuge
		/// </summary>
		public void ConnectValves(ValveController feedValve, ValveController dischargeValve, ValveController wasteValve = null)
		{
			_feedValve = feedValve;
			_dischargeValve = dischargeValve;
			_wasteValve = wasteValve;
		}

		/// <summary>
		/// Connect flow meters to the centrifuge
		/// </summary>
		public void ConnectFlowMeters(FlowMeter feedFlowMeter, FlowMeter dischargeFlowMeter = null)
		{
			_feedFlowMeter = feedFlowMeter;
			_dischargeFlowMeter = dischargeFlowMeter;
		}

		#endregion

		protected override void SimulateFault()
		{
			int faultType = Random.Next(6);

			switch (faultType)
			{
				case 0: // Motor overheating
					AddAlarm("MOTOR_OVERHEAT", "Drive motor overheating", AlarmSeverity.Major);
					Temperature += 5.0;

					if (_driveMotor != null)
					{
						_driveMotor.SimulateFault();
					}
					break;

				case 1: // Significant imbalance
					AddAlarm("SUDDEN_IMBALANCE", "Sudden rotor imbalance detected", AlarmSeverity.Major);
					ImbalanceLevel += 30.0;
					VibrationLevel += 25.0;
					break;

				case 2: // Bearing failure
					AddAlarm("BEARING_WEAR", "Bearing wear detected", AlarmSeverity.Warning);
					VibrationLevel += 15.0;
					RotorWearLevel += 10.0;
					break;

				case 3: // Cooling system failure
					AddAlarm("COOLING_FAILURE", "Cooling system failure", AlarmSeverity.Major);
					_coolingSytemActive = false;
					Temperature += 3.0;
					break;

				case 4: // Door interlock issue
					if (CurrentSpeed > 100)
					{
						AddAlarm("DOOR_INTERLOCK", "Door interlock fault - safety violation", AlarmSeverity.Critical);
						EmergencyStop("Door interlock failure");
					}
					else
					{
						AddAlarm("INTERLOCK_SENSOR", "Door interlock sensor fault", AlarmSeverity.Warning);
					}
					break;

				case 5: // Process contamination
					AddAlarm("CONTAMINATION", "Possible process contamination", AlarmSeverity.Minor);
					SeparationEfficiency *= 0.8; // 20% reduction in efficiency
					break;
			}
		}
	}

	/// <summary>
	/// Represents the different types of centrifuge rotors
	/// </summary>
	public enum CentrifugeRotorType
	{
		FixedAngle,         // Fixed angle rotor - sample tubes held at constant angle
		SwingingBucket,     // Swinging bucket rotor - tubes swing out horizontally during operation
		ContinuousFlow,     // Continuous flow rotor - processes material continuously
		ZonalRotor,         // Zonal rotor - specialized for density gradient separations
		DensityGradient     // Density gradient rotor - optimized for gradient separations
	}

	/// <summary>
	/// Represents the different phases of the centrifugation process
	/// </summary>
	public enum CentrifugePhase
	{
		Idle,           // System idle, no processing
		Loading,        // Loading material into the rotor
		Acceleration,   // Rotor accelerating to target speed
		Processing,     // Main processing phase at target speed
		Deceleration,   // Rotor decelerating
		Unloading,      // Unloading material from rotor
		Cleaning        // Cleaning cycle
	}

	/// <summary>
	/// Represents the different operation modes of the centrifuge
	/// </summary>
	public enum CentrifugeOperationMode
	{
		Batch,              // Standard batch operation
		BatchWithCleaning,  // Batch operation with automatic cleaning
		Continuous          // Continuous flow operation
	}

	/// <summary>
	/// Represents a vibration sensor for centrifuges
	/// </summary>
	public class VibrationSensor : DeviceBase
	{
		public double VibrationLevel { get; private set; } // 0-100%

		public VibrationSensor(string deviceId, string name) : base(deviceId, name)
		{
			VibrationLevel = 0;
			DiagnosticData["VibrationLevel"] = VibrationLevel;
		}

		public void SetVibrationLevel(double level)
		{
			VibrationLevel = Math.Max(0, Math.Min(100, level));
			DiagnosticData["VibrationLevel"] = VibrationLevel;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			// Add small random fluctuation to readings
			if (Status == DeviceStatus.Running)
			{
				VibrationLevel += (Random.NextDouble() * 2 - 1) * 0.5;
				VibrationLevel = Math.Max(0, Math.Min(100, VibrationLevel));
				DiagnosticData["VibrationLevel"] = VibrationLevel;
			}
		}

		protected override void SimulateFault()
		{
			AddAlarm("VIBRATION_SENSOR_FAULT", "Vibration sensor reading error", AlarmSeverity.Minor);
			VibrationLevel += 20 + Random.NextDouble() * 10;
			VibrationLevel = Math.Min(100, VibrationLevel);
		}
	}
}