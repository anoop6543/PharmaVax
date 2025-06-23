using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
    public class LevelTransmitter : DeviceBase
    {
        public override DeviceType Type => DeviceType.Sensor;
        
        public double Level { get; private set; } // Current level (0-100%)
        public double ActualLevel { get; private set; } // Physical level for simulation
        public double RawLevel { get; private set; } // Raw measured level
        public double MaxRange { get; private set; } // Maximum measurable level (e.g., in mm)
        public double Accuracy { get; private set; } // Accuracy in % of span
        public LevelTransmitterType TransmitterType { get; private set; }
        
        // Configuration
        private double _damping; // Damping in seconds
        private double _noiseFactor; // Normal noise as % of span
        private double _hysteresis; // Hysteresis in % of span
        private double _driftFactor; // Drift per month as % of span
        private DateTime _lastCalibration;
        
        // Special features
        private bool _hasHartSupport;
        private bool _hasMultipleEchoes; // For radar/ultrasonic sensors
        private bool _hasFoamDetection;
        private bool _hasTemperatureCompensation;
        
        // State variables
        private double _accumulatedDrift;
        private double _lastMeasurement;
        private bool _foamPresent;
        private double _processTemperature;
        
        public LevelTransmitter(
            string deviceId, 
            string name,
            LevelTransmitterType transmitterType,
            double maxRange,
            double accuracy = 0.5)
            : base(deviceId, name)
        {
            TransmitterType = transmitterType;
            MaxRange = maxRange;
            Accuracy = accuracy;
            
            // Set defaults based on transmitter type
            switch (transmitterType)
            {
                case LevelTransmitterType.Radar:
                    _damping = 2.0;
                    _noiseFactor = 0.05;
                    _hysteresis = 0.1;
                    _driftFactor = 0.02;
                    _hasHartSupport = true;
                    _hasMultipleEchoes = true;
                    _hasFoamDetection = true;
                    _hasTemperatureCompensation = true;
                    break;
                case LevelTransmitterType.Ultrasonic:
                    _damping = 1.0;
                    _noiseFactor = 0.1;
                    _hysteresis = 0.2;
                    _driftFactor = 0.05;
                    _hasHartSupport = true;
                    _hasMultipleEchoes = true;
                    _hasFoamDetection = false;
                    _hasTemperatureCompensation = true;
                    break;
                case LevelTransmitterType.Hydrostatic:
                    _damping = 0.5;
                    _noiseFactor = 0.02;
                    _hysteresis = 0.05;
                    _driftFactor = 0.1;
                    _hasHartSupport = true;
                    _hasMultipleEchoes = false;
                    _hasFoamDetection = false;
                    _hasTemperatureCompensation = false;
                    break;
                case LevelTransmitterType.Capacitive:
                    _damping = 0.2;
                    _noiseFactor = 0.07;
                    _hysteresis = 0.15;
                    _driftFactor = 0.15;
                    _hasHartSupport = true;
                    _hasMultipleEchoes = false;
                    _hasFoamDetection = false;
                    _hasTemperatureCompensation = false;
                    break;
                case LevelTransmitterType.LoadCell:
                    _damping = 0.1;
                    _noiseFactor = 0.01;
                    _hysteresis = 0.02;
                    _driftFactor = 0.04;
                    _hasHartSupport = false;
                    _hasMultipleEchoes = false;
                    _hasFoamDetection = false;
                    _hasTemperatureCompensation = true;
                    break;
                default:
                    _damping = 1.0;
                    _noiseFactor = 0.1;
                    _hysteresis = 0.1;
                    _driftFactor = 0.1;
                    _hasHartSupport = false;
                    _hasMultipleEchoes = false;
                    _hasFoamDetection = false;
                    _hasTemperatureCompensation = false;
                    break;
            }
            
            // Initialize state
            Level = 0;
            ActualLevel = 0;
            RawLevel = 0;
            _lastMeasurement = 0;
            _lastCalibration = DateTime.Now;
            _accumulatedDrift = 0;
            _foamPresent = false;
            _processTemperature = 25; // Default to room temperature
            
            // Add diagnostic data
            DiagnosticData["TransmitterType"] = TransmitterType.ToString();
            DiagnosticData["MaxRange"] = MaxRange;
            DiagnosticData["Accuracy"] = Accuracy;
            DiagnosticData["Damping"] = _damping;
            DiagnosticData["HARTSupport"] = _hasHartSupport;
            DiagnosticData["MultipleEchoes"] = _hasMultipleEchoes;
            DiagnosticData["FoamDetection"] = _hasFoamDetection;
            DiagnosticData["TemperatureCompensation"] = _hasTemperatureCompensation;
        }

        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);
            
            if (Status != DeviceStatus.Running)
                return;
            
            // Calculate drift based on time since calibration
            double monthsSinceCalibration = (DateTime.Now - _lastCalibration).TotalDays / 30.0;
            _accumulatedDrift = _driftFactor * monthsSinceCalibration;
            
            // Apply damping
            double dampingFactor = 1.0 - Math.Exp(-elapsedTime.TotalSeconds / _damping);
            RawLevel = _lastMeasurement + (ActualLevel - _lastMeasurement) * dampingFactor;
            _lastMeasurement = RawLevel;
            
            // Apply foam detection if supported
            if (_hasFoamDetection && _foamPresent)
            {
                // With foam detection, we can see through the foam to the actual level
                // Without it, foam would cause erratic readings
                RawLevel = ActualLevel;
                AddAlarm("FOAM_DETECTED", "Foam layer detected on surface", AlarmSeverity.Information);
            }
            else if (_foamPresent)
            {
                // Without foam detection, foam causes erratic readings
                double foamError = MaxRange * 0.05 * (Random.NextDouble() * 2 - 1);
                RawLevel += foamError;
            }
            
            // Apply temperature compensation if supported
            double tempEffect = 0;
            if (_processTemperature != 25) // If not at reference temperature
            {
                tempEffect = (_processTemperature - 25) * 0.002 * MaxRange; // 0.2% per 10°C
                if (_hasTemperatureCompensation)
                {
                    // Compensate for temperature effect
                    tempEffect *= 0.1; // Reduce effect by 90%
                }
                RawLevel += tempEffect;
            }
            
            // Apply accuracy error and random noise
            double accuracyError = (Random.NextDouble() * 2 - 1) * (Accuracy / 100.0) * MaxRange;
            double noise = (Random.NextDouble() * 2 - 1) * (_noiseFactor / 100.0) * MaxRange;
            
            // Apply drift
            double drift = (_accumulatedDrift / 100.0) * MaxRange;
            
            // Calculate final level value with all effects
            Level = RawLevel + accuracyError + noise + drift;
            
            // Apply hysteresis - small changes don't update reading
            if (Math.Abs(Level - _lastMeasurement) < (_hysteresis / 100.0) * MaxRange)
            {
                Level = _lastMeasurement;
            }
            
            // Convert to percentage
            double levelPercent = (Level / MaxRange) * 100.0;
            levelPercent = Math.Max(0, Math.Min(100, levelPercent));
            
            // Update diagnostic data
            DiagnosticData["Level"] = Level;
            DiagnosticData["LevelPercent"] = levelPercent;
            DiagnosticData["AccumulatedDrift"] = _accumulatedDrift;
            DiagnosticData["ProcessTemperature"] = _processTemperature;
            
            // Special diagnostics for radar/ultrasonic
            if (_hasMultipleEchoes)
            {
                // Simulate multiple echo detection
                int echoCount = _foamPresent ? Random.Next(2, 5) : 1;
                DiagnosticData["EchoCount"] = echoCount;
                DiagnosticData["EchoStrength"] = _foamPresent ? 45 + Random.Next(20) : 70 + Random.Next(25);
            }
            
            // Check for abnormal conditions
            if (levelPercent > 95)
            {
                AddAlarm("LEVEL_HIGH", "Level approaching maximum range", AlarmSeverity.Warning);
            }
            else if (levelPercent < 5)
            {
                AddAlarm("LEVEL_LOW", "Level approaching minimum range", AlarmSeverity.Warning);
            }
            
            // Check for excessive drift
            if (Math.Abs(_accumulatedDrift) > 2.0) // 2% drift is concerning
            {
                AddAlarm("DRIFT_WARNING", "Excessive measurement drift detected", AlarmSeverity.Minor);
            }
        }
        
        /// <summary>
        /// Set the actual process level (for simulation)
        /// </summary>
        public void SetProcessLevel(double level)
        {
            ActualLevel = Math.Max(0, Math.Min(MaxRange, level));
        }
        
        /// <summary>
        /// Set the process temperature (for simulating temperature effects)
        /// </summary>
        public void SetProcessTemperature(double temperature)
        {
            _processTemperature = temperature;
            DiagnosticData["ProcessTemperature"] = _processTemperature;
        }
        
        /// <summary>
        /// Set foam presence (for simulating foam effects)
        /// </summary>
        public void SetFoamPresence(bool foamPresent)
        {
            _foamPresent = foamPresent;
            DiagnosticData["FoamPresent"] = _foamPresent;
        }
        
        /// <summary>
        /// Calibrate the transmitter (reset drift)
        /// </summary>
        public void Calibrate()
        {
            _lastCalibration = DateTime.Now;
            _accumulatedDrift = 0;
            DiagnosticData["LastCalibration"] = _lastCalibration;
            AddAlarm("CALIBRATION", "Device calibration performed", AlarmSeverity.Information);
        }

        protected override void SimulateFault()
        {
            int faultType = Random.Next(5);
            
            switch (faultType)
            {
                case 0: // Loss of echo (radar/ultrasonic)
                    if (TransmitterType == LevelTransmitterType.Radar || TransmitterType == LevelTransmitterType.Ultrasonic)
                    {
                        AddAlarm("ECHO_LOSS", "Echo signal lost", AlarmSeverity.Major);
                        Level = 0;
                    }
                    else
                    {
                        AddAlarm("SIGNAL_LOSS", "Level signal lost", AlarmSeverity.Major);
                        Level = 0;
                    }
                    break;
                    
                case 1: // False echo (radar/ultrasonic)
                    if (TransmitterType == LevelTransmitterType.Radar || TransmitterType == LevelTransmitterType.Ultrasonic)
                    {
                        AddAlarm("FALSE_ECHO", "False echo detected", AlarmSeverity.Minor);
                        Level = MaxRange * (0.3 + Random.NextDouble() * 0.4); // Random false reading
                    }
                    break;
                    
                case 2: // Pressure connection fault (hydrostatic)
                    if (TransmitterType == LevelTransmitterType.Hydrostatic)
                    {
                        AddAlarm("PRESSURE_FAULT", "Pressure connection fault", AlarmSeverity.Major);
                        Level = MaxRange; // Reads full scale
                    }
                    break;
                    
                case 3: // Coating/buildup
                    AddAlarm("SENSOR_COATING", "Possible sensor coating or buildup", AlarmSeverity.Warning);
                    // Gradual shift in reading
                    Level += MaxRange * 0.05;
                    break;
                    
                case 4: // Electronics fault
                    AddAlarm("ELECTRONIC_FAULT", "Electronic module fault", AlarmSeverity.Critical);
                    Status = DeviceStatus.Fault;
                    break;
            }
        }
    }

    public enum LevelTransmitterType
    {
        Radar,
        Ultrasonic,
        Hydrostatic,
        Capacitive,
        LoadCell,
        Guided,
        Float
    }
}