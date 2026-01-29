using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaVax.HardwareComponents.Controllers
{
    /// <summary>
    /// Controls fluid pumps in pharmaceutical process equipment
    /// </summary>
    public class PumpController : DeviceBase
    {
        public override DeviceType Type => DeviceType.Actuator;

        // Pump configuration properties
        public PumpType PumpType { get; private set; }
        public double MaxFlowRate { get; private set; }        // Maximum flow rate in L/min
        public double MaxPressure { get; private set; }        // Maximum pressure in bar
        
        // Operating state properties
        public double Speed { get; private set; }              // Current speed as percentage (0-100%)
        public double FlowRate { get; private set; }           // Current flow rate in L/min
        public double DischargePressure { get; private set; }  // Current discharge pressure in bar
        public double SuctionPressure { get; private set; }    // Current suction pressure in bar
        public double DifferentialPressure => DischargePressure - SuctionPressure;
        public PumpDirection Direction { get; private set; }   // Current flow direction
        public bool IsCavitating { get; private set; }         // Whether pump is cavitating

        // Internal state variables
        private double _targetSpeed;                           // Target speed in percentage
        private double _rampRate;                              // Speed change rate in %/second
        private double _pumpEfficiency;                        // Current efficiency as percentage
        private double _energyConsumption;                     // kWh
        private double _viscosity;                             // Fluid viscosity in cP
        private double _specificGravity;                       // Fluid specific gravity
        private double _systemResistance;                      // System resistance coefficient
        private double _operatingTime;                         // Cumulative operating hours
        private bool _isPrimed;                                // Whether pump is primed
        
        /// <summary>
        /// Creates a new pump controller
        /// </summary>
        /// <param name="deviceId">Unique device identifier</param>
        /// <param name="name">Human-readable device name</param>
        /// <param name="pumpType">Type of pump</param>
        /// <param name="maxFlowRate">Maximum flow rate in L/min</param>
        /// <param name="maxPressure">Maximum pressure in bar</param>
        public PumpController(
            string deviceId, 
            string name, 
            PumpType pumpType = PumpType.Centrifugal,
            double maxFlowRate = 100.0, 
            double maxPressure = 6.0)
            : base(deviceId, name)
        {
            PumpType = pumpType;
            MaxFlowRate = maxFlowRate;
            MaxPressure = maxPressure;
            
            // Initialize state variables
            Speed = 0;
            _targetSpeed = 0;
            FlowRate = 0;
            DischargePressure = 0;
            SuctionPressure = 0;
            Direction = PumpDirection.Forward;
            IsCavitating = false;
            
            // Initialize internal parameters based on pump type
            ConfigurePumpParameters(pumpType);
            
            // Initialize diagnostics
            InitializeDiagnostics();
        }
        
        private void ConfigurePumpParameters(PumpType pumpType)
        {
            switch (pumpType)
            {
                case PumpType.Centrifugal:
                    _rampRate = 10.0;           // 10% per second
                    _pumpEfficiency = 75.0;     // 75% efficiency
                    _systemResistance = 0.1;    // Low resistance coefficient
                    break;
                    
                case PumpType.PositiveDisplacement:
                    _rampRate = 5.0;            // 5% per second
                    _pumpEfficiency = 85.0;     // 85% efficiency
                    _systemResistance = 0.05;   // Very low resistance coefficient
                    break;
                    
                case PumpType.Peristaltic:
                    _rampRate = 20.0;           // 20% per second
                    _pumpEfficiency = 60.0;     // 60% efficiency
                    _systemResistance = 0.2;    // Higher resistance coefficient
                    break;
                    
                case PumpType.Diaphragm:
                    _rampRate = 15.0;           // 15% per second
                    _pumpEfficiency = 70.0;     // 70% efficiency
                    _systemResistance = 0.15;   // Moderate resistance
                    break;
                    
                default:
                    _rampRate = 10.0;
                    _pumpEfficiency = 70.0;
                    _systemResistance = 0.1;
                    break;
            }
            
            // Initialize fluid properties with defaults
            _viscosity = 1.0;         // Water equivalent
            _specificGravity = 1.0;   // Water equivalent
            _isPrimed = true;         // Assume primed initially
        }
        
        private void InitializeDiagnostics()
        {
            DiagnosticData["PumpType"] = PumpType.ToString();
            DiagnosticData["MaxFlowRate"] = MaxFlowRate;
            DiagnosticData["MaxPressure"] = MaxPressure;
            DiagnosticData["Speed"] = Speed;
            DiagnosticData["FlowRate"] = FlowRate;
            DiagnosticData["DischargePressure"] = DischargePressure;
            DiagnosticData["SuctionPressure"] = SuctionPressure;
            DiagnosticData["DifferentialPressure"] = DifferentialPressure;
            DiagnosticData["Direction"] = Direction.ToString();
            DiagnosticData["IsCavitating"] = IsCavitating;
            DiagnosticData["Efficiency"] = _pumpEfficiency;
            DiagnosticData["EnergyConsumption"] = _energyConsumption;
            DiagnosticData["OperatingTime"] = _operatingTime;
            DiagnosticData["IsPrimed"] = _isPrimed;
        }
        
        public override void Initialize()
        {
            base.Initialize();
            
            // Reset operating parameters
            Speed = 0;
            _targetSpeed = 0;
            FlowRate = 0;
            DischargePressure = 0;
            SuctionPressure = 0;
            IsCavitating = false;
            
            // Update diagnostics
            UpdateDiagnostics();
        }
        
        public override bool Start()
        {
            if (base.Start())
            {
                // Apply minimum speed to start movement
                if (_targetSpeed < 5.0)
                {
                    _targetSpeed = 5.0;
                }
                
                // Check if pump is primed
                if (!_isPrimed && PumpType != PumpType.SelfPriming)
                {
                    AddAlarm("PRIMING_REQUIRED", "Pump requires priming before operation", AlarmSeverity.Warning);
                    Status = DeviceStatus.Warning;
                }
                
                AddAlarm("PUMP_STARTED", $"Pump {Name} started", AlarmSeverity.Information);
                return true;
            }
            
            return false;
        }
        
        public override bool Stop()
        {
            if (base.Stop())
            {
                _targetSpeed = 0;
                AddAlarm("PUMP_STOPPED", $"Pump {Name} stopped", AlarmSeverity.Information);
                return true;
            }
            
            return false;
        }
        
        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);
            
            if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
                return;
                
            // Update operating time
            _operatingTime += elapsedTime.TotalHours;
            
            // Gradually move speed toward target with ramp rate limitation
            double speedStep = _rampRate * elapsedTime.TotalSeconds;
            if (Math.Abs(Speed - _targetSpeed) <= speedStep)
            {
                Speed = _targetSpeed;
            }
            else
            {
                Speed += Math.Sign(_targetSpeed - Speed) * speedStep;
            }
            
            // Cap speed between 0-100%
            Speed = Math.Max(0, Math.Min(100, Speed));
            
            // Calculate flow rate based on pump type and speed
            CalculateFlowRate();
            
            // Calculate pressures
            CalculatePressures();
            
            // Check for cavitation
            CheckCavitation();
            
            // Calculate energy consumption
            CalculateEnergyConsumption(elapsedTime);
            
            // Update diagnostics
            UpdateDiagnostics();
        }
        
        private void CalculateFlowRate()
        {
            // Base calculation of flow rate is linear with speed for positive displacement pumps
            if (PumpType == PumpType.PositiveDisplacement || PumpType == PumpType.Peristaltic)
            {
                FlowRate = Speed / 100.0 * MaxFlowRate;
            }
            // Non-linear relationship for centrifugal pumps (approximation of affinity laws)
            else if (PumpType == PumpType.Centrifugal)
            {
                // Flow is approximately proportional to speed
                FlowRate = Speed / 100.0 * MaxFlowRate;
                
                // Add system curve effect - flow decreases as pressure increases
                double pressureFactor = 1.0 - DischargePressure / MaxPressure * 0.2;
                FlowRate *= Math.Max(0.1, pressureFactor);
            }
            // Diaphragm pumps have characteristics in between
            else
            {
                FlowRate = Speed / 100.0 * MaxFlowRate;
                double pressureFactor = 1.0 - DischargePressure / MaxPressure * 0.1;
                FlowRate *= Math.Max(0.5, pressureFactor);
            }
            
            // Direction affects flow rate sign
            if (Direction == PumpDirection.Reverse)
            {
                FlowRate *= -1;
            }
            
            // If not primed, flow rate is severely reduced
            if (!_isPrimed && PumpType != PumpType.SelfPriming)
            {
                FlowRate *= 0.1; // 90% reduction in flow
            }
            
            // Account for viscosity effects
            if (_viscosity > 1.0) 
            {
                // Higher viscosity reduces flow rate
                double viscosityFactor = Math.Pow(1.0 / _viscosity, 0.2);
                FlowRate *= viscosityFactor;
            }
        }
        
        private void CalculatePressures()
        {
            // Calculate discharge pressure
            if (PumpType == PumpType.Centrifugal)
            {
                // Pressure increases with square of speed in centrifugal pumps
                DischargePressure = Math.Pow(Speed / 100.0, 2) * MaxPressure;
                
                // System resistance affects pressure as flow increases
                DischargePressure += FlowRate * FlowRate * _systemResistance;
            }
            else
            {
                // Positive displacement pumps can maintain pressure regardless of speed
                DischargePressure = Math.Max(1.0, Speed / 100.0) * MaxPressure;
            }
            
            // Simulate some inlet pressure (typically low)
            SuctionPressure = 0.1 + Random.NextDouble() * 0.1;
            
            // Cavitation reduces discharge pressure
            if (IsCavitating)
            {
                DischargePressure *= 0.7; // 30% reduction
            }
            
            // Ensure we don't exceed maximum pump pressure
            DischargePressure = Math.Min(DischargePressure, MaxPressure);
        }
        
        private void CheckCavitation()
        {
            // Risk factors for cavitation:
            // 1. Low suction pressure
            // 2. High speed
            // 3. High fluid temperature (implied in viscosity)
            
            // Calculate cavitation risk
            double cavitationRisk = 0;
            
            if (SuctionPressure < 0.2)
                cavitationRisk += 0.3;
                
            if (Speed > 90)
                cavitationRisk += 0.3;
                
            if (_viscosity < 0.7) // Low viscosity often implies higher temperature
                cavitationRisk += 0.2;
                
            // Random element for simulation
            cavitationRisk += Random.NextDouble() * 0.2;
            
            // Determine if cavitation is occurring
            bool wasCavitating = IsCavitating;
            IsCavitating = cavitationRisk > 0.7 && Status == DeviceStatus.Running;
            
            // Create alarm if cavitation begins
            if (!wasCavitating && IsCavitating)
            {
                AddAlarm("CAVITATION", "Pump cavitation detected", AlarmSeverity.Warning);
                Status = DeviceStatus.Warning;
            }
            else if (wasCavitating && !IsCavitating)
            {
                AddAlarm("CAVITATION_CLEARED", "Pump cavitation cleared", AlarmSeverity.Information);
                Status = DeviceStatus.Running;
            }
        }
        
        private void CalculateEnergyConsumption(TimeSpan elapsedTime)
        {
            // Power is proportional to flow rate and pressure
            double powerFactor = 0.0;
            
            switch (PumpType)
            {
                case PumpType.Centrifugal:
                    // Power ∝ ρQH (density × flow × head)
                    powerFactor = 0.5; // kW per (100 L/min at 1 bar)
                    break;
                    
                case PumpType.PositiveDisplacement:
                    // Linear with flow, less dependent on pressure
                    powerFactor = 0.4;
                    break;
                    
                case PumpType.Peristaltic:
                    // Generally less efficient
                    powerFactor = 0.6;
                    break;
                    
                case PumpType.Diaphragm:
                    powerFactor = 0.45;
                    break;
                    
                default:
                    powerFactor = 0.5;
                    break;
            }
            
            // Calculate instantaneous power in kW
            double power = FlowRate / 100.0 * DischargePressure * powerFactor;
            
            // Adjust for efficiency
            power = power * (100.0 / _pumpEfficiency);
            
            // Add base power for electronics
            power += 0.1; // 100W base load
            
            // Calculate energy in kWh
            _energyConsumption += power * elapsedTime.TotalHours;
        }
        
        private void UpdateDiagnostics()
        {
            DiagnosticData["Speed"] = Speed;
            DiagnosticData["TargetSpeed"] = _targetSpeed;
            DiagnosticData["FlowRate"] = FlowRate;
            DiagnosticData["DischargePressure"] = DischargePressure;
            DiagnosticData["SuctionPressure"] = SuctionPressure;
            DiagnosticData["DifferentialPressure"] = DifferentialPressure;
            DiagnosticData["Direction"] = Direction.ToString();
            DiagnosticData["IsCavitating"] = IsCavitating;
            DiagnosticData["Efficiency"] = _pumpEfficiency;
            DiagnosticData["EnergyConsumption"] = _energyConsumption;
            DiagnosticData["OperatingTime"] = _operatingTime;
        }
        
        #region Public Control Methods
        
        /// <summary>
        /// Sets the pump speed as a percentage of maximum speed
        /// </summary>
        /// <param name="speed">Speed percentage (0-100%)</param>
        public void SetSpeed(double speed)
        {
            _targetSpeed = Math.Max(0, Math.Min(100, speed));
            DiagnosticData["TargetSpeed"] = _targetSpeed;
            
            // If pump is running at very low speed, log information
            if (Status == DeviceStatus.Running && _targetSpeed < 10 && _targetSpeed > 0)
            {
                AddAlarm("LOW_SPEED", "Pump operating at low speed", AlarmSeverity.Information);
            }
            
            // If pump is stopped but speed is being set, automatically start it
            if (Status == DeviceStatus.Ready && _targetSpeed > 0)
            {
                Start();
            }
        }
        
        /// <summary>
        /// Sets the desired flow rate, and automatically adjusts pump speed
        /// </summary>
        /// <param name="flowRate">Target flow rate in L/min</param>
        public void SetFlowRate(double flowRate)
        {
            // Limit to maximum flow
            double targetFlow = Math.Max(0, Math.Min(MaxFlowRate, flowRate));
            
            // Estimate required speed to achieve flow rate
            double requiredSpeed = 0;
            
            if (PumpType == PumpType.Centrifugal)
            {
                // Account for discharge pressure in centrifugal pumps
                double pressureFactor = 1.0 + DischargePressure / MaxPressure * 0.2;
                requiredSpeed = targetFlow / MaxFlowRate * 100.0 * pressureFactor;
            }
            else
            {
                // Linear relationship for positive displacement pumps
                requiredSpeed = targetFlow / MaxFlowRate * 100.0;
            }
            
            // Set the speed
            SetSpeed(requiredSpeed);
            
            DiagnosticData["TargetFlowRate"] = targetFlow;
        }
        
        /// <summary>
        /// Sets the pump direction
        /// </summary>
        /// <param name="direction">Flow direction</param>
        public void SetDirection(PumpDirection direction)
        {
            // Only change direction when pump is stopped or at low speed
            if (Speed < 10 || Status != DeviceStatus.Running)
            {
                Direction = direction;
                DiagnosticData["Direction"] = Direction.ToString();
                
                if (Speed >= 10)
                {
                    AddAlarm("DIRECTION_CHANGE", "Pump direction changed while running", AlarmSeverity.Warning);
                }
            }
            else
            {
                AddAlarm("DIRECTION_DENIED", "Cannot change direction at high speed", AlarmSeverity.Warning);
            }
        }
        
        /// <summary>
        /// Primes the pump to ensure proper fluid flow
        /// </summary>
        /// <returns>True if priming was successful</returns>
        public bool Prime()
        {
            if (Status == DeviceStatus.Running && Speed > 30)
            {
                // Only prime if running at decent speed
                _isPrimed = true;
                DiagnosticData["IsPrimed"] = _isPrimed;
                AddAlarm("PRIMING_COMPLETE", "Pump successfully primed", AlarmSeverity.Information);
                
                if (Status == DeviceStatus.Warning && !IsCavitating)
                {
                    Status = DeviceStatus.Running;
                }
                
                return true;
            }
            
            AddAlarm("PRIMING_FAILED", "Pump priming failed - insufficient speed", AlarmSeverity.Minor);
            return false;
        }
        
        /// <summary>
        /// Sets fluid properties for accurate simulation
        /// </summary>
        /// <param name="viscosity">Fluid viscosity in centipoise (water = 1.0)</param>
        /// <param name="specificGravity">Fluid specific gravity (water = 1.0)</param>
        public void SetFluidProperties(double viscosity, double specificGravity)
        {
            _viscosity = Math.Max(0.1, viscosity);
            _specificGravity = Math.Max(0.1, specificGravity);
            
            DiagnosticData["Viscosity"] = _viscosity;
            DiagnosticData["SpecificGravity"] = _specificGravity;
            
            // High viscosity warning
            if (_viscosity > 50.0 && PumpType == PumpType.Centrifugal)
            {
                AddAlarm("HIGH_VISCOSITY", "Fluid viscosity exceeds recommended range for centrifugal pump", AlarmSeverity.Warning);
            }
        }
        
        #endregion
        
        protected override void SimulateFault()
        {
            if (Status == DeviceStatus.Fault)
                return;
            
            int faultType = Random.Next(5);
            
            switch (faultType)
            {
                case 0: // Seal leak
                    AddAlarm("SEAL_LEAK", "Possible pump seal leak detected", AlarmSeverity.Warning);
                    _pumpEfficiency *= 0.9;
                    Status = DeviceStatus.Warning;
                    break;
                    
                case 1: // Bearing failure
                    AddAlarm("BEARING_ISSUE", "Pump bearing wear detected", AlarmSeverity.Major);
                    _pumpEfficiency *= 0.7;
                    Status = DeviceStatus.Fault;
                    break;
                    
                case 2: // Flow restriction
                    AddAlarm("FLOW_RESTRICTION", "Flow restriction detected", AlarmSeverity.Warning);
                    _systemResistance *= 2.0;
                    break;
                    
                case 3: // Motor overload
                    AddAlarm("MOTOR_OVERLOAD", "Pump motor overload", AlarmSeverity.Major);
                    Speed *= 0.5;
                    _targetSpeed *= 0.5;
                    Status = DeviceStatus.Fault;
                    break;
                    
                case 4: // Loss of prime
                    AddAlarm("PRIME_LOST", "Pump lost prime", AlarmSeverity.Warning);
                    _isPrimed = false;
                    Status = DeviceStatus.Warning;
                    break;
            }
        }
    }
    
    public enum PumpType
    {
        Centrifugal,
        PositiveDisplacement,
        Peristaltic,
        Diaphragm,
        SelfPriming
    }
    
    public enum PumpDirection
    {
        Forward,
        Reverse
    }
}