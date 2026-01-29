using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using PharmaVax.HardwareComponents.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace PharmaceuticalProcess.HardwareComponents.ProcessEquipment
{
	/// <summary>
	/// Simulates a Clean-in-Place (CIP) system for automated cleaning of pharmaceutical process equipment
	/// </summary>
	public class CIPSystem : DeviceBase
	{
		public override DeviceType Type => DeviceType.ProcessEquipment;

		#region Properties

		// CIP system core parameters
		public double FlowRate { get; private set; }                  // L/min
		public double SupplyPressure { get; private set; }            // bar
		public double ReturnPressure { get; private set; }            // bar
		public double SupplyTemperature { get; private set; }         // °C
		public double ReturnTemperature { get; private set; }         // °C
		public double ReturnConductivity { get; private set; }        // mS/cm
		public double ReturnTurbidity { get; private set; }           // NTU
		public double ChemicalConcentration { get; private set; }     // %

		// Tank levels (%)
		public double WaterTankLevel { get; private set; }
		public double CausticTankLevel { get; private set; }
		public double AcidTankLevel { get; private set; }
		public double SanitizationTankLevel { get; private set; }

		// Process state
		public CIPPhase CurrentPhase { get; private set; }
		public CIPState CurrentState { get; private set; }
		public string CurrentRecipe { get; private set; }
		public double ProcessTime { get; private set; }               // Minutes
		public double PhaseTime { get; private set; }                 // Minutes
		public bool ValidatedCycle { get; private set; }
		public double TotalWaterUsed { get; private set; }            // Liters
		public double TotalCausticUsed { get; private set; }          // Liters
		public double TotalAcidUsed { get; private set; }             // Liters
		public double TotalSanitizerUsed { get; private set; }        // Liters
		public double TotalEnergyUsed { get; private set; }           // kWh

		// Operational parameters
		public double TargetFlowRate { get; private set; }            // L/min
		public double TargetTemperature { get; private set; }         // °C
		public double TargetCausticConcentration { get; private set; } // %
		public double TargetAcidConcentration { get; private set; }    // %
		public double TargetSanitizerConcentration { get; private set; } // %
		public double MinimumConductivity { get; private set; }       // mS/cm
		public double MaximumTurbidity { get; private set; }          // NTU

		// Connected equipment
		public string ConnectedEquipmentId { get; private set; }
		public string ConnectedEquipmentType { get; private set; }

		#endregion

		#region Private Fields

		// Connected hardware components
		private PumpController _supplyPump;
		private PumpController _returnPump;
		private PumpController _causticPump;
		private PumpController _acidPump;
		private PumpController _sanitizerPump;
		private ValveController _supplyValve;
		private ValveController _returnValve;
		private ValveController _drainValve;
		private ValveController _waterValve;
		private ValveController _causticValve;
		private ValveController _acidValve;
		private ValveController _sanitizerValve;
		private HeatingElement _heater;
		private TemperatureSensor _supplyTempSensor;
		private TemperatureSensor _returnTempSensor;
		private PressureSensor _supplyPressureSensor;
		private PressureSensor _returnPressureSensor;
		private ConductivitySensor _conductivitySensor;
		private TurbiditySensor _turbiditySensor;

		// Recipe and phase configuration
		private Dictionary<string, CIPRecipe> _recipes;
		private CIPRecipe _currentRecipe;
		private Dictionary<CIPPhase, double> _phaseDurations;
		private Dictionary<CIPPhase, CIPPhaseParameters> _phaseParameters;
		private double _phaseStartTime;

		// Resource levels
		private double _waterTankCapacity = 2000.0;      // Liters
		private double _causticTankCapacity = 500.0;     // Liters
		private double _acidTankCapacity = 500.0;        // Liters
		private double _sanitizationTankCapacity = 300.0; // Liters

		// Operational tracking
		private bool _cycleInProgress;
		private int _currentCycleStep;
		private List<CIPLogEntry> _cycleLog;
		private DateTime _cycleStartTime;
		private bool _recipeLoaded;
		private double _heaterPower;                     // kW
		private int _maintenanceCountdown;               // Operating hours until maintenance
		private double _waterConsumptionRate;            // L/min
		private double _chemicalConsumptionRate;         // L/min

		#endregion

		/// <summary>
		/// Initializes a new instance of the CIPSystem class
		/// </summary>
		public CIPSystem(
			string deviceId,
			string name,
			double supplyPumpCapacity = 500.0,
			double maxTemperature = 95.0,
			PumpController supplyPump = null,
			PumpController returnPump = null,
			HeatingElement heater = null)
			: base(deviceId, name)
		{
			// Initialize basic parameters
			FlowRate = 0.0;
			SupplyPressure = 0.0;
			ReturnPressure = 0.0;
			SupplyTemperature = 20.0; // Ambient temperature
			ReturnTemperature = 20.0;
			ReturnConductivity = 0.0;
			ReturnTurbidity = 0.0;
			ChemicalConcentration = 0.0;

			// Initialize tank levels (%)
			WaterTankLevel = 100.0;
			CausticTankLevel = 100.0;
			AcidTankLevel = 100.0;
			SanitizationTankLevel = 100.0;

			// Initialize state
			CurrentPhase = CIPPhase.Idle;
			CurrentState = CIPState.Idle;
			CurrentRecipe = "";
			ProcessTime = 0.0;
			PhaseTime = 0.0;
			ValidatedCycle = false;

			// Initialize operational parameters
			TargetFlowRate = supplyPumpCapacity * 0.8; // 80% of pump capacity
			TargetTemperature = 65.0; // Default temperature
			TargetCausticConcentration = 2.0; // 2% NaOH
			TargetAcidConcentration = 1.5; // 1.5% acid
			TargetSanitizerConcentration = 0.2; // 0.2% sanitizer
			MinimumConductivity = 0.05; // Clean water threshold
			MaximumTurbidity = 10.0; // Clean water threshold

			// Initialize connected equipment
			ConnectedEquipmentId = "";
			ConnectedEquipmentType = "";

			// Initialize connected hardware components
			_supplyPump = supplyPump;
			_returnPump = returnPump;
			_heater = heater;

			// Initialize recipe collection
			_recipes = new Dictionary<string, CIPRecipe>();
			_phaseDurations = new Dictionary<CIPPhase, double>();
			_phaseParameters = new Dictionary<CIPPhase, CIPPhaseParameters>();

			// Initialize operational tracking
			_cycleInProgress = false;
			_currentCycleStep = 0;
			_cycleLog = new List<CIPLogEntry>();
			_recipeLoaded = false;
			_heaterPower = 50.0; // 50kW heater
			_maintenanceCountdown = 2000; // 2000 operating hours
			_waterConsumptionRate = 0;
			_chemicalConsumptionRate = 0;

			// Initialize default recipes
			InitializeDefaultRecipes();

			// Initialize diagnostics
			InitializeDiagnostics();
		}

		private void InitializeDiagnostics()
		{
			// Basic operational parameters
			DiagnosticData["FlowRate"] = FlowRate;
			DiagnosticData["SupplyPressure"] = SupplyPressure;
			DiagnosticData["ReturnPressure"] = ReturnPressure;
			DiagnosticData["SupplyTemperature"] = SupplyTemperature;
			DiagnosticData["ReturnTemperature"] = ReturnTemperature;
			DiagnosticData["ReturnConductivity"] = ReturnConductivity;
			DiagnosticData["ReturnTurbidity"] = ReturnTurbidity;

			// Tank levels
			DiagnosticData["WaterTankLevel"] = WaterTankLevel;
			DiagnosticData["CausticTankLevel"] = CausticTankLevel;
			DiagnosticData["AcidTankLevel"] = AcidTankLevel;
			DiagnosticData["SanitizationTankLevel"] = SanitizationTankLevel;

			// Process state
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["CurrentRecipe"] = CurrentRecipe;
			DiagnosticData["ProcessTime"] = ProcessTime;
			DiagnosticData["PhaseTime"] = PhaseTime;

			// Resource usage
			DiagnosticData["TotalWaterUsed"] = TotalWaterUsed;
			DiagnosticData["TotalCausticUsed"] = TotalCausticUsed;
			DiagnosticData["TotalAcidUsed"] = TotalAcidUsed;
			DiagnosticData["TotalSanitizerUsed"] = TotalSanitizerUsed;
			DiagnosticData["TotalEnergyUsed"] = TotalEnergyUsed;

			// Maintenance
			DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;
		}

		private void InitializeDefaultRecipes()
		{
			// Standard recipe for tanks and vessels
			var standardRecipe = new CIPRecipe
			{
				Name = "Standard Vessel",
				Description = "Standard CIP cycle for tanks and vessels",
				IncludePreRinse = true,
				IncludeCausticWash = true,
				IncludeIntermediateRinse = true,
				IncludeAcidWash = true,
				IncludeFinalRinse = true,
				IncludeSanitization = true,
				PreRinseDuration = 10.0,
				CausticWashDuration = 20.0,
				IntermediateRinseDuration = 10.0,
				AcidWashDuration = 15.0,
				FinalRinseDuration = 15.0,
				SanitizationDuration = 10.0,
				PreRinseTemperature = 25.0,
				CausticWashTemperature = 75.0,
				IntermediateRinseTemperature = 25.0,
				AcidWashTemperature = 65.0,
				FinalRinseTemperature = 25.0,
				SanitizationTemperature = 80.0,
				CausticConcentration = 2.0,
				AcidConcentration = 1.5,
				SanitizerConcentration = 0.2,
				RequiredFlowRate = 300.0
			};
			_recipes["Standard Vessel"] = standardRecipe;

			// Brief recipe for simple piping
			var pipingRecipe = new CIPRecipe
			{
				Name = "Piping",
				Description = "Quick CIP cycle for product transfer lines",
				IncludePreRinse = true,
				IncludeCausticWash = true,
				IncludeIntermediateRinse = true,
				IncludeAcidWash = false,
				IncludeFinalRinse = true,
				IncludeSanitization = true,
				PreRinseDuration = 5.0,
				CausticWashDuration = 10.0,
				IntermediateRinseDuration = 5.0,
				FinalRinseDuration = 10.0,
				SanitizationDuration = 5.0,
				PreRinseTemperature = 25.0,
				CausticWashTemperature = 70.0,
				IntermediateRinseTemperature = 25.0,
				FinalRinseTemperature = 25.0,
				SanitizationTemperature = 80.0,
				CausticConcentration = 1.5,
				SanitizerConcentration = 0.2,
				RequiredFlowRate = 400.0
			};
			_recipes["Piping"] = pipingRecipe;

			// Thorough recipe for chromatography systems
			var chromatographyRecipe = new CIPRecipe
			{
				Name = "Chromatography",
				Description = "Thorough cleaning for chromatography skids",
				IncludePreRinse = true,
				IncludeCausticWash = true,
				IncludeIntermediateRinse = true,
				IncludeAcidWash = true,
				IncludeFinalRinse = true,
				IncludeSanitization = true,
				PreRinseDuration = 15.0,
				CausticWashDuration = 30.0,
				IntermediateRinseDuration = 15.0,
				AcidWashDuration = 20.0,
				FinalRinseDuration = 20.0,
				SanitizationDuration = 15.0,
				PreRinseTemperature = 30.0,
				CausticWashTemperature = 65.0,
				IntermediateRinseTemperature = 30.0,
				AcidWashTemperature = 60.0,
				FinalRinseTemperature = 30.0,
				SanitizationTemperature = 85.0,
				CausticConcentration = 1.0, // Lower for sensitive resins
				AcidConcentration = 1.0,
				SanitizerConcentration = 0.1,
				RequiredFlowRate = 250.0
			};
			_recipes["Chromatography"] = chromatographyRecipe;

			// Fill line recipe
			var fillLineRecipe = new CIPRecipe
			{
				Name = "Fill Line",
				Description = "CIP cycle for fill lines with extended sanitization",
				IncludePreRinse = true,
				IncludeCausticWash = true,
				IncludeIntermediateRinse = true,
				IncludeAcidWash = true,
				IncludeFinalRinse = true,
				IncludeSanitization = true,
				PreRinseDuration = 10.0,
				CausticWashDuration = 25.0,
				IntermediateRinseDuration = 15.0,
				AcidWashDuration = 20.0,
				FinalRinseDuration = 20.0,
				SanitizationDuration = 30.0, // Longer sanitization for aseptic areas
				PreRinseTemperature = 30.0,
				CausticWashTemperature = 80.0,
				IntermediateRinseTemperature = 30.0,
				AcidWashTemperature = 65.0,
				FinalRinseTemperature = 30.0,
				SanitizationTemperature = 90.0, // Higher temp sanitization
				CausticConcentration = 2.5,
				AcidConcentration = 2.0,
				SanitizerConcentration = 0.5,
				RequiredFlowRate = 200.0
			};
			_recipes["Fill Line"] = fillLineRecipe;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize connected devices if available
			_supplyPump?.Initialize();
			_returnPump?.Initialize();
			_causticPump?.Initialize();
			_acidPump?.Initialize();
			_sanitizerPump?.Initialize();
			_supplyValve?.Initialize();
			_returnValve?.Initialize();
			_drainValve?.Initialize();
			_waterValve?.Initialize();
			_causticValve?.Initialize();
			_acidValve?.Initialize();
			_sanitizerValve?.Initialize();
			_heater?.Initialize();
			_supplyTempSensor?.Initialize();
			_returnTempSensor?.Initialize();
			_supplyPressureSensor?.Initialize();
			_returnPressureSensor?.Initialize();
			_conductivitySensor?.Initialize();
			_turbiditySensor?.Initialize();

			// Reset process state
			CurrentPhase = CIPPhase.Idle;
			CurrentState = CIPState.Idle;
			ProcessTime = 0.0;
			PhaseTime = 0.0;

			// Reset counters
			TotalWaterUsed = 0.0;
			TotalCausticUsed = 0.0;
			TotalAcidUsed = 0.0;
			TotalSanitizerUsed = 0.0;
			TotalEnergyUsed = 0.0;

			// Reset operational tracking
			_cycleInProgress = false;
			_currentCycleStep = 0;
			_cycleLog.Clear();
			_waterConsumptionRate = 0;
			_chemicalConsumptionRate = 0;

			// Reset sensors to ambient values
			SupplyTemperature = 20.0;
			ReturnTemperature = 20.0;
			SupplyPressure = 0.0;
			ReturnPressure = 0.0;
			ReturnConductivity = 0.05; // Clean water baseline
			ReturnTurbidity = 0.5;     // Clean water baseline
			FlowRate = 0.0;

			// Update diagnostics
			UpdateDiagnostics();
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Update process time
			ProcessTime += elapsedTime.TotalMinutes;
			if (CurrentPhase != CIPPhase.Idle)
			{
				PhaseTime += elapsedTime.TotalMinutes;
			}

			// Update maintenance countdown
			if (CurrentState == CIPState.CycleRunning || CurrentState == CIPState.CycleCompleting)
			{
				_maintenanceCountdown -= elapsedTime.TotalHours;
			}

			// Read sensor values if available
			UpdateSensorReadings();

			// Process current phase
			ProcessCurrentPhase(elapsedTime);

			// Update resource levels
			UpdateResourceLevels(elapsedTime);

			// Check for phase completion
			CheckPhaseCompletion();

			// Check alarm conditions
			CheckAlarmConditions();

			// Update diagnostics
			UpdateDiagnostics();
		}

		private void UpdateSensorReadings()
		{
			// Update temperature readings from sensors if available
			if (_supplyTempSensor != null && _supplyTempSensor.Status == DeviceStatus.Running)
			{
				SupplyTemperature = _supplyTempSensor.Temperature;
			}

			if (_returnTempSensor != null && _returnTempSensor.Status == DeviceStatus.Running)
			{
				ReturnTemperature = _returnTempSensor.Temperature;
			}

			// Update pressure readings from sensors if available
			if (_supplyPressureSensor != null && _supplyPressureSensor.Status == DeviceStatus.Running)
			{
				SupplyPressure = _supplyPressureSensor.Pressure;
			}

			if (_returnPressureSensor != null && _returnPressureSensor.Status == DeviceStatus.Running)
			{
				ReturnPressure = _returnPressureSensor.Pressure;
			}

			// Update analytical readings from sensors if available
			if (_conductivitySensor != null && _conductivitySensor.Status == DeviceStatus.Running)
			{
				ReturnConductivity = _conductivitySensor.Conductivity;
			}

			if (_turbiditySensor != null && _turbiditySensor.Status == DeviceStatus.Running)
			{
				ReturnTurbidity = _turbiditySensor.Turbidity;
			}

			// Calculate flow rate from pump speeds
			double flowRate = 0;
			if (_supplyPump != null && _supplyPump.Status == DeviceStatus.Running)
			{
				// Convert pump speed % to flow rate
				flowRate = (_supplyPump.Speed / 100.0) * TargetFlowRate;
			}
			FlowRate = flowRate;
		}

		private void ProcessCurrentPhase(TimeSpan elapsedTime)
		{
			if (!_cycleInProgress)
				return;

			switch (CurrentPhase)
			{
				case CIPPhase.Idle:
					// Nothing to do in idle state
					break;

				case CIPPhase.PreRinse:
					ProcessPreRinsePhase(elapsedTime);
					break;

				case CIPPhase.CausticWash:
					ProcessCausticWashPhase(elapsedTime);
					break;

				case CIPPhase.IntermediateRinse:
					ProcessIntermediateRinsePhase(elapsedTime);
					break;

				case CIPPhase.AcidWash:
					ProcessAcidWashPhase(elapsedTime);
					break;

				case CIPPhase.FinalRinse:
					ProcessFinalRinsePhase(elapsedTime);
					break;

				case CIPPhase.Sanitization:
					ProcessSanitizationPhase(elapsedTime);
					break;

				case CIPPhase.Draining:
					ProcessDrainingPhase(elapsedTime);
					break;
			}
		}

		private void ProcessPreRinsePhase(TimeSpan elapsedTime)
		{
			// Pre-rinse uses fresh water at ambient temperature
			TargetTemperature = _currentRecipe.PreRinseTemperature;
			_waterConsumptionRate = FlowRate;
			_chemicalConsumptionRate = 0;
			ChemicalConcentration = 0;

			// Generate conductivity and turbidity readings based on rinse progress
			// Initial rinse has high turbidity and conductivity, which gradually decreases
			double phaseProgress = PhaseTime / (_phaseDurations[CIPPhase.PreRinse] ?? 10.0);
			ReturnTurbidity = 50.0 * Math.Max(0, 1.0 - phaseProgress) + 0.5;
			ReturnConductivity = 0.5 * Math.Max(0, 1.0 - phaseProgress) + 0.05;

			// Adjust supply temperature
			AdjustTemperature(elapsedTime);

			// Track water usage
			TotalWaterUsed += (_waterConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
		}

		private void ProcessCausticWashPhase(TimeSpan elapsedTime)
		{
			// Caustic wash uses hot water with caustic solution
			TargetTemperature = _currentRecipe.CausticWashTemperature;
			_waterConsumptionRate = FlowRate * 0.98; // 98% water, 2% caustic
			_chemicalConsumptionRate = FlowRate * 0.02;
			ChemicalConcentration = _currentRecipe.CausticConcentration;

			// Generate conductivity and turbidity readings
			// Caustic solution has high conductivity as it dissolves contaminants
			double phaseProgress = PhaseTime / (_phaseDurations[CIPPhase.CausticWash] ?? 20.0);

			// Initial conductivity rise as caustic circulates, then plateau, then slight decrease
			if (phaseProgress < 0.2)
			{
				ReturnConductivity = 10.0 * (phaseProgress / 0.2) + 0.5;
			}
			else if (phaseProgress < 0.7)
			{
				ReturnConductivity = 10.5 + (phaseProgress - 0.2) * 5.0;
			}
			else
			{
				ReturnConductivity = 15.5 - (phaseProgress - 0.7) * 2.0;
			}

			// Turbidity increases as soils are removed, then decreases
			ReturnTurbidity = 40.0 * Math.Sin(phaseProgress * Math.PI) + 10.0;

			// Adjust supply temperature
			AdjustTemperature(elapsedTime);

			// Track resource usage
			TotalWaterUsed += (_waterConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
			TotalCausticUsed += (_chemicalConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
		}

		private void ProcessIntermediateRinsePhase(TimeSpan elapsedTime)
		{
			// Intermediate rinse uses fresh water to remove caustic
			TargetTemperature = _currentRecipe.IntermediateRinseTemperature;
			_waterConsumptionRate = FlowRate;
			_chemicalConsumptionRate = 0;
			ChemicalConcentration = 0;

			// Generate conductivity and turbidity readings based on rinse progress
			// Initial rinse has high conductivity from caustic residue, which gradually decreases
			double phaseProgress = PhaseTime / (_phaseDurations[CIPPhase.IntermediateRinse] ?? 10.0);
			ReturnConductivity = 10.0 * Math.Max(0, 1.0 - phaseProgress) + 0.1;
			ReturnTurbidity = 5.0 * Math.Max(0, 1.0 - phaseProgress) + 0.5;

			// Adjust supply temperature
			AdjustTemperature(elapsedTime);

			// Track water usage
			TotalWaterUsed += (_waterConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
		}

		private void ProcessAcidWashPhase(TimeSpan elapsedTime)
		{
			// Acid wash uses warm water with acid solution
			TargetTemperature = _currentRecipe.AcidWashTemperature;
			_waterConsumptionRate = FlowRate * 0.985; // 98.5% water, 1.5% acid
			_chemicalConsumptionRate = FlowRate * 0.015;
			ChemicalConcentration = _currentRecipe.AcidConcentration;

			// Generate conductivity and turbidity readings
			// Acid solution has high conductivity as it dissolves mineral deposits
			double phaseProgress = PhaseTime / (_phaseDurations[CIPPhase.AcidWash] ?? 15.0);

			// Conductivity increases as acid dissolves minerals then plateaus
			if (phaseProgress < 0.3)
			{
				ReturnConductivity = 8.0 * (phaseProgress / 0.3) + 0.2;
			}
			else
			{
				ReturnConductivity = 8.2 + (phaseProgress - 0.3) * 1.0;
			}

			// Turbidity increases slightly as minerals dissolve, then decreases
			ReturnTurbidity = 10.0 * Math.Sin(phaseProgress * Math.PI) + 2.0;

			// Adjust supply temperature
			AdjustTemperature(elapsedTime);

			// Track resource usage
			TotalWaterUsed += (_waterConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
			TotalAcidUsed += (_chemicalConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
		}

		private void ProcessFinalRinsePhase(TimeSpan elapsedTime)
		{
			// Final rinse uses fresh water to remove all chemicals
			TargetTemperature = _currentRecipe.FinalRinseTemperature;
			_waterConsumptionRate = FlowRate;
			_chemicalConsumptionRate = 0;
			ChemicalConcentration = 0;

			// Generate conductivity and turbidity readings based on rinse progress
			// Initial rinse has moderate conductivity from acid residue, which gradually decreases
			double phaseProgress = PhaseTime / (_phaseDurations[CIPPhase.FinalRinse] ?? 15.0);

			// Exponential decay to near-zero conductivity (pure water)
			ReturnConductivity = 5.0 * Math.Exp(-5.0 * phaseProgress) + 0.05;
			ReturnTurbidity = 2.0 * Math.Exp(-5.0 * phaseProgress) + 0.2;

			// Adjust supply temperature
			AdjustTemperature(elapsedTime);

			// Track water usage
			TotalWaterUsed += (_waterConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
		}

		private void ProcessSanitizationPhase(TimeSpan elapsedTime)
		{
			// Sanitization uses hot water with sanitizer
			TargetTemperature = _currentRecipe.SanitizationTemperature;
			_waterConsumptionRate = FlowRate * 0.998; // 99.8% water, 0.2% sanitizer
			_chemicalConsumptionRate = FlowRate * 0.002;
			ChemicalConcentration = _currentRecipe.SanitizerConcentration;

			// Generate conductivity and turbidity readings
			// Sanitizer doesn't affect conductivity much, but temperature does
			ReturnConductivity = 0.2 + (SupplyTemperature / 100.0) * 0.3;
			ReturnTurbidity = 0.5;

			// Adjust supply temperature
			AdjustTemperature(elapsedTime);

			// Track resource usage
			TotalWaterUsed += (_waterConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
			TotalSanitizerUsed += (_chemicalConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
		}

		private void ProcessDrainingPhase(TimeSpan elapsedTime)
		{
			// Draining phase involves air purging and minimal water usage
			TargetTemperature = 20.0; // Ambient temperature
			_waterConsumptionRate = 0;
			_chemicalConsumptionRate = 0;

			// Flow rate gradually decreases as system drains
			double phaseProgress = PhaseTime / (_phaseDurations[CIPPhase.Draining] ?? 5.0);
			FlowRate = TargetFlowRate * Math.Max(0, 1.0 - phaseProgress);

			// Conductivity and turbidity remain at final rinse levels
			ReturnConductivity = 0.05;
			ReturnTurbidity = 0.2;

			// Pressure decreases during draining
			SupplyPressure = 2.0 * Math.Max(0, 1.0 - phaseProgress);
			ReturnPressure = 0.2 * Math.Max(0, 1.0 - phaseProgress);

			// Temperature gradually returns to ambient
			SupplyTemperature = 20.0 + (ReturnTemperature - 20.0) * Math.Max(0, 1.0 - phaseProgress);
			ReturnTemperature = SupplyTemperature;
		}

		private void AdjustTemperature(TimeSpan elapsedTime)
		{
			// Temperature adjustment with heating/cooling lag
			double temperatureDelta = TargetTemperature - SupplyTemperature;
			double maxTempChangeRate = 1.0; // °C per minute
			double actualChangeRate = Math.Sign(temperatureDelta) *
									 Math.Min(Math.Abs(temperatureDelta) * 0.1, maxTempChangeRate);

			// Apply temperature change with some lag to simulate heating element response
			SupplyTemperature += actualChangeRate * elapsedTime.TotalMinutes;

			// Return temperature lags slightly behind supply temperature
			ReturnTemperature += ((SupplyTemperature - ReturnTemperature) * 0.2) * elapsedTime.TotalMinutes;

			// Calculate energy usage for heating
			if (temperatureDelta > 0)
			{
				// Energy used = mass * specific heat * temperature change
				// Assuming water with 4.186 kJ/kg/°C specific heat
				double massFlowRate = FlowRate / 60.0; // Convert to kg/s (assuming 1L = 1kg)
				double energyRate = massFlowRate * 4.186 * actualChangeRate / 60.0; // kW
				TotalEnergyUsed += energyRate * elapsedTime.TotalMinutes / 60.0; // kWh
			}
		}

		private void UpdateResourceLevels(TimeSpan elapsedTime)
		{
			// Update water tank level
			double waterUsedThisCycle = (_waterConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
			WaterTankLevel = Math.Max(0, WaterTankLevel - (waterUsedThisCycle / _waterTankCapacity * 100.0));

			// Update caustic tank level (if using caustic)
			if (CurrentPhase == CIPPhase.CausticWash)
			{
				double causticUsedThisCycle = (_chemicalConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
				CausticTankLevel = Math.Max(0, CausticTankLevel - (causticUsedThisCycle / _causticTankCapacity * 100.0));
			}

			// Update acid tank level (if using acid)
			if (CurrentPhase == CIPPhase.AcidWash)
			{
				double acidUsedThisCycle = (_chemicalConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
				AcidTankLevel = Math.Max(0, AcidTankLevel - (acidUsedThisCycle / _acidTankCapacity * 100.0));
			}

			// Update sanitizer tank level (if using sanitizer)
			if (CurrentPhase == CIPPhase.Sanitization)
			{
				double sanitizerUsedThisCycle = (_chemicalConsumptionRate / 60.0) * elapsedTime.TotalMinutes;
				SanitizationTankLevel = Math.Max(0, SanitizationTankLevel - (sanitizerUsedThisCycle / _sanitizationTankCapacity * 100.0));
			}
		}

		private void CheckPhaseCompletion()
		{
			// Check if current phase is complete
			if (CurrentPhase == CIPPhase.Idle || !_cycleInProgress || !_phaseDurations.ContainsKey(CurrentPhase))
				return;

			double phaseDuration = _phaseDurations[CurrentPhase];
			if (PhaseTime >= phaseDuration)
			{
				// Log phase completion
				_cycleLog.Add(new CIPLogEntry
				{
					TimeStamp = DateTime.Now,
					Phase = CurrentPhase,
					Message = $"{CurrentPhase} phase completed after {PhaseTime:F1} minutes",
					SupplyTemperature = SupplyTemperature,
					ReturnTemperature = ReturnTemperature,
					FlowRate = FlowRate,
					Conductivity = ReturnConductivity,
					Turbidity = ReturnTurbidity
				});

				// Add alarm for phase completion
				AddAlarm($"{CurrentPhase.ToString().ToUpper()}_COMPLETE",
						$"{CurrentPhase} phase completed",
						AlarmSeverity.Information);

				// Move to next phase
				AdvanceToNextPhase();
			}
		}

		private void AdvanceToNextPhase()
		{
			if (!_recipeLoaded || _currentRecipe == null)
				return;

			// Determine next phase based on current phase and recipe
			CIPPhase nextPhase = CIPPhase.Idle;

			switch (CurrentPhase)
			{
				case CIPPhase.PreRinse:
					nextPhase = _currentRecipe.IncludeCausticWash ?
						CIPPhase.CausticWash : (_currentRecipe.IncludeAcidWash ?
							CIPPhase.AcidWash : (_currentRecipe.IncludeFinalRinse ?
								CIPPhase.FinalRinse : (_currentRecipe.IncludeSanitization ?
									CIPPhase.Sanitization : CIPPhase.Draining)));
					break;

				case CIPPhase.CausticWash:
					nextPhase = _currentRecipe.IncludeIntermediateRinse ?
						CIPPhase.IntermediateRinse : (_currentRecipe.IncludeAcidWash ?
							CIPPhase.AcidWash : (_currentRecipe.IncludeFinalRinse ?
								CIPPhase.FinalRinse : (_currentRecipe.IncludeSanitization ?
									CIPPhase.Sanitization : CIPPhase.Draining)));
					break;

				case CIPPhase.IntermediateRinse:
					nextPhase = _currentRecipe.IncludeAcidWash ?
						CIPPhase.AcidWash : (_currentRecipe.IncludeFinalRinse ?
							CIPPhase.FinalRinse : (_currentRecipe.IncludeSanitization ?
								CIPPhase.Sanitization : CIPPhase.Draining));
					break;

				case CIPPhase.AcidWash:
					nextPhase = _currentRecipe.IncludeFinalRinse ?
						CIPPhase.FinalRinse : (_currentRecipe.IncludeSanitization ?
							CIPPhase.Sanitization : CIPPhase.Draining);
					break;

				case CIPPhase.FinalRinse:
					nextPhase = _currentRecipe.IncludeSanitization ?
						CIPPhase.Sanitization : CIPPhase.Draining;
					break;

				case CIPPhase.Sanitization:
					nextPhase = CIPPhase.Draining;
					break;

				case CIPPhase.Draining:
					// After draining, we're done
					CompleteCycle();
					return;
			}

			// Set the next phase
			SetPhase(nextPhase);
		}

		private void SetPhase(CIPPhase phase)
		{
			// Save previous phase for reporting
			CIPPhase previousPhase = CurrentPhase;

			// Update phase
			CurrentPhase = phase;
			PhaseTime = 0.0; // Reset phase time
			_phaseStartTime = ProcessTime;

			// Configure hardware for the new phase
			ConfigureHardwareForPhase(phase);

			// Update diagnostic data
			DiagnosticData["CurrentPhase"] = phase.ToString();
			DiagnosticData["PhaseStartTime"] = _phaseStartTime;

			// Log phase transition
			AddAlarm("PHASE_CHANGE", $"CIP Phase changed from {previousPhase} to {phase}", AlarmSeverity.Information);
		}

		private void ConfigureHardwareForPhase(CIPPhase phase)
		{
			// Configure pumps and valves based on the current phase
			switch (phase)
			{
				case CIPPhase.Idle:
					// Stop all pumps
					_supplyPump?.Stop();
					_returnPump?.Stop();
					_causticPump?.Stop();
					_acidPump?.Stop();
					_sanitizerPump?.Stop();

					// Close all valves
					CloseAllValves();
					break;

				case CIPPhase.PreRinse:
					// Configure for water flow
					_supplyPump?.Start();
					_returnPump?.Start();
					_supplyPump?.SetSpeed(80);
					_returnPump?.SetSpeed(75);

					// Open appropriate valves
					_waterValve?.SetPosition(100);
					_supplyValve?.SetPosition(100);
					_returnValve?.SetPosition(100);
					_causticValve?.SetPosition(0);
					_acidValve?.SetPosition(0);
					_sanitizerValve?.SetPosition(0);
					_drainValve?.SetPosition(0);
					break;

				case CIPPhase.CausticWash:
					// Configure for caustic circulation
					_supplyPump?.Start();
					_returnPump?.Start();
					_causticPump?.Start();
					_supplyPump?.SetSpeed(85);
					_returnPump?.SetSpeed(80);
					_causticPump?.SetSpeed(40); // Low speed for chemical dosing

					// Turn on heater
					_heater?.TurnOn();
					_heater?.SetPowerLevel(80);

					// Open appropriate valves
					_waterValve?.SetPosition(80);
					_causticValve?.SetPosition(100);
					_supplyValve?.SetPosition(100);
					_returnValve?.SetPosition(100);
					_acidValve?.SetPosition(0);
					_sanitizerValve?.SetPosition(0);
					_drainValve?.SetPosition(0);
					break;

				case CIPPhase.IntermediateRinse:
					// Configure for water flow
					_supplyPump?.Start();
					_returnPump?.Start();
					_supplyPump?.SetSpeed(90);
					_returnPump?.SetSpeed(85);
					_causticPump?.Stop();

					// Turn off heater
					_heater?.SetPowerLevel(0);

					// Open appropriate valves
					_waterValve?.SetPosition(100);
					_supplyValve?.SetPosition(100);
					_returnValve?.SetPosition(100);
					_causticValve?.SetPosition(0);
					_acidValve?.SetPosition(0);
					_sanitizerValve?.SetPosition(0);
					_drainValve?.SetPosition(0);
					break;

				case CIPPhase.AcidWash:
					// Configure for acid circulation
					_supplyPump?.Start();
					_returnPump?.Start();
					_acidPump?.Start();
					_supplyPump?.SetSpeed(80);
					_returnPump?.SetSpeed(75);
					_acidPump?.SetSpeed(35); // Low speed for chemical dosing

					// Turn on heater
					_heater?.TurnOn();
					_heater?.SetPowerLevel(70);

					// Open appropriate valves
					_waterValve?.SetPosition(85);
					_acidValve?.SetPosition(100);
					_supplyValve?.SetPosition(100);
					_returnValve?.SetPosition(100);
					_causticValve?.SetPosition(0);
					_sanitizerValve?.SetPosition(0);
					_drainValve?.SetPosition(0);
					break;

				case CIPPhase.FinalRinse:
					// Configure for water flow
					_supplyPump?.Start();
					_returnPump?.Start();
					_supplyPump?.SetSpeed(95);
					_returnPump?.SetSpeed(90);
					_acidPump?.Stop();

					// Turn off heater
					_heater?.SetPowerLevel(0);

					// Open appropriate valves
					_waterValve?.SetPosition(100);
					_supplyValve?.SetPosition(100);
					_returnValve?.SetPosition(100);
					_causticValve?.SetPosition(0);
					_acidValve?.SetPosition(0);
					_sanitizerValve?.SetPosition(0);
					_drainValve?.SetPosition(0);
					break;

				case CIPPhase.Sanitization:
					// Configure for sanitizer circulation
					_supplyPump?.Start();
					_returnPump?.Start();
					_sanitizerPump?.Start();
					_supplyPump?.SetSpeed(75);
					_returnPump?.SetSpeed(70);
					_sanitizerPump?.SetSpeed(20); // Low speed for chemical dosing

					// Turn on heater at high power
					_heater?.TurnOn();
					_heater?.SetPowerLevel(90);

					// Open appropriate valves
					_waterValve?.SetPosition(90);
					_sanitizerValve?.SetPosition(100);
					_supplyValve?.SetPosition(100);
					_returnValve?.SetPosition(100);
					_causticValve?.SetPosition(0);
					_acidValve?.SetPosition(0);
					_drainValve?.SetPosition(0);
					break;

				case CIPPhase.Draining:
					// Configure for draining
					_supplyPump?.SetSpeed(30); // Low speed for air purge
					_returnPump?.Stop();
					_causticPump?.Stop();
					_acidPump?.Stop();
					_sanitizerPump?.Stop();

					// Turn off heater
					_heater?.TurnOff();

					// Open drain valve, close others
					_drainValve?.SetPosition(100);
					_waterValve?.SetPosition(30); // Low water for final purge
					_supplyValve?.SetPosition(100);
					_returnValve?.SetPosition(100);
					_causticValve?.SetPosition(0);
					_acidValve?.SetPosition(0);
					_sanitizerValve?.SetPosition(0);
					break;
			}
		}

		private void CloseAllValves()
		{
			_supplyValve?.SetPosition(0);
			_returnValve?.SetPosition(0);
			_drainValve?.SetPosition(0);
			_waterValve?.SetPosition(0);
			_causticValve?.SetPosition(0);
			_acidValve?.SetPosition(0);
			_sanitizerValve?.SetPosition(0);
		}

		private void CheckAlarmConditions()
		{
			// Skip if not running
			if (!_cycleInProgress)
				return;

			// Check for resource levels
			if (WaterTankLevel < 10.0)
			{
				AddAlarm("WATER_LOW", "Water tank level critical", AlarmSeverity.Major);
			}
			else if (WaterTankLevel < 20.0)
			{
				AddAlarm("WATER_LOW", "Water tank level low", AlarmSeverity.Warning);
			}

			if (CurrentPhase == CIPPhase.CausticWash && CausticTankLevel < 10.0)
			{
				AddAlarm("CAUSTIC_LOW", "Caustic tank level low", AlarmSeverity.Warning);
			}

			if (CurrentPhase == CIPPhase.AcidWash && AcidTankLevel < 10.0)
			{
				AddAlarm("ACID_LOW", "Acid tank level low", AlarmSeverity.Warning);
			}

			if (CurrentPhase == CIPPhase.Sanitization && SanitizationTankLevel < 10.0)
			{
				AddAlarm("SANITIZER_LOW", "Sanitizer tank level low", AlarmSeverity.Warning);
			}

			// Check flow rates
			if (CurrentPhase != CIPPhase.Idle && CurrentPhase != CIPPhase.Draining)
			{
				double minRequiredFlow = _currentRecipe.RequiredFlowRate * 0.7; // 70% of required
				if (FlowRate < minRequiredFlow)
				{
					AddAlarm("LOW_FLOW", $"CIP flow rate below minimum: {FlowRate:F1} L/min", AlarmSeverity.Warning);
				}
			}

			// Check temperature for phases requiring heating
			if (CurrentPhase == CIPPhase.CausticWash ||
				CurrentPhase == CIPPhase.AcidWash ||
				CurrentPhase == CIPPhase.Sanitization)
			{
				double minRequiredTemp = TargetTemperature * 0.9; // 90% of target
				if (SupplyTemperature < minRequiredTemp && PhaseTime > 5.0) // Allow 5 min for heating
				{
					AddAlarm("LOW_TEMP", $"CIP temperature below minimum: {SupplyTemperature:F1}°C", AlarmSeverity.Warning);
				}

				// Check for excessive temperature
				if (SupplyTemperature > TargetTemperature * 1.1)
				{
					AddAlarm("HIGH_TEMP", $"CIP temperature too high: {SupplyTemperature:F1}°C", AlarmSeverity.Major);
				}
			}

			// Check conductivity during rinse phases
			if ((CurrentPhase == CIPPhase.FinalRinse || CurrentPhase == CIPPhase.IntermediateRinse) &&
				PhaseTime > _phaseDurations[CurrentPhase] * 0.7) // Check near end of rinse
			{
				if (ReturnConductivity > MinimumConductivity * 3.0)
				{
					AddAlarm("HIGH_CONDUCTIVITY",
						$"Return conductivity too high: {ReturnConductivity:F2} mS/cm",
						AlarmSeverity.Warning);
				}
			}

			// Check for maintenance due
			if (_maintenanceCountdown <= 0)
			{
				AddAlarm("MAINTENANCE_DUE", "CIP system maintenance required", AlarmSeverity.Warning);
			}
		}

		private void UpdateDiagnostics()
		{
			// Update real-time parameters
			DiagnosticData["FlowRate"] = FlowRate;
			DiagnosticData["SupplyPressure"] = SupplyPressure;
			DiagnosticData["ReturnPressure"] = ReturnPressure;
			DiagnosticData["SupplyTemperature"] = SupplyTemperature;
			DiagnosticData["ReturnTemperature"] = ReturnTemperature;
			DiagnosticData["ReturnConductivity"] = ReturnConductivity;
			DiagnosticData["ReturnTurbidity"] = ReturnTurbidity;
			DiagnosticData["ChemicalConcentration"] = ChemicalConcentration;

			// Update tank levels
			DiagnosticData["WaterTankLevel"] = WaterTankLevel;
			DiagnosticData["CausticTankLevel"] = CausticTankLevel;
			DiagnosticData["AcidTankLevel"] = AcidTankLevel;
			DiagnosticData["SanitizationTankLevel"] = SanitizationTankLevel;

			// Update process state
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["ProcessTime"] = ProcessTime;
			DiagnosticData["PhaseTime"] = PhaseTime;

			// Update resource usage
			DiagnosticData["TotalWaterUsed"] = TotalWaterUsed;
			DiagnosticData["TotalCausticUsed"] = TotalCausticUsed;
			DiagnosticData["TotalAcidUsed"] = TotalAcidUsed;
			DiagnosticData["TotalSanitizerUsed"] = TotalSanitizerUsed;
			DiagnosticData["TotalEnergyUsed"] = TotalEnergyUsed;

			// Update maintenance
			DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;

			// Update connections
			if (!string.IsNullOrEmpty(ConnectedEquipmentId))
			{
				DiagnosticData["ConnectedEquipment"] = $"{ConnectedEquipmentType}: {ConnectedEquipmentId}";
			}
		}

		#region Public Control Methods

		/// <summary>
		/// Load a CIP recipe
		/// </summary>
		public bool LoadRecipe(string recipeName)
		{
			if (!_recipes.ContainsKey(recipeName))
			{
				AddAlarm("RECIPE_NOT_FOUND", $"Recipe not found: {recipeName}", AlarmSeverity.Warning);
				return false;
			}

			if (_cycleInProgress)
			{
				AddAlarm("RECIPE_LOAD_FAILED", "Cannot load recipe while cycle is in progress", AlarmSeverity.Warning);
				return false;
			}

			// Store the recipe
			_currentRecipe = _recipes[recipeName];
			CurrentRecipe = recipeName;
			_recipeLoaded = true;

			// Store phase durations from recipe
			_phaseDurations.Clear();
			_phaseDurations[CIPPhase.PreRinse] = _currentRecipe.PreRinseDuration;
			_phaseDurations[CIPPhase.CausticWash] = _currentRecipe.CausticWashDuration;
			_phaseDurations[CIPPhase.IntermediateRinse] = _currentRecipe.IntermediateRinseDuration;
			_phaseDurations[CIPPhase.AcidWash] = _currentRecipe.AcidWashDuration;
			_phaseDurations[CIPPhase.FinalRinse] = _currentRecipe.FinalRinseDuration;
			_phaseDurations[CIPPhase.Sanitization] = _currentRecipe.SanitizationDuration;
			_phaseDurations[CIPPhase.Draining] = 5.0; // Fixed draining time

			// Update flow rate
			TargetFlowRate = _currentRecipe.RequiredFlowRate;

			// Update temperature setpoints based on recipe
			TargetTemperature = _currentRecipe.PreRinseTemperature;

			// Update chemical concentrations
			TargetCausticConcentration = _currentRecipe.CausticConcentration;
			TargetAcidConcentration = _currentRecipe.AcidConcentration;
			TargetSanitizerConcentration = _currentRecipe.SanitizerConcentration;

			// Log recipe loading
			AddAlarm("RECIPE_LOADED", $"CIP Recipe loaded: {recipeName}", AlarmSeverity.Information);

			// Update diagnostics
			DiagnosticData["LoadedRecipe"] = recipeName;
			DiagnosticData["RecipeDescription"] = _currentRecipe.Description;
			DiagnosticData["TargetFlowRate"] = TargetFlowRate;

			return true;
		}

		/// <summary>
		/// Start a CIP cycle
		/// </summary>
		public bool StartCycle()
		{
			if (!_recipeLoaded)
			{
				AddAlarm("START_FAILED", "No recipe loaded", AlarmSeverity.Warning);
				return false;
			}

			if (_cycleInProgress)
			{
				AddAlarm("START_FAILED", "CIP cycle already in progress", AlarmSeverity.Warning);
				return false;
			}

			// Check resource levels
			if (WaterTankLevel < 20.0)
			{
				AddAlarm("START_FAILED", "Water tank level too low", AlarmSeverity.Warning);
				return false;
			}

			if (_currentRecipe.IncludeCausticWash && CausticTankLevel < 20.0)
			{
				AddAlarm("START_FAILED", "Caustic tank level too low", AlarmSeverity.Warning);
				return false;
			}

			if (_currentRecipe.IncludeAcidWash && AcidTankLevel < 20.0)
			{
				AddAlarm("START_FAILED", "Acid tank level too low", AlarmSeverity.Warning);
				return false;
			}

			if (_currentRecipe.IncludeSanitization && SanitizationTankLevel < 20.0)
			{
				AddAlarm("START_FAILED", "Sanitizer tank level too low", AlarmSeverity.Warning);
				return false;
			}

			// Reset cycle parameters
			_cycleInProgress = true;
			ProcessTime = 0.0;
			PhaseTime = 0.0;
			_cycleStartTime = DateTime.Now;
			TotalWaterUsed = 0.0;
			TotalCausticUsed = 0.0;
			TotalAcidUsed = 0.0;
			TotalSanitizerUsed = 0.0;
			TotalEnergyUsed = 0.0;
			_cycleLog.Clear();

			// Update state
			CurrentState = CIPState.CycleRunning;
			Status = DeviceStatus.Running;

			// Start with first applicable phase
			CIPPhase initialPhase = _currentRecipe.IncludePreRinse ? CIPPhase.PreRinse :
				(_currentRecipe.IncludeCausticWash ? CIPPhase.CausticWash :
				(_currentRecipe.IncludeAcidWash ? CIPPhase.AcidWash :
				(_currentRecipe.IncludeFinalRinse ? CIPPhase.FinalRinse :
				(_currentRecipe.IncludeSanitization ? CIPPhase.Sanitization : CIPPhase.Draining))));

			// Set initial phase
			SetPhase(initialPhase);

			// Log cycle start
			AddAlarm("CYCLE_STARTED",
				$"CIP cycle started with recipe: {CurrentRecipe}, target equipment: {ConnectedEquipmentId}",
				AlarmSeverity.Information);

			// Start pumps
			if (_supplyPump != null) _supplyPump.Start();
			if (_returnPump != null) _returnPump.Start();

			return true;
		}

		/// <summary>
		/// Manually advance to the next phase
		/// </summary>
		public bool AdvancePhase()
		{
			if (!_cycleInProgress)
			{
				AddAlarm("ADVANCE_FAILED", "No cycle in progress", AlarmSeverity.Warning);
				return false;
			}

			// Log manual phase advancement
			_cycleLog.Add(new CIPLogEntry
			{
				TimeStamp = DateTime.Now,
				Phase = CurrentPhase,
				Message = $"{CurrentPhase} phase manually advanced",
				SupplyTemperature = SupplyTemperature,
				ReturnTemperature = ReturnTemperature,
				FlowRate = FlowRate,
				Conductivity = ReturnConductivity,
				Turbidity = ReturnTurbidity
			});

			// Move to the next phase
			AdvanceToNextPhase();

			AddAlarm("PHASE_ADVANCED", $"Phase manually advanced to {CurrentPhase}", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Completes the CIP cycle and returns to idle state
		/// </summary>
		private void CompleteCycle()
		{
			// Update state
			CurrentState = CIPState.CycleCompleting;

			// Log cycle completion metrics
			_cycleLog.Add(new CIPLogEntry
			{
				TimeStamp = DateTime.Now,
				Phase = CurrentPhase,
				Message = "Cycle completed",
				SupplyTemperature = SupplyTemperature,
				ReturnTemperature = ReturnTemperature,
				FlowRate = FlowRate,
				Conductivity = ReturnConductivity,
				Turbidity = ReturnTurbidity
			});

			// Stop all active hardware
			_supplyPump?.Stop();
			_returnPump?.Stop();
			_causticPump?.Stop();
			_acidPump?.Stop();
			_sanitizerPump?.Stop();
			_heater?.TurnOff();
			CloseAllValves();

			// Reset process state
			_cycleInProgress = false;
			CurrentPhase = CIPPhase.Idle;
			CurrentState = CIPState.Idle;

			// Calculate if this was a validated cycle
			ValidatedCycle = CalculateIfValidatedCycle();

			// Update diagnostic data
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["ValidatedCycle"] = ValidatedCycle;
			DiagnosticData["CycleEndTime"] = DateTime.Now;
			DiagnosticData["TotalWaterUsed"] = TotalWaterUsed;
			DiagnosticData["TotalCausticUsed"] = TotalCausticUsed;
			DiagnosticData["TotalAcidUsed"] = TotalAcidUsed;
			DiagnosticData["TotalSanitizerUsed"] = TotalSanitizerUsed;
			DiagnosticData["TotalEnergyUsed"] = TotalEnergyUsed;

			AddAlarm("CYCLE_COMPLETED", $"CIP cycle completed: {(ValidatedCycle ? "Valid" : "Invalid")}",
				ValidatedCycle ? AlarmSeverity.Information : AlarmSeverity.Warning);
		}

		/// <summary>
		/// Determines if the completed cycle meets validation criteria
		/// </summary>
		private bool CalculateIfValidatedCycle()
		{
			// Check for any critical alarms during the cycle
			bool hasCriticalAlarms = ActiveAlarms.Any(a => a.Severity == AlarmSeverity.Critical || a.Severity == AlarmSeverity.Major);

			// Check if all required phases were completed
			bool completedAllPhases = true;
			if (_currentRecipe != null)
			{
				if (_currentRecipe.IncludePreRinse && !_cycleLog.Any(l => l.Phase == CIPPhase.PreRinse && l.Message.Contains("completed")))
					completedAllPhases = false;

				if (_currentRecipe.IncludeCausticWash && !_cycleLog.Any(l => l.Phase == CIPPhase.CausticWash && l.Message.Contains("completed")))
					completedAllPhases = false;

				if (_currentRecipe.IncludeIntermediateRinse && !_cycleLog.Any(l => l.Phase == CIPPhase.IntermediateRinse && l.Message.Contains("completed")))
					completedAllPhases = false;

				if (_currentRecipe.IncludeAcidWash && !_cycleLog.Any(l => l.Phase == CIPPhase.AcidWash && l.Message.Contains("completed")))
					completedAllPhases = false;

				if (_currentRecipe.IncludeFinalRinse && !_cycleLog.Any(l => l.Phase == CIPPhase.FinalRinse && l.Message.Contains("completed")))
					completedAllPhases = false;

				if (_currentRecipe.IncludeSanitization && !_cycleLog.Any(l => l.Phase == CIPPhase.Sanitization && l.Message.Contains("completed")))
					completedAllPhases = false;
			}

			// Check if final conductivity was below threshold (clean water indicator)
			bool finalConductivityOk = false;
			var lastRinseLog = _cycleLog.LastOrDefault(l => l.Phase == CIPPhase.FinalRinse);
			if (lastRinseLog != null)
			{
				finalConductivityOk = lastRinseLog.Conductivity <= MinimumConductivity * 1.5;
			}

			return !hasCriticalAlarms && completedAllPhases && finalConductivityOk;
		}

		/// <summary>
		/// Aborts the current CIP cycle
		/// </summary>
		public bool AbortCycle(string reason)
		{
			if (!_cycleInProgress)
			{
				AddAlarm("ABORT_FAILED", "No cycle in progress", AlarmSeverity.Warning);
				return false;
			}

			// Stop all active hardware
			_supplyPump?.Stop();
			_returnPump?.Stop();
			_causticPump?.Stop();
			_acidPump?.Stop();
			_sanitizerPump?.Stop();
			_heater?.TurnOff();
			CloseAllValves();

			// Log cycle abortion
			_cycleLog.Add(new CIPLogEntry
			{
				TimeStamp = DateTime.Now,
				Phase = CurrentPhase,
				Message = $"Cycle aborted: {reason}",
				SupplyTemperature = SupplyTemperature,
				ReturnTemperature = ReturnTemperature,
				FlowRate = FlowRate,
				Conductivity = ReturnConductivity,
				Turbidity = ReturnTurbidity
			});

			// Reset process state
			_cycleInProgress = false;
			CurrentPhase = CIPPhase.Idle;
			CurrentState = CIPState.Idle;
			ValidatedCycle = false;

			// Update diagnostic data
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["AbortReason"] = reason;
			DiagnosticData["CycleEndTime"] = DateTime.Now;

			AddAlarm("CYCLE_ABORTED", $"CIP cycle aborted: {reason}", AlarmSeverity.Warning);
			return true;
		}

		/// <summary>
		/// Connect equipment to the CIP system
		/// </summary>
		public bool ConnectEquipment(string equipmentId, string equipmentType)
		{
			// Cannot connect while a cycle is running
			if (_cycleInProgress)
			{
				AddAlarm("CONNECT_FAILED", "Cannot connect equipment during active cycle", AlarmSeverity.Warning);
				return false;
			}

			ConnectedEquipmentId = equipmentId;
			ConnectedEquipmentType = equipmentType;

			DiagnosticData["ConnectedEquipment"] = $"{equipmentType}: {equipmentId}";
			AddAlarm("EQUIPMENT_CONNECTED", $"Connected to {equipmentType}: {equipmentId}", AlarmSeverity.Information);

			return true;
		}

		/// <summary>
		/// Refill the chemical tanks
		/// </summary>
		public bool RefillTanks(double waterLevel = 100.0, double causticLevel = 100.0,
								double acidLevel = 100.0, double sanitizerLevel = 100.0)
		{
			// Cannot refill during an active cycle
			if (_cycleInProgress)
			{
				AddAlarm("REFILL_FAILED", "Cannot refill tanks during active cycle", AlarmSeverity.Warning);
				return false;
			}

			// Update tank levels
			WaterTankLevel = Math.Min(100.0, waterLevel);
			CausticTankLevel = Math.Min(100.0, causticLevel);
			AcidTankLevel = Math.Min(100.0, acidLevel);
			SanitizationTankLevel = Math.Min(100.0, sanitizerLevel);

			// Update diagnostics
			DiagnosticData["WaterTankLevel"] = WaterTankLevel;
			DiagnosticData["CausticTankLevel"] = CausticTankLevel;
			DiagnosticData["AcidTankLevel"] = AcidTankLevel;
			DiagnosticData["SanitizationTankLevel"] = SanitizationTankLevel;

			AddAlarm("TANKS_REFILLED", "CIP system tanks refilled", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Perform maintenance on the CIP system
		/// </summary>
		public bool PerformMaintenance()
		{
			// Cannot perform maintenance during an active cycle
			if (_cycleInProgress)
			{
				AddAlarm("MAINTENANCE_FAILED", "Cannot perform maintenance during active cycle", AlarmSeverity.Warning);
				return false;
			}

			// Reset maintenance counter
			_maintenanceCountdown = 2000; // Reset to 2000 hours

			// Simulate maintenance tasks
			CloseAllValves();
			_heater?.TurnOff();

			// Update status
			Status = DeviceStatus.Maintenance;

			// Update diagnostics
			DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;
			DiagnosticData["LastMaintenanceDate"] = DateTime.Now;

			AddAlarm("MAINTENANCE_PERFORMED", "CIP system maintenance completed", AlarmSeverity.Information);

			// Return to ready state
			Status = DeviceStatus.Ready;
			return true;
		}

		/// <summary>
		/// Add a new CIP recipe to the system
		/// </summary>
		public bool AddRecipe(CIPRecipe recipe)
		{
			if (recipe == null || string.IsNullOrEmpty(recipe.Name))
			{
				AddAlarm("INVALID_RECIPE", "Cannot add null or unnamed recipe", AlarmSeverity.Minor);
				return false;
			}

			// Add or update the recipe
			_recipes[recipe.Name] = recipe;
			DiagnosticData["RecipeCount"] = _recipes.Count;

			AddAlarm("RECIPE_ADDED", $"Recipe added: {recipe.Name}", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Get a report of the last CIP cycle
		/// </summary>
		public CIPCycleReport GetLastCycleReport()
		{
			if (_cycleLog.Count == 0)
			{
				return null;
			}

			return new CIPCycleReport
			{
				CycleStartTime = _cycleStartTime,
				CycleEndTime = DateTime.Now,
				RecipeName = CurrentRecipe,
				ConnectedEquipment = ConnectedEquipmentId,
				EquipmentType = ConnectedEquipmentType,
				TotalDuration = ProcessTime,
				WaterUsed = TotalWaterUsed,
				CausticUsed = TotalCausticUsed,
				AcidUsed = TotalAcidUsed,
				SanitizerUsed = TotalSanitizerUsed,
				EnergyUsed = TotalEnergyUsed,
				ValidatedCycle = ValidatedCycle,
				PhaseLog = _cycleLog.ToList()
			};
		}

		#endregion
	}



}