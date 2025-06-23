using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PharmaceuticalProcess.HardwareComponents.ProcessEquipment
{
	/// <summary>
	/// Simulates an aseptic filling machine used for pharmaceutical product filling operations
	/// </summary>
	public class FillingMachine : DeviceBase
	{
		public override DeviceType Type => DeviceType.ProcessEquipment;

		// Core parameters
		public double FillingSpeed { get; private set; } // Vials per minute
		public double TargetFillVolume { get; private set; } // mL per vial
		public double FillAccuracy { get; private set; } // % deviation from target
		public double BatchVolume { get; private set; } // Total volume for current batch (L)
		public double RemainingVolume { get; private set; } // Remaining product volume (L)
		public int VialsFilled { get; private set; } // Number of vials filled in current batch
		public int VialsRejected { get; private set; } // Number of vials rejected in current batch
		public double AirParticleCount { get; private set; } // Particles per cubic meter
		public double DifferentialPressure { get; private set; } // Pascal

		// Equipment parameters
		public double MaxFillingSpeed { get; private set; } // Maximum vials per minute
		public int NeedleCount { get; private set; } // Number of filling needles
		public double MinFillVolume { get; private set; } // Minimum fill volume (mL)
		public double MaxFillVolume { get; private set; } // Maximum fill volume (mL)
		public VialFormat CurrentVialFormat { get; private set; } // Current vial format

		// Process state
		public FillingMachineState CurrentState { get; private set; }
		public FillingOperationMode OperationMode { get; private set; }
		public double ProcessTime { get; private set; } // Hours
		public string BatchId { get; private set; }
		public bool IsCleanRoomReady { get; private set; } // Clean room environmental status
		public bool DoorsSealed { get; private set; } // RABS/isolator doors sealed
		public bool StopperFeederActive { get; private set; } // Stopper bowl feeder status
		public bool CapFeederActive { get; private set; } // Cap bowl feeder status

		// Quality parameters
		public double RejectRate { get; private set; } // % of vials rejected
		public Dictionary<RejectReason, int> RejectReasonCounts { get; private set; }
		public double LastMeasuredWeight { get; private set; } // Last measured vial weight (g)
		public double LastFillVolume { get; private set; } // Last measured fill volume (mL)

		// Connected systems
		private SterilizingTunnel _vialInfeedTunnel;
		private PumpController _fillingPump;
		private MotorController _conveyorMotor;
		private WeightSensor _checkweigher;
		private ParticleSensor _particleCounter;
		private PressureSensor _pressureSensor;
		private List<VisionSystem> _visionSystems;

		// Internal state tracking
		private Queue<Vial> _vialsInProcess;
		private List<FillingStep> _processSteps;
		private int _currentStepIndex;
		private DateTime _batchStartTime;
		private double _cleaningCycleTimer;
		private double _maintenanceCountdown;
		private Dictionary<string, double> _processParameters;
		private Dictionary<string, double> _qualityParameters;

		// Environmental parameters
		private double _baseParticleCount;
		private double _baseDifferentialPressure;
		private double _environmentalDecayRate;

		public FillingMachine(
			string deviceId,
			string name,
			double maxFillingSpeed,
			int needleCount,
			VialFormat initialVialFormat = VialFormat.Standard10mL,
			PumpController fillingPump = null,
			MotorController conveyorMotor = null)
			: base(deviceId, name)
		{
			// Initialize equipment parameters
			MaxFillingSpeed = maxFillingSpeed;
			NeedleCount = needleCount;
			CurrentVialFormat = initialVialFormat;
			SetVialFormatParameters(initialVialFormat);

			// Initialize connected devices
			_fillingPump = fillingPump;
			_conveyorMotor = conveyorMotor;
			_visionSystems = new List<VisionSystem>();

			// Initialize process state
			CurrentState = FillingMachineState.Idle;
			OperationMode = FillingOperationMode.Standard;
			FillingSpeed = 0;
			TargetFillVolume = 0;
			FillAccuracy = 0.5; // 0.5% accuracy
			BatchVolume = 0;
			RemainingVolume = 0;
			VialsFilled = 0;
			VialsRejected = 0;
			BatchId = "";
			ProcessTime = 0;
			IsCleanRoomReady = false;
			DoorsSealed = false;
			StopperFeederActive = false;
			CapFeederActive = false;

			// Quality metrics
			RejectRate = 0;
			RejectReasonCounts = new Dictionary<RejectReason, int>();
			foreach (RejectReason reason in Enum.GetValues(typeof(RejectReason)))
			{
				RejectReasonCounts[reason] = 0;
			}

			// Initialize internal tracking
			_vialsInProcess = new Queue<Vial>();
			_processSteps = InitializeProcessSteps();
			_currentStepIndex = 0;
			_cleaningCycleTimer = 0;
			_maintenanceCountdown = 720; // 30 days until maintenance
			_processParameters = new Dictionary<string, double>();
			_qualityParameters = new Dictionary<string, double>();

			// Initialize environmental parameters
			_baseParticleCount = 100; // Class A environment baseline
			_baseDifferentialPressure = 45; // 45 Pa positive pressure
			AirParticleCount = _baseParticleCount;
			DifferentialPressure = _baseDifferentialPressure;
			_environmentalDecayRate = 0.01; // Rate at which environmental controls decay

			// Initialize diagnostic data
			InitializeDiagnostics();
		}

		private void InitializeDiagnostics()
		{
			DiagnosticData["MaxFillingSpeed"] = MaxFillingSpeed;
			DiagnosticData["NeedleCount"] = NeedleCount;
			DiagnosticData["CurrentVialFormat"] = CurrentVialFormat.ToString();
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["OperationMode"] = OperationMode.ToString();
			DiagnosticData["FillingSpeed"] = FillingSpeed;
			DiagnosticData["FillAccuracy"] = FillAccuracy;
			DiagnosticData["IsCleanRoomReady"] = IsCleanRoomReady;
			DiagnosticData["RejectRate"] = RejectRate;
		}

		private void SetVialFormatParameters(VialFormat format)
		{
			switch (format)
			{
				case VialFormat.Standard2mL:
					MinFillVolume = 0.5;
					MaxFillVolume = 2.0;
					break;
				case VialFormat.Standard5mL:
					MinFillVolume = 1.0;
					MaxFillVolume = 5.0;
					break;
				case VialFormat.Standard10mL:
					MinFillVolume = 2.0;
					MaxFillVolume = 10.0;
					break;
				case VialFormat.Standard20mL:
					MinFillVolume = 5.0;
					MaxFillVolume = 20.0;
					break;
				case VialFormat.Standard50mL:
					MinFillVolume = 10.0;
					MaxFillVolume = 50.0;
					break;
				default:
					MinFillVolume = 2.0;
					MaxFillVolume = 10.0;
					break;
			}
		}

		private List<FillingStep> InitializeProcessSteps()
		{
			return new List<FillingStep>
			{
				new FillingStep
				{
					Name = "Vial Infeed",
					Duration = TimeSpan.FromMinutes(2),
					CanFail = true,
					FailureProbability = 0.003,
					FailureMessage = "Vial infeed jam"
				},
				new FillingStep
				{
					Name = "Filling",
					Duration = TimeSpan.FromMinutes(15),
					CanFail = true,
					FailureProbability = 0.002,
					FailureMessage = "Filling pump malfunction"
				},
				new FillingStep
				{
					Name = "Stoppering",
					Duration = TimeSpan.FromMinutes(5),
					CanFail = true,
					FailureProbability = 0.005,
					FailureMessage = "Stopper placement failure"
				},
				new FillingStep
				{
					Name = "Capping",
					Duration = TimeSpan.FromMinutes(5),
					CanFail = true,
					FailureProbability = 0.004,
					FailureMessage = "Crimping failure"
				},
				new FillingStep
				{
					Name = "Inspection",
					Duration = TimeSpan.FromMinutes(8),
					CanFail = true,
					FailureProbability = 0.001,
					FailureMessage = "Vision system error"
				},
				new FillingStep
				{
					Name = "Labeling",
					Duration = TimeSpan.FromMinutes(5),
					CanFail = true,
					FailureProbability = 0.002,
					FailureMessage = "Label application error"
				}
			};
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize connected devices
			_fillingPump?.Initialize();
			_conveyorMotor?.Initialize();
			_checkweigher?.Initialize();
			_particleCounter?.Initialize();
			_pressureSensor?.Initialize();

			foreach (var visionSystem in _visionSystems)
			{
				visionSystem.Initialize();
			}

			// Reset process state
			CurrentState = FillingMachineState.Idle;
			FillingSpeed = 0;
			VialsFilled = 0;
			VialsRejected = 0;
			RemainingVolume = 0;
			ProcessTime = 0;
			IsCleanRoomReady = false;
			DoorsSealed = false;
			StopperFeederActive = false;
			CapFeederActive = false;

			// Reset quality metrics
			RejectRate = 0;
			foreach (RejectReason reason in Enum.GetValues(typeof(RejectReason)))
			{
				RejectReasonCounts[reason] = 0;
			}

			// Reset environment parameters
			AirParticleCount = _baseParticleCount;
			DifferentialPressure = _baseDifferentialPressure;

			// Clear vial queue
			_vialsInProcess.Clear();
			_currentStepIndex = 0;

			// Update diagnostics
			UpdateDiagnostics();
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Update process time
			ProcessTime += elapsedTime.TotalHours;

			// Update environmental parameters
			UpdateEnvironmentalParameters(elapsedTime);

			// Process based on current state
			switch (CurrentState)
			{
				case FillingMachineState.Setup:
					UpdateSetupState(elapsedTime);
					break;

				case FillingMachineState.EnvironmentalPreparation:
					UpdateEnvironmentalPrepState(elapsedTime);
					break;

				case FillingMachineState.Running:
					UpdateRunningState(elapsedTime);
					break;

				case FillingMachineState.Paused:
					// No processing when paused
					break;

				case FillingMachineState.Cleaning:
					UpdateCleaningState(elapsedTime);
					break;

				case FillingMachineState.Maintenance:
					// Maintenance state handled externally
					break;

				case FillingMachineState.Fault:
					// Fault state handled by alarm system
					break;
			}

			// Check for alarm conditions
			CheckAlarmConditions();

			// Update maintenance countdown
			if (CurrentState == FillingMachineState.Running || CurrentState == FillingMachineState.Cleaning)
			{
				_maintenanceCountdown -= elapsedTime.TotalHours;
			}

			// Update diagnostic data
			UpdateDiagnostics();
		}

		private void UpdateEnvironmentalParameters(TimeSpan elapsedTime)
		{
			// Get readings from sensors if available
			if (_particleCounter != null && _particleCounter.Status == DeviceStatus.Running)
			{
				AirParticleCount = _particleCounter.ParticleCount;
			}
			else
			{
				// Simulate gradual degradation of environmental conditions when running
				if (CurrentState == FillingMachineState.Running)
				{
					double randomFactor = 1.0 + ((Random.NextDouble() * 0.02) - 0.01); // ±1% random variation
					AirParticleCount = _baseParticleCount * (1.0 + (_environmentalDecayRate * ProcessTime)) * randomFactor;

					// Periodically simulate air handler adjustments
					if (Random.NextDouble() < 0.05 * elapsedTime.TotalMinutes)
					{
						AirParticleCount = Math.Max(_baseParticleCount, AirParticleCount * 0.9);
					}
				}
				else if (CurrentState == FillingMachineState.EnvironmentalPreparation)
				{
					// During preparation, particle count decreases toward baseline
					AirParticleCount -= (AirParticleCount - _baseParticleCount) * 0.1 * elapsedTime.TotalMinutes;
					AirParticleCount = Math.Max(_baseParticleCount, AirParticleCount);
				}
				else if (CurrentState == FillingMachineState.Cleaning)
				{
					// During cleaning, particle count is higher
					AirParticleCount = _baseParticleCount * (3.0 - (_cleaningCycleTimer / 30.0) * 2.0);
				}
			}

			// Update differential pressure
			if (_pressureSensor != null && _pressureSensor.Status == DeviceStatus.Running)
			{
				DifferentialPressure = _pressureSensor.Pressure;
			}
			else
			{
				// Simulate pressure fluctuations
				double randomFactor = 1.0 + ((Random.NextDouble() * 0.05) - 0.025); // ±2.5% random variation
				DifferentialPressure = _baseDifferentialPressure * randomFactor;

				// Simulate door opening effects
				if (!DoorsSealed && CurrentState == FillingMachineState.Running)
				{
					DifferentialPressure *= 0.8; // 20% pressure loss when doors not sealed
				}
			}
		}

		private void UpdateSetupState(TimeSpan elapsedTime)
		{
			// Check if setup is complete
			if (ProcessTime >= 0.5) // Setup takes 30 minutes
			{
				// Transition to environmental preparation
				CurrentState = FillingMachineState.EnvironmentalPreparation;
				AddAlarm("SETUP_COMPLETE", "Machine setup completed", AlarmSeverity.Information);
			}
		}

		private void UpdateEnvironmentalPrepState(TimeSpan elapsedTime)
		{
			// Check if clean room environment is ready
			if (AirParticleCount <= _baseParticleCount * 1.1 &&
				DifferentialPressure >= _baseDifferentialPressure * 0.95)
			{
				IsCleanRoomReady = true;

				// Check if doors are sealed
				if (DoorsSealed)
				{
					// Transition to running state if all conditions met
					CurrentState = FillingMachineState.Running;
					AddAlarm("ENV_READY", "Environmental conditions ready for production", AlarmSeverity.Information);
				}
			}
		}

		private void UpdateRunningState(TimeSpan elapsedTime)
		{
			// Check if we still have product to fill
			if (RemainingVolume <= 0)
			{
				AddAlarm("BATCH_COMPLETE", "Filling batch completed", AlarmSeverity.Information);
				CurrentState = FillingMachineState.Idle;
				return;
			}

			// Process filling operations
			ProcessFillingOperations(elapsedTime);
		}

		private void UpdateCleaningState(TimeSpan elapsedTime)
		{
			// Update cleaning cycle timer
			_cleaningCycleTimer += elapsedTime.TotalMinutes;

			// Check if cleaning cycle is complete (30 minutes)
			if (_cleaningCycleTimer >= 30.0)
			{
				_cleaningCycleTimer = 0;
				CurrentState = FillingMachineState.Idle;
				AddAlarm("CLEANING_COMPLETE", "CIP/SIP cycle completed", AlarmSeverity.Information);

				// Reset environmental conditions
				AirParticleCount = _baseParticleCount * 1.5; // Still slightly elevated after cleaning
			}
		}

		private void ProcessFillingOperations(TimeSpan elapsedTime)
		{
			// Calculate how many vials to process in this time step
			double vialsPerSecond = FillingSpeed / 60.0;
			double vialsToProcess = vialsPerSecond * elapsedTime.TotalSeconds;
			int wholeVialsToProcess = (int)vialsToProcess;

			// Account for fractional vials (probability-based)
			if (Random.NextDouble() < (vialsToProcess - wholeVialsToProcess))
			{
				wholeVialsToProcess++;
			}

			// Process each vial
			for (int i = 0; i < wholeVialsToProcess; i++)
			{
				// Check if we have enough product
				if (RemainingVolume < (TargetFillVolume / 1000.0)) // Convert mL to L
				{
					AddAlarm("PRODUCT_DEPLETED", "Product volume depleted", AlarmSeverity.Information);
					break;
				}

				// Create and process a new vial
				ProcessVial();
			}

			// Update filling speed based on motor controller
			UpdateFillingSpeed();
		}

		private void ProcessVial()
		{
			// Calculate actual fill volume (with accuracy variation)
			double accuracyVariation = (Random.NextDouble() * 2.0 - 1.0) * FillAccuracy / 100.0; // ±FillAccuracy%
			double actualFillVolume = TargetFillVolume * (1.0 + accuracyVariation);
			LastFillVolume = actualFillVolume;

			// Update remaining volume
			RemainingVolume -= actualFillVolume / 1000.0; // Convert mL to L
			RemainingVolume = Math.Max(0, RemainingVolume);

			// Check if vial passes quality criteria
			bool rejected = false;
			RejectReason rejectReason = RejectReason.None;

			// Check for environmental problems
			if (AirParticleCount > _baseParticleCount * 3.0)
			{
				if (Random.NextDouble() < 0.05) // 5% chance of contamination
				{
					rejected = true;
					rejectReason = RejectReason.Contamination;
				}
			}

			// Check for fill volume problems
			if (Math.Abs(accuracyVariation) > (FillAccuracy * 3.0 / 100.0)) // 3x the allowed deviation
			{
				rejected = true;
				rejectReason = accuracyVariation > 0 ?
					RejectReason.Overfill :
					RejectReason.Underfill;
			}

			// Check for stopper or cap problems
			if (!StopperFeederActive || (Random.NextDouble() < 0.002)) // 0.2% chance of stopper issue
			{
				rejected = true;
				rejectReason = RejectReason.StopperDefect;
			}

			if (!CapFeederActive || (Random.NextDouble() < 0.003)) // 0.3% chance of cap issue
			{
				rejected = true;
				rejectReason = RejectReason.CapDefect;
			}

			// Check for cosmetic defects
			if (Random.NextDouble() < 0.005) // 0.5% chance of cosmetic issue
			{
				rejected = true;
				rejectReason = RejectReason.CosmeticDefect;
			}

			// Update counters
			if (rejected)
			{
				VialsRejected++;
				RejectReasonCounts[rejectReason]++;
			}
			else
			{
				VialsFilled++;
			}

			// Calculate reject rate
			int totalVials = VialsFilled + VialsRejected;
			if (totalVials > 0)
			{
				RejectRate = (double)VialsRejected / totalVials * 100.0;
			}

			// Calculate weight (for checkweigher)
			// Assuming product density of 1g/mL plus vial tare weight
			double vialTareWeight = GetVialTareWeight(CurrentVialFormat);
			LastMeasuredWeight = vialTareWeight + actualFillVolume;

			// Update checkweigher if available
			if (_checkweigher != null && _checkweigher.Status == DeviceStatus.Running)
			{
				_checkweigher.SetWeight(LastMeasuredWeight);
			}
		}

		private double GetVialTareWeight(VialFormat format)
		{
			// Return typical vial weights in grams
			switch (format)
			{
				case VialFormat.Standard2mL: return 3.5;
				case VialFormat.Standard5mL: return 5.2;
				case VialFormat.Standard10mL: return 7.8;
				case VialFormat.Standard20mL: return 12.5;
				case VialFormat.Standard50mL: return 22.0;
				default: return 7.8; // Default to 10mL vial
			}
		}

		private void UpdateFillingSpeed()
		{
			// Get speed from motor controller if available
			if (_conveyorMotor != null && _conveyorMotor.Status == DeviceStatus.Running)
			{
				// Scale motor speed percentage to vials per minute
				FillingSpeed = (_conveyorMotor.Speed / 100.0) * MaxFillingSpeed;
			}
		}

		private void CheckAlarmConditions()
		{
			// Check environmental conditions
			if (CurrentState == FillingMachineState.Running)
			{
				if (AirParticleCount > _baseParticleCount * 5.0)
				{
					AddAlarm("CRITICAL_PARTICLE_LEVEL",
						$"Critical particle count: {AirParticleCount:F0} particles/m³",
						AlarmSeverity.Critical);
				}
				else if (AirParticleCount > _baseParticleCount * 3.0)
				{
					AddAlarm("HIGH_PARTICLE_LEVEL",
						$"High particle count: {AirParticleCount:F0} particles/m³",
						AlarmSeverity.Major);
				}

				if (DifferentialPressure < _baseDifferentialPressure * 0.7)
				{
					AddAlarm("LOW_PRESSURE_DIFF",
						$"Low differential pressure: {DifferentialPressure:F1} Pa",
						AlarmSeverity.Major);
				}

				// Check reject rate
				if (RejectRate > 5.0 && VialsFilled + VialsRejected > 100)
				{
					AddAlarm("HIGH_REJECT_RATE",
						$"High rejection rate: {RejectRate:F1}%",
						AlarmSeverity.Warning);
				}

				// Check for consistent issues with specific reject reason
				foreach (var kvp in RejectReasonCounts)
				{
					if (kvp.Key != RejectReason.None && kvp.Value > 10)
					{
						AddAlarm("REJECT_PATTERN",
							$"Pattern of rejects: {kvp.Value} vials with {kvp.Key}",
							AlarmSeverity.Warning);
					}
				}
			}

			// Check for maintenance due
			if (_maintenanceCountdown <= 0)
			{
				AddAlarm("MAINTENANCE_DUE", "Scheduled maintenance required", AlarmSeverity.Warning);
			}
		}

		private void UpdateDiagnostics()
		{
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["FillingSpeed"] = FillingSpeed;
			DiagnosticData["TargetFillVolume"] = TargetFillVolume;
			DiagnosticData["RemainingVolume"] = RemainingVolume;
			DiagnosticData["VialsFilled"] = VialsFilled;
			DiagnosticData["VialsRejected"] = VialsRejected;
			DiagnosticData["AirParticleCount"] = AirParticleCount;
			DiagnosticData["DifferentialPressure"] = DifferentialPressure;
			DiagnosticData["RejectRate"] = RejectRate;
			DiagnosticData["IsCleanRoomReady"] = IsCleanRoomReady;
			DiagnosticData["DoorsSealed"] = DoorsSealed;
			DiagnosticData["StopperFeederActive"] = StopperFeederActive;
			DiagnosticData["CapFeederActive"] = CapFeederActive;
			DiagnosticData["ProcessTime"] = ProcessTime;
			DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;
			DiagnosticData["LastMeasuredWeight"] = LastMeasuredWeight;
			DiagnosticData["LastFillVolume"] = LastFillVolume;

			// Add reject reason counts
			foreach (var kvp in RejectReasonCounts)
			{
				if (kvp.Value > 0)
				{
					DiagnosticData[$"Rejects_{kvp.Key}"] = kvp.Value;
				}
			}
		}

		#region Public Control Methods

		/// <summary>
		/// Starts a new filling batch
		/// </summary>
		public bool StartBatch(string batchId, double totalVolume, double targetFillVolume, VialFormat vialFormat)
		{
			// Check if machine is ready
			if (CurrentState != FillingMachineState.Idle)
			{
				AddAlarm("START_FAILED", "Cannot start batch: Machine not in idle state", AlarmSeverity.Warning);
				return false;
			}

			// Validate parameters
			if (totalVolume <= 0)
			{
				AddAlarm("INVALID_VOLUME", "Total volume must be greater than zero", AlarmSeverity.Warning);
				return false;
			}

			if (targetFillVolume < MinFillVolume || targetFillVolume > MaxFillVolume)
			{
				AddAlarm("INVALID_FILL_VOLUME",
					$"Fill volume {targetFillVolume} mL outside range for vial format",
					AlarmSeverity.Warning);
				return false;
			}

			// Set batch parameters
			BatchId = batchId;
			BatchVolume = totalVolume;
			RemainingVolume = totalVolume;
			TargetFillVolume = targetFillVolume;
			CurrentVialFormat = vialFormat;
			SetVialFormatParameters(vialFormat);

			// Reset counters
			VialsFilled = 0;
			VialsRejected = 0;
			ProcessTime = 0;
			_batchStartTime = DateTime.Now;

			foreach (RejectReason reason in Enum.GetValues(typeof(RejectReason)))
			{
				RejectReasonCounts[reason] = 0;
			}

			// Start setup process
			CurrentState = FillingMachineState.Setup;
			Status = DeviceStatus.Running;

			// Update diagnostic data
			DiagnosticData["BatchId"] = BatchId;
			DiagnosticData["BatchVolume"] = BatchVolume;
			DiagnosticData["TargetFillVolume"] = TargetFillVolume;
			DiagnosticData["CurrentVialFormat"] = CurrentVialFormat.ToString();
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["BatchStartTime"] = _batchStartTime;

			AddAlarm("BATCH_STARTED", $"Started new filling batch: {batchId}", AlarmSeverity.Information);
			return true;
		}

		/// <summary>
		/// Pauses the current filling operation
		/// </summary>
		public void PauseProduction()
		{
			if (CurrentState != FillingMachineState.Running)
			{
				return;
			}

			// Pause operation
			CurrentState = FillingMachineState.Paused;

			// Stop conveyor
			if (_conveyorMotor != null)
			{
				_conveyorMotor.SetSpeed(0);
			}

			// Stop filling pump
			if (_fillingPump != null)
			{
				_fillingPump.Stop();
			}

			FillingSpeed = 0;

			// Update diagnostic data
			DiagnosticData["CurrentState"] = CurrentState.ToString();

			AddAlarm("PRODUCTION_PAUSED", "Filling operation paused", AlarmSeverity.Information);
		}

		/// <summary>
		/// Resumes filling after a pause
		/// </summary>
		public void ResumeProduction()
		{
			if (CurrentState != FillingMachineState.Paused)
			{
				return;
			}

			// Check environmental conditions
			if (!IsCleanRoomReady || !DoorsSealed)
			{
				AddAlarm("RESUME_FAILED", "Cannot resume: Environmental conditions not met", AlarmSeverity.Warning);
				return;
			}

			// Resume operation
			CurrentState = FillingMachineState.Running;

			// Start conveyor
			if (_conveyorMotor != null)
			{
				_conveyorMotor.Start();
				_conveyorMotor.SetSpeed(50); // Start at 50% speed
			}

			// Start filling pump
			if (_fillingPump != null)
			{
				_fillingPump.Start();
				_fillingPump.SetSpeed(50); // Start at 50% speed
			}

			// Update diagnostic data
			DiagnosticData["CurrentState"] = CurrentState.ToString();

			AddAlarm("PRODUCTION_RESUMED", "Filling operation resumed", AlarmSeverity.Information);
		}

		/// <summary>
		/// Stops the current batch and transitions to cleaning
		/// </summary>
		public void StopAndClean()
		{
			if (CurrentState == FillingMachineState.Idle || CurrentState == FillingMachineState.Cleaning)
			{
				return;
			}

			// Stop all operations
			if (_conveyorMotor != null) _conveyorMotor.Stop();
			if (_fillingPump != null) _fillingPump.Stop();

			// Reset counters
			FillingSpeed = 0;

			// Start cleaning cycle
			CurrentState = FillingMachineState.Cleaning;
			_cleaningCycleTimer = 0;

			// Update diagnostic data
			DiagnosticData["CurrentState"] = CurrentState.ToString();

			AddAlarm("CLEANING_STARTED", "CIP/SIP cycle started", AlarmSeverity.Information);
		}

		/// <summary>
		/// Connect to a sterilizing tunnel for vial infeed
		/// </summary>
		public void ConnectToSterilizingTunnel(SterilizingTunnel tunnel)
		{
			_vialInfeedTunnel = tunnel;
		}

		/// <summary>
		/// Set the filling machine speed
		/// </summary>
		public void SetFillingSpeed(double speed)
		{
			// Validate speed
			if (speed < 0 || speed > MaxFillingSpeed)
			{
				AddAlarm("INVALID_SPEED", $"Invalid filling speed: {speed} vials/min", AlarmSeverity.Warning);
				return;
			}

			// Update speed setpoint
			double speedPercentage = (speed / MaxFillingSpeed) * 100.0;

			// Update motor controller if available
			if (_conveyorMotor != null && _conveyorMotor.Status == DeviceStatus.Running)
			{
				_conveyorMotor.SetSpeed(speedPercentage);
			}
			else
			{
				FillingSpeed = speed;
			}

			DiagnosticData["FillingSpeed"] = FillingSpeed;
		}

		/// <summary>
		/// Sets the environmental controls (doors, feeders)
		/// </summary>
		public void SetEnvironmentalControls(bool doorsSealedState, bool stopperFeederState, bool capFeederState)
		{
			DoorsSealed = doorsSealedState;
			StopperFeederActive = stopperFeederState;
			CapFeederActive = capFeederState;

			DiagnosticData["DoorsSealed"] = DoorsSealed;
			DiagnosticData["StopperFeederActive"] = StopperFeederActive;
			DiagnosticData["CapFeederActive"] = CapFeederActive;

			if (!DoorsSealed && CurrentState == FillingMachineState.Running)
			{
				AddAlarm("DOORS_OPEN", "RABS/isolator doors unsealed during operation", AlarmSeverity.Major);
			}

			if ((!StopperFeederActive || !CapFeederActive) && CurrentState == FillingMachineState.Running)
			{
				AddAlarm("FEEDER_INACTIVE", "Component feeder inactive during operation", AlarmSeverity.Warning);
			}
		}

		/// <summary>
		/// Perform maintenance on the filling machine
		/// </summary>
		public void PerformMaintenance()
		{
			if (CurrentState != FillingMachineState.Idle && CurrentState != FillingMachineState.Cleaning)
			{
				AddAlarm("MAINTENANCE_FAILED", "Cannot perform maintenance in current state", AlarmSeverity.Warning);
				return;
			}

			// Set maintenance state
			CurrentState = FillingMachineState.Maintenance;
			Status = DeviceStatus.Maintenance;

			// Reset maintenance countdown
			_maintenanceCountdown = 720; // 720 hours (30 days)

			// Update diagnostic data
			DiagnosticData["CurrentState"] = CurrentState.ToString();
			DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;

			AddAlarm("MAINTENANCE_STARTED", "Maintenance mode activated", AlarmSeverity.Information);
		}

		/// <summary>
		/// Complete maintenance and return to operational status
		/// </summary>
		public void CompleteMaintenance()
		{
			if (CurrentState != FillingMachineState.Maintenance)
			{
				return;
			}

			// Return to idle state
			CurrentState = FillingMachineState.Idle;
			Status = DeviceStatus.Ready;

			// Update diagnostic data
			DiagnosticData["CurrentState"] = CurrentState.ToString();

			AddAlarm("MAINTENANCE_COMPLETE", "Maintenance completed", AlarmSeverity.Information);
		}

		/// <summary>
		/// Connect sensors to the filling machine
		/// </summary>
		public void ConnectSensors(ParticleSensor particleCounter = null, PressureSensor pressureSensor = null, WeightSensor checkweigher = null)
		{
			_particleCounter = particleCounter;
			_pressureSensor = pressureSensor;
			_checkweigher = checkweigher;
		}

		/// <summary>
		/// Connect vision systems for inspection
		/// </summary>
		public void ConnectVisionSystems(List<VisionSystem> visionSystems)
		{
			_visionSystems = visionSystems ?? new List<VisionSystem>();
		}

		#endregion

		protected override void SimulateFault()
		{
			int faultType = Random.Next(6);

			switch (faultType)
			{
				case 0: // Stopper feed jam
					AddAlarm("STOPPER_JAM", "Stopper feeder jam detected", AlarmSeverity.Major);
					StopperFeederActive = false;
					break;

				case 1: // Environmental control failure
					AddAlarm("ENV_CONTROL_FAILURE", "Environmental control system malfunction", AlarmSeverity.Major);
					AirParticleCount *= 5.0;
					DifferentialPressure *= 0.6;
					break;

				case 2: // Filling pump issue
					AddAlarm("FILLING_ACCURACY_ISSUE", "Filling volume accuracy degraded", AlarmSeverity.Warning);
					FillAccuracy *= 3.0;
					if (_fillingPump != null)
					{
						_fillingPump.SimulateFault();
					}
					break;

				case 3: // Vial breakage
					AddAlarm("VIAL_BREAKAGE", "Vial breakage detected in filling area", AlarmSeverity.Critical);
					PauseProduction();
					AirParticleCount *= 10.0;
					break;

				case 4: // Vision system failure
					AddAlarm("VISION_SYSTEM_FAULT", "Inspection camera system failure", AlarmSeverity.Major);
					if (_visionSystems.Count > 0)
					{
						_visionSystems[Random.Next(_visionSystems.Count)].SimulateFault();
					}
					break;

				case 5: // Product leak
					AddAlarm("PRODUCT_LEAK", "Product leak detected in filling area", AlarmSeverity.Major);
					RemainingVolume -= RemainingVolume * 0.05; // Lose 5% of remaining product
					break;
			}
		}
	}

	/// <summary>
	/// Represents common vial formats used in filling operations
	/// </summary>
	public enum VialFormat
	{
		Standard2mL,
		Standard5mL,
		Standard10mL,
		Standard20mL,
		Standard50mL
	}

	/// <summary>
	/// Represents the operational states of the filling machine
	/// </summary>
	public enum FillingMachineState
	{
		Idle,
		Setup,
		EnvironmentalPreparation,
		Running,
		Paused,
		Cleaning,
		Maintenance,
		Fault
	}

	/// <summary>
	/// Represents different operational modes for the filling machine
	/// </summary>
	public enum FillingOperationMode
	{
		Standard,
		HighSpeed,
		HighPrecision,
		SmallBatch
	}

	/// <summary>
	/// Represents reasons for vial rejection
	/// </summary>
	public enum RejectReason
	{
		None,
		Underfill,
		Overfill,
		StopperDefect,
		CapDefect,
		Contamination,
		CosmeticDefect,
		ForeignParticle,
		LabelDefect
	}

	/// <summary>
	/// Represents a step in the filling process
	/// </summary>
	public class FillingStep
	{
		public string Name { get; set; }
		public TimeSpan Duration { get; set; }
		public bool CanFail { get; set; }
		public double FailureProbability { get; set; }
		public string FailureMessage { get; set; }
	}

	/// <summary>
	/// Simulates a vision system for vial inspection
	/// </summary>
	public class VisionSystem : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		public VisionInspectionType InspectionType { get; private set; }
		public double DetectionAccuracy { get; private set; } // 0-100%
		public int FalsePositiveRate { get; private set; } // per 10,000
		public int FalseNegativeRate { get; private set; } // per 10,000

		public VisionSystem(string deviceId, string name, VisionInspectionType inspectionType)
			: base(deviceId, name)
		{
			InspectionType = inspectionType;

			// Set default values based on inspection type
			switch (inspectionType)
			{
				case VisionInspectionType.ParticleInspection:
					DetectionAccuracy = 98.5;
					FalsePositiveRate = 5; // 5 per 10,000
					FalseNegativeRate = 3; // 3 per 10,000
					break;
				case VisionInspectionType.CosmeticInspection:
					DetectionAccuracy = 97.0;
					FalsePositiveRate = 8;
					FalseNegativeRate = 5;
					break;
				case VisionInspectionType.FillLevelInspection:
					DetectionAccuracy = 99.2;
					FalsePositiveRate = 3;
					FalseNegativeRate = 2;
					break;
				case VisionInspectionType.StopperInspection:
					DetectionAccuracy = 99.0;
					FalsePositiveRate = 4;
					FalseNegativeRate = 3;
					break;
				case VisionInspectionType.CapInspection:
					DetectionAccuracy = 98.8;
					FalsePositiveRate = 5;
					FalseNegativeRate = 3;
					break;
			}

			DiagnosticData["InspectionType"] = InspectionType.ToString();
			DiagnosticData["DetectionAccuracy"] = DetectionAccuracy;
			DiagnosticData["FalsePositiveRate"] = FalsePositiveRate;
			DiagnosticData["FalseNegativeRate"] = FalseNegativeRate;
		}

		public bool InspectVial(out RejectReason rejectReason)
		{
			rejectReason = RejectReason.None;

			// Determine if this is a false positive (reject good vial)
			if (Random.NextDouble() * 10000 < FalsePositiveRate)
			{
				// Assign a rejection reason based on inspection type
				switch (InspectionType)
				{
					case VisionInspectionType.ParticleInspection:
						rejectReason = RejectReason.ForeignParticle;
						break;
					case VisionInspectionType.CosmeticInspection:
						rejectReason = RejectReason.CosmeticDefect;
						break;
					case VisionInspectionType.FillLevelInspection:
						rejectReason = Random.NextDouble() > 0.5 ? RejectReason.Underfill : RejectReason.Overfill;
						break;
					case VisionInspectionType.StopperInspection:
						rejectReason = RejectReason.StopperDefect;
						break;
					case VisionInspectionType.CapInspection:
						rejectReason = RejectReason.CapDefect;
						break;
				}
				return true; // Reject vial
			}

			// No rejection
			return false;
		}

		protected override void SimulateFault()
		{
			// Camera system malfunctions affect detection rates
			DetectionAccuracy *= 0.8;
			FalsePositiveRate *= 3;
			FalseNegativeRate *= 2;

			AddAlarm("CAMERA_FAILURE",
				$"Vision system degraded: {DetectionAccuracy:F1}% accuracy",
				AlarmSeverity.Warning);

			DiagnosticData["DetectionAccuracy"] = DetectionAccuracy;
			DiagnosticData["FalsePositiveRate"] = FalsePositiveRate;
			DiagnosticData["FalseNegativeRate"] = FalseNegativeRate;
		}
	}

	/// <summary>
	/// Types of vision inspection systems
	/// </summary>
	public enum VisionInspectionType
	{
		ParticleInspection,
		CosmeticInspection,
		FillLevelInspection,
		StopperInspection,
		CapInspection
	}

	/// <summary>
	/// Simulates a particle counter for cleanroom monitoring
	/// </summary>
	public class ParticleSensor : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		public double ParticleCount { get; private set; } // Particles per cubic meter
		public double ParticleSize { get; private set; } // Microns

		public ParticleSensor(string deviceId, string name, double particleSize = 0.5)
			: base(deviceId, name)
		{
			ParticleCount = 100; // Default to Class A environment
			ParticleSize = particleSize;

			DiagnosticData["ParticleCount"] = ParticleCount;
			DiagnosticData["ParticleSize"] = ParticleSize;
		}

		public void SetParticleCount(double count)
		{
			ParticleCount = Math.Max(0, count);
			DiagnosticData["ParticleCount"] = ParticleCount;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Add random fluctuation
			ParticleCount *= 1.0 + ((Random.NextDouble() * 0.05) - 0.025); // ±2.5% variation

			DiagnosticData["ParticleCount"] = ParticleCount;
		}

		protected override void SimulateFault()
		{
			// Sensor malfunction - could be various issues
			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Sensor drift
					ParticleCount *= 0.1; // Reads 10% of actual count
					AddAlarm("SENSOR_DRIFT", "Particle counter reading abnormally low", AlarmSeverity.Warning);
					break;
				case 1: // False high readings
					ParticleCount *= 10.0; // Reads 10x actual count
					AddAlarm("FALSE_HIGH", "Particle counter reading abnormally high", AlarmSeverity.Minor);
					break;
				case 2: // Erratic readings
					ParticleCount = Random.NextDouble() * 1000 + 50;
					AddAlarm("ERRATIC_READINGS", "Particle counter showing erratic values", AlarmSeverity.Major);
					break;
			}

			DiagnosticData["ParticleCount"] = ParticleCount;
		}
	}

	/// <summary>
	/// Simulates a weight sensor for filled vial verification
	/// </summary>
	public class WeightSensor : DeviceBase
	{
		public override DeviceType Type => DeviceType.Sensor;

		public double Weight { get; private set; } // Grams
		public double Accuracy { get; private set; } // ±grams

		public WeightSensor(string deviceId, string name, double accuracy = 0.01)
			: base(deviceId, name)
		{
			Weight = 0.0;
			Accuracy = accuracy;

			DiagnosticData["Weight"] = Weight;
			DiagnosticData["Accuracy"] = Accuracy;
		}

		public void SetWeight(double weight)
		{
			// Add measurement variation based on accuracy
			double variation = (Random.NextDouble() * 2.0 - 1.0) * Accuracy;
			Weight = weight + variation;
			Weight = Math.Max(0, Weight);

			DiagnosticData["Weight"] = Weight;
		}

		protected override void SimulateFault()
		{
			// Scale calibration issues
			Accuracy *= 5; // Much worse accuracy

			// Apply an offset
			Weight += (Random.NextDouble() * 0.5) - 0.25; // ±0.25g shift

			AddAlarm("SCALE_CALIBRATION", "Weight sensor calibration error", AlarmSeverity.Warning);

			DiagnosticData["Weight"] = Weight;
			DiagnosticData["Accuracy"] = Accuracy;
		}
	}
}