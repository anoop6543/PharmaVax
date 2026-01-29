using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using PharmaVax.HardwareComponents.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PharmaceuticalProcess.HardwareComponents.ProcessEquipment
{
	/// <summary>
	/// Simulates a chromatography skid used for protein purification in biopharmaceutical manufacturing
	/// </summary>
	public class ChromatographySkid : DeviceBase
	{
		public override DeviceType Type => DeviceType.ProcessEquipment;

		// Basic parameters
		public double ColumnVolume { get; private set; } // Column volume (CV) in liters
		public double FlowRate { get; private set; } // Current flow rate in mL/min
		public double Pressure { get; private set; } // Column pressure in bar
		public double UV280Reading { get; private set; } // UV absorbance at 280nm
		public double UV260Reading { get; private set; } // UV absorbance at 260nm
		public double Conductivity { get; private set; } // Conductivity in mS/cm
		public double pH { get; private set; } // Current pH
		public double Temperature { get; private set; } // Current temperature in Celsius

		// Column parameters
		public ChromatographyColumnType ColumnType { get; private set; }
		public double ColumnHeight { get; private set; } // Height in cm
		public double ColumnDiameter { get; private set; } // Diameter in cm
		public double BedHeight { get; private set; } // Resin bed height in cm
		public double ResinCapacity { get; private set; } // Dynamic binding capacity in mg/mL
		public double ResinLifetime { get; private set; } // Remaining column lifetime (0-100%)

		// Process state
		public ChromatographyPhase CurrentPhase { get; private set; }
		public double CurrentCV { get; private set; } // Current column volumes processed
		public double ProcessTime { get; private set; } // Process time in minutes
		public bool IsCollecting { get; private set; } // Whether currently collecting fractions
		public int CurrentFractionNumber { get; private set; } // Current fraction being collected
		public double ProductRecovery { get; private set; } // Product recovery in %
		public double ProductPurity { get; private set; } // Product purity in %

		// Buffer system
		public Dictionary<string, BufferSolution> BufferSolutions { get; private set; }
		public string CurrentBuffer { get; private set; } // Current buffer being pumped
		public double BufferBLevel { get; private set; } // % of buffer B in gradient

		// Fractions
		public List<Fraction> CollectedFractions { get; private set; }

		// Connected devices
		private PumpController _samplePump;
		private PumpController _bufferAPump;
		private PumpController _bufferBPump;
		private ValveController _injectionValve;
		private ValveController _columnBypassValve;
		private ValveController _fractionValve;
		private UVMonitor _uvMonitor;
		private ConductivitySensor _conductivitySensor;
		private PressureSensor _pressureSensor;
		private PHAnalyzer _phAnalyzer;

		// Operation parameters
		public double FlowRateSetpoint { get; private set; } = 100.0; // mL/min
		public double MaxPressureLimit { get; private set; } = 5.0; // bar
		public double LoadAmount { get; private set; } = 0.0; // mg of product loaded
		public double SampleVolume { get; private set; } = 0.0; // mL of sample
		public double SampleConcentration { get; private set; } = 0.0; // mg/mL

		// Internal model parameters
		private double _theoreticalPlates;
		private double _asympmetryFactor;
		private double _backPressureCoefficient;
		private double _columnEfficiency;
		private double _peakWidth;

		// Product characteristics
		private double _productRetentionTime;
		private double _productPeakWidth;
		private List<Contaminant> _contaminants;

		// Internal process tracking
		private Dictionary<ChromatographyPhase, double> _phaseDurations;
		private double _phaseStartTime;
		private double _lastFractionTime;
		private bool _methodLoaded;
		private ChromatographyMethod _currentMethod;

		public ChromatographySkid(
			string deviceId,
			string name,
			ChromatographyColumnType columnType,
			double columnVolume,
			double columnDiameter,
			double columnHeight,
			PumpController samplePump = null,
			PumpController bufferAPump = null,
			PumpController bufferBPump = null,
			ValveController injectionValve = null,
			ValveController columnBypassValve = null,
			ValveController fractionValve = null)
			: base(deviceId, name)
		{
			// Initialize column parameters
			ColumnType = columnType;
			ColumnVolume = columnVolume;
			ColumnDiameter = columnDiameter;
			ColumnHeight = columnHeight;
			BedHeight = columnHeight * 0.8; // 80% of column height

			// Initialize resin parameters based on column type
			InitializeResinParameters();

			// Initialize connected devices
			_samplePump = samplePump;
			_bufferAPump = bufferAPump;
			_bufferBPump = bufferBPump;
			_injectionValve = injectionValve;
			_columnBypassValve = columnBypassValve;
			_fractionValve = fractionValve;

			// Initialize process parameters
			FlowRate = 0.0;
			Pressure = 0.0;
			UV280Reading = 0.0;
			UV260Reading = 0.0;
			Conductivity = 0.0;
			pH = 7.0;
			Temperature = 25.0;
			CurrentPhase = ChromatographyPhase.Idle;
			CurrentCV = 0.0;
			ProcessTime = 0.0;
			IsCollecting = false;
			CurrentFractionNumber = 0;
			ProductRecovery = 0.0;
			ProductPurity = 0.0;

			// Initialize buffer system
			BufferSolutions = new Dictionary<string, BufferSolution>();
			CurrentBuffer = "";
			BufferBLevel = 0.0;

			// Initialize fractions collection
			CollectedFractions = new List<Fraction>();

			// Initialize model parameters
			_theoreticalPlates = 3000.0; // Default value
			_asympmetryFactor = 1.1; // Slightly asymmetrical peaks
			_backPressureCoefficient = 0.001; // Pressure per flow rate unit
			_columnEfficiency = 0.9;

			// Initialize product characteristics
			_productRetentionTime = 0.0;
			_productPeakWidth = 0.0;
			_contaminants = new List<Contaminant>();

			// Initialize phase tracking
			_phaseDurations = new Dictionary<ChromatographyPhase, double>();
			_phaseStartTime = 0.0;
			_lastFractionTime = 0.0;
			_methodLoaded = false;

			// Initialize diagnostic data
			DiagnosticData["ColumnType"] = ColumnType.ToString();
			DiagnosticData["ColumnVolume"] = ColumnVolume;
			DiagnosticData["ResinLifetime"] = ResinLifetime;
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
		}

		private void InitializeResinParameters()
		{
			switch (ColumnType)
			{
				case ChromatographyColumnType.AffiniChromatography:
					ResinCapacity = 25.0; // mg/mL
					ResinLifetime = 100.0;
					_theoreticalPlates = 1000;
					_productRetentionTime = 1.5; // CV units
					break;
				case ChromatographyColumnType.IonExchange:
					ResinCapacity = 40.0; // mg/mL
					ResinLifetime = 100.0;
					_theoreticalPlates = 2000;
					_productRetentionTime = 2.0; // CV units
					break;
				case ChromatographyColumnType.HydrophobicInteraction:
					ResinCapacity = 20.0; // mg/mL
					ResinLifetime = 100.0;
					_theoreticalPlates = 1500;
					_productRetentionTime = 1.8; // CV units
					break;
				case ChromatographyColumnType.SizeExclusion:
					ResinCapacity = 10.0; // mg/mL - not really applicable but needed
					ResinLifetime = 100.0;
					_theoreticalPlates = 5000;
					_productRetentionTime = 1.0; // CV units
					break;
				default:
					ResinCapacity = 30.0; // mg/mL
					ResinLifetime = 100.0;
					_theoreticalPlates = 2000;
					_productRetentionTime = 1.5; // CV units
					break;
			}

			// Calculate peak width based on theoretical plates
			_productPeakWidth = ColumnVolume * 4.0 / Math.Sqrt(_theoreticalPlates);
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize connected devices
			_samplePump?.Initialize();
			_bufferAPump?.Initialize();
			_bufferBPump?.Initialize();
			_injectionValve?.Initialize();
			_columnBypassValve?.Initialize();
			_fractionValve?.Initialize();
			_uvMonitor?.Initialize();
			_conductivitySensor?.Initialize();
			_pressureSensor?.Initialize();
			_phAnalyzer?.Initialize();

			// Reset process parameters
			FlowRate = 0.0;
			Pressure = 0.0;
			CurrentPhase = ChromatographyPhase.Idle;
			CurrentCV = 0.0;
			ProcessTime = 0.0;
			IsCollecting = false;
			CurrentFractionNumber = 0;
			CollectedFractions.Clear();

			// Reset buffer state
			BufferBLevel = 0.0;
			CurrentBuffer = "";

			// Reset tracking variables
			_phaseDurations.Clear();
			_phaseStartTime = 0.0;
			_lastFractionTime = 0.0;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
				return;

			// Update process time
			ProcessTime += elapsedTime.TotalMinutes;

			// Update flow rate based on pumps
			UpdateFlowRate();

			// Update pressure based on flow rate
			UpdatePressure();

			// Update UV/conductivity readings based on current phase and elution profile
			UpdateProcessReadings(elapsedTime);

			// Process phase-specific logic
			ProcessCurrentPhase(elapsedTime);

			// Check for phase completion
			CheckPhaseCompletion();

			// Update collection of fractions if appropriate
			UpdateFractionCollection();

			// Check for alarms
			CheckAlarmConditions();

			// Update diagnostics
			UpdateDiagnostics();

			// Apply gradual resin degradation
			ResinLifetime -= 0.001 * elapsedTime.TotalHours; // Very small degradation per run
			ResinLifetime = Math.Max(0, ResinLifetime);
		}

		private void UpdateFlowRate()
		{
			double targetFlowRate = 0.0;

			// Calculate flow rate based on active pumps
			if (_samplePump != null && _samplePump.Status == DeviceStatus.Running)
			{
				targetFlowRate += _samplePump.FlowRate;
			}

			if (_bufferAPump != null && _bufferAPump.Status == DeviceStatus.Running)
			{
				targetFlowRate += _bufferAPump.FlowRate * (1.0 - BufferBLevel);
			}

			if (_bufferBPump != null && _bufferBPump.Status == DeviceStatus.Running)
			{
				targetFlowRate += _bufferBPump.FlowRate * BufferBLevel;
			}

			// Gradually adjust flow rate to target (pumps don't respond instantly)
			FlowRate += (targetFlowRate - FlowRate) * 0.3;

			// If column is bypassed, pressure will be very low regardless of flow
			if (_columnBypassValve != null && _columnBypassValve.Position > 50)
			{
				FlowRate = targetFlowRate; // No restriction when bypassed
			}
		}

		private void UpdatePressure()
		{
			// Calculate pressure based on flow rate, resin properties, and column dimensions
			double nominalPressure = FlowRate * _backPressureCoefficient * (ColumnHeight / ColumnDiameter);

			// Factor in resin compaction over time
			double compactionFactor = 1.0 + (0.2 * (100.0 - ResinLifetime) / 100.0);

			// Factor in column bypass valve position
			double bypassFactor = 1.0;
			if (_columnBypassValve != null && _columnBypassValve.Status == DeviceStatus.Running)
			{
				bypassFactor = 1.0 - (_columnBypassValve.Position / 100.0);
			}

			// Calculate final pressure
			Pressure = nominalPressure * compactionFactor * bypassFactor;

			// Add pressure from sensors if available
			if (_pressureSensor != null && _pressureSensor.Status == DeviceStatus.Running)
			{
				// Use a blend of calculated and measured pressure
				Pressure = (Pressure + _pressureSensor.Pressure) / 2.0;
			}

			// Check maximum pressure limit
			if (Pressure > MaxPressureLimit)
			{
				// Safety feature - reduce flow to prevent column damage
				if (_samplePump != null) _samplePump.SetSpeed(_samplePump.Speed * 0.8);
				if (_bufferAPump != null) _bufferAPump.SetSpeed(_bufferAPump.Speed * 0.8);
				if (_bufferBPump != null) _bufferBPump.SetSpeed(_bufferBPump.Speed * 0.8);

				AddAlarm("PRESSURE_LIMIT", $"Maximum pressure limit reached: {Pressure:F2} bar", AlarmSeverity.Warning);
			}
		}

		private void UpdateProcessReadings(TimeSpan elapsedTime)
		{
			// Update volume processed
			double volumeProcessed = FlowRate * (elapsedTime.TotalMinutes / 60.0); // L
			double cvProcessed = volumeProcessed / ColumnVolume;
			CurrentCV += cvProcessed;

			// Calculate UV and conductivity readings based on phase and elution profile
			switch (CurrentPhase)
			{
				case ChromatographyPhase.Equilibration:
					// Equilibration should show stable baseline
					UV280Reading = 0.02 + (Random.NextDouble() * 0.01); // Near zero with noise
					UV260Reading = 0.03 + (Random.NextDouble() * 0.01);
					UpdateBufferReadings();
					break;

				case ChromatographyPhase.SampleLoad:
					// During loading, non-binding material may pass through
					CalculateLoadPhaseReadings();
					UpdateBufferReadings();
					break;

				case ChromatographyPhase.Wash:
					// Wash phase - UV usually decreases back to baseline
					CalculateWashPhaseReadings();
					UpdateBufferReadings();
					break;

				case ChromatographyPhase.Elution:
					// Elution phase - product elutes in peaks
					CalculateElutionProfile();
					UpdateBufferReadings();
					break;

				case ChromatographyPhase.StripRegeneration:
					// Stripping shows additional peaks of tightly bound contaminants
					CalculateStripPhaseReadings();
					UpdateBufferReadings();
					break;

				case ChromatographyPhase.Sanitization:
					// Usually minimal UV with possible resin leachables
					UV280Reading = 0.05 + (Random.NextDouble() * 0.02);
					UV260Reading = 0.06 + (Random.NextDouble() * 0.02);
					Conductivity = 10.0 + (Random.NextDouble() * 2.0);
					pH = 13.0 - (Random.NextDouble() * 0.5); // NaOH sanitization
					break;

				case ChromatographyPhase.Storage:
				case ChromatographyPhase.Idle:
					// Minimal signals during standby
					UV280Reading = 0.01 + (Random.NextDouble() * 0.005);
					UV260Reading = 0.015 + (Random.NextDouble() * 0.005);
					Conductivity = 0.5 + (Random.NextDouble() * 0.1);
					pH = 7.0 + (Random.NextDouble() * 0.2 - 0.1);
					break;
			}

			// Get readings from sensors if available
			if (_uvMonitor != null && _uvMonitor.Status == DeviceStatus.Running)
			{
				UV280Reading = _uvMonitor.UV280Reading;
				UV260Reading = _uvMonitor.UV260Reading;
			}

			if (_conductivitySensor != null && _conductivitySensor.Status == DeviceStatus.Running)
			{
				Conductivity = _conductivitySensor.Conductivity;
			}

			if (_phAnalyzer != null && _phAnalyzer.Status == DeviceStatus.Running)
			{
				pH = _phAnalyzer.pH;
			}
		}

		private void UpdateBufferReadings()
		{
			// Update conductivity and pH based on buffer composition
			if (!string.IsNullOrEmpty(CurrentBuffer) && BufferSolutions.ContainsKey(CurrentBuffer))
			{
				var buffer = BufferSolutions[CurrentBuffer];

				if (ColumnType == ChromatographyColumnType.IonExchange && CurrentPhase == ChromatographyPhase.Elution)
				{
					// For ion exchange, conductivity increases during salt gradient
					Conductivity = buffer.BaseConductivity + (buffer.MaxConductivity - buffer.BaseConductivity) * BufferBLevel;

					// pH may change slightly due to salt concentration
					pH = buffer.pH + (BufferBLevel * 0.2);
				}
				else if (ColumnType == ChromatographyColumnType.HydrophobicInteraction && CurrentPhase == ChromatographyPhase.Elution)
				{
					// For HIC, conductivity decreases during gradient
					Conductivity = buffer.MaxConductivity - (buffer.MaxConductivity - buffer.BaseConductivity) * BufferBLevel;

					// pH usually stable
					pH = buffer.pH;
				}
				else
				{
					// Standard buffer behavior
					Conductivity = buffer.BaseConductivity + (Random.NextDouble() * 0.1);
					pH = buffer.pH + (Random.NextDouble() * 0.05 - 0.025);
				}
			}
			else
			{
				// Default values if no buffer defined
				Conductivity = 5.0 + (Random.NextDouble() * 0.5);
				pH = 7.0 + (Random.NextDouble() * 0.1 - 0.05);
			}
		}

		private void CalculateLoadPhaseReadings()
		{
			// During load phase, some non-bound material might flow through
			// while product binds to column (except in size exclusion)
			if (ColumnType == ChromatographyColumnType.SizeExclusion)
			{
				// In SEC, product and contaminants separate solely by size
				// No binding occurs, so we'll see minimal baseline
				UV280Reading = 0.03 + (Random.NextDouble() * 0.01);
				UV260Reading = 0.04 + (Random.NextDouble() * 0.01);
			}
			else
			{
				// For binding chromatography, calculate the breakthrough
				double columnCapacity = ColumnVolume * ResinCapacity * _columnEfficiency;
				double loadedAmount = Math.Min(LoadAmount, SampleVolume * SampleConcentration);
				double breakthroughRatio = Math.Max(0, (loadedAmount - columnCapacity) / loadedAmount);

				// Breakthrough curve follows a sigmoidal function
				double sigmoid = 1.0 / (1.0 + Math.Exp(-10 * (CurrentCV - 3.0)));
				double breakthroughLevel = breakthroughRatio * sigmoid;

				// Calculate the UV based on sample concentration and breakthrough
				UV280Reading = 0.02 + SampleConcentration * breakthroughLevel * 0.05;
				UV260Reading = 0.03 + SampleConcentration * breakthroughLevel * 0.04;
			}
		}

		private void CalculateWashPhaseReadings()
		{
			// During wash, loosely bound material elutes out
			double washProgress = (ProcessTime - _phaseStartTime) / (_phaseDurations[ChromatographyPhase.Wash] ?? 5.0);
			double washDecay = Math.Exp(-3.0 * washProgress); // Exponential decay

			// Calculate UV readings
			UV280Reading = 0.02 + (0.1 * washDecay) + (Random.NextDouble() * 0.01);
			UV260Reading = 0.03 + (0.12 * washDecay) + (Random.NextDouble() * 0.01);
		}

		private void CalculateElutionProfile()
		{
			// Model the product peak and contaminant peaks during elution
			double elutionProgress = CurrentCV - (_phaseDurations[ChromatographyPhase.Equilibration] ?? 0.0) -
									(_phaseDurations[ChromatographyPhase.SampleLoad] ?? 0.0) -
									(_phaseDurations[ChromatographyPhase.Wash] ?? 0.0);

			// Adjust for current phase
			if (CurrentPhase == ChromatographyPhase.Elution)
			{
				elutionProgress = (ProcessTime - _phaseStartTime) / (_phaseDurations[ChromatographyPhase.Elution] ?? 10.0);
				elutionProgress *= 5.0; // Scale to reasonable CV range for elution
			}

			// Base readings
			double uv280 = 0.02 + (Random.NextDouble() * 0.01);
			double uv260 = 0.03 + (Random.NextDouble() * 0.01);

			// Add product peak (Gaussian distribution)
			double productCV = _productRetentionTime * (0.9 + (0.2 * BufferBLevel));
			double productPeakValue = CalculatePeakValue(elutionProgress, productCV, _productPeakWidth);
			double productPeakHeight = (SampleConcentration / 10.0); // Scale to reasonable absorbance

			uv280 += productPeakHeight * productPeakValue;
			uv260 += productPeakHeight * 0.7 * productPeakValue; // Typical protein A260/A280 ratio

			// Add contaminant peaks
			foreach (var contaminant in _contaminants)
			{
				double contaminantCV = contaminant.RetentionTime * (0.95 + (0.1 * BufferBLevel));
				double contaminantPeakValue = CalculatePeakValue(elutionProgress, contaminantCV, contaminant.PeakWidth);

				uv280 += contaminant.Concentration * contaminantPeakValue;
				uv260 += contaminant.Concentration * contaminant.A260_A280_Ratio * contaminantPeakValue;
			}

			// Update global readings
			UV280Reading = uv280;
			UV260Reading = uv260;

			// Update buffer gradient
			if (_currentMethod != null && CurrentPhase == ChromatographyPhase.Elution)
			{
				// Different gradient types
				if (_currentMethod.ElutionGradientType == GradientType.Linear)
				{
					BufferBLevel = elutionProgress / 5.0; // Linear increase over elution
				}
				else if (_currentMethod.ElutionGradientType == GradientType.Step)
				{
					// Step gradient at specific points
					if (elutionProgress > 1.0) BufferBLevel = 0.3;
					if (elutionProgress > 2.0) BufferBLevel = 0.6;
					if (elutionProgress > 3.0) BufferBLevel = 1.0;
				}

				BufferBLevel = Math.Min(1.0, Math.Max(0.0, BufferBLevel)); // Clamp to 0-100%
			}
		}

		private double CalculatePeakValue(double x, double center, double width)
		{
			// Calculate Gaussian peak with optional asymmetry
			double sigma = width / 2.355; // Convert FWHM to sigma
			double z = (x - center) / sigma;

			if (x <= center)
			{
				return Math.Exp(-0.5 * z * z); // Standard Gaussian
			}
			else
			{
				return Math.Exp(-0.5 * z * z / _asympmetryFactor); // Asymmetric tail
			}
		}

		private void CalculateStripPhaseReadings()
		{
			// During strip phase, tightly bound contaminants elute
			double stripProgress = (ProcessTime - _phaseStartTime) / (_phaseDurations[ChromatographyPhase.StripRegeneration] ?? 5.0);

			// Model strip peaks
			double stripPeakTime = 0.3; // Peak at 30% through strip phase
			double stripPeakValue = CalculatePeakValue(stripProgress, stripPeakTime, 0.2);

			// Calculate UV readings - usually higher in strip phase due to strong conditions
			UV280Reading = 0.02 + (0.3 * stripPeakValue) + (Random.NextDouble() * 0.02);
			UV260Reading = 0.03 + (0.4 * stripPeakValue) + (Random.NextDouble() * 0.02);

			// Conductivity and pH depend on strip buffer
			Conductivity = 20.0 + (Random.NextDouble() * 3.0); // Usually high salt
			pH = 3.0 + (Random.NextDouble() * 0.2); // Often acidic
		}

		private void ProcessCurrentPhase(TimeSpan elapsedTime)
		{
			// Process specific actions for each phase
			switch (CurrentPhase)
			{
				case ChromatographyPhase.Equilibration:
					// Set buffer composition for equilibration
					CurrentBuffer = "EquilibrationBuffer";
					BufferBLevel = 0.0;
					break;

				case ChromatographyPhase.SampleLoad:
					// Manage sample application
					CurrentBuffer = "LoadBuffer";
					BufferBLevel = 0.0;
					break;

				case ChromatographyPhase.Wash:
					// Set wash buffer
					CurrentBuffer = "WashBuffer";
					BufferBLevel = 0.0;
					break;

				case ChromatographyPhase.Elution:
					// Manage gradient elution
					CurrentBuffer = "ElutionBuffer";
					// BufferBLevel updated in CalculateElutionProfile
					break;

				case ChromatographyPhase.StripRegeneration:
					// Use strip/regeneration buffer
					CurrentBuffer = "StripBuffer";
					BufferBLevel = 1.0;
					break;

				case ChromatographyPhase.Sanitization:
					// Use sanitization solution (e.g., NaOH)
					CurrentBuffer = "SanitizationBuffer";
					BufferBLevel = 1.0;
					break;

				case ChromatographyPhase.Storage:
					// Use storage buffer (e.g., 20% ethanol)
					CurrentBuffer = "StorageBuffer";
					BufferBLevel = 1.0;
					break;
			}
		}

		private void CheckPhaseCompletion()
		{
			// Skip if we don't have phase durations loaded yet
			if (!_phaseDurations.ContainsKey(CurrentPhase))
				return;

			// Calculate how much time has elapsed in this phase
			double elapsedInCurrentPhase = ProcessTime - _phaseStartTime;
			double phaseDuration = _phaseDurations[CurrentPhase];

			// Check if the current phase is complete based on time
			if (elapsedInCurrentPhase >= phaseDuration)
			{
				// Move to the next phase
				switch (CurrentPhase)
				{
					case ChromatographyPhase.Equilibration:
						AddAlarm("PHASE_COMPLETE", "Equilibration phase complete", AlarmSeverity.Information);
						if (_methodLoaded && _currentMethod.IncludeSampleLoad)
						{
							SetPhase(ChromatographyPhase.SampleLoad);
						}
						else
						{
							SetPhase(ChromatographyPhase.Idle);
						}
						break;

					case ChromatographyPhase.SampleLoad:
						AddAlarm("PHASE_COMPLETE", "Sample load phase complete", AlarmSeverity.Information);
						if (_methodLoaded && _currentMethod.IncludeWash)
						{
							SetPhase(ChromatographyPhase.Wash);
						}
						else
						{
							SetPhase(ChromatographyPhase.Idle);
						}
						break;

					case ChromatographyPhase.Wash:
						AddAlarm("PHASE_COMPLETE", "Wash phase complete", AlarmSeverity.Information);
						if (_methodLoaded && _currentMethod.IncludeElution)
						{
							SetPhase(ChromatographyPhase.Elution);
						}
						else
						{
							SetPhase(ChromatographyPhase.Idle);
						}
						break;

					case ChromatographyPhase.Elution:
						AddAlarm("PHASE_COMPLETE", "Elution phase complete", AlarmSeverity.Information);
						if (_methodLoaded && _currentMethod.IncludeStripRegeneration)
						{
							SetPhase(ChromatographyPhase.StripRegeneration);
						}
						else if (_methodLoaded && _currentMethod.IncludeSanitization)
						{
							SetPhase(ChromatographyPhase.Sanitization);
						}
						else
						{
							SetPhase(ChromatographyPhase.Idle);
						}
						break;

					case ChromatographyPhase.StripRegeneration:
						AddAlarm("PHASE_COMPLETE", "Strip/regeneration phase complete", AlarmSeverity.Information);
						if (_methodLoaded && _currentMethod.IncludeSanitization)
						{
							SetPhase(ChromatographyPhase.Sanitization);
						}
						else
						{
							SetPhase(ChromatographyPhase.Idle);
						}
						break;

					case ChromatographyPhase.Sanitization:
						AddAlarm("PHASE_COMPLETE", "Sanitization phase complete", AlarmSeverity.Information);
						if (_methodLoaded && _currentMethod.IncludeStorage)
						{
							SetPhase(ChromatographyPhase.Storage);
						}
						else
						{
							SetPhase(ChromatographyPhase.Idle);
						}
						break;

					case ChromatographyPhase.Storage:
						AddAlarm("PHASE_COMPLETE", "Storage phase complete", AlarmSeverity.Information);
						SetPhase(ChromatographyPhase.Idle);
						break;
				}
			}
		}

		private void UpdateFractionCollection()
		{
			// Only collect fractions during elution, with appropriate hardware connected
			if (CurrentPhase != ChromatographyPhase.Elution ||
				_fractionValve == null ||
				!IsCollecting)
			{
				return;
			}

			double elapsedSinceLastFraction = ProcessTime - _lastFractionTime;
			bool collectNewFraction = false;
			string collectionReason = "";

			// Check collection triggers
			if (_currentMethod != null)
			{
				switch (_currentMethod.CollectionMode)
				{
					case FractionCollectionMode.TimeBased:
						// Collect every X minutes
						if (elapsedSinceLastFraction >= _currentMethod.CollectionInterval)
						{
							collectNewFraction = true;
							collectionReason = "Time interval";
						}
						break;

					case FractionCollectionMode.VolumeBased:
						// Convert time to volume
						double volumeProcessed = elapsedSinceLastFraction * FlowRate / 1000.0; // L
						if (volumeProcessed >= _currentMethod.CollectionInterval)
						{
							collectNewFraction = true;
							collectionReason = "Volume interval";
						}
						break;

					case FractionCollectionMode.PeakBased:
						// Check if we're on an upslope or downslope of a peak
						if (UV280Reading > _currentMethod.CollectionThreshold)
						{
							collectNewFraction = true;
							collectionReason = "Peak detection";
						}
						break;

					case FractionCollectionMode.Manual:
						// No automatic collection
						break;
				}
			}

			if (collectNewFraction)
			{
				// Create new fraction
				CurrentFractionNumber++;

				// Set fraction valve position
				if (_fractionValve != null)
				{
					// Route to appropriate fraction port (simple implementation)
					_fractionValve.SetPosition((CurrentFractionNumber % 10) * 10);
				}

				// Record fraction details
				var fraction = new Fraction
				{
					FractionNumber = CurrentFractionNumber,
					Volume = elapsedSinceLastFraction * FlowRate / 1000.0, // L
					CollectionTime = ProcessTime,
					UV280Value = UV280Reading,
					UV260Value = UV260Reading,
					Conductivity = Conductivity,
					pH = pH,
					BufferBPercentage = BufferBLevel * 100.0,
					CollectionReason = collectionReason
				};

				// Estimate protein content based on UV
				fraction.EstimatedProteinConcentration = UV280Reading * 0.7; // Simple approximation

				// Add to collection
				CollectedFractions.Add(fraction);
				_lastFractionTime = ProcessTime;

				AddAlarm("FRACTION_COLLECTED", $"Fraction #{CurrentFractionNumber} collected", AlarmSeverity.Information);

				// Compute purity and recovery for each fraction
				CalculateProductRecoveryAndPurity();
			}
		}

		private void CalculateProductRecoveryAndPurity()
		{
			// Skip if no fractions
			if (CollectedFractions.Count == 0)
				return;

			// Calculate total amount of product loaded
			double totalProductLoaded = LoadAmount;
			if (totalProductLoaded <= 0) return;

			// Calculate total protein in all fractions
			double totalProteinInFractions = 0;
			double totalProductInFractions = 0;
			double totalContaminantInFractions = 0;

			foreach (var fraction in CollectedFractions)
			{
				// Estimate how much of the UV is from product vs contaminants
				// This is a simplified model - in reality would be more complex
				double productUVContribution = fraction.EstimatedProteinConcentration * 0.9;
				double contaminantUVContribution = fraction.EstimatedProteinConcentration * 0.1;

				// Calculate volumes
				double fractionVolume = fraction.Volume;

				// Calculate masses
				double productMass = productUVContribution * fractionVolume; // mg
				double contaminantMass = contaminantUVContribution * fractionVolume; // mg

				totalProductInFractions += productMass;
				totalContaminantInFractions += contaminantMass;
				totalProteinInFractions += productMass + contaminantMass;
			}

			// Calculate recovery and purity
			ProductRecovery = (totalProductInFractions / totalProductLoaded) * 100.0;
			ProductPurity = (totalProductInFractions / totalProteinInFractions) * 100.0;

			// Update diagnostic data
			DiagnosticData["ProductRecovery"] = ProductRecovery;
			DiagnosticData["ProductPurity"] = ProductPurity;
		}

		private void CheckAlarmConditions()
		{
			// Check for column lifetime
			if (ResinLifetime < 20)
			{
				AddAlarm("RESIN_LIFETIME", "Column resin approaching end of life", AlarmSeverity.Warning);
			}

			// Check for abnormal readings based on current phase
			switch (CurrentPhase)
			{
				case ChromatographyPhase.Equilibration:
					// High UV during equilibration suggests contamination
					if (UV280Reading > 0.1)
					{
						AddAlarm("HIGH_BASELINE", "Elevated baseline UV detected during equilibration", AlarmSeverity.Minor);
					}
					break;

				case ChromatographyPhase.SampleLoad:
					// Early breakthrough suggests overloading or resin damage
					if (UV280Reading > 0.5 && CurrentCV < 2.0)
					{
						AddAlarm("EARLY_BREAKTHROUGH", "Early product breakthrough detected", AlarmSeverity.Warning);
					}
					break;

				case ChromatographyPhase.Elution:
					// Abnormally low peak suggests loading issue
					if (CurrentCV > 2.0 && UV280Reading < 0.1 && CurrentPhase == ChromatographyPhase.Elution)
					{
						AddAlarm("LOW_YIELD", "Low UV signal during elution indicates poor recovery", AlarmSeverity.Minor);
					}
					break;
			}

			// Check for air bubbles (indicated by sudden spikes)
			if (Math.Abs(UV280Reading - DiagnosticData.TryGetValue("PreviousUV280", out object prevUV)
				? (double)prevUV : UV280Reading) > 0.5)
			{
				AddAlarm("SIGNAL_SPIKE", "Sudden UV signal change detected", AlarmSeverity.Information);
			}

			// Store current values for next comparison
			DiagnosticData["PreviousUV280"] = UV280Reading;
			DiagnosticData["PreviousUV260"] = UV260Reading;
		}

		private void UpdateDiagnostics()
		{
			// Update basic parameters
			DiagnosticData["FlowRate"] = FlowRate;
			DiagnosticData["Pressure"] = Pressure;
			DiagnosticData["UV280Reading"] = UV280Reading;
			DiagnosticData["UV260Reading"] = UV260Reading;
			DiagnosticData["Conductivity"] = Conductivity;
			DiagnosticData["pH"] = pH;
			DiagnosticData["Temperature"] = Temperature;
			DiagnosticData["CurrentPhase"] = CurrentPhase.ToString();
			DiagnosticData["CurrentCV"] = CurrentCV;
			DiagnosticData["ProcessTime"] = ProcessTime;
			DiagnosticData["IsCollecting"] = IsCollecting;
			DiagnosticData["CurrentFractionNumber"] = CurrentFractionNumber;
			DiagnosticData["BufferBLevel"] = BufferBLevel;

			// Update additional parameters
			if (_currentMethod != null)
			{
				DiagnosticData["MethodName"] = _currentMethod.MethodName;
			}
		}

		#region Public Control Methods

		/// <summary>
		/// Load a chromatography method to control the run
		/// </summary>
		public bool LoadMethod(ChromatographyMethod method)
		{
			if (CurrentPhase != ChromatographyPhase.Idle)
			{
				AddAlarm("METHOD_LOAD_FAILED", "Cannot load method while run is in progress", AlarmSeverity.Warning);
				return false;
			}

			_currentMethod = method;
			_methodLoaded = true;

			// Set phase durations from method
			_phaseDurations.Clear();

			if (method.IncludeEquilibration)
				_phaseDurations[ChromatographyPhase.Equilibration] = method.EquilibrationDuration;

			if (method.IncludeSampleLoad)
				_phaseDurations[ChromatographyPhase.SampleLoad] = method.SampleLoadDuration;

			if (method.IncludeWash)
				_phaseDurations[ChromatographyPhase.Wash] = method.WashDuration;

			if (method.IncludeElution)
				_phaseDurations[ChromatographyPhase.Elution] = method.ElutionDuration;

			if (method.IncludeStripRegeneration)
				_phaseDurations[ChromatographyPhase.StripRegeneration] = method.StripRegenerationDuration;

			if (method.IncludeSanitization)
				_phaseDurations[ChromatographyPhase.Sanitization] = method.SanitizationDuration;

			if (method.IncludeStorage)
				_phaseDurations[ChromatographyPhase.Storage] = method.StorageDuration;

			// Set flow rate setpoint
			FlowRateSetpoint = method.FlowRate;

			// Configure sample information if provided
			if (method.SampleVolume > 0)
			{
				SampleVolume = method.SampleVolume;
			}

			if (method.SampleConcentration > 0)
			{
				SampleConcentration = method.SampleConcentration;
			}

			LoadAmount = SampleVolume * SampleConcentration;

			// Log the method loading
			DiagnosticData["LoadedMethod"] = method.MethodName;
			AddAlarm("METHOD_LOADED", $"Method '{method.MethodName}' loaded", AlarmSeverity.Information);

			return true;
		}

		/// <summary>
		/// Start running the chromatography method
		/// </summary>
		public bool StartMethod()
		{
			if (!_methodLoaded)
			{
				AddAlarm("START_FAILED", "No method loaded", AlarmSeverity.Warning);
				return false;
			}

			if (CurrentPhase != ChromatographyPhase.Idle)
			{
				AddAlarm("START_FAILED", "Run already in progress", AlarmSeverity.Warning);
				return false;
			}

			// Reset parameters
			CurrentCV = 0.0;
			ProcessTime = 0.0;
			IsCollecting = false;
			CurrentFractionNumber = 0;
			CollectedFractions.Clear();
			ProductRecovery = 0.0;
			ProductPurity = 0.0;

			// Start with first phase in method
			if (_currentMethod.IncludeEquilibration)
			{
				SetPhase(ChromatographyPhase.Equilibration);
			}
			else if (_currentMethod.IncludeSampleLoad)
			{
				SetPhase(ChromatographyPhase.SampleLoad);
			}
			else if (_currentMethod.IncludeElution)
			{
				SetPhase(ChromatographyPhase.Elution);
			}
			else
			{
				AddAlarm("METHOD_INVALID", "Method has no valid phases", AlarmSeverity.Warning);
				return false;
			}

			// Start necessary equipment
			if (_bufferAPump != null) _bufferAPump.Start();
			if (_bufferBPump != null) _bufferBPump.Start();

			// Only start sample pump if needed
			if (_currentMethod.IncludeSampleLoad && _samplePump != null)
			{
				_samplePump.Start();
			}

			// Update status
			Status = DeviceStatus.Running;
			AddAlarm("RUN_STARTED", "Chromatography run started", AlarmSeverity.Information);

			return true;
		}

		/// <summary>
		/// Stop the current run
		/// </summary>
		public void StopMethod()
		{
			if (CurrentPhase == ChromatographyPhase.Idle)
				return;

			// Stop all pumps
			if (_samplePump != null) _samplePump.Stop();
			if (_bufferAPump != null) _bufferAPump.Stop();
			if (_bufferBPump != null) _bufferBPump.Stop();

			// Go to idle state
			SetPhase(ChromatographyPhase.Idle);

			// Update status
			Status = DeviceStatus.Idle;
			AddAlarm("RUN_STOPPED", "Chromatography run stopped", AlarmSeverity.Information);
		}

		/// <summary>
		/// Manually advance to the next phase
		/// </summary>
		public bool AdvanceToNextPhase()
		{
			if (CurrentPhase == ChromatographyPhase.Idle || !_methodLoaded)
			{
				AddAlarm("ADVANCE_FAILED", "No active run to advance", AlarmSeverity.Warning);
				return false;
			}

			// Determine next phase based on current phase
			ChromatographyPhase nextPhase;

			switch (CurrentPhase)
			{
				case ChromatographyPhase.Equilibration:
					nextPhase = _currentMethod.IncludeSampleLoad ?
						ChromatographyPhase.SampleLoad : ChromatographyPhase.Idle;
					break;

				case ChromatographyPhase.SampleLoad:
					nextPhase = _currentMethod.IncludeWash ?
						ChromatographyPhase.Wash :
						(_currentMethod.IncludeElution ? ChromatographyPhase.Elution : ChromatographyPhase.Idle);
					break;

				case ChromatographyPhase.Wash:
					nextPhase = _currentMethod.IncludeElution ?
						ChromatographyPhase.Elution : ChromatographyPhase.Idle;
					break;

				case ChromatographyPhase.Elution:
					nextPhase = _currentMethod.IncludeStripRegeneration ?
						ChromatographyPhase.StripRegeneration :
						(_currentMethod.IncludeSanitization ? ChromatographyPhase.Sanitization : ChromatographyPhase.Idle);
					break;

				case ChromatographyPhase.StripRegeneration:
					nextPhase = _currentMethod.IncludeSanitization ?
						ChromatographyPhase.Sanitization : ChromatographyPhase.Idle;
					break;

				case ChromatographyPhase.Sanitization:
					nextPhase = _currentMethod.IncludeStorage ?
						ChromatographyPhase.Storage : ChromatographyPhase.Idle;
					break;

				default:
					nextPhase = ChromatographyPhase.Idle;
					break;
			}

			// Set the next phase
			SetPhase(nextPhase);

			return true;
		}

		/// <summary>
		/// Set the operating phase
		/// </summary>
		private void SetPhase(ChromatographyPhase phase)
		{
			// Save previous phase
			ChromatographyPhase previousPhase = CurrentPhase;

			// Update phase
			CurrentPhase = phase;
			_phaseStartTime = ProcessTime;

			// Configure hardware based on phase
			ConfigureHardwareForPhase(phase, previousPhase);

			// Start/stop fraction collection if entering/exiting elution
			if (phase == ChromatographyPhase.Elution && previousPhase != ChromatographyPhase.Elution)
			{
				IsCollecting = _currentMethod != null && _currentMethod.AutoStartCollection;
				_lastFractionTime = ProcessTime;
			}
			else if (phase != ChromatographyPhase.Elution && previousPhase == ChromatographyPhase.Elution)
			{
				IsCollecting = false;
			}

			// Update diagnostics
			DiagnosticData["CurrentPhase"] = phase.ToString();
			DiagnosticData["PhaseStartTime"] = _phaseStartTime;

			AddAlarm("PHASE_CHANGE", $"Phase changed: {previousPhase} -> {phase}", AlarmSeverity.Information);
		}

		/// <summary>
		/// Configure hardware devices for the current phase
		/// </summary>
		private void ConfigureHardwareForPhase(ChromatographyPhase phase, ChromatographyPhase previousPhase)
		{
			// Set pump speeds based on phase
			switch (phase)
			{
				case ChromatographyPhase.Idle:
					// Stop all pumps
					if (_samplePump != null) _samplePump.Stop();
					if (_bufferAPump != null) _bufferAPump.SetSpeed(0);
					if (_bufferBPump != null) _bufferBPump.SetSpeed(0);
					break;

				case ChromatographyPhase.Equilibration:
					// Run buffer pump at desired flow rate
					if (_samplePump != null) _samplePump.Stop();
					if (_bufferAPump != null)
					{
						_bufferAPump.Start();
						_bufferAPump.SetSpeed(FlowRateSetpoint / 5.0); // Assuming flow is about 5 times the speed in %
					}
					if (_bufferBPump != null)
					{
						_bufferBPump.Start();
						_bufferBPump.SetSpeed(0); // No buffer B initially
					}
					break;

				case ChromatographyPhase.SampleLoad:
					// Switch to sample pump
					if (_bufferAPump != null) _bufferAPump.Stop();
					if (_bufferBPump != null) _bufferBPump.Stop();
					if (_samplePump != null)
					{
						_samplePump.Start();
						_samplePump.SetSpeed(FlowRateSetpoint / 5.0);
					}

					// Open injection valve
					if (_injectionValve != null)
					{
						_injectionValve.Start();
						_injectionValve.SetPosition(100); // Fully open
					}
					break;

				case ChromatographyPhase.Wash:
					// Switch back to buffer pump
					if (_samplePump != null) _samplePump.Stop();
					if (_bufferAPump != null)
					{
						_bufferAPump.Start();
						_bufferAPump.SetSpeed(FlowRateSetpoint / 5.0);
					}
					if (_bufferBPump != null)
					{
						_bufferBPump.Start();
						_bufferBPump.SetSpeed(0); // No buffer B
					}

					// Close injection valve
					if (_injectionValve != null)
					{
						_injectionValve.SetPosition(0); // Closed position
					}
					break;

				case ChromatographyPhase.Elution:
					// Run buffer pumps for gradient
					if (_samplePump != null) _samplePump.Stop();
					if (_bufferAPump != null)
					{
						_bufferAPump.Start();
						_bufferAPump.SetSpeed(FlowRateSetpoint / 5.0);
					}
					if (_bufferBPump != null)
					{
						_bufferBPump.Start();
						_bufferBPump.SetSpeed(FlowRateSetpoint / 5.0); // Initially equal to A
					}
					break;

				case ChromatographyPhase.StripRegeneration:
				case ChromatographyPhase.Sanitization:
				case ChromatographyPhase.Storage:
					// Similar setup for maintenance phases
					if (_samplePump != null) _samplePump.Stop();
					if (_bufferAPump != null)
					{
						_bufferAPump.Start();
						_bufferAPump.SetSpeed(FlowRateSetpoint / 10.0); // Lower flow for maintenance
					}
					if (_bufferBPump != null)
					{
						_bufferBPump.Start();
						_bufferBPump.SetSpeed(FlowRateSetpoint / 10.0);
					}
					break;
			}

			// Configure fraction valve for collection
			if (phase == ChromatographyPhase.Elution && _fractionValve != null)
			{
				_fractionValve.Start();
				_fractionValve.SetPosition(0); // Initial position
			}
			else if (previousPhase == ChromatographyPhase.Elution && _fractionValve != null)
			{
				_fractionValve.SetPosition(0); // Reset valve
			}
		}

		/// <summary>
		/// Start or stop fraction collection
		/// </summary>
		public void SetFractionCollection(bool collectFractions)
		{
			if (CurrentPhase == ChromatographyPhase.Elution)
			{
				IsCollecting = collectFractions;
				_lastFractionTime = ProcessTime; // Reset the timer

				if (collectFractions)
				{
					AddAlarm("COLLECTION_STARTED", "Fraction collection started", AlarmSeverity.Information);
				}
				else
				{
					AddAlarm("COLLECTION_STOPPED", "Fraction collection stopped", AlarmSeverity.Information);
				}
			}
			else
			{
				AddAlarm("COLLECTION_ERROR", "Fraction collection only available during elution", AlarmSeverity.Warning);
			}
		}

		/// <summary>
		/// Collect a single fraction manually
		/// </summary>
		public void CollectFractionManually()
		{
			if (CurrentPhase == ChromatographyPhase.Elution)
			{
				// Temporary enable collection to trigger the mechanism
				bool wasCollecting = IsCollecting;
				IsCollecting = true;
				UpdateFractionCollection();
				IsCollecting = wasCollecting;
			}
			else
			{
				AddAlarm("COLLECTION_ERROR", "Manual collection only available during elution", AlarmSeverity.Warning);
			}
		}

		/// <summary>
		/// Add a buffer to the system
		/// </summary>
		public void AddBuffer(string bufferName, double pH, double baseConductivity, double maxConductivity)
		{
			BufferSolutions[bufferName] = new BufferSolution
			{
				Name = bufferName,
				pH = pH,
				BaseConductivity = baseConductivity,
				MaxConductivity = maxConductivity
			};

			DiagnosticData[$"Buffer_{bufferName}"] = $"pH {pH}, Conductivity {baseConductivity}-{maxConductivity} mS/cm";
		}

		/// <summary>
		/// Add a contaminant profile to the sample
		/// </summary>
		public void AddContaminant(double retentionTime, double concentration, double peakWidth, double a260_a280_ratio)
		{
			_contaminants.Add(new Contaminant
			{
				RetentionTime = retentionTime,
				Concentration = concentration,
				PeakWidth = peakWidth,
				A260_A280_Ratio = a260_a280_ratio
			});
		}

		/// <summary>
		/// Change the column type and reset parameters
		/// </summary>
		public void ChangeColumnType(ChromatographyColumnType columnType)
		{
			if (Status == DeviceStatus.Running)
			{
				AddAlarm("COLUMN_CHANGE_ERROR", "Cannot change column while running", AlarmSeverity.Warning);
				return;
			}

			ColumnType = columnType;
			InitializeResinParameters();

			DiagnosticData["ColumnType"] = columnType.ToString();
			DiagnosticData["ResinCapacity"] = ResinCapacity;
			DiagnosticData["TheoreticalPlates"] = _theoreticalPlates;

			AddAlarm("COLUMN_CHANGED", $"Column changed to {columnType}", AlarmSeverity.Information);
		}

		/// <summary>
		/// Set the sample information
		/// </summary>
		public void SetSampleInformation(double volume, double concentration)
		{
			SampleVolume = volume;
			SampleConcentration = concentration;
			LoadAmount = volume * concentration;

			DiagnosticData["SampleVolume"] = volume;
			DiagnosticData["SampleConcentration"] = concentration;
			DiagnosticData["LoadAmount"] = LoadAmount;
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(8);

			switch (faultType)
			{
				case 0: // Air in detector
					AddAlarm("AIR_IN_DETECTOR", "Air bubble in flow cell detected", AlarmSeverity.Minor);
					UV280Reading = Math.Abs(UV280Reading * 5.0 + Random.NextDouble() * 2.0);
					UV260Reading = Math.Abs(UV260Reading * 5.0 + Random.NextDouble() * 2.0);
					break;

				case 1: // Clogged column frit
					AddAlarm("COLUMN_CLOGGED", "Column inlet frit appears clogged", AlarmSeverity.Major);
					// Increase pressure, decrease flow
					Pressure = MaxPressureLimit * 1.2;
					FlowRate *= 0.3;
					break;

				case 2: // Pump failure
					AddAlarm("PUMP_FAILURE", "Buffer pump failure detected", AlarmSeverity.Critical);
					if (_bufferAPump != null)
						_bufferAPump.SimulateFault();
					break;

				case 3: // Buffer depletion
					AddAlarm("BUFFER_DEPLETED", "Buffer supply depleted", AlarmSeverity.Warning);
					FlowRate *= 0.5;
					if (_bufferAPump != null)
						_bufferAPump.SetSpeed(_bufferAPump.Speed * 0.1);
					break;

				case 4: // Column channeling
					AddAlarm("COLUMN_CHANNELING", "Possible column channeling detected", AlarmSeverity.Major);
					// Reduced theoretical plates, early elution
					_theoreticalPlates *= 0.5;
					_productRetentionTime *= 0.8;
					_productPeakWidth *= 1.5;
					break;

				case 5: // Valve leakage
					AddAlarm("VALVE_LEAK", "Injection valve leakage detected", AlarmSeverity.Warning);
					// Sample leaking into system when it shouldn't
					if (CurrentPhase != ChromatographyPhase.SampleLoad && Random.NextDouble() > 0.7)
					{
						UV280Reading += Random.NextDouble() * 0.3;
					}
					break;

				case 6: // Temperature excursion
					AddAlarm("TEMPERATURE_HIGH", "Column temperature excursion", AlarmSeverity.Minor);
					Temperature = 35.0 + Random.NextDouble() * 10.0;
					// Affects peak shape
					_asympmetryFactor = 1.5;
					break;

				case 7: // UV lamp failure
					AddAlarm("UV_LAMP_FAILURE", "UV detector lamp failure", AlarmSeverity.Critical);
					UV280Reading = 0.01 + Random.NextDouble() * 0.01;
					UV260Reading = 0.01 + Random.NextDouble() * 0.01;
					break;
			}
		}

		#endregion
	}

	#region Support Classes

	public class BufferSolution
	{
		public string Name { get; set; }
		public double pH { get; set; }
		public double BaseConductivity { get; set; } // mS/cm
		public double MaxConductivity { get; set; } // mS/cm
	}

	public class Fraction
	{
		public int FractionNumber { get; set; }
		public double Volume { get; set; } // L
		public double CollectionTime { get; set; } // Minutes from run start
		public double UV280Value { get; set; } // Absorbance
		public double UV260Value { get; set; } // Absorbance
		public double Conductivity { get; set; } // mS/cm
		public double pH { get; set; }
		public double BufferBPercentage { get; set; } // %
		public double EstimatedProteinConcentration { get; set; } // mg/mL
		public string CollectionReason { get; set; }
	}

	public class Contaminant
	{
		public double RetentionTime { get; set; } // In CV units
		public double Concentration { get; set; } // Relative to sample concentration
		public double PeakWidth { get; set; } // In CV units
		public double A260_A280_Ratio { get; set; } // Ratio for UV absorbance
	}

	public class ChromatographyMethod
	{
		public string MethodName { get; set; } = "Default Method";

		// Flow parameters
		public double FlowRate { get; set; } = 100.0; // mL/min

		// Sample parameters
		public double SampleVolume { get; set; } = 10.0; // mL
		public double SampleConcentration { get; set; } = 1.0; // mg/mL

		// Phase inclusion flags
		public bool IncludeEquilibration { get; set; } = true;
		public bool IncludeSampleLoad { get; set; } = true;
		public bool IncludeWash { get; set; } = true;
		public bool IncludeElution { get; set; } = true;
		public bool IncludeStripRegeneration { get; set; } = true;
		public bool IncludeSanitization { get; set; } = false;
		public bool IncludeStorage { get; set; } = false;

		// Phase durations (minutes)
		public double EquilibrationDuration { get; set; } = 20.0;
		public double SampleLoadDuration { get; set; } = 30.0;
		public double WashDuration { get; set; } = 15.0;
		public double ElutionDuration { get; set; } = 45.0;
		public double StripRegenerationDuration { get; set; } = 20.0;
		public double SanitizationDuration { get; set; } = 30.0;
		public double StorageDuration { get; set; } = 15.0;

		// Gradient settings
		public GradientType ElutionGradientType { get; set; } = GradientType.Linear;

		// Fraction collection
		public bool AutoStartCollection { get; set; } = true;
		public FractionCollectionMode CollectionMode { get; set; } = FractionCollectionMode.TimeBased;
		public double CollectionInterval { get; set; } = 2.0; // Minutes or mL
		public double CollectionThreshold { get; set; } = 0.1; // UV units
	}

	public enum ChromatographyColumnType
	{
		AffiniChromatography,
		IonExchange,
		HydrophobicInteraction,
		SizeExclusion,
		MixedMode
	}

	public enum ChromatographyPhase
	{
		Idle,
		Equilibration,
		SampleLoad,
		Wash,
		Elution,
		StripRegeneration,
		Sanitization,
		Storage
	}

	public enum GradientType
	{
		Linear,
		Step,
		Exponential,
		Concave
	}

	public enum FractionCollectionMode
	{
		Manual,
		TimeBased,
		VolumeBased,
		PeakBased
	}

	#endregion
}