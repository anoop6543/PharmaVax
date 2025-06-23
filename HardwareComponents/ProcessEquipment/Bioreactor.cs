using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.ProcessEquipment
{
	public class Bioreactor : DeviceBase
	{
		public override DeviceType Type => DeviceType.ProcessEquipment;

		// Reactor parameters
		public double Volume { get; private set; } // Total volume in liters
		public double WorkingVolume { get; private set; } // Current working volume in liters
		public double Temperature { get; private set; } // Current temperature in Celsius
		public double pH { get; private set; } // Current pH value
		public double DissolvedOxygen { get; private set; } // Current DO in percentage
		public double AgitationSpeed { get; private set; } // Current agitation speed in RPM
		public double GasFlowRate { get; private set; } // Gas flow rate in LPM
		public double Pressure { get; private set; } // Pressure in bar
		public double CellDensity { get; private set; } // Cell density in cells/mL

		// Control setpoints
		public double TemperatureSetpoint { get; private set; } = 37.0;
		public double pHSetpoint { get; private set; } = 7.2;
		public double DOSetpoint { get; private set; } = 40.0;
		public double AgitationSetpoint { get; private set; } = 100.0;
		public double GasFlowSetpoint { get; private set; } = 5.0;

		// Process state
		public BioreactorState CurrentState { get; private set; }
		public double BatchTime { get; private set; } // Hours
		public string BatchId { get; private set; }
		public double ProductTiter { get; private set; } // Product concentration

		// Connected devices
		private TemperatureSensor _temperatureSensor;
		private VFDController _agitatorVFD;
		private ValveController _gasFlowValve;

		// Bioreactor process model parameters
		private double _kLa; // Oxygen mass transfer coefficient
		private double _cellGrowthRate; // Cell growth rate (per hour)
		private double _cellDeathRate; // Cell death rate (per hour)
		private double _metabolicRate; // Rate of substrate consumption per cell
		private double _productionRate; // Rate of product formation per cell
		private double _oxygenUptakeRate; // Oxygen uptake rate per cell
		private double _heatGenerationCoefficient; // Heat generation from metabolism

		// Process states
		private bool _contaminationPresent = false;
		private double _contaminationLevel = 0;
		private double _foulingLevel = 0;
		private bool _foamingPresent = false;
		private double _substrateConcentration;

		public Bioreactor(
			string deviceId,
			string name,
			double volume,
			TemperatureSensor temperatureSensor = null,
			VFDController agitatorVFD = null,
			ValveController gasFlowValve = null)
			: base(deviceId, name)
		{
			Volume = volume;
			WorkingVolume = 0; // Empty to start
			_temperatureSensor = temperatureSensor;
			_agitatorVFD = agitatorVFD;
			_gasFlowValve = gasFlowValve;

			CurrentState = BioreactorState.Idle;
			BatchId = "";

			// Initialize model parameters
			_kLa = 10.0; // Default value
			_cellGrowthRate = 0.05; // Default growth rate
			_cellDeathRate = 0.001; // Default death rate
			_metabolicRate = 0.0001; // Default metabolic rate
			_productionRate = 0.00001; // Default production rate
			_oxygenUptakeRate = 0.0002; // Default OUR
			_heatGenerationCoefficient = 0.02; // Default heat generation

			// Initialize process values
			Temperature = 22.0; // Room temperature
			pH = 7.0;
			DissolvedOxygen = 100.0; // Fully saturated
			AgitationSpeed = 0;
			GasFlowRate = 0;
			Pressure = 1.0; // Atmospheric
			CellDensity = 0;
			ProductTiter = 0;
			_substrateConcentration = 0;

			// Initialize diagnostics
			DiagnosticData["Volume"] = Volume;
			DiagnosticData["BatchId"] = BatchId;
			DiagnosticData["CurrentState"] = CurrentState.ToString();
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize connected devices if they exist
			_temperatureSensor?.Initialize();
			_agitatorVFD?.Initialize();
			_gasFlowValve?.Initialize();
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Update batch time
			if (CurrentState != BioreactorState.Idle && CurrentState != BioreactorState.Clean)
			{
				BatchTime += elapsedTime.TotalHours;
			}

			// Update temperature based on sensor if available
			if (_temperatureSensor != null && _temperatureSensor.Status == DeviceStatus.Running)
			{
				Temperature = _temperatureSensor.Temperature;
			}
			else
			{
				// Simple temperature model if no sensor
				double tempDiff = TemperatureSetpoint - Temperature;
				Temperature += tempDiff * 0.1 * elapsedTime.TotalMinutes;

				// Add metabolic heat generation
				Temperature += _heatGenerationCoefficient * CellDensity * elapsedTime.TotalHours / 1e6;
			}

			// Update agitation from VFD if available
			if (_agitatorVFD != null && _agitatorVFD.Status == DeviceStatus.Running)
			{
				// Convert percentage to RPM
				AgitationSpeed = (_agitatorVFD.Speed / 100.0) * 1200; // Assuming max RPM is 1200
			}
			else
			{
				// Simple agitation model if no VFD
				double agitDiff = AgitationSetpoint - AgitationSpeed;
				AgitationSpeed += agitDiff * 0.2 * elapsedTime.TotalSeconds;
			}

			// Update gas flow from valve if available
			if (_gasFlowValve != null && _gasFlowValve.Status == DeviceStatus.Running)
			{
				// Convert valve position to flow rate
				GasFlowRate = (_gasFlowValve.Position / 100.0) * GasFlowSetpoint;
			}
			else
			{
				// Simple gas flow model if no valve
				double gasDiff = GasFlowSetpoint - GasFlowRate;
				GasFlowRate += gasDiff * 0.3 * elapsedTime.TotalSeconds;
			}

			// Calculate kLa based on agitation and gas flow
			_kLa = 2.0 + (0.01 * AgitationSpeed) + (0.5 * GasFlowRate);

			// Update dissolved oxygen based on kLa, cell density, and oxygen uptake
			double oxygenIn = _kLa * (100 - DissolvedOxygen) * elapsedTime.TotalMinutes;
			double oxygenConsumption = CellDensity * _oxygenUptakeRate * elapsedTime.TotalMinutes;
			DissolvedOxygen = Math.Min(Math.Max(DissolvedOxygen + oxygenIn - oxygenConsumption, 0), 100);

			// Update cell growth based on current conditions
			if (CurrentState == BioreactorState.Growth || CurrentState == BioreactorState.Production)
			{
				// Adjust growth rate based on conditions
				double tempFactor = GetTemperatureFactor();
				double pHFactor = GetpHFactor();
				double doFactor = GetDOFactor();
				double substrateFactor = GetSubstrateFactor();

				double effectiveGrowthRate = _cellGrowthRate * tempFactor * pHFactor * doFactor * substrateFactor;
				effectiveGrowthRate = Math.Max(0, effectiveGrowthRate); // Can't be negative

				// Cell growth equation (simplified logistic growth)
				double maxDensity = 100e6; // Maximum cell density
				double growth = CellDensity * effectiveGrowthRate * (1 - CellDensity / maxDensity) * elapsedTime.TotalHours;

				// Cell death
				double death = CellDensity * _cellDeathRate * elapsedTime.TotalHours;

				// Update cell density
				CellDensity += growth - death;
				CellDensity = Math.Max(0, CellDensity);

				// Update substrate concentration
				_substrateConcentration -= CellDensity * _metabolicRate * elapsedTime.TotalHours;
				_substrateConcentration = Math.Max(0, _substrateConcentration);

				// Update product titer in production phase
				if (CurrentState == BioreactorState.Production)
				{
					ProductTiter += CellDensity * _productionRate * elapsedTime.TotalHours;
				}

				// Check for contamination growth
				if (_contaminationPresent)
				{
					_contaminationLevel *= (1 + 0.2 * elapsedTime.TotalHours); // Contamination grows faster

					// If contamination gets too high, it affects the culture
					if (_contaminationLevel > 0.01) // 1% contamination
					{
						AddAlarm("BIOREACTOR_CONTAMINATION", "Possible contamination detected", AlarmSeverity.Major);
					}
				}

				// Random chance of contamination occurring
				if (Random.NextDouble() < 0.0001 * elapsedTime.TotalHours)
				{
					_contaminationPresent = true;
					_contaminationLevel = 0.0001; // Initial 0.01% contamination
				}
			}

			// pH model (simplified)
			// Cell metabolism tends to make the medium more acidic
			if (CellDensity > 0)
			{
				double pHShift = -0.01 * CellDensity / 1e6 * elapsedTime.TotalHours;
				pH += pHShift;

				// pH control (simplified)
				double pHError = pHSetpoint - pH;
				pH += pHError * 0.1 * elapsedTime.TotalMinutes;
			}

			// Check for foaming
			if (AgitationSpeed > 800 && GasFlowRate > 8)
			{
				_foamingPresent = Random.NextDouble() < 0.1;

				if (_foamingPresent)
				{
					AddAlarm("BIOREACTOR_FOAMING", "Excessive foaming detected", AlarmSeverity.Minor);
				}
			}
			else
			{
				_foamingPresent = false;
			}

			// Update diagnostic data
			DiagnosticData["Temperature"] = Temperature;
			DiagnosticData["pH"] = pH;
			DiagnosticData["DissolvedOxygen"] = DissolvedOxygen;
			DiagnosticData["AgitationSpeed"] = AgitationSpeed;
			DiagnosticData["GasFlowRate"] = GasFlowRate;
			DiagnosticData["Pressure"] = Pressure;
			DiagnosticData["CellDensity"] = CellDensity;
			DiagnosticData["WorkingVolume"] = WorkingVolume;
			DiagnosticData["BatchTime"] = BatchTime;
			DiagnosticData["ProductTiter"] = ProductTiter;
			DiagnosticData["kLa"] = _kLa;
			DiagnosticData["SubstrateConcentration"] = _substrateConcentration;
			DiagnosticData["CurrentState"] = CurrentState.ToString();

			// Check alarm conditions
			CheckProcessAlarms();
		}

		private double GetTemperatureFactor()
		{
			// Simplified temperature effect on growth - peak at 37°C
			double optimumTemp = 37.0;
			double sensitivity = 0.1;
			return Math.Exp(-sensitivity * Math.Pow(Temperature - optimumTemp, 2));
		}

		private double GetpHFactor()
		{
			// Simplified pH effect on growth - peak at pH 7.2
			double optimumPH = 7.2;
			double sensitivity = 2.0;
			return Math.Exp(-sensitivity * Math.Pow(pH - optimumPH, 2));
		}

		private double GetDOFactor()
		{
			// Simplified DO effect - reduced growth below 20% DO
			if (DissolvedOxygen > 20)
				return 1.0;
			else
				return DissolvedOxygen / 20.0;
		}

		private double GetSubstrateFactor()
		{
			// Monod kinetics for substrate limitation
			double Ks = 0.1; // Half-saturation constant
			return _substrateConcentration / (Ks + _substrateConcentration);
		}

		private void CheckProcessAlarms()
		{
			// Temperature alarms
			if (Math.Abs(Temperature - TemperatureSetpoint) > 2.0)
			{
				AddAlarm("TEMP_DEVIATION",
					$"Temperature deviation: {Temperature:F1}°C vs setpoint {TemperatureSetpoint:F1}°C",
					AlarmSeverity.Warning);
			}

			// pH alarms
			if (Math.Abs(pH - pHSetpoint) > 0.5)
			{
				AddAlarm("PH_DEVIATION",
					$"pH deviation: {pH:F2} vs setpoint {pHSetpoint:F2}",
					AlarmSeverity.Warning);
			}

			// DO alarms
			if (DissolvedOxygen < 20)
			{
				AddAlarm("DO_LOW",
					$"Low dissolved oxygen: {DissolvedOxygen:F1}%",
					AlarmSeverity.Minor);
			}

			if (DissolvedOxygen < 5)
			{
				AddAlarm("DO_CRITICAL",
					$"Critical dissolved oxygen: {DissolvedOxygen:F1}%",
					AlarmSeverity.Major);
			}

			// Cell density alarms
			if (CurrentState == BioreactorState.Growth && BatchTime > 24 && CellDensity < 0.5e6)
			{
				AddAlarm("LOW_CELL_GROWTH",
					$"Below expected cell density: {CellDensity:E2} cells/mL",
					AlarmSeverity.Minor);
			}
		}

		public void StartBatch(string batchId, double initialVolume, double initialCells, double initialSubstrate)
		{
			if (CurrentState == BioreactorState.Idle || CurrentState == BioreactorState.Clean)
			{
				BatchId = batchId;
				WorkingVolume = Math.Min(initialVolume, Volume * 0.8); // Max 80% of total volume
				CellDensity = initialCells;
				_substrateConcentration = initialSubstrate;
				BatchTime = 0;
				ProductTiter = 0;
				_contaminationPresent = false;
				_contaminationLevel = 0;
				_foulingLevel = 0;

				CurrentState = BioreactorState.Growth;
				Status = DeviceStatus.Running;

				DiagnosticData["BatchId"] = BatchId;

				// Start connected devices
				_temperatureSensor?.Start();
				if (_agitatorVFD != null)
				{
					_agitatorVFD.Start();
					_agitatorVFD.SetSpeed(AgitationSetpoint / 12.0); // Convert RPM to percentage
				}
				if (_gasFlowValve != null)
				{
					_gasFlowValve.Start();
					_gasFlowValve.SetPosition(GasFlowSetpoint * 20); // Scale setpoint to valve position
				}

				// Set temperature setpoint if sensor available
				if (_temperatureSensor != null)
				{
					_temperatureSensor.SetTargetTemperature(TemperatureSetpoint);
				}
			}
			else
			{
				AddAlarm("INVALID_STATE", "Cannot start batch in current state", AlarmSeverity.Warning);
			}
		}

		public void TransitionToProduction()
		{
			if (CurrentState == BioreactorState.Growth)
			{
				CurrentState = BioreactorState.Production;
				DiagnosticData["CurrentState"] = CurrentState.ToString();
			}
			else
			{
				AddAlarm("INVALID_STATE", "Cannot transition to production in current state", AlarmSeverity.Warning);
			}
		}

		public void HarvestBatch()
		{
			if (CurrentState == BioreactorState.Production)
			{
				CurrentState = BioreactorState.Harvest;
				DiagnosticData["CurrentState"] = CurrentState.ToString();

				// Stop agitation and gas flow
				if (_agitatorVFD != null)
				{
					_agitatorVFD.SetSpeed(0);
				}
				if (_gasFlowValve != null)
				{
					_gasFlowValve.SetPosition(0);
				}

				// Return harvested product titer
				double harvestedProduct = ProductTiter;

				// Reset bioreactor state
				WorkingVolume = 0;
				CellDensity = 0;
				ProductTiter = 0;

				CurrentState = BioreactorState.Dirty;
				DiagnosticData["CurrentState"] = CurrentState.ToString();
			}
			else
			{
				AddAlarm("INVALID_STATE", "Cannot harvest in current state", AlarmSeverity.Warning);
			}
		}

		public void CleanBioreactor()
		{
			if (CurrentState == BioreactorState.Dirty)
			{
				CurrentState = BioreactorState.Clean;

				// Simulate cleaning process
				_contaminationPresent = false;
				_contaminationLevel = 0;
				_foulingLevel = 0;

				DiagnosticData["CurrentState"] = CurrentState.ToString();
			}
			else
			{
				AddAlarm("INVALID_STATE", "Cannot clean in current state", AlarmSeverity.Warning);
			}
		}

		public void SetSetpoints(double temperature, double pH, double dissolvedOxygen, double agitation, double gasFlow)
		{
			TemperatureSetpoint = temperature;
			pHSetpoint = pH;
			DOSetpoint = dissolvedOxygen;
			AgitationSetpoint = agitation;
			GasFlowSetpoint = gasFlow;

			// Update connected devices
			if (_temperatureSensor != null)
			{
				_temperatureSensor.SetTargetTemperature(temperature);
			}

			if (_agitatorVFD != null)
			{
				_agitatorVFD.SetSpeed(agitation / 12.0); // Convert RPM to percentage (assuming 1200 RPM max)
			}

			if (_gasFlowValve != null)
			{
				_gasFlowValve.SetPosition(gasFlow * 20); // Scale gas flow to valve position
			}
		}

		public void AddMedium(double volume, double substrateConcentration)
		{
			if (WorkingVolume + volume <= Volume * 0.9) // Prevent overflow
			{
				// Calculate new substrate concentration (mix of existing and added)
				double totalSubstrate = (_substrateConcentration * WorkingVolume) + (substrateConcentration * volume);
				WorkingVolume += volume;
				_substrateConcentration = totalSubstrate / WorkingVolume;

				// Dilute cell density
				CellDensity = CellDensity * (WorkingVolume - volume) / WorkingVolume;

				DiagnosticData["WorkingVolume"] = WorkingVolume;
				DiagnosticData["SubstrateConcentration"] = _substrateConcentration;
			}
			else
			{
				AddAlarm("VOLUME_LIMIT", "Cannot add medium, volume limit reached", AlarmSeverity.Warning);
			}
		}

		public double RemoveMedium(double volume)
		{
			double actualRemoved = Math.Min(volume, WorkingVolume * 0.9); // Never remove more than 90%

			if (actualRemoved > 0)
			{
				WorkingVolume -= actualRemoved;
				DiagnosticData["WorkingVolume"] = WorkingVolume;
			}

			return actualRemoved;
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(5);

			switch (faultType)
			{
				case 0: // Temperature control failure
					AddAlarm("TEMP_CONTROL_FAILURE", "Temperature control system failure", AlarmSeverity.Major);
					Temperature += (Random.NextDouble() * 10) - 5; // Random temp shift
					break;

				case 1: // Agitation failure
					AddAlarm("AGITATION_FAILURE", "Agitation system failure", AlarmSeverity.Major);
					AgitationSpeed = 0;
					break;

				case 2: // Contamination
					if (!_contaminationPresent)
					{
						_contaminationPresent = true;
						_contaminationLevel = 0.01;
						AddAlarm("CONTAMINATION", "Possible contamination detected", AlarmSeverity.Major);
					}
					break;

				case 3: // Gas supply issue
					AddAlarm("GAS_SUPPLY_ISSUE", "Gas supply pressure fluctuation", AlarmSeverity.Minor);
					GasFlowRate *= 0.5;
					break;

				case 4: // pH probe drift
					AddAlarm("PH_PROBE_DRIFT", "pH probe calibration drift detected", AlarmSeverity.Warning);
					pH += 0.5 * (Random.NextDouble() * 2 - 1); // Random pH shift
					break;
			}
		}
	}

	public enum BioreactorState
	{
		Idle,
		Growth,
		Production,
		Harvest,
		Dirty,
		Clean
	}
}