using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.ProcessEquipment
{
    /// <summary>
    /// Simulates a Tangential Flow Filtration (TFF) system used for concentration,
    /// diafiltration, and purification in biopharmaceutical processing
    /// </summary>
    public class TangentialFlowFilter : DeviceBase
    {
        public override DeviceType Type => DeviceType.ProcessEquipment;

        // Core parameters
        public double FeedVolume { get; private set; } // Current feed volume in liters
        public double RetentateTankVolume { get; private set; } // Current retentate volume in liters
        public double PermeateVolume { get; private set; } // Current permeate volume in liters
        public double TransmembranePressure { get; private set; } // TMP in bar
        public double CrossflowRate { get; private set; } // Crossflow rate in L/min
        public double RetentateBackpressure { get; private set; } // Retentate pressure in bar
        public double PermeateFlux { get; private set; } // Permeate flux in LMH (L/m²/hr)
        public double MassTransferCoefficient { get; private set; } // Mass transfer coefficient

        // Membrane parameters
        public double MembraneSurfaceArea { get; private set; } // Membrane area in m²
        public double MolecularWeightCutoff { get; private set; } // MWCO in kDa
        public double MembraneResistance { get; private set; } // Membrane resistance
        public double FoulingLevel { get; private set; } // 0-100%

        // Product parameters
        public double ProductConcentration { get; private set; } // Concentration in g/L
        public double BufferConcentration { get; private set; } // Buffer concentration in mM
        public double ProductYield { get; private set; } // Product yield as percentage

        // Process state
        public TFFOperationMode CurrentMode { get; private set; }
        public TFFProcessState CurrentState { get; private set; }
        public double ProcessTime { get; private set; } // Hours

        // Connected devices
        private PumpController _feedPump;
        private PumpController _retentatePump;
        private ValveController _backpressureValve;
        private PressureSensor _feedPressureSensor;
        private PressureSensor _retentatePressureSensor;
        private PressureSensor _permeatePressureSensor;
        private FlowMeter _feedFlowMeter;
        private FlowMeter _permeateFlowMeter;

        // Operational setpoints
        public double CrossflowRateSetpoint { get; private set; } = 5.0; // L/min
        public double TMPSetpoint { get; private set; } = 1.0; // bar
        public double ConcentrationFactor { get; private set; } = 1.0; // Current CF
        public double TargetConcentrationFactor { get; private set; } = 5.0; // Target CF
        public int DiafiltrationVolumes { get; private set; } = 0; // Current DV
        public int TargetDiafiltrationVolumes { get; private set; } = 7; // Target DV

        // Internal process model parameters
        private double _gelLayerThickness; // Gel layer in micrometers
        private double _polarizationFactor; // Concentration polarization factor
        private double _shearRate; // Shear rate at membrane surface
        private double _foulingRate; // Rate of fouling increase
        private double _temperatureFactor; // Temperature effect on viscosity
        private double _productTransmission; // Product transmission through membrane

        public TangentialFlowFilter(
            string deviceId,
            string name,
            double membraneSurfaceArea,
            double molecularWeightCutoff,
            PumpController feedPump = null,
            PumpController retentatePump = null,
            ValveController backpressureValve = null,
            PressureSensor feedPressureSensor = null,
            PressureSensor retentatePressureSensor = null,
            PressureSensor permeatePressureSensor = null)
            : base(deviceId, name)
        {
            // Initialize membrane parameters
            MembraneSurfaceArea = membraneSurfaceArea;
            MolecularWeightCutoff = molecularWeightCutoff;
            MembraneResistance = 1.0e12; // Initial clean membrane resistance
            FoulingLevel = 0.0;

            // Initialize connected devices
            _feedPump = feedPump;
            _retentatePump = retentatePump;
            _backpressureValve = backpressureValve;
            _feedPressureSensor = feedPressureSensor;
            _retentatePressureSensor = retentatePressureSensor;
            _permeatePressureSensor = permeatePressureSensor;

            // Initialize volumes and operating parameters
            FeedVolume = 0.0;
            RetentateTankVolume = 0.0;
            PermeateVolume = 0.0;
            TransmembranePressure = 0.0;
            CrossflowRate = 0.0;
            RetentateBackpressure = 0.0;
            PermeateFlux = 0.0;
            MassTransferCoefficient = 30.0; // Initial value
            ProductConcentration = 0.0;
            BufferConcentration = 0.0;
            ProductYield = 100.0;

            // Initialize process state
            CurrentMode = TFFOperationMode.Idle;
            CurrentState = TFFProcessState.Ready;
            ProcessTime = 0.0;

            // Initialize internal model parameters
            _gelLayerThickness = 0.0;
            _polarizationFactor = 1.0;
            _shearRate = 0.0;
            _foulingRate = 0.0001; // Base fouling rate per hour
            _temperatureFactor = 1.0; // At standard temperature
            _productTransmission = 0.05; // 5% product loss through membrane initially

            // Update diagnostic data
            DiagnosticData["MembraneSurfaceArea"] = MembraneSurfaceArea;
            DiagnosticData["MWCO"] = MolecularWeightCutoff;
            DiagnosticData["CurrentMode"] = CurrentMode.ToString();
            DiagnosticData["CurrentState"] = CurrentState.ToString();
        }

        public override void Initialize()
        {
            base.Initialize();

            // Initialize connected devices
            _feedPump?.Initialize();
            _retentatePump?.Initialize();
            _backpressureValve?.Initialize();
            _feedPressureSensor?.Initialize();
            _retentatePressureSensor?.Initialize();
            _permeatePressureSensor?.Initialize();
            _feedFlowMeter?.Initialize();
            _permeateFlowMeter?.Initialize();

            // Reset process parameters
            FoulingLevel = 0.0;
            ProcessTime = 0.0;
            CurrentState = TFFProcessState.Ready;
        }

        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);

            if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
                return;

            // Update process time
            if (CurrentState != TFFProcessState.Ready && CurrentState != TFFProcessState.Cleaning)
            {
                ProcessTime += elapsedTime.TotalHours;
            }

            // Update flow rates from connected pumps and valves
            UpdateFlowRatesFromDevices();

            // Calculate transmembrane pressure
            CalculateTransmembranePressure();

            // Calculate flux based on current operating conditions
            CalculateFlux(elapsedTime);

            // Process mode-specific operations
            switch (CurrentMode)
            {
                case TFFOperationMode.Concentration:
                    ProcessConcentrationMode(elapsedTime);
                    break;
                case TFFOperationMode.Diafiltration:
                    ProcessDiafiltrationMode(elapsedTime);
                    break;
                case TFFOperationMode.Recovery:
                    ProcessRecoveryMode(elapsedTime);
                    break;
                case TFFOperationMode.Idle:
                    // No processing when idle
                    break;
            }

            // Update membrane fouling
            UpdateFouling(elapsedTime);

            // Check for alarms
            CheckAlarmConditions();

            // Update diagnostics
            UpdateDiagnostics();
        }

        private void UpdateFlowRatesFromDevices()
        {
            // Update crossflow rate from feed pump if available
            if (_feedPump != null && _feedPump.Status == DeviceStatus.Running)
            {
                CrossflowRate = _feedPump.FlowRate;
            }
            else
            {
                // Simple model for crossflow rate
                double crossflowDiff = CrossflowRateSetpoint - CrossflowRate;
                CrossflowRate += crossflowDiff * 0.2; // Gradual adjustment
            }

            // Update backpressure from valve if available
            if (_backpressureValve != null && _backpressureValve.Status == DeviceStatus.Running)
            {
                // Convert valve position to backpressure
                RetentateBackpressure = 3.0 * (_backpressureValve.Position / 100.0);
            }
        }

        private void CalculateTransmembranePressure()
        {
            // Get feed and retentate pressures from sensors if available
            double feedPressure = 0.0;
            double retentatePressure = 0.0;
            double permeatePressure = 0.0;

            if (_feedPressureSensor != null && _feedPressureSensor.Status == DeviceStatus.Running)
            {
                feedPressure = _feedPressureSensor.Pressure;
            }
            else
            {
                // Estimate feed pressure based on pump flow
                feedPressure = CrossflowRate * 0.3;
            }

            if (_retentatePressureSensor != null && _retentatePressureSensor.Status == DeviceStatus.Running)
            {
                retentatePressure = _retentatePressureSensor.Pressure;
            }
            else
            {
                // Estimate retentate pressure based on backpressure valve
                retentatePressure = RetentateBackpressure;
            }

            if (_permeatePressureSensor != null && _permeatePressureSensor.Status == DeviceStatus.Running)
            {
                permeatePressure = _permeatePressureSensor.Pressure;
            }

            // Calculate TMP = ((P_feed + P_retentate) / 2) - P_permeate
            TransmembranePressure = ((feedPressure + retentatePressure) / 2.0) - permeatePressure;
        }

        private void CalculateFlux(TimeSpan elapsedTime)
        {
            // Calculate shear rate at membrane surface
            _shearRate = 5000 * CrossflowRate / MembraneSurfaceArea;

            // Calculate concentration polarization factor
            // Higher shear reduces polarization, higher conc. factor increases it
            _polarizationFactor = 1.0 + (0.3 * ConcentrationFactor) * (1.0 - Math.Min(1.0, _shearRate / 10000.0));

            // Calculate total membrane resistance including fouling
            double totalResistance = MembraneResistance * (1.0 + (FoulingLevel / 100.0) * 2.0);

            // Flux calculation using Darcy's law: J = TMP / (? * R)
            // Where: J = flux, TMP = transmembrane pressure, ? = viscosity, R = resistance
            double viscosityFactor = 1.0 / _temperatureFactor; // Higher temperature = lower viscosity
            
            // Calculate the flux in LMH (L/m²/hr)
            PermeateFlux = (3600.0 * TransmembranePressure) / (viscosityFactor * totalResistance * _polarizationFactor);

            // Limit flux based on physical constraints
            PermeateFlux = Math.Max(0, Math.Min(150, PermeateFlux)); // Maximum flux ~150 LMH

            // Calculate volume transferred in this time step
            double volumeTransferred = PermeateFlux * MembraneSurfaceArea * elapsedTime.TotalHours;

            // In real operation, we'd update volumes based on mode
            ProcessVolumeTransfers(volumeTransferred);
        }

        private void ProcessVolumeTransfers(double volumeTransferred)
        {
            // Ensure we don't transfer more than available
            volumeTransferred = Math.Min(volumeTransferred, RetentateTankVolume);
            
            if (volumeTransferred <= 0)
                return;
                
            switch (CurrentMode)
            {
                case TFFOperationMode.Concentration:
                    // Move volume from retentate to permeate
                    RetentateTankVolume -= volumeTransferred;
                    PermeateVolume += volumeTransferred;
                    
                    // Update concentration factor
                    if (FeedVolume > 0)
                    {
                        ConcentrationFactor = FeedVolume / RetentateTankVolume;
                    }
                    
                    // Update product concentration
                    ProductConcentration *= (RetentateTankVolume + volumeTransferred) / RetentateTankVolume;
                    break;
                    
                case TFFOperationMode.Diafiltration:
                    // In diafiltration, buffer is added to maintain volume
                    PermeateVolume += volumeTransferred;
                    
                    // Calculate diafiltration volumes
                    DiafiltrationVolumes = (int)(PermeateVolume / RetentateTankVolume);
                    
                    // Update buffer concentration (exponential decay)
                    BufferConcentration *= Math.Exp(-volumeTransferred / RetentateTankVolume);
                    break;
                    
                case TFFOperationMode.Recovery:
                    // Recovery mode - system draining
                    RetentateTankVolume -= volumeTransferred;
                    PermeateVolume += volumeTransferred;
                    break;
            }
            
            // Calculate product loss to permeate
            double productLoss = ProductConcentration * volumeTransferred * _productTransmission;
            ProductYield -= (productLoss / (ProductConcentration * FeedVolume)) * 100.0;
            ProductYield = Math.Max(0, ProductYield);
        }

        private void ProcessConcentrationMode(TimeSpan elapsedTime)
        {
            // Check if concentration target reached
            if (ConcentrationFactor >= TargetConcentrationFactor)
            {
                AddAlarm("CONCENTRATION_COMPLETE", $"Target concentration factor {TargetConcentrationFactor}X reached", AlarmSeverity.Information);
                CurrentState = TFFProcessState.TargetReached;
            }
            
            // Check for excessive concentration (potential dry-running)
            if (RetentateTankVolume < FeedVolume * 0.1) // Less than 10% of feed volume
            {
                AddAlarm("RETENTATE_LOW", "Retentate volume critically low", AlarmSeverity.Major);
            }
        }

        private void ProcessDiafiltrationMode(TimeSpan elapsedTime)
        {
            // Add buffer at the same rate as permeate removal to maintain volume
            double bufferAddition = PermeateFlux * MembraneSurfaceArea * elapsedTime.TotalHours;
            bufferAddition = Math.Min(bufferAddition, 5.0 * elapsedTime.TotalHours); // Limit by buffer pump rate
            
            // Check if diafiltration target reached
            if (DiafiltrationVolumes >= TargetDiafiltrationVolumes)
            {
                AddAlarm("DIAFILTRATION_COMPLETE", $"Target diafiltration volume {TargetDiafiltrationVolumes}DV reached", AlarmSeverity.Information);
                CurrentState = TFFProcessState.TargetReached;
            }
        }

        private void ProcessRecoveryMode(TimeSpan elapsedTime)
        {
            // Check if recovery is complete (system drained)
            if (RetentateTankVolume < 0.1) // Less than 0.1L remaining
            {
                AddAlarm("RECOVERY_COMPLETE", "System recovery complete", AlarmSeverity.Information);
                CurrentState = TFFProcessState.Complete;
            }
        }

        private void UpdateFouling(TimeSpan elapsedTime)
        {
            // Fouling increases with time, flux, and product concentration
            double fluxFactor = PermeateFlux / 50.0; // Normalized flux effect
            double concFactor = ProductConcentration / 10.0; // Normalized concentration effect
            double shearFactor = 1.0 - Math.Min(1.0, _shearRate / 15000.0); // Higher shear reduces fouling
            
            // Calculate fouling increase for this time step
            double foulingIncrease = _foulingRate * elapsedTime.TotalHours * fluxFactor * concFactor * shearFactor;
            FoulingLevel += foulingIncrease;
            
            // Limit fouling level
            FoulingLevel = Math.Min(100.0, FoulingLevel);
            
            // Update gel layer thickness based on fouling
            _gelLayerThickness = FoulingLevel * 0.2; // 0-20 micrometers
        }

        private void CheckAlarmConditions()
        {
            // Check for high TMP
            if (TransmembranePressure > 2.5)
            {
                AddAlarm("HIGH_TMP", $"High transmembrane pressure: {TransmembranePressure:F2} bar", AlarmSeverity.Warning);
            }
            
            // Check for excessive fouling
            if (FoulingLevel > 70.0)
            {
                AddAlarm("EXCESSIVE_FOULING", $"Membrane fouling: {FoulingLevel:F1}%", AlarmSeverity.Warning);
            }
            
            // Check for low flux
            if (CurrentState == TFFProcessState.Running && PermeateFlux < 5.0)
            {
                AddAlarm("LOW_FLUX", $"Low permeate flux: {PermeateFlux:F1} LMH", AlarmSeverity.Minor);
            }
            
            // Check for low product yield
            if (ProductYield < 90.0)
            {
                AddAlarm("YIELD_WARNING", $"Product yield below target: {ProductYield:F1}%", AlarmSeverity.Minor);
            }
        }

        private void UpdateDiagnostics()
        {
            DiagnosticData["FeedVolume"] = FeedVolume;
            DiagnosticData["RetentateTankVolume"] = RetentateTankVolume;
            DiagnosticData["PermeateVolume"] = PermeateVolume;
            DiagnosticData["TMP"] = TransmembranePressure;
            DiagnosticData["CrossflowRate"] = CrossflowRate;
            DiagnosticData["PermeateFlux"] = PermeateFlux;
            DiagnosticData["ConcentrationFactor"] = ConcentrationFactor;
            DiagnosticData["DiafiltrationVolumes"] = DiafiltrationVolumes;
            DiagnosticData["FoulingLevel"] = FoulingLevel;
            DiagnosticData["ProductConcentration"] = ProductConcentration;
            DiagnosticData["ProductYield"] = ProductYield;
            DiagnosticData["CurrentMode"] = CurrentMode.ToString();
            DiagnosticData["CurrentState"] = CurrentState.ToString();
            DiagnosticData["ProcessTime"] = ProcessTime;
        }

        #region Public Control Methods

        /// <summary>
        /// Start a concentration process
        /// </summary>
        public bool StartConcentration(double feedVolume, double initialProductConcentration, double targetConcentrationFactor)
        {
            if (CurrentState != TFFProcessState.Ready && CurrentState != TFFProcessState.Complete)
            {
                AddAlarm("INVALID_STATE", "Cannot start concentration in current state", AlarmSeverity.Warning);
                return false;
            }
            
            // Initialize process parameters
            FeedVolume = feedVolume;
            RetentateTankVolume = feedVolume;
            PermeateVolume = 0.0;
            ProductConcentration = initialProductConcentration;
            BufferConcentration = 0.0;
            TargetConcentrationFactor = targetConcentrationFactor;
            ConcentrationFactor = 1.0;
            DiafiltrationVolumes = 0;
            ProcessTime = 0.0;
            ProductYield = 100.0;
            
            // Set operation mode
            CurrentMode = TFFOperationMode.Concentration;
            CurrentState = TFFProcessState.Running;
            Status = DeviceStatus.Running;
            
            // Start pumps
            _feedPump?.Start();
            _retentatePump?.Start();
            if (_backpressureValve != null)
            {
                _backpressureValve.Start();
                _backpressureValve.SetPosition(50); // Initial valve position
            }
            
            // Update diagnostics
            DiagnosticData["FeedVolume"] = FeedVolume;
            DiagnosticData["ProductConcentration"] = ProductConcentration;
            DiagnosticData["TargetConcentrationFactor"] = TargetConcentrationFactor;
            DiagnosticData["CurrentMode"] = CurrentMode.ToString();
            DiagnosticData["CurrentState"] = CurrentState.ToString();
            
            AddAlarm("CONCENTRATION_STARTED", "Concentration process started", AlarmSeverity.Information);
            return true;
        }

        /// <summary>
        /// Start a diafiltration process
        /// </summary>
        public bool StartDiafiltration(int targetDiafiltrationVolumes, double initialBufferConcentration)
        {
            if (CurrentState != TFFProcessState.TargetReached && CurrentState != TFFProcessState.Complete)
            {
                AddAlarm("INVALID_STATE", "Cannot start diafiltration in current state", AlarmSeverity.Warning);
                return false;
            }
            
            // Initialize diafiltration parameters
            PermeateVolume = 0.0;
            DiafiltrationVolumes = 0;
            TargetDiafiltrationVolumes = targetDiafiltrationVolumes;
            BufferConcentration = initialBufferConcentration;
            ProcessTime = 0.0;
            
            // Set operation mode
            CurrentMode = TFFOperationMode.Diafiltration;
            CurrentState = TFFProcessState.Running;
            Status = DeviceStatus.Running;
            
            // Start pumps if they're not running
            _feedPump?.Start();
            _retentatePump?.Start();
            
            // Update diagnostics
            DiagnosticData["TargetDiafiltrationVolumes"] = TargetDiafiltrationVolumes;
            DiagnosticData["BufferConcentration"] = BufferConcentration;
            DiagnosticData["CurrentMode"] = CurrentMode.ToString();
            DiagnosticData["CurrentState"] = CurrentState.ToString();
            
            AddAlarm("DIAFILTRATION_STARTED", "Diafiltration process started", AlarmSeverity.Information);
            return true;
        }

        /// <summary>
        /// Start the system recovery process
        /// </summary>
        public bool StartRecovery()
        {
            if (CurrentState != TFFProcessState.TargetReached && CurrentState != TFFProcessState.Complete)
            {
                AddAlarm("INVALID_STATE", "Cannot start recovery in current state", AlarmSeverity.Warning);
                return false;
            }
            
            // Initialize recovery parameters
            ProcessTime = 0.0;
            
            // Set operation mode
            CurrentMode = TFFOperationMode.Recovery;
            CurrentState = TFFProcessState.Running;
            
            DiagnosticData["CurrentMode"] = CurrentMode.ToString();
            DiagnosticData["CurrentState"] = CurrentState.ToString();
            
            AddAlarm("RECOVERY_STARTED", "Recovery process started", AlarmSeverity.Information);
            return true;
        }

        /// <summary>
        /// Stop the current process
        /// </summary>
        public void Stop()
        {
            if (CurrentState == TFFProcessState.Running)
            {
                CurrentState = TFFProcessState.Stopped;
                
                // Stop pumps
                _feedPump?.Stop();
                _retentatePump?.Stop();
                
                DiagnosticData["CurrentState"] = CurrentState.ToString();
                AddAlarm("PROCESS_STOPPED", "Process stopped by operator", AlarmSeverity.Information);
            }
        }

        /// <summary>
        /// Pause the current process
        /// </summary>
        public void Pause()
        {
            if (CurrentState == TFFProcessState.Running)
            {
                CurrentState = TFFProcessState.Paused;
                
                // Reduce pump speeds
                if (_feedPump != null)
                    _feedPump.SetSpeed(_feedPump.Speed * 0.2);
                if (_retentatePump != null)
                    _retentatePump.SetSpeed(_retentatePump.Speed * 0.2);
                
                DiagnosticData["CurrentState"] = CurrentState.ToString();
                AddAlarm("PROCESS_PAUSED", "Process paused by operator", AlarmSeverity.Information);
            }
        }

        /// <summary>
        /// Resume the current process
        /// </summary>
        public void Resume()
        {
            if (CurrentState == TFFProcessState.Paused)
            {
                CurrentState = TFFProcessState.Running;
                
                // Restore pump speeds
                if (_feedPump != null)
                    _feedPump.SetSpeed(_feedPump.Speed * 5.0);
                if (_retentatePump != null)
                    _retentatePump.SetSpeed(_retentatePump.Speed * 5.0);
                
                DiagnosticData["CurrentState"] = CurrentState.ToString();
                AddAlarm("PROCESS_RESUMED", "Process resumed by operator", AlarmSeverity.Information);
            }
        }

        /// <summary>
        /// Start clean-in-place process
        /// </summary>
        public void StartCIP()
        {
            // Only allow cleaning when not processing
            if (CurrentState == TFFProcessState.Complete || CurrentState == TFFProcessState.Ready)
            {
                CurrentState = TFFProcessState.Cleaning;
                CurrentMode = TFFOperationMode.Idle;
                
                // Set up optimum cleaning flows
                if (_feedPump != null)
                {
                    _feedPump.Start();
                    _feedPump.SetSpeed(100);
                }
                
                if (_retentatePump != null)
                {
                    _retentatePump.Start();
                    _retentatePump.SetSpeed(0);
                }
                
                if (_backpressureValve != null)
                {
                    _backpressureValve.Start();
                    _backpressureValve.SetPosition(30);
                }
                
                // Reset volumes
                RetentateTankVolume = 5.0; // Cleaning solution volume
                PermeateVolume = 0.0;
                
                DiagnosticData["CurrentState"] = CurrentState.ToString();
                AddAlarm("CLEANING_STARTED", "CIP process started", AlarmSeverity.Information);
            }
            else
            {
                AddAlarm("INVALID_STATE", "Cannot start cleaning in current state", AlarmSeverity.Warning);
            }
        }

        /// <summary>
        /// Complete cleaning and reset system
        /// </summary>
        public void CompleteCleaning()
        {
            if (CurrentState == TFFProcessState.Cleaning)
            {
                // Reset fouling level
                FoulingLevel = Math.Max(0, FoulingLevel - 95.0); // Remove 95% of fouling
                
                // Stop pumps
                _feedPump?.Stop();
                _retentatePump?.Stop();
                
                // Reset system state
                CurrentState = TFFProcessState.Ready;
                CurrentMode = TFFOperationMode.Idle;
                RetentateTankVolume = 0.0;
                PermeateVolume = 0.0;
                
                DiagnosticData["CurrentState"] = CurrentState.ToString();
                DiagnosticData["FoulingLevel"] = FoulingLevel;
                AddAlarm("CLEANING_COMPLETE", "CIP process completed", AlarmSeverity.Information);
            }
        }

        /// <summary>
        /// Set operation parameters
        /// </summary>
        public void SetOperationParameters(double crossflowRate, double tmp)
        {
            CrossflowRateSetpoint = crossflowRate;
            TMPSetpoint = tmp;
            
            // Update pumps and valves to achieve setpoints
            if (_feedPump != null)
            {
                _feedPump.SetSpeed(crossflowRate * 20.0); // Convert to pump speed
            }
            
            if (_backpressureValve != null)
            {
                // Calculate valve position to achieve TMP
                double valvePosition = tmp * 30.0; // Simplified conversion
                _backpressureValve.SetPosition(valvePosition);
            }
            
            DiagnosticData["CrossflowRateSetpoint"] = CrossflowRateSetpoint;
            DiagnosticData["TMPSetpoint"] = TMPSetpoint;
        }

        #endregion

        protected override void SimulateFault()
        {
            int faultType = Random.Next(6);
            
            switch (faultType)
            {
                case 0: // Membrane rupture
                    AddAlarm("MEMBRANE_RUPTURE", "Possible membrane integrity failure", AlarmSeverity.Critical);
                    _productTransmission = 0.5; // 50% product loss
                    ProductYield -= 10;
                    break;
                
                case 1: // Excessive pressure
                    AddAlarm("EXCESSIVE_PRESSURE", "Pressure exceeds membrane limits", AlarmSeverity.Major);
                    TransmembranePressure = 3.5;
                    FoulingLevel = Math.Min(100, FoulingLevel + 20);
                    break;
                
                case 2: // Pump cavitation
                    AddAlarm("PUMP_CAVITATION", "Feed pump cavitation detected", AlarmSeverity.Major);
                    if (_feedPump != null)
                    {
                        _feedPump.SetSpeed(_feedPump.Speed * 0.5);
                    }
                    CrossflowRate *= 0.5;
                    break;
                
                case 3: // Flow path obstruction
                    AddAlarm("FLOW_OBSTRUCTION", "Partial flow path obstruction detected", AlarmSeverity.Minor);
                    FoulingLevel = Math.Min(100, FoulingLevel + 15);
                    PermeateFlux *= 0.6;
                    break;
                
                case 4: // Leak detection
                    AddAlarm("PERMEATE_LEAK", "Possible leak in permeate line", AlarmSeverity.Major);
                    PermeateVolume *= 0.9; // 10% permeate loss
                    break;
                
                case 5: // Control system issue
                    AddAlarm("CONTROL_ISSUE", "TMP control unstable", AlarmSeverity.Warning);
                    TransmembranePressure = TMPSetpoint * (1.0 + (Random.NextDouble() - 0.5));
                    break;
            }
        }
    }

    public enum TFFOperationMode
    {
        Idle,
        Concentration,
        Diafiltration,
        Recovery
    }

    public enum TFFProcessState
    {
        Ready,
        Running,
        Paused,
        Stopped,
        TargetReached,
        Complete,
        Cleaning
    }
}