using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PharmaceuticalProcess.HardwareComponents.ProcessEquipment
{
	/// <summary>
	/// Simulates a sterilizing tunnel used for depyrogenation of glass vials in pharmaceutical manufacturing
	/// </summary>
	public class SterilizingTunnel : DeviceBase
	{
		public override DeviceType Type => DeviceType.ProcessEquipment;

		// Core parameters
		public double EntranceZoneTemperature { get; private set; } // °C
		public double HeatingZoneTemperature { get; private set; } // °C
		public double SterilizationZoneTemperature { get; private set; } // °C
		public double CoolingZoneTemperature { get; private set; } // °C
		public double ExitZoneTemperature { get; private set; } // °C
		public double ConveyorSpeed { get; private set; } // mm/s
		public double AirflowRate { get; private set; } // m³/h
		public int VialCapacity { get; private set; } // Number of vials in tunnel
		public int ProcessedVialCount { get; private set; } // Total vials processed in current run
		public double PowerConsumption { get; private set; } // kW

		// Setpoints
		public double SterilizationTemperatureSetpoint { get; private set; } // °C
		public double ConveyorSpeedSetpoint { get; private set; } // mm/s
		public double AirflowRateSetpoint { get; private set; } // m³/h

		// Process metrics
		public double LogReduction { get; private set; } // Log reduction of endotoxins
		public double ResidenceTime { get; private set; } // Minutes in sterilization zone
		public double TotalTunnelLength { get; private set; } // mm
		public double ProcessTime { get; private set; } // Minutes

		// System state
		public TunnelOperationState OperationState { get; private set; }
		public bool IsReady { get; private set; } // Tunnel at temperature and ready for vials
		public bool HasAlarm { get; private set; }
		public bool EmergencyStopActive { get; private set; }

		// Equipment specifications
		public TunnelConfiguration Configuration { get; }
		public int VialThroughputCapacity { get; } // Vials per hour
		public DateTime LastMaintenanceDate { get; private set; }
		public int OperatingHours { get; private set; }

		// Connected systems
		private List<TemperatureSensor> _temperatureSensors;
		private MotorController _conveyorMotor;
		private List<HeatingElement> _heatingElements;
		private List<CoolingElement> _coolingElements;
		private AirflowController _airflowController;
		private VialCounter _inletCounter;
		private VialCounter _outletCounter;

		// Internal state tracking
		private double[] _zoneTargetTemperatures; // Target temperatures for each zone
		private double[] _zoneCurrentTemperatures; // Current temperatures for each zone
		private double[] _zoneHeatCapacities; // Heat capacity for each zone
		private double[] _zoneCoolingCapacities; // Cooling capacity for each zone
		private bool[] _zoneHeatingElements; // Heating element status for each zone
		private bool[] _zoneReadyStates; // Ready state for each zone
		private DateTime _startupTime;
		private DateTime _shutdownTime;
		private double _heatupProgress;
		private double _cooldownProgress;
		private double _energyUsed; // kWh
		private int _maintenanceCountdown; // Operating hours until maintenance
		private Dictionary<string, double> _qualificationParameters;

		// Vial tracking
		private Queue<Vial> _vialsInTunnel;
		private double _vialThroughputRate; // Vials per minute

		public SterilizingTunnel(
			string deviceId,
			string name,
			TunnelConfiguration configuration,
			int vialThroughputCapacity,
			MotorController conveyorMotor = null)
			: base(deviceId, name)
		{
			Configuration = configuration;
			VialThroughputCapacity = vialThroughputCapacity;
			_conveyorMotor = conveyorMotor;

			// Initialize parameters
			TotalTunnelLength = configuration.TunnelLength;
			OperationState = TunnelOperationState.Off;
			IsReady = false;
			HasAlarm = false;
			EmergencyStopActive = false;
			LastMaintenanceDate = DateTime.Now.AddDays(-30); // Assuming last maintenance 30 days ago
			OperatingHours = 0;
			_maintenanceCountdown = 720; // 720 hours (30 days) between maintenance

			// Initialize temperatures based on configuration
			_zoneTargetTemperatures = new double[5] {
				50.0,  // Entrance zone
                150.0, // Heating zone
                configuration.MaxTemperature, // Sterilization zone (from configuration)
                100.0, // Cooling zone
                40.0   // Exit zone
            };

			// Initial current temperatures (ambient)
			_zoneCurrentTemperatures = new double[5] { 22.0, 22.0, 22.0, 22.0, 22.0 };

			// Zone heat capacities (how quickly they heat up)
			_zoneHeatCapacities = new double[5] { 0.5, 1.0, 0.8, 0.4, 0.3 };

			// Zone cooling capacities (how quickly they cool down)
			_zoneCoolingCapacities = new double[5] { 0.3, 0.2, 0.2, 1.0, 0.5 };

			// Heating element status (all off initially)
			_zoneHeatingElements = new bool[5] { false, false, false, false, false };

			// Zone ready status (all false initially)
			_zoneReadyStates = new bool[5] { false, false, false, false, false };

			// Set default setpoints
			SterilizationTemperatureSetpoint = configuration.DefaultSterilizationTemperature;
			ConveyorSpeedSetpoint = configuration.DefaultConveyorSpeed;
			AirflowRateSetpoint = configuration.DefaultAirflowRate;

			// Initialize tracking collections
			_temperatureSensors = new List<TemperatureSensor>();
			_heatingElements = new List<HeatingElement>();
			_coolingElements = new List<CoolingElement>();
			_vialsInTunnel = new Queue<Vial>();
			_qualificationParameters = new Dictionary<string, double>();

			// Initialize diagnostic data
			InitializeDiagnostics();
		}

		private void InitializeDiagnostics()
		{
			DiagnosticData["TunnelConfiguration"] = Configuration.ConfigurationType.ToString();
			DiagnosticData["MaxTemperature"] = Configuration.MaxTemperature;
			DiagnosticData["TunnelLength"] = TotalTunnelLength;
			DiagnosticData["VialThroughputCapacity"] = VialThroughputCapacity;
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["SterilizationTemperatureSetpoint"] = SterilizationTemperatureSetpoint;
			DiagnosticData["LastMaintenanceDate"] = LastMaintenanceDate;
			DiagnosticData["OperatingHours"] = OperatingHours;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize connected systems
			_conveyorMotor?.Initialize();

			foreach (var sensor in _temperatureSensors)
			{
				sensor.Initialize();
			}

			// Reset process parameters
			EntranceZoneTemperature = 22.0;
			HeatingZoneTemperature = 22.0;
			SterilizationZoneTemperature = 22.0;
			CoolingZoneTemperature = 22.0;
			ExitZoneTemperature = 22.0;
			ConveyorSpeed = 0;
			AirflowRate = 0;
			PowerConsumption = 0;

			ProcessedVialCount = 0;
			VialCapacity = CalculateVialCapacity();
			_vialsInTunnel.Clear();

			LogReduction = 0;
			ResidenceTime = 0;
			ProcessTime = 0;
			_energyUsed = 0;

			// Set initial state
			OperationState = TunnelOperationState.Off;
			IsReady = false;
			HasAlarm = false;
			EmergencyStopActive = false;

			// Reset zone temps and states
			for (int i = 0; i < 5; i++)
			{
				_zoneCurrentTemperatures[i] = 22.0;
				_zoneHeatingElements[i] = false;
				_zoneReadyStates[i] = false;
			}

			// Update diagnostics
			UpdateDiagnostics();
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (EmergencyStopActive) return;

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Update process time
			ProcessTime += elapsedTime.TotalMinutes;

			// Add operating hours when in Running state
			if (OperationState == TunnelOperationState.Running ||
				OperationState == TunnelOperationState.Heating ||
				OperationState == TunnelOperationState.Cooling)
			{
				OperatingHours += (int)(elapsedTime.TotalHours * 10) / 10; // Rounded to 0.1 hours
				_maintenanceCountdown -= (int)(elapsedTime.TotalHours * 10) / 10;
			}

			// Process based on current state
			switch (OperationState)
			{
				case TunnelOperationState.Off:
					// Nothing to do in off state
					break;

				case TunnelOperationState.Heating:
					UpdateHeatingPhase(elapsedTime);
					break;

				case TunnelOperationState.Ready:
					MaintainTemperatures(elapsedTime);
					break;

				case TunnelOperationState.Running:
					ProcessRunningState(elapsedTime);
					break;

				case TunnelOperationState.Cooling:
					UpdateCoolingPhase(elapsedTime);
					break;

				case TunnelOperationState.Maintenance:
					// Maintenance state handled externally
					break;

				case TunnelOperationState.Fault:
					// Fault state handled by alarm system
					break;
			}

			// Update zone temperatures
			UpdateZoneTemperatures(elapsedTime);

			// Update power consumption
			CalculatePowerConsumption(elapsedTime);

			// Check for maintenance requirements
			CheckMaintenanceRequirements();

			// Check for alarm conditions
			CheckAlarmConditions();

			// Update diagnostic data
			UpdateDiagnostics();
		}

		private void UpdateHeatingPhase(TimeSpan elapsedTime)
		{
			// Activate heating elements based on target temperatures
			for (int i = 0; i < 5; i++)
			{
				if (_zoneCurrentTemperatures[i] < _zoneTargetTemperatures[i] - 5.0)
				{
					_zoneHeatingElements[i] = true;
				}
				else if (_zoneCurrentTemperatures[i] >= _zoneTargetTemperatures[i])
				{
					_zoneHeatingElements[i] = false;
					_zoneReadyStates[i] = true;
				}
			}

			// Calculate heating progress
			int readyZones = _zoneReadyStates.Count(ready => ready);
			_heatupProgress = (readyZones / 5.0) * 100.0;

			// Update air flow rate during heating
			AirflowRate = Math.Min(AirflowRate + 50.0 * elapsedTime.TotalMinutes, AirflowRateSetpoint);

			// Check if all zones are at temperature
			if (readyZones == 5)
			{
				OperationState = TunnelOperationState.Ready;
				IsReady = true;
				AddAlarm("TUNNEL_READY", "Depyrogenation tunnel ready for operation", AlarmSeverity.Information);
			}

			DiagnosticData["HeatupProgress"] = _heatupProgress;
		}

		private void MaintainTemperatures(TimeSpan elapsedTime)
		{
			// In ready state, maintain temperatures within setpoint ranges
			for (int i = 0; i < 5; i++)
			{
				if (_zoneCurrentTemperatures[i] < _zoneTargetTemperatures[i] - 3.0)
				{
					_zoneHeatingElements[i] = true;
				}
				else if (_zoneCurrentTemperatures[i] > _zoneTargetTemperatures[i] + 1.0)
				{
					_zoneHeatingElements[i] = false;
				}
			}

			// Keep airflow at setpoint
			AirflowRate = AirflowRateSetpoint;
		}

		private void ProcessRunningState(TimeSpan elapsedTime)
		{
			// Maintain temperatures like in Ready state
			MaintainTemperatures(elapsedTime);

			// Control conveyor speed
			if (_conveyorMotor != null && _conveyorMotor.Status == DeviceStatus.Running)
			{
				// Scale motor speed percentage to mm/s
				ConveyorSpeed = (_conveyorMotor.Speed / 100.0) * Configuration.MaxConveyorSpeed;
			}
			else
			{
				// Gradually adjust to setpoint
				double speedDiff = ConveyorSpeedSetpoint - ConveyorSpeed;
				ConveyorSpeed += speedDiff * 0.2 * elapsedTime.TotalSeconds; // Gradual change
			}

			// Process vials through the tunnel
			ProcessVialsInTunnel(elapsedTime);

			// Calculate key metrics
			CalculateResidenceTime();
			CalculateLogReduction();

			// Check for any issues that would affect running state
			if (_zoneCurrentTemperatures[2] < SterilizationTemperatureSetpoint - 10.0)
			{
				AddAlarm("TEMP_TOO_LOW", "Sterilization zone temperature below minimum", AlarmSeverity.Major);
				HasAlarm = true;
			}
		}

		private void UpdateCoolingPhase(TimeSpan elapsedTime)
		{
			// Turn off all heating elements
			for (int i = 0; i < 5; i++)
			{
				_zoneHeatingElements[i] = false;

				// Zone is considered cooled when below 60°C
				if (_zoneCurrentTemperatures[i] <= 60.0)
				{
					_zoneReadyStates[i] = true;
				}
				else
				{
					_zoneReadyStates[i] = false;
				}
			}

			// Keep airflow high during cooling
			AirflowRate = Math.Min(AirflowRate + 10.0 * elapsedTime.TotalMinutes, AirflowRateSetpoint * 1.2);

			// Calculate cooling progress
			int cooledZones = _zoneReadyStates.Count(ready => ready);
			_cooldownProgress = (cooledZones / 5.0) * 100.0;

			// Check if all zones are cooled below safety threshold
			if (cooledZones == 5)
			{
				OperationState = TunnelOperationState.Off;
				IsReady = false;
				AirflowRate = 0;
				AddAlarm("TUNNEL_COOLED", "Depyrogenation tunnel cooled down", AlarmSeverity.Information);
			}

			DiagnosticData["CooldownProgress"] = _cooldownProgress;
		}

		private void UpdateZoneTemperatures(TimeSpan elapsedTime)
		{
			// Get readings from external sensors if available
			UpdateSensorReadings();

			// Update each zone's temperature based on heating element status and thermal properties
			for (int i = 0; i < 5; i++)
			{
				double tempChangeRate = 0;

				if (_zoneHeatingElements[i])
				{
					// Heating phase - temperature rises based on heat capacity
					tempChangeRate = _zoneHeatCapacities[i] * elapsedTime.TotalMinutes;

					// Apply diminishing returns as we approach target temperature
					double tempDifference = _zoneTargetTemperatures[i] - _zoneCurrentTemperatures[i];
					tempChangeRate *= Math.Max(0.1, Math.Min(1.0, tempDifference / 50.0));
				}
				else
				{
					// Cooling phase - temperature falls based on cooling capacity
					double ambientTemp = 22.0; // Room temperature
					double tempDifference = _zoneCurrentTemperatures[i] - ambientTemp;
					tempChangeRate = -(_zoneCoolingCapacities[i] * tempDifference / 100.0) * elapsedTime.TotalMinutes;
				}

				// Apply thermal transfer between adjacent zones (simplified model)
				if (i > 0)
				{
					double transferFromPrev = (_zoneCurrentTemperatures[i - 1] - _zoneCurrentTemperatures[i]) * 0.05 * elapsedTime.TotalMinutes;
					tempChangeRate += transferFromPrev;
				}

				if (i < 4)
				{
					double transferToNext = (_zoneCurrentTemperatures[i] - _zoneCurrentTemperatures[i + 1]) * 0.05 * elapsedTime.TotalMinutes;
					tempChangeRate -= transferToNext;
				}

				// Apply air flow cooling effect
				if (AirflowRate > 0)
				{
					double airflowCoolingFactor = 0.01 * (AirflowRate / AirflowRateSetpoint);
					tempChangeRate -= airflowCoolingFactor * elapsedTime.TotalMinutes;
				}

				// Update temperature with limits
				_zoneCurrentTemperatures[i] += tempChangeRate;

				// Apply maximum temperature limit
				_zoneCurrentTemperatures[i] = Math.Min(_zoneCurrentTemperatures[i], Configuration.MaxTemperature + 5);
			}

			// Update public temperature properties
			EntranceZoneTemperature = _zoneCurrentTemperatures[0];
			HeatingZoneTemperature = _zoneCurrentTemperatures[1];
			SterilizationZoneTemperature = _zoneCurrentTemperatures[2];
			CoolingZoneTemperature = _zoneCurrentTemperatures[3];
			ExitZoneTemperature = _zoneCurrentTemperatures[4];
		}

		private void UpdateSensorReadings()
		{
			// Update temperature readings from external sensors if available
			foreach (var sensor in _temperatureSensors)
			{
				if (sensor.Status != DeviceStatus.Running) continue;

				// Map each sensor to its zone based on sensor name/ID
				if (sensor.Name.Contains("Entrance") || sensor.DeviceId.Contains("Z1"))
				{
					_zoneCurrentTemperatures[0] = sensor.Temperature;
				}
				else if (sensor.Name.Contains("Heating") || sensor.DeviceId.Contains("Z2"))
				{
					_zoneCurrentTemperatures[1] = sensor.Temperature;
				}
				else if (sensor.Name.Contains("Steril") || sensor.DeviceId.Contains("Z3"))
				{
					_zoneCurrentTemperatures[2] = sensor.Temperature;
				}
				else if (sensor.Name.Contains("Cooling") || sensor.DeviceId.Contains("Z4"))
				{
					_zoneCurrentTemperatures[3] = sensor.Temperature;
				}
				else if (sensor.Name.Contains("Exit") || sensor.DeviceId.Contains("Z5"))
				{
					_zoneCurrentTemperatures[4] = sensor.Temperature;
				}
			}
		}

		private void CalculatePowerConsumption(TimeSpan elapsedTime)
		{
			// Calculate power consumption based on heating element usage
			double basePower = 2.0; // Base power for controls, motors, etc.
			double heatingPower = 0;

			// Each active heating element contributes to power usage
			for (int i = 0; i < 5; i++)
			{
				if (_zoneHeatingElements[i])
				{
					// Power usage depends on zone and target temperature
					double zoneFactor = (i == 2) ? 1.5 : 1.0; // Sterilization zone uses more power
					heatingPower += zoneFactor * (_zoneTargetTemperatures[i] / 100.0) * Configuration.HeatingElementPower;
				}
			}

			// Add motor power consumption
			double motorPower = 0;
			if (ConveyorSpeed > 0)
			{
				motorPower = 0.5 * (ConveyorSpeed / Configuration.MaxConveyorSpeed);
			}

			// Add airflow system power consumption
			double airflowPower = 0;
			if (AirflowRate > 0)
			{
				airflowPower = 0.8 * (AirflowRate / Configuration.MaxAirflowRate);
			}

			// Calculate total power
			PowerConsumption = basePower + heatingPower + motorPower + airflowPower;

			// Add to cumulative energy usage (kWh)
			_energyUsed += PowerConsumption * (elapsedTime.TotalHours);
		}

		private void ProcessVialsInTunnel(TimeSpan elapsedTime)
		{
			if (ConveyorSpeed <= 0) return;

			// Calculate how many new vials enter during this time step
			double vialEntryRate = (_vialThroughputRate / 60.0) * elapsedTime.TotalSeconds;
			int newVials = (int)vialEntryRate;

			// Add random variation to simulate real-world variability
			if (Random.NextDouble() < (vialEntryRate - newVials))
			{
				newVials += 1;
			}

			// Create and add new vials entering the tunnel
			for (int i = 0; i < newVials; i++)
			{
				if (_vialsInTunnel.Count < VialCapacity)
				{
					Vial newVial = new Vial
					{
						EntryTime = ProcessTime,
						Position = 0,
						InitialContamination = Random.NextDouble() * 4.0 + 1.0, // 1-5 EU/vial
						IsSterile = false,
						Temperature = 22.0 // Start at room temperature
					};

					_vialsInTunnel.Enqueue(newVial);

					// Update inlet counter if available
					_inletCounter?.IncrementCount();
				}
			}

			// Calculate how far vials move in this time step
			double distanceMoved = ConveyorSpeed * elapsedTime.TotalSeconds; // mm

			// Process each vial in the tunnel
			int vialsToProcess = _vialsInTunnel.Count;
			List<Vial> completedVials = new List<Vial>();

			for (int i = 0; i < vialsToProcess; i++)
			{
				Vial vial = _vialsInTunnel.Dequeue();

				// Update vial position
				vial.Position += distanceMoved;

				// Update vial temperature and sterilization based on current zone
				UpdateVialStatus(vial, elapsedTime);

				// Check if vial has exited the tunnel
				if (vial.Position >= TotalTunnelLength)
				{
					// Process completed vial
					completedVials.Add(vial);
					ProcessedVialCount++;
					_outletCounter?.IncrementCount();
				}
				else
				{
					// Put vial back in queue if still in tunnel
					_vialsInTunnel.Enqueue(vial);
				}
			}

			// Log statistics from completed vials
			if (completedVials.Count > 0)
			{
				double avgLogReduction = completedVials.Average(v =>
					Math.Log10(v.InitialContamination / Math.Max(0.001, v.CurrentContamination)));
				double sterilePercentage = completedVials.Count(v => v.IsSterile) / (double)completedVials.Count * 100.0;

				DiagnosticData["LastBatchLogReduction"] = avgLogReduction;
				DiagnosticData["LastBatchSterilePercentage"] = sterilePercentage;

				if (avgLogReduction < 3.0)
				{
					AddAlarm("LOW_LOG_REDUCTION", $"Insufficient depyrogenation: {avgLogReduction:F2} log reduction",
						AlarmSeverity.Major);
				}
			}
		}

		private void UpdateVialStatus(Vial vial, TimeSpan elapsedTime)
		{
			// Determine which zone the vial is in based on position
			int zoneIndex = DetermineVialZone(vial.Position);

			// Update vial temperature based on zone temperature
			// Use simple heat transfer model
			double zoneTemp = _zoneCurrentTemperatures[zoneIndex];
			double tempDifference = zoneTemp - vial.Temperature;
			double heatTransferRate = 0.05; // Heat transfer coefficient
			vial.Temperature += tempDifference * heatTransferRate * elapsedTime.TotalSeconds;

			// Calculate depyrogenation effect
			// Using temperature-time relationship for depyrogenation
			if (vial.Temperature > 200.0) // Above 200°C, endotoxin destruction occurs
			{
				// Model endotoxin destruction rate based on temperature
				// Uses a simplified version of Arrhenius equation
				double destructionRate = Math.Pow(10, (vial.Temperature - 250.0) / 25.0) * elapsedTime.TotalSeconds / 60.0;

				// Apply destruction rate to current contamination
				vial.CurrentContamination *= Math.Exp(-destructionRate);

				// Check if vial meets sterility criteria
				if (vial.CurrentContamination < 0.01) // Below 0.01 EU/vial is considered sterile
				{
					vial.IsSterile = true;
				}
			}
		}

		private int DetermineVialZone(double position)
		{
			// Determine which zone the vial is in based on position
			double zoneLength = TotalTunnelLength / 5.0;

			if (position < zoneLength)
				return 0; // Entrance zone
			else if (position < zoneLength * 2)
				return 1; // Heating zone
			else if (position < zoneLength * 3)
				return 2; // Sterilization zone
			else if (position < zoneLength * 4)
				return 3; // Cooling zone
			else
				return 4; // Exit zone
		}

		private void CalculateResidenceTime()
		{
			if (ConveyorSpeed <= 0) return;

			// Calculate residence time in sterilization zone
			double sterilizationZoneLength = TotalTunnelLength / 5.0;
			ResidenceTime = (sterilizationZoneLength / ConveyorSpeed) / 60.0; // Convert to minutes

			// Check if residence time is sufficient
			if (ResidenceTime < 2.5 && OperationState == TunnelOperationState.Running)
			{
				AddAlarm("LOW_RESIDENCE_TIME", $"Insufficient sterilization time: {ResidenceTime:F1} minutes",
					AlarmSeverity.Warning);
			}
		}

		private void CalculateLogReduction()
		{
			// Calculate theoretical log reduction based on temperature and residence time
			if (SterilizationZoneTemperature < 200.0 || ResidenceTime <= 0)
			{
				LogReduction = 0;
				return;
			}

			// Based on temperature-time relationship for depyrogenation
			// Simplified model for simulation purposes
			LogReduction = ((SterilizationZoneTemperature - 200.0) / 50.0) * Math.Sqrt(ResidenceTime);

			// Apply a reduction factor for non-optimal conditions
			double optimalityFactor = 1.0;

			// Reduce if temperature fluctuating
			if (Math.Abs(_zoneCurrentTemperatures[2] - SterilizationTemperatureSetpoint) > 5.0)
			{
				optimalityFactor *= 0.9;
			}

			// Reduce if airflow not optimal
			if (Math.Abs(AirflowRate - AirflowRateSetpoint) > AirflowRateSetpoint * 0.1)
			{
				optimalityFactor *= 0.95;
			}

			LogReduction *= optimalityFactor;
			LogReduction = Math.Max(0, Math.Min(6.0, LogReduction)); // Cap between 0-6 log reduction
		}

		private int CalculateVialCapacity()
		{
			// Calculate how many vials can be in the tunnel at once
			double vialDiameter = 20.0; // mm, typical vial diameter
			double vialSpacing = 10.0; // mm, typical spacing between vials
			double tunnelWidth = 300.0; // mm, typical tunnel width
			int vialsPerRow = (int)(tunnelWidth / (vialDiameter + vialSpacing));

			// Calculate total vials in tunnel
			double vialLength = vialDiameter + vialSpacing;
			int totalVials = (int)((TotalTunnelLength / vialLength) * vialsPerRow);

			return totalVials;
		}

		private void CheckMaintenanceRequirements()
		{
			// Check if maintenance is due
			if (_maintenanceCountdown <= 0 && OperationState != TunnelOperationState.Maintenance)
			{
				AddAlarm("MAINTENANCE_DUE", "Scheduled maintenance required", AlarmSeverity.Warning);
				HasAlarm = true;
			}
		}

		private void CheckAlarmConditions()
		{
			// Check for temperature alarms
			if (SterilizationZoneTemperature > Configuration.MaxTemperature + 10.0)
			{
				AddAlarm("OVERTEMPERATURE", $"Critical high temperature: {SterilizationZoneTemperature:F1}°C",
					AlarmSeverity.Critical);
				EmergencyStop("Critical temperature limit exceeded");
			}

			// Check for temperature setpoint deviation
			if (Math.Abs(SterilizationZoneTemperature - SterilizationTemperatureSetpoint) > 20.0 &&
				(OperationState == TunnelOperationState.Ready || OperationState == TunnelOperationState.Running))
			{
				AddAlarm("TEMP_DEVIATION", $"Temperature deviation from setpoint: " +
						 $"{SterilizationZoneTemperature - SterilizationTemperatureSetpoint:F1}°C",
					AlarmSeverity.Major);
				HasAlarm = true;
			}

			// Check for conveyor issues
			if (OperationState == TunnelOperationState.Running && ConveyorSpeed < ConveyorSpeedSetpoint * 0.5)
			{
				AddAlarm("CONVEYOR_SLOW", "Conveyor speed below target", AlarmSeverity.Minor);
				HasAlarm = true;

				if (ConveyorSpeed < 1.0)
				{
					AddAlarm("CONVEYOR_STOPPED", "Conveyor stopped during production", AlarmSeverity.Major);
					HasAlarm = true;
				}
			}

			// Check airflow issues
			if ((OperationState == TunnelOperationState.Ready || OperationState == TunnelOperationState.Running) &&
				AirflowRate < AirflowRateSetpoint * 0.7)
			{
				AddAlarm("LOW_AIRFLOW", "Airflow below minimum requirements", AlarmSeverity.Warning);
				HasAlarm = true;
			}

			// Update status based on alarms
			if (HasAlarm && Status == DeviceStatus.Running)
			{
				Status = DeviceStatus.Warning;
			}
		}

		private void UpdateDiagnostics()
		{
			// Update temperatures
			DiagnosticData["EntranceZoneTemperature"] = EntranceZoneTemperature;
			DiagnosticData["HeatingZoneTemperature"] = HeatingZoneTemperature;
			DiagnosticData["SterilizationZoneTemperature"] = SterilizationZoneTemperature;
			DiagnosticData["CoolingZoneTemperature"] = CoolingZoneTemperature;
			DiagnosticData["ExitZoneTemperature"] = ExitZoneTemperature;

			// Update operational parameters
			DiagnosticData["ConveyorSpeed"] = ConveyorSpeed;
			DiagnosticData["AirflowRate"] = AirflowRate;
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["IsReady"] = IsReady;
			DiagnosticData["PowerConsumption"] = PowerConsumption;
			DiagnosticData["EnergyUsed"] = _energyUsed;

			// Update process metrics
			DiagnosticData["LogReduction"] = LogReduction;
			DiagnosticData["ResidenceTime"] = ResidenceTime;
			DiagnosticData["ProcessedVialCount"] = ProcessedVialCount;
			DiagnosticData["VialsInTunnel"] = _vialsInTunnel.Count;

			// Update maintenance info
			DiagnosticData["OperatingHours"] = OperatingHours;
			DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;
		}

		#region Public Control Methods

		/// <summary>
		/// Starts the heating process for the sterilizing tunnel
		/// </summary>
		public bool StartHeating()
		{
			// Check if system is available to start
			if (OperationState != TunnelOperationState.Off && OperationState != TunnelOperationState.Ready)
			{
				AddAlarm("START_FAILED", "Cannot start heating: System not in OFF state", AlarmSeverity.Warning);
				return false;
			}

			// If already at temperature, just mark as ready
			if (_zoneReadyStates.All(ready => ready) &&
				SterilizationZoneTemperature >= SterilizationTemperatureSetpoint - 5.0)
			{
				OperationState = TunnelOperationState.Ready;
				IsReady = true;
				AddAlarm("TUNNEL_READY", "Tunnel already at temperature", AlarmSeverity.Information);
				return true;
			}

			// Start heating process
			OperationState = TunnelOperationState.Heating;
			_startupTime = DateTime.Now;
			Status = DeviceStatus.Running;
			IsReady = false;
			HasAlarm = false;

			// Reset zone ready states
			for (int i = 0; i < 5; i++)
			{
				_zoneReadyStates[i] = false;
			}

			// Update target temperatures based on setpoint
			_zoneTargetTemperatures[0] = 100.0;  // Entrance zone
			_zoneTargetTemperatures[1] = SterilizationTemperatureSetpoint * 0.6;  // Heating zone
			_zoneTargetTemperatures[2] = SterilizationTemperatureSetpoint;  // Sterilization zone
			_zoneTargetTemperatures[3] = SterilizationTemperatureSetpoint * 0.4;  // Cooling zone
			_zoneTargetTemperatures[4] = 80.0;   // Exit zone

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["StartupTime"] = _startupTime;

			AddAlarm("HEATING_STARTED", "Depyrogenation tunnel heating started", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Starts the production process (conveyor and vial processing)
		/// </summary>
		public bool StartProduction()
		{
			// Check if system is ready to start production
			if (OperationState != TunnelOperationState.Ready)
			{
				AddAlarm("PRODUCTION_FAILED", "Cannot start production: Tunnel not ready", AlarmSeverity.Warning);
				return false;
			}

			// Check if temperatures are correct
			if (SterilizationZoneTemperature < SterilizationTemperatureSetpoint - 10.0)
			{
				AddAlarm("TEMP_TOO_LOW", "Cannot start production: Temperature too low", AlarmSeverity.Warning);
				return false;
			}

			// Start production
			OperationState = TunnelOperationState.Running;
			Status = DeviceStatus.Running;

			// Start conveyor motor if available
			if (_conveyorMotor != null && _conveyorMotor.Status == DeviceStatus.Ready)
			{
				_conveyorMotor.Start();
				_conveyorMotor.SetSpeed((ConveyorSpeedSetpoint / Configuration.MaxConveyorSpeed) * 100.0);
			}
			else
			{
				// Simulation mode - set conveyor speed directly
				ConveyorSpeed = ConveyorSpeedSetpoint;
			}

			// Calculate vial throughput rate based on speed
			_vialThroughputRate = CalculateVialThroughputRate();

			// Reset processed vial count for this run
			ProcessedVialCount = 0;
			_vialsInTunnel.Clear();

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["VialThroughputRate"] = _vialThroughputRate;

			AddAlarm("PRODUCTION_STARTED", "Depyrogenation tunnel production started", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Stops production but maintains temperatures
		/// </summary>
		public void PauseProduction()
		{
			if (OperationState != TunnelOperationState.Running)
			{
				return;
			}

			// Stop conveyor but maintain temperatures
			OperationState = TunnelOperationState.Ready;

			// Stop conveyor motor if available
			if (_conveyorMotor != null)
			{
				_conveyorMotor.SetSpeed(0);
			}

			// Directly set conveyor speed for simulation
			ConveyorSpeed = 0;

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();

			AddAlarm("PRODUCTION_PAUSED", "Depyrogenation tunnel production paused", AlarmSeverity.Information);
		}

		/// <summary>
		/// Starts the cooldown process
		/// </summary>
		public void StartCooldown()
		{
			if (OperationState == TunnelOperationState.Off || OperationState == TunnelOperationState.Cooling)
			{
				return;
			}

			// If in production, stop first
			if (OperationState == TunnelOperationState.Running)
			{
				PauseProduction();
			}

			// Start cooling process
			OperationState = TunnelOperationState.Cooling;
			_shutdownTime = DateTime.Now;

			// Turn off all heating elements
			for (int i = 0; i < 5; i++)
			{
				_zoneHeatingElements[i] = false;
				_zoneReadyStates[i] = false;
			}

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["ShutdownTime"] = _shutdownTime;

			AddAlarm("COOLING_STARTED", "Depyrogenation tunnel cooling started", AlarmSeverity.Information);
		}

		/// <summary>
		/// Performs emergency shutdown of the tunnel
		/// </summary>
		public void EmergencyStop(string reason)
		{
			// Immediately shut down all heating systems
			for (int i = 0; i < 5; i++)
			{
				_zoneHeatingElements[i] = false;
			}

			// Stop conveyor
			if (_conveyorMotor != null)
			{
				_conveyorMotor.EmergencyStop();
			}
			ConveyorSpeed = 0;

			// Set emergency state
			OperationState = TunnelOperationState.Fault;
			Status = DeviceStatus.Fault;
			EmergencyStopActive = true;

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["EmergencyStopReason"] = reason;

			AddAlarm("EMERGENCY_STOP", $"Emergency stop activated: {reason}", AlarmSeverity.Critical);
		}

		/// <summary>
		/// Reset after emergency stop
		/// </summary>
		public bool ResetEmergencyStop()
		{
			if (!EmergencyStopActive)
			{
				return false;
			}

			// Reset emergency stop state
			EmergencyStopActive = false;

			// Set to Off state
			OperationState = TunnelOperationState.Off;
			Status = DeviceStatus.Ready;

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["EmergencyStopReason"] = null;

			AddAlarm("EMERGENCY_RESET", "Emergency stop reset", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Set the sterilization temperature setpoint
		/// </summary>
		public void SetSterilizationTemperature(double temperature)
		{
			// Validate temperature is within allowed range
			if (temperature < 200.0 || temperature > Configuration.MaxTemperature)
			{
				AddAlarm("INVALID_TEMP", $"Invalid temperature setpoint: {temperature}°C", AlarmSeverity.Warning);
				return;
			}

			// Set new temperature setpoint
			SterilizationTemperatureSetpoint = temperature;

			// Update target temperatures if in heating or ready state
			if (OperationState == TunnelOperationState.Heating ||
				OperationState == TunnelOperationState.Ready ||
				OperationState == TunnelOperationState.Running)
			{
				_zoneTargetTemperatures[1] = SterilizationTemperatureSetpoint * 0.6;  // Heating zone
				_zoneTargetTemperatures[2] = SterilizationTemperatureSetpoint;  // Sterilization zone
				_zoneTargetTemperatures[3] = SterilizationTemperatureSetpoint * 0.4;  // Cooling zone

				// Reset ready states for recalculation
				for (int i = 0; i < 5; i++)
				{
					_zoneReadyStates[i] =
						_zoneCurrentTemperatures[i] >= _zoneTargetTemperatures[i] - 5.0;
				}

				// Check if still ready
				IsReady = _zoneReadyStates.All(ready => ready);
				if (!IsReady && OperationState == TunnelOperationState.Ready)
				{
					OperationState = TunnelOperationState.Heating;
				}
			}

			// Update diagnostic data
			DiagnosticData["SterilizationTemperatureSetpoint"] = SterilizationTemperatureSetpoint;

			AddAlarm("TEMP_SETPOINT_CHANGED", $"Temperature setpoint changed to {temperature}°C",
				AlarmSeverity.Information);
		}

		/// <summary>
		/// Set the conveyor speed setpoint
		/// </summary>
		public void SetConveyorSpeed(double speed)
		{
			// Validate speed is within allowed range
			if (speed < 0 || speed > Configuration.MaxConveyorSpeed)
			{
				AddAlarm("INVALID_SPEED", $"Invalid conveyor speed: {speed} mm/s", AlarmSeverity.Warning);
				return;
			}

			// Set new speed setpoint
			ConveyorSpeedSetpoint = speed;

			// Update actual speed if in running state
			if (OperationState == TunnelOperationState.Running)
			{
				if (_conveyorMotor != null && _conveyorMotor.Status == DeviceStatus.Running)
				{
					_conveyorMotor.SetSpeed((ConveyorSpeedSetpoint / Configuration.MaxConveyorSpeed) * 100.0);
				}

				// Recalculate vial throughput rate
				_vialThroughputRate = CalculateVialThroughputRate();
				DiagnosticData["VialThroughputRate"] = _vialThroughputRate;
			}

			// Update diagnostic data
			DiagnosticData["ConveyorSpeedSetpoint"] = ConveyorSpeedSetpoint;

			AddAlarm("SPEED_SETPOINT_CHANGED", $"Conveyor speed setpoint changed to {speed} mm/s",
				AlarmSeverity.Information);
		}

		/// <summary>
		/// Set the airflow rate setpoint
		/// </summary>
		public void SetAirflowRate(double rate)
		{
			// Validate rate is within allowed range
			if (rate < 0 || rate > Configuration.MaxAirflowRate)
			{
				AddAlarm("INVALID_AIRFLOW", $"Invalid airflow rate: {rate} m³/h", AlarmSeverity.Warning);
				return;
			}

			// Set new airflow setpoint
			AirflowRateSetpoint = rate;

			// Update actual airflow if controller available
			if (_airflowController != null && _airflowController.Status == DeviceStatus.Running)
			{
				_airflowController.SetFlowRate(AirflowRateSetpoint);
			}

			// Update diagnostic data
			DiagnosticData["AirflowRateSetpoint"] = AirflowRateSetpoint;

			AddAlarm("AIRFLOW_SETPOINT_CHANGED", $"Airflow rate setpoint changed to {rate} m³/h",
				AlarmSeverity.Information);
		}

		/// <summary>
		/// Perform maintenance on the system
		/// </summary>
		public void PerformMaintenance()
		{
			// Check if system is in a state where maintenance can be performed
			if (OperationState != TunnelOperationState.Off)
			{
				AddAlarm("MAINTENANCE_FAILED", "Cannot perform maintenance while system is not in OFF state",
					AlarmSeverity.Warning);
				return;
			}

			// Enter maintenance state
			OperationState = TunnelOperationState.Maintenance;
			Status = DeviceStatus.Maintenance;

			// Reset maintenance countdown
			_maintenanceCountdown = 720; // Reset to 720 hours (30 days)
			LastMaintenanceDate = DateTime.Now;

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();
			DiagnosticData["LastMaintenanceDate"] = LastMaintenanceDate;
			DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;

			AddAlarm("MAINTENANCE_STARTED", "Maintenance mode activated", AlarmSeverity.Information);
		}

		/// <summary>
		/// Complete maintenance and return to operational status
		/// </summary>
		public void CompleteMaintenance()
		{
			if (OperationState != TunnelOperationState.Maintenance)
			{
				return;
			}

			// Exit maintenance state
			OperationState = TunnelOperationState.Off;
			Status = DeviceStatus.Ready;
			HasAlarm = false;

			// Update diagnostic data
			DiagnosticData["OperationState"] = OperationState.ToString();

			AddAlarm("MAINTENANCE_COMPLETE", "Maintenance completed, system ready", AlarmSeverity.Information);
		}

		/// <summary>
		/// Connect temperature sensors to specific zones
		/// </summary>
		public void ConnectTemperatureSensors(IEnumerable<TemperatureSensor> sensors)
		{
			_temperatureSensors.Clear();
			_temperatureSensors.AddRange(sensors);
		}

		/// <summary>
		/// Connect heating elements to the tunnel
		/// </summary>
		public void ConnectHeatingElements(IEnumerable<HeatingElement> elements)
		{
			_heatingElements.Clear();
			_heatingElements.AddRange(elements);
		}

		/// <summary>
		/// Connect cooling elements to the tunnel
		/// </summary>
		public void ConnectCoolingElements(IEnumerable<CoolingElement> elements)
		{
			_coolingElements.Clear();
			_coolingElements.AddRange(elements);
		}

		/// <summary>
		/// Connect airflow controller to the tunnel
		/// </summary>
		public void ConnectAirflowController(AirflowController controller)
		{
			_airflowController = controller;
		}

		/// <summary>
		/// Connect vial counters to the tunnel
		/// </summary>
		public void ConnectVialCounters(VialCounter inlet, VialCounter outlet)
		{
			_inletCounter = inlet;
			_outletCounter = outlet;
		}

		/// <summary>
		/// Calculate the vial throughput rate based on current settings
		/// </summary>
		private double CalculateVialThroughputRate()
		{
			if (ConveyorSpeedSetpoint <= 0)
				return 0;

			// Calculate how many vials per minute can be processed
			double vialDiameter = 20.0; // mm
			double vialSpacing = 10.0; // mm
			double tunnelWidth = 300.0; // mm
			int vialsPerRow = (int)(tunnelWidth / (vialDiameter + vialSpacing));

			// Calculate vials per minute based on conveyor speed
			double vialLength = vialDiameter + vialSpacing; // mm
			double rowsPerMinute = (ConveyorSpeedSetpoint * 60.0) / vialLength;
			double vialsPerMinute = rowsPerMinute * vialsPerRow;

			return vialsPerMinute;
		}

		/// <summary>
		/// Set qualification parameters for tunnel validation
		/// </summary>
		public void SetQualificationParameters(Dictionary<string, double> parameters)
		{
			_qualificationParameters = parameters;

			// Example parameters:
			// - "MinLogReduction": 3.0,
			// - "MaxTemperatureDeviation": 5.0,
			// - "MinResidenceTime": 2.5

			foreach (var param in parameters)
			{
				DiagnosticData[$"Qual_{param.Key}"] = param.Value;
			}

			AddAlarm("QUAL_PARAMS_SET", "Qualification parameters updated", AlarmSeverity.Information);
		}

		#endregion

		protected override void SimulateFault()
		{
			int faultType = Random.Next(6);

			switch (faultType)
			{
				case 0: // Heating element failure
					int zoneIndex = Random.Next(5);
					_zoneHeatingElements[zoneIndex] = false;
					AddAlarm("HEATER_FAILURE", $"Heating element failure in zone {zoneIndex + 1}", AlarmSeverity.Major);
					break;

				case 1: // Temperature sensor drift
					zoneIndex = Random.Next(5);
					_zoneCurrentTemperatures[zoneIndex] += 15.0 - Random.NextDouble() * 30.0; // ±15°C drift
					AddAlarm("SENSOR_DRIFT", $"Temperature sensor drift in zone {zoneIndex + 1}", AlarmSeverity.Minor);
					break;

				case 2: // Conveyor motor issue
					if (OperationState == TunnelOperationState.Running)
					{
						ConveyorSpeed *= 0.5; // Slow down to half speed
						AddAlarm("CONVEYOR_ISSUE", "Conveyor motor performance degraded", AlarmSeverity.Warning);

						if (_conveyorMotor != null)
						{
							_conveyorMotor.SimulateFault();
						}
					}
					break;

				case 3: // Airflow problem
					AirflowRate *= 0.6; // 40% reduction in airflow
					AddAlarm("AIRFLOW_REDUCED", "Airflow system performance degraded", AlarmSeverity.Warning);
					break;

				case 4: // Vial jam
					if (OperationState == TunnelOperationState.Running && _vialsInTunnel.Count > 0)
					{
						ConveyorSpeed = 0;
						AddAlarm("VIAL_JAM", "Vial jam detected in tunnel", AlarmSeverity.Major);

						if (_conveyorMotor != null)
						{
							_conveyorMotor.Stop();
						}
					}
					break;

				case 5: // Power fluctuation
						// Briefly affect heating elements
					for (int i = 0; i < 5; i++)
					{
						if (_zoneHeatingElements[i])
						{
							_zoneCurrentTemperatures[i] -= 5.0 + Random.NextDouble() * 5.0;
						}
					}
					AddAlarm("POWER_FLUCTUATION", "Power supply fluctuation detected", AlarmSeverity.Minor);
					break;
			}

			HasAlarm = true;
			Status = DeviceStatus.Warning;
		}
	}

	/// <summary>
	/// Represents a vial being processed through the depyrogenation tunnel
	/// </summary>
	public class Vial
	{
		public double EntryTime { get; set; } // Minutes from process start
		public double Position { get; set; } // Position in mm from entrance
		public double Temperature { get; set; } // Current vial temperature in °C
		public double InitialContamination { get; set; } // Initial endotoxin level (EU/vial)
		public double CurrentContamination { get; set; } // Current endotoxin level (EU/vial)
		public bool IsSterile { get; set; } // Whether the vial meets sterility requirements

		public Vial()
		{
			Position = 0;
			Temperature = 22.0; // Room temperature
			InitialContamination = 0;
			CurrentContamination = 0;
			IsSterile = false;
		}
	}

	/// <summary>
	/// Configuration for a sterilizing tunnel
	/// </summary>
	public class TunnelConfiguration
	{
		public TunnelConfigType ConfigurationType { get; set; }
		public double TunnelLength { get; set; } // mm
		public double MaxTemperature { get; set; } // °C
		public double MaxConveyorSpeed { get; set; } // mm/s
		public double MaxAirflowRate { get; set; } // m³/h
		public double HeatingElementPower { get; set; } // kW per element
		public double DefaultSterilizationTemperature { get; set; } // °C
		public double DefaultConveyorSpeed { get; set; } // mm/s
		public double DefaultAirflowRate { get; set; } // m³/h

		public TunnelConfiguration(TunnelConfigType type)
		{
			ConfigurationType = type;

			switch (type)
			{
				case TunnelConfigType.SmallScale:
					TunnelLength = 3000; // 3 meters
					MaxTemperature = 320;
					MaxConveyorSpeed = 5.0;
					MaxAirflowRate = 500;
					HeatingElementPower = 5.0;
					DefaultSterilizationTemperature = 280;
					DefaultConveyorSpeed = 2.0;
					DefaultAirflowRate = 300;
					break;

				case TunnelConfigType.MidScale:
					TunnelLength = 5000; // 5 meters
					MaxTemperature = 350;
					MaxConveyorSpeed = 8.0;
					MaxAirflowRate = 1000;
					HeatingElementPower = 10.0;
					DefaultSterilizationTemperature = 300;
					DefaultConveyorSpeed = 3.0;
					DefaultAirflowRate = 600;
					break;

				case TunnelConfigType.ProductionScale:
					TunnelLength = 8000; // 8 meters
					MaxTemperature = 380;
					MaxConveyorSpeed = 12.0;
					MaxAirflowRate = 1500;
					HeatingElementPower = 15.0;
					DefaultSterilizationTemperature = 320;
					DefaultConveyorSpeed = 4.0;
					DefaultAirflowRate = 1000;
					break;

				default:
					TunnelLength = 5000;
					MaxTemperature = 350;
					MaxConveyorSpeed = 8.0;
					MaxAirflowRate = 1000;
					HeatingElementPower = 10.0;
					DefaultSterilizationTemperature = 300;
					DefaultConveyorSpeed = 3.0;
					DefaultAirflowRate = 600;
					break;
			}
		}
	}

	/// <summary>
	/// Types of tunnel configurations available
	/// </summary>
	public enum TunnelConfigType
	{
		SmallScale,      // For R&D or small batch production
		MidScale,        // For clinical trial production
		ProductionScale  // For commercial production
	}

	/// <summary>
	/// Operational states for the sterilizing tunnel
	/// </summary>
	public enum TunnelOperationState
	{
		Off,         // System powered off or idle
		Heating,     // System heating up to temperature
		Ready,       // System at temperature, ready for production
		Running,     // System processing vials
		Cooling,     // System cooling down
		Maintenance, // System in maintenance mode
		Fault        // System in fault state
	}

	/// <summary>
	/// Controls airflow in the tunnel
	/// </summary>
	public class AirflowController : DeviceBase
	{
		public double FlowRate { get; private set; } // m³/h
		public double SetPoint { get; private set; } // m³/h

		public AirflowController(string deviceId, string name)
			: base(deviceId, name)
		{
			FlowRate = 0;
			SetPoint = 0;
		}

		public void SetFlowRate(double flowRate)
		{
			SetPoint = flowRate;
			DiagnosticData["SetPoint"] = SetPoint;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Gradually adjust flowrate to setpoint
			double delta = (SetPoint - FlowRate) * 0.2 * elapsedTime.TotalSeconds;
			FlowRate += delta;

			// Add small random variation to simulate real-world behavior
			FlowRate += (Random.NextDouble() * 0.02 - 0.01) * SetPoint;

			// Ensure flow rate stays within reasonable limits
			FlowRate = Math.Max(0, FlowRate);

			// Update diagnostic data
			DiagnosticData["FlowRate"] = FlowRate;
		}

		protected override void SimulateFault()
		{
			AddAlarm("AIRFLOW_FAULT", "Airflow controller malfunction", AlarmSeverity.Warning);
			FlowRate *= 0.5; // Reduce flow by 50%
		}
	}

	/// <summary>
	/// Simulates a heating element used in the sterilizing tunnel
	/// </summary>
	public class HeatingElement : DeviceBase
	{
		public override DeviceType Type => DeviceType.Actuator;

		public bool IsActive { get; private set; }
		public double PowerLevel { get; private set; } // 0-100%
		public double MaxPower { get; private set; } // kW
		public double CurrentTemperature { get; private set; } // °C

		public HeatingElement(string deviceId, string name, double maxPower)
			: base(deviceId, name)
		{
			MaxPower = maxPower;
			PowerLevel = 0;
			IsActive = false;
			CurrentTemperature = 22.0; // Room temperature

			DiagnosticData["MaxPower"] = MaxPower;
			DiagnosticData["PowerLevel"] = PowerLevel;
			DiagnosticData["IsActive"] = IsActive;
			DiagnosticData["CurrentTemperature"] = CurrentTemperature;
		}

		public void SetPowerLevel(double level)
		{
			PowerLevel = Math.Max(0, Math.Min(100, level));
			IsActive = PowerLevel > 0;
			DiagnosticData["PowerLevel"] = PowerLevel;
			DiagnosticData["IsActive"] = IsActive;
		}

		public void TurnOn()
		{
			IsActive = true;
			if (PowerLevel == 0)
				PowerLevel = 100;

			DiagnosticData["IsActive"] = true;
			DiagnosticData["PowerLevel"] = PowerLevel;
		}

		public void TurnOff()
		{
			IsActive = false;
			PowerLevel = 0;
			DiagnosticData["IsActive"] = false;
			DiagnosticData["PowerLevel"] = 0;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Update temperature based on power level
			if (IsActive)
			{
				double heatRate = (PowerLevel / 100.0) * 5.0; // °C per minute
				CurrentTemperature += heatRate * elapsedTime.TotalMinutes;
				CurrentTemperature = Math.Min(800, CurrentTemperature);
			}
			else
			{
				// Cool down when off
				double coolRate = 2.0; // °C per minute
				double ambientTemp = 22.0;

				double tempDiff = CurrentTemperature - ambientTemp;
				CurrentTemperature -= Math.Min(coolRate * elapsedTime.TotalMinutes,
											 tempDiff * 0.1 * elapsedTime.TotalMinutes);
				CurrentTemperature = Math.Max(ambientTemp, CurrentTemperature);
			}

			DiagnosticData["CurrentTemperature"] = CurrentTemperature;
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Heater stuck on
					IsActive = true;
					AddAlarm("HEATER_STUCK_ON", "Heating element stuck in ON state", AlarmSeverity.Major);
					break;

				case 1: // Heater stuck off
					IsActive = false;
					PowerLevel = 0;
					AddAlarm("HEATER_STUCK_OFF", "Heating element failure", AlarmSeverity.Major);
					break;

				case 2: // Temperature sensor failure
					CurrentTemperature += 100 + Random.NextDouble() * 200;
					AddAlarm("TEMP_SENSOR_FAILURE", "Temperature sensor failure on heating element",
						AlarmSeverity.Warning);
					break;
			}

			DiagnosticData["IsActive"] = IsActive;
			DiagnosticData["PowerLevel"] = PowerLevel;
			DiagnosticData["CurrentTemperature"] = CurrentTemperature;
		}
	}

	/// <summary>
	/// Simulates a cooling element used in the sterilizing tunnel
	/// </summary>
	public class CoolingElement : DeviceBase
	{
		public override DeviceType Type => DeviceType.Actuator;

		public bool IsActive { get; private set; }
		public double PowerLevel { get; private set; } // 0-100%
		public double CoolingCapacity { get; private set; } // kW
		public double CurrentTemperature { get; private set; } // °C

		public CoolingElement(string deviceId, string name, double coolingCapacity)
			: base(deviceId, name)
		{
			CoolingCapacity = coolingCapacity;
			PowerLevel = 0;
			IsActive = false;
			CurrentTemperature = 22.0; // Room temperature

			DiagnosticData["CoolingCapacity"] = CoolingCapacity;
			DiagnosticData["PowerLevel"] = PowerLevel;
			DiagnosticData["IsActive"] = IsActive;
			DiagnosticData["CurrentTemperature"] = CurrentTemperature;
		}

		public void SetPowerLevel(double level)
		{
			PowerLevel = Math.Max(0, Math.Min(100, level));
			IsActive = PowerLevel > 0;
			DiagnosticData["PowerLevel"] = PowerLevel;
			DiagnosticData["IsActive"] = IsActive;
		}

		public void TurnOn()
		{
			IsActive = true;
			if (PowerLevel == 0)
				PowerLevel = 100;

			DiagnosticData["IsActive"] = true;
			DiagnosticData["PowerLevel"] = PowerLevel;
		}

		public void TurnOff()
		{
			IsActive = false;
			PowerLevel = 0;
			DiagnosticData["IsActive"] = false;
			DiagnosticData["PowerLevel"] = 0;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Update temperature based on power level
			if (IsActive)
			{
				double coolRate = (PowerLevel / 100.0) * 3.0; // °C per minute
				CurrentTemperature -= coolRate * elapsedTime.TotalMinutes;
				CurrentTemperature = Math.Max(5, CurrentTemperature);
			}
			else
			{
				// Warm up when off
				double warmRate = 1.0; // °C per minute
				double ambientTemp = 22.0;

				double tempDiff = ambientTemp - CurrentTemperature;
				CurrentTemperature += Math.Min(warmRate * elapsedTime.TotalMinutes,
											 tempDiff * 0.1 * elapsedTime.TotalMinutes);
				CurrentTemperature = Math.Min(ambientTemp, CurrentTemperature);
			}

			DiagnosticData["CurrentTemperature"] = CurrentTemperature;
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Cooler stuck on
					IsActive = true;
					AddAlarm("COOLER_STUCK_ON", "Cooling element stuck in ON state", AlarmSeverity.Major);
					break;

				case 1: // Cooler stuck off
					IsActive = false;
					PowerLevel = 0;
					AddAlarm("COOLER_FAILURE", "Cooling element failure", AlarmSeverity.Major);
					break;

				case 2: // Temperature sensor failure
					CurrentTemperature -= 10 + Random.NextDouble() * 20;
					AddAlarm("TEMP_SENSOR_FAILURE", "Temperature sensor failure on cooling element",
						AlarmSeverity.Warning);
					break;
			}

			DiagnosticData["IsActive"] = IsActive;
			DiagnosticData["PowerLevel"] = PowerLevel;
			DiagnosticData["CurrentTemperature"] = CurrentTemperature;
		}
	}

	/// <summary>
	/// Simulates a vial counter used to track vials entering and exiting the tunnel
	/// </summary>
	public class VialCounter : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		public int Count { get; private set; }
		public int TotalCount { get; private set; }
		public int CountRate { get; private set; } // Vials per minute

		private DateTime _lastCountTime;
		private int _countsInLastMinute;

		public VialCounter(string deviceId, string name)
			: base(deviceId, name)
		{
			Count = 0;
			TotalCount = 0;
			CountRate = 0;
			_lastCountTime = DateTime.Now;
			_countsInLastMinute = 0;

			DiagnosticData["Count"] = Count;
			DiagnosticData["TotalCount"] = TotalCount;
			DiagnosticData["CountRate"] = CountRate;
		}

		public void IncrementCount()
		{
			Count++;
			TotalCount++;
			_countsInLastMinute++;

			DiagnosticData["Count"] = Count;
			DiagnosticData["TotalCount"] = TotalCount;
		}

		public void ResetCount()
		{
			Count = 0;
			DiagnosticData["Count"] = Count;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			// Update count rate calculation
			TimeSpan timeSinceLastCount = DateTime.Now - _lastCountTime;
			if (timeSinceLastCount.TotalMinutes >= 1.0)
			{
				CountRate = _countsInLastMinute;
				_countsInLastMinute = 0;
				_lastCountTime = DateTime.Now;

				DiagnosticData["CountRate"] = CountRate;
			}
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(2);

			switch (faultType)
			{
				case 0: // Missing counts
					_countsInLastMinute = (int)(_countsInLastMinute * 0.6);
					AddAlarm("COUNTER_MISSING", "Counter missing vials", AlarmSeverity.Minor);
					break;

				case 1: // Double counting
					_countsInLastMinute = (int)(_countsInLastMinute * 1.5);
					AddAlarm("COUNTER_DOUBLE", "Counter registering false positives", AlarmSeverity.Minor);
					break;
			}

			DiagnosticData["CountRate"] = CountRate;
		}
	}
}