using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
    public class PhAnalyzer : DeviceBase
    {
        public override DeviceType Type => DeviceType.Sensor;
        
        public double PH { get; private set; } // Current pH value
        public double Temperature { get; private set; } // Temperature of sample in Celsius
        public double ActualPH { get; private set; } // Actual process pH (for simulation)
        public double MinPH { get; private set; }
        public double MaxPH { get; private set; }
        public double Accuracy { get; private set; } // Accuracy in pH units
        
        // Internal parameters
        private double _slope; // Electrode slope (mV per pH unit) - ideally 59.16 mV/pH at 25°C
        private double _offset; // Electrode zero offset (mV)
        private double _driftRate; // pH drift per day
        private double _temperatureCoef; // Temperature compensation coefficient
        private double _responseTime; // Response time in seconds
        private bool _hasAutomaticTemperatureCompensation;
        private DateTime _lastCalibration;
        private DateTime _lastCleaningDate;
        
        // Electrode health and status
        private double _electrodeAge; // 0-100%
        private double _referenceCellCondition; // 0-100%
        private double _coatingLevel; // 0-100%
        private double _glassBulbCondition; // 0-100%
        
        // Standardization parameters
        private double _lastStandardValue; // pH value of last standard used
        private DateTime _lastStandardization;
        
        public PhAnalyzer(
            string deviceId, 
            string name,
            double minPH = 0.0,
            double maxPH = 14.0,
            double accuracy = 0.01,
            bool automaticTemperatureCompensation = true)
            : base(deviceId, name)
        {
            MinPH = minPH;
            MaxPH = maxPH;
            Accuracy = accuracy;
            _hasAutomaticTemperatureCompensation = automaticTemperatureCompensation;
            
            // Initialize to neutral pH
            PH = 7.0;
            ActualPH = 7.0;
            Temperature = 25.0; // Standard room temperature
            
            // Set typical starting parameters for a good electrode
            _slope = -59.16; // mV per pH unit at 25°C (Nernst equation)
            _offset = 0; // No offset when perfectly calibrated
            _driftRate = 0.01; // 0.01 pH per day
            _responseTime = 5.0; // 5 seconds response time
            _temperatureCoef = 0.003; // 0.3% per °C
            
            // Set initial electrode condition
            _electrodeAge = 0; // New electrode
            _referenceCellCondition = 100; // Perfect condition
            _coatingLevel = 0; // No coating
            _glassBulbCondition = 100; // Perfect condition
            
            // Set initial calibration dates
            _lastCalibration = DateTime.Now;
            _lastCleaningDate = DateTime.Now;
            _lastStandardization = DateTime.Now;
            _lastStandardValue = 7.0; // Most common buffer
            
            // Update diagnostic data
            DiagnosticData["Range"] = $"{MinPH} to {MaxPH} pH";
            DiagnosticData["Accuracy"] = $"±{Accuracy} pH";
            DiagnosticData["AutomaticTemperatureCompensation"] = _hasAutomaticTemperatureCompensation;
            DiagnosticData["LastCalibration"] = _lastCalibration;
            DiagnosticData["ElectrodeSlope"] = _slope;
            DiagnosticData["ElectrodeOffset"] = _offset;
            DiagnosticData["ElectrodeAge"] = _electrodeAge;
            DiagnosticData["ReferenceCellCondition"] = _referenceCellCondition;
        }

        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);
            
            if (Status != DeviceStatus.Running)
                return;
            
            // Calculate days since calibration for drift calculation
            double daysSinceCalibration = (DateTime.Now - _lastCalibration).TotalDays;
            double drift = _driftRate * daysSinceCalibration;
            
            // Apply response time (first-order lag)
            double responseRate = 1.0 - Math.Exp(-elapsedTime.TotalSeconds / _responseTime);
            double filteredPH = PH + (ActualPH - PH) * responseRate;
            
            // Apply temperature effects
            double temperatureDeviation = Temperature - 25.0; // Deviation from standard 25°C
            double temperatureEffect = 0;
            
            if (!_hasAutomaticTemperatureCompensation)
            {
                // Without temperature compensation, measurements are affected by temperature
                temperatureEffect = temperatureDeviation * _temperatureCoef;
            }
            else
            {
                // With temperature compensation, effect is greatly reduced but not eliminated
                temperatureEffect = temperatureDeviation * _temperatureCoef * 0.1;
            }
            
            // Apply aging and condition effects
            double agingEffect = (_electrodeAge / 100.0) * 0.1; // Up to 0.1 pH error for old electrode
            double referenceEffect = (100.0 - _referenceCellCondition) / 100.0 * 0.2; // Up to 0.2 pH error for bad reference
            double coatingEffect = (_coatingLevel / 100.0) * 0.3; // Up to 0.3 pH error for coated electrode
            double bulbEffect = (100.0 - _glassBulbCondition) / 100.0 * 0.3; // Up to 0.3 pH error for damaged bulb
            
            // Calculate random noise (more noise with poor electrode condition)
            double conditionFactor = 1.0 + ((_electrodeAge / 100.0) * 2.0);
            double noise = (Random.NextDouble() * 2 - 1) * Accuracy * conditionFactor;
            
            // Calculate final pH reading
            PH = filteredPH + drift + temperatureEffect + agingEffect + referenceEffect + coatingEffect + bulbEffect + noise;
            
            // Clamp to valid range
            PH = Math.Max(MinPH, Math.Min(MaxPH, PH));
            
            // Update diagnostics
            DiagnosticData["pH"] = PH;
            DiagnosticData["Temperature"] = Temperature;
            DiagnosticData["Drift"] = drift;
            DiagnosticData["TemperatureEffect"] = temperatureEffect;
            DiagnosticData["ElectrodeCondition"] = 100 - (_electrodeAge + (100 - _referenceCellCondition) + _coatingLevel + (100 - _glassBulbCondition)) / 4;
            
            // Update electrode condition (very slow aging)
            _electrodeAge += 0.001 * elapsedTime.TotalHours; // 0.1% aging per 100 hours
            _referenceCellCondition -= 0.0005 * elapsedTime.TotalHours; // 0.05% degradation per 100 hours
            _coatingLevel += 0.002 * elapsedTime.TotalHours; // 0.2% coating buildup per 100 hours
            _glassBulbCondition -= 0.0005 * elapsedTime.TotalHours; // 0.05% degradation per 100 hours
            
            // Check for electrode issues
            if (_electrodeAge > 90)
            {
                AddAlarm("ELECTRODE_OLD", "pH electrode nearing end of life", AlarmSeverity.Warning);
            }
            
            if (_referenceCellCondition < 50)
            {
                AddAlarm("REFERENCE_DEGRADED", "Reference cell degraded", AlarmSeverity.Minor);
            }
            
            if (_coatingLevel > 50)
            {
                AddAlarm("ELECTRODE_COATING", "pH electrode coating detected", AlarmSeverity.Minor);
            }
            
            if (_glassBulbCondition < 60)
            {
                AddAlarm("GLASS_DEGRADED", "Glass bulb sensitivity reduced", AlarmSeverity.Warning);
            }
            
            // Check calibration age
            if (daysSinceCalibration > 30)
            {
                AddAlarm("CALIBRATION_DUE", "pH calibration overdue", AlarmSeverity.Warning);
            }
        }
        
        /// <summary>
        /// Set the actual process pH (for simulation)
        /// </summary>
        public void SetProcessPH(double pH)
        {
            ActualPH = Math.Max(MinPH, Math.Min(MaxPH, pH));
        }
        
        /// <summary>
        /// Set the process temperature
        /// </summary>
        public void SetTemperature(double temperature)
        {
            Temperature = Math.Max(-10, Math.Min(130, temperature));
            DiagnosticData["Temperature"] = Temperature;
        }
        
        /// <summary>
        /// Calibrate the pH analyzer
        /// </summary>
        public void Calibrate(double lowBufferPH, double highBufferPH)
        {
            // Two-point calibration
            _lastCalibration = DateTime.Now;
            
            // Calculate new slope (simplified) - in a real analyzer, this would be more complex
            _slope = -59.16 * (0.9 + (Random.NextDouble() * 0.2)); // 90-110% of ideal
            
            // Calculate new offset based on the middle of the buffer range
            double midPointPH = (lowBufferPH + highBufferPH) / 2.0;
            _offset = (Random.NextDouble() * 2 - 1) * 5; // ±5 mV offset
            
            DiagnosticData["LastCalibration"] = _lastCalibration;
            DiagnosticData["ElectrodeSlope"] = _slope;
            DiagnosticData["ElectrodeOffset"] = _offset;
            
            AddAlarm("CALIBRATION", "pH electrode calibrated", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Clean the electrode to remove coating
        /// </summary>
        public void CleanElectrode()
        {
            _lastCleaningDate = DateTime.Now;
            _coatingLevel = Math.Max(0, _coatingLevel - 90); // Remove 90% of coating
            
            DiagnosticData["LastCleaning"] = _lastCleaningDate;
            DiagnosticData["CoatingLevel"] = _coatingLevel;
            
            AddAlarm("ELECTRODE_CLEANED", "pH electrode cleaned", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Standardize against a single buffer (for minor adjustments between calibrations)
        /// </summary>
        public void Standardize(double bufferPH)
        {
            _lastStandardization = DateTime.Now;
            _lastStandardValue = bufferPH;
            
            // Apply a small correction to the offset
            _offset += (Random.NextDouble() * 2 - 1) * 2; // ±2 mV adjustment
            
            DiagnosticData["LastStandardization"] = _lastStandardization;
            DiagnosticData["LastStandardValue"] = _lastStandardValue;
            
            AddAlarm("STANDARDIZATION", "pH electrode standardized", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Replace electrode with a new one
        /// </summary>
        public void ReplaceElectrode()
        {
            _lastCalibration = DateTime.Now;
            _lastCleaningDate = DateTime.Now;
            
            _electrodeAge = 0;
            _referenceCellCondition = 100;
            _coatingLevel = 0;
            _glassBulbCondition = 100;
            
            // New electrodes typically have values close to ideal
            _slope = -59.16 * (0.97 + (Random.NextDouble() * 0.06)); // 97-103% of ideal
            _offset = (Random.NextDouble() * 2 - 1) * 3; // ±3 mV offset for new electrode
            
            DiagnosticData["ElectrodeSlope"] = _slope;
            DiagnosticData["ElectrodeOffset"] = _offset;
            DiagnosticData["ElectrodeAge"] = _electrodeAge;
            DiagnosticData["ReferenceCellCondition"] = _referenceCellCondition;
            DiagnosticData["CoatingLevel"] = _coatingLevel;
            DiagnosticData["GlassBulbCondition"] = _glassBulbCondition;
            
            AddAlarm("ELECTRODE_REPLACED", "pH electrode replaced", AlarmSeverity.Information);
        }

        protected override void SimulateFault()
        {
            int faultType = Random.Next(5);
            
            switch (faultType)
            {
                case 0: // Broken glass
                    AddAlarm("BROKEN_GLASS", "pH glass bulb possibly cracked", AlarmSeverity.Critical);
                    PH = 7.0; // Reads neutral regardless of actual pH
                    _glassBulbCondition = 0;
                    break;
                    
                case 1: // Reference contamination
                    AddAlarm("REFERENCE_CONTAMINATION", "Reference contamination detected", AlarmSeverity.Major);
                    _referenceCellCondition = 20;
                    break;
                    
                case 2: // Heavy coating
                    AddAlarm("HEAVY_COATING", "Heavy electrode coating detected", AlarmSeverity.Major);
                    _coatingLevel = 90;
                    _responseTime = 30.0; // Very slow response
                    break;
                    
                case 3: // Ground loop
                    AddAlarm("GROUND_LOOP", "Electrical ground loop affecting measurement", AlarmSeverity.Minor);
                    PH += (Random.NextDouble() * 2 - 1) * 0.5; // Random offset
                    break;
                    
                case 4: // Cable/connection issue
                    AddAlarm("CONNECTION_ISSUE", "Electrode connection issue detected", AlarmSeverity.Major);
                    PH = Random.Next((int)MinPH, (int)MaxPH + 1); // Random reading within range
                    break;
            }
        }
    }
}