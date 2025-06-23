using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
    public class FlowMeter : DeviceBase
    {
        public override DeviceType Type => DeviceType.Sensor;
        
        public double FlowRate { get; private set; } // Current flow rate
        public double TotalFlow { get; private set; } // Accumulated total flow
        public double MinFlow { get; private set; } // Minimum flow rate
        public double MaxFlow { get; private set; } // Maximum flow rate
        public double Accuracy { get; private set; } // Accuracy in % of reading
        public FlowMeterType MeterType { get; private set; }
        public string FlowUnits { get; private set; } // Units for flow rate (e.g., "L/min", "kg/h")
        public string TotalUnits { get; private set; } // Units for total flow (e.g., "L", "kg")
        
        // Internal variables
        private double _actualFlow; // The "real" flow (for simulation)
        private double _damping; // Damping in seconds
        private double _zeroStability; // Zero stability as % of max flow
        private double _densityFactor; // For mass-based flow meters
        private double _flowNoise; // Random noise level
        private bool _bidirectional; // Whether flow meter can measure reverse flow
        private bool _isZeroed; // Whether flow meter is properly zeroed
        
        // Internal state
        private double _lastFlowRate;
        private DateTime _lastUpdateTime;
        
        public FlowMeter(
            string deviceId, 
            string name,
            FlowMeterType meterType,
            double minFlow,
            double maxFlow, 
            string flowUnits = "L/min",
            string totalUnits = "L",
            double accuracy = 0.5,
            bool bidirectional = false)
            : base(deviceId, name)
        {
            MeterType = meterType;
            MinFlow = minFlow;
            MaxFlow = maxFlow;
            FlowUnits = flowUnits;
            TotalUnits = totalUnits;
            Accuracy = accuracy;
            _bidirectional = bidirectional;
            
            // Initialize flow values
            FlowRate = 0;
            _actualFlow = 0;
            TotalFlow = 0;
            
            // Set sensor-specific defaults
            switch (MeterType)
            {
                case FlowMeterType.Magnetic:
                    _damping = 1.0;
                    _zeroStability = 0.1; // % of max flow
                    _flowNoise = 0.05; // % of reading
                    break;
                case FlowMeterType.Coriolis:
                    _damping = 0.5;
                    _zeroStability = 0.05;
                    _flowNoise = 0.02;
                    _densityFactor = 1.0; // Normalized density
                    break;
                case FlowMeterType.ThermalMass:
                    _damping = 2.0;
                    _zeroStability = 0.2;
                    _flowNoise = 0.1;
                    break;
                case FlowMeterType.Vortex:
                    _damping = 0.3;
                    _zeroStability = 0.15;
                    _flowNoise = 0.07;
                    break;
                default:
                    _damping = 1.0;
                    _zeroStability = 0.2;
                    _flowNoise = 0.1;
                    break;
            }
            
            _isZeroed = true;
            _lastFlowRate = 0;
            _lastUpdateTime = DateTime.Now;
            
            DiagnosticData["MeterType"] = MeterType.ToString();
            DiagnosticData["Range"] = $"{MinFlow} to {MaxFlow} {FlowUnits}";
            DiagnosticData["Accuracy"] = $"±{Accuracy}% of reading";
            DiagnosticData["FlowUnits"] = FlowUnits;
            DiagnosticData["TotalUnits"] = TotalUnits;
            DiagnosticData["Damping"] = _damping;
            DiagnosticData["ZeroStability"] = _zeroStability;
            DiagnosticData["Bidirectional"] = _bidirectional;
        }

        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);
            
            if (Status != DeviceStatus.Running)
                return;
            
            // Apply damping to flow changes
            double dampingFactor = 1.0 - Math.Exp(-elapsedTime.TotalSeconds / _damping);
            double dampedFlow = _lastFlowRate + (_actualFlow - _lastFlowRate) * dampingFactor;
            
            // Apply zero stability effects
            if (Math.Abs(_actualFlow) < (_zeroStability / 100.0) * MaxFlow)
            {
                // Below zero stability threshold, flow should read as zero
                dampedFlow = 0;
            }
            
            // Apply calibration error
            double span = MaxFlow - MinFlow;
            double calibrationError = 0;
            
            if (!_isZeroed)
            {
                calibrationError = (_zeroStability / 100.0) * MaxFlow; // Zero error
            }
            
            // Apply accuracy error (proportional to reading, not full span)
            double accuracyError = (Random.NextDouble() * 2 - 1) * (Math.Abs(dampedFlow) * Accuracy / 100.0);
            
            // Apply random noise
            double noise = (Random.NextDouble() * 2 - 1) * (_flowNoise / 100.0) * Math.Abs(dampedFlow);
            
            // Special considerations for specific meter types
            switch (MeterType)
            {
                case FlowMeterType.Magnetic:
                    // Magnetic flow meters can have issues with low conductivity or empty pipe
                    if (Random.NextDouble() < 0.001) // 0.1% chance
                    {
                        AddAlarm("CONDUCTIVITY_LOW", "Low conductivity affecting measurement", AlarmSeverity.Warning);
                        noise *= 3; // Triple the noise
                    }
                    break;
                    
                case FlowMeterType.Coriolis:
                    // Apply density effects for Coriolis
                    dampedFlow *= _densityFactor;
                    
                    // Air entrainment effects
                    if (Random.NextDouble() < 0.0005) // 0.05% chance
                    {
                        AddAlarm("AIR_ENTRAINMENT", "Air entrainment detected", AlarmSeverity.Minor);
                        noise *= 5; // Five times the noise with lots of variation
                    }
                    break;
                    
                case FlowMeterType.ThermalMass:
                    // Thermal mass can have pressure/temperature effects
                    if (Random.NextDouble() < 0.001) // 0.1% chance
                    {
                        AddAlarm("PRESSURE_FLUCTUATION", "Pressure fluctuation affecting measurement", AlarmSeverity.Warning);
                        accuracyError *= 2; // Double the accuracy error
                    }
                    break;
            }
            
            // Calculate final flow value
            FlowRate = dampedFlow + calibrationError + accuracyError + noise;
            
            // If not bidirectional and flow is negative, clip to zero
            if (!_bidirectional && FlowRate < 0)
            {
                FlowRate = 0;
            }
            
            // Calculate totalization
            double avgFlow = (_lastFlowRate + FlowRate) / 2; // Trapezoidal approximation
            double volumeChange = avgFlow * elapsedTime.TotalHours; // Flow rate per hour * time in hours
            
            // Update totalizer (only accumulate positive flow)
            if (volumeChange > 0)
            {
                TotalFlow += volumeChange;
            }
            else if (_bidirectional && volumeChange < 0)
            {
                TotalFlow += volumeChange; // Allow negative accumulation for bidirectional meters
            }
            
            // Store current flow for next update
            _lastFlowRate = FlowRate;
            _lastUpdateTime = DateTime.Now;
            
            // Update diagnostic data
            DiagnosticData["FlowRate"] = FlowRate;
            DiagnosticData["TotalFlow"] = TotalFlow;
            
            // Check for abnormal conditions
            double flowPercent = (FlowRate / MaxFlow) * 100;
            
            if (flowPercent > 100)
            {
                AddAlarm("FLOW_OVERRANGE", $"Flow exceeds maximum range ({flowPercent:F1}%)", AlarmSeverity.Minor);
            }
            else if (flowPercent > 95)
            {
                AddAlarm("FLOW_NEAR_MAX", $"Flow near maximum range ({flowPercent:F1}%)", AlarmSeverity.Warning);
            }
            
            // Check for rapid flow changes
            double flowChange = Math.Abs(FlowRate - _lastFlowRate) / (MaxFlow - MinFlow);
            if (flowChange > 0.2) // 20% of span change in one cycle
            {
                AddAlarm("RAPID_FLOW_CHANGE", $"Rapid flow rate change detected", AlarmSeverity.Information);
            }
        }
        
        /// <summary>
        /// Set the actual process flow (for simulation)
        /// </summary>
        public void SetProcessFlow(double flow)
        {
            _actualFlow = flow;
        }
        
        /// <summary>
        /// Reset the totalizer
        /// </summary>
        public void ResetTotalizer()
        {
            TotalFlow = 0;
        }
        
        /// <summary>
        /// Set the damping time in seconds
        /// </summary>
        public void SetDamping(double seconds)
        {
            _damping = Math.Max(0, Math.Min(60, seconds)); // Limit to 0-60 seconds range
            DiagnosticData["Damping"] = _damping;
        }
        
        /// <summary>
        /// For Coriolis meters, set the fluid density factor
        /// </summary>
        public void SetDensityFactor(double factor)
        {
            if (MeterType == FlowMeterType.Coriolis)
            {
                _densityFactor = Math.Max(0.1, Math.Min(5.0, factor)); // Limit to realistic range
                DiagnosticData["DensityFactor"] = _densityFactor;
            }
        }
        
        /// <summary>
        /// Perform a zero calibration
        /// </summary>
        public void PerformZeroCalibration()
        {
            if (Math.Abs(_actualFlow) < (0.02 * MaxFlow)) // Must have near-zero flow to calibrate
            {
                _isZeroed = true;
                AddAlarm("ZERO_CALIBRATION", "Zero calibration successful", AlarmSeverity.Information);
            }
            else
            {
                _isZeroed = false;
                AddAlarm("ZERO_CALIBRATION_FAILED", "Zero calibration failed - flow must be stopped", AlarmSeverity.Warning);
            }
            DiagnosticData["IsZeroed"] = _isZeroed;
        }

        protected override void SimulateFault()
        {
            int faultType = Random.Next(4);
            
            switch (faultType)
            {
                case 0: // Sensor disconnection
                    AddAlarm("SENSOR_DISCONNECT", "Flow meter disconnected", AlarmSeverity.Major);
                    FlowRate = 0;
                    break;
                    
                case 1: // Meter fouling (affects reading)
                    AddAlarm("METER_FOULING", "Flow meter fouling detected", AlarmSeverity.Minor);
                    FlowRate *= 0.9; // 10% low reading due to fouling
                    break;
                    
                case 2: // Empty pipe or low flow cutoff issue
                    AddAlarm("EMPTY_PIPE", "Empty pipe condition detected", AlarmSeverity.Warning);
                    FlowRate = 0;
                    break;
                    
                case 3:
                    // Meter type specific faults
                    switch (MeterType)
                    {
                        case FlowMeterType.Magnetic:
                            AddAlarm("COIL_FAULT", "Excitation coil fault", AlarmSeverity.Major);
                            FlowRate = Random.NextDouble() * MaxFlow; // Random incorrect reading
                            break;
                            
                        case FlowMeterType.Coriolis:
                            AddAlarm("TUBE_VIBRATION", "Abnormal tube vibration", AlarmSeverity.Warning);
                            FlowRate *= 1.2; // 20% high reading
                            break;
                            
                        case FlowMeterType.ThermalMass:
                            AddAlarm("SENSOR_DRIFT", "Thermal sensor drift", AlarmSeverity.Minor);
                            FlowRate *= 0.8; // 20% low reading
                            break;
                            
                        case FlowMeterType.Vortex:
                            AddAlarm("PULSATION", "Flow pulsation affecting measurement", AlarmSeverity.Warning);
                            FlowRate *= (1.0 + 0.3 * Math.Sin(DateTime.Now.Second)); // Pulsating reading
                            break;
                    }
                    break;
            }
        }
    }

    public enum FlowMeterType
    {
        Magnetic,
        Coriolis,
        ThermalMass,
        Vortex,
        Ultrasonic,
        DifferentialPressure,
        PositiveDisplacement
    }
}

