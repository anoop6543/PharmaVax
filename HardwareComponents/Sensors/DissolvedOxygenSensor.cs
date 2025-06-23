using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
    public class DissolvedOxygenSensor : DeviceBase
    {
        public override DeviceType Type => DeviceType.Sensor;
        
        public double DissolvedOxygen { get; private set; } // Current DO in % saturation (0-100)
        public double Temperature { get; private set; } // Temperature in Celsius
        public double ActualDO { get; private set; } // Actual process DO (for simulation)
        public double Accuracy { get; private set; } // Accuracy in % of reading
        public DOSensorType SensorType { get; private set; }
        
        // Internal parameters
        private double _driftRate; // % saturation drift per day
        private double _responseTime; // Response time in seconds
        private double _respirationRate; // Simulated oxygen consumption by cells
        private bool _hasAutomaticTemperatureCompensation;
        private bool _hasAutomaticPressureCompensation;
        private DateTime _lastCalibration;
        
        // Sensor health and status
        private double _sensorAge; // 0-100%
        private double _foulingLevel; // 0-100%
        private double _electrolyteDepleted; // 0-100% (for polarographic sensors)
        private double _opticalAgingLevel; // 0-100% (for optical sensors)
        
        // Calibration parameters
        private double _zeroOffset; // Zero offset in % saturation
        private double _slopeError; // Slope error as multiplier
        private double _ambientPressure; // Ambient pressure in mbar
        
        public DissolvedOxygenSensor(
            string deviceId, 
            string name,
            DOSensorType sensorType = DOSensorType.Optical,
            double accuracy = 0.5)
            : base(deviceId, name)
        {
            SensorType = sensorType;
            Accuracy = accuracy;
            
            // Initialize to air-saturated water
            DissolvedOxygen = 100.0;
            ActualDO = 100.0;
            Temperature = 25.0;
            
            // Set parameters based on sensor type
            if (sensorType == DOSensorType.Optical)
            {
                _responseTime = 15.0; // Typically 15-30 seconds for optical sensors
                _driftRate = 0.01; // Very low drift for optical sensors
            }
            else // Polarographic/Amperometric
            {
                _responseTime = 60.0; // Typically 30-90 seconds for polarographic sensors
                _driftRate = 0.05; // Higher drift for polarographic sensors
            }
            
            // Common parameters
            _respirationRate = 0.0;
            _hasAutomaticTemperatureCompensation = true;
            _hasAutomaticPressureCompensation = true;
            _lastCalibration = DateTime.Now;
            
            // Sensor health parameters
            _sensorAge = 0.0; // New sensor
            _foulingLevel = 0.0; // No fouling
            _electrolyteDepleted = 0.0;
            _opticalAgingLevel = 0.0;
            
            // Calibration parameters
            _zeroOffset = 0.0;
            _slopeError = 1.0; // Perfect slope
            _ambientPressure = 1013.25; // Standard atmospheric pressure in mbar
            
            // Update diagnostic data
            DiagnosticData["SensorType"] = SensorType.ToString();
            DiagnosticData["Accuracy"] = $"±{Accuracy}% of reading";
            DiagnosticData["ResponseTime"] = _responseTime;
            DiagnosticData["AutomaticTemperatureCompensation"] = _hasAutomaticTemperatureCompensation;
            DiagnosticData["AutomaticPressureCompensation"] = _hasAutomaticPressureCompensation;
            DiagnosticData["LastCalibration"] = _lastCalibration;
            DiagnosticData["AmbientPressure"] = _ambientPressure;
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
            double filteredDO = DissolvedOxygen + (ActualDO - DissolvedOxygen) * responseRate;
            
            // Simulate oxygen consumption by cells
            ActualDO -= _respirationRate * elapsedTime.TotalSeconds;
            ActualDO = Math.Max(0, ActualDO);
            
            // Apply temperature effects
            double temperatureEffect = 0;
            if (!_hasAutomaticTemperatureCompensation)
            {
                // Without temperature compensation, readings are affected by temperature
                // DO solubility decreases with increasing temperature
                double temperatureFactor = 1.0 - ((Temperature - 25.0) * 0.02);
                temperatureEffect = (1.0 - temperatureFactor) * filteredDO;
            }
            
            // Apply pressure effects
            double pressureEffect = 0;
            if (!_hasAutomaticPressureCompensation)
            {
                // Without pressure compensation, readings are affected by pressure
                // DO solubility increases with increasing pressure
                double pressureFactor = _ambientPressure / 1013.25;
                pressureEffect = (pressureFactor - 1.0) * filteredDO;
            }
            
            // Apply sensor-specific effects
            double sensorSpecificError = 0;
            
            if (SensorType == DOSensorType.Polarographic)
            {
                // Polarographic sensors are affected by electrolyte depletion
                double electrolyteFactor = 1.0 - (_electrolyteDepleted / 100.0) * 0.5;
                sensorSpecificError -= (1.0 - electrolyteFactor) * filteredDO;
                
                // Polarographic sensors drift more with age
                _electrolyteDepleted += 0.003 * elapsedTime.TotalHours;
            }
            else // Optical
            {
                // Optical sensors are affected by photobleaching of the luminophore
                double opticalAgingFactor = 1.0 - (_opticalAgingLevel / 100.0) * 0.3;
                sensorSpecificError -= (1.0 - opticalAgingFactor) * filteredDO;
                
                // Optical sensors age slowly
                _opticalAgingLevel += 0.001 * elapsedTime.TotalHours;
            }
            
            // Apply fouling effects (both sensor types)
            double foulingEffect = -(_foulingLevel / 100.0) * 0.3 * filteredDO;
            
            // Natural fouling over time
            _foulingLevel += 0.005 * elapsedTime.TotalHours;
            
            // Apply calibration errors
            double calibratedDO = (filteredDO + _zeroOffset) * _slopeError;
            
            // Apply all effects
            double measuredDO = calibratedDO + drift + temperatureEffect + pressureEffect + sensorSpecificError + foulingEffect;
            
            // Add random noise (more with age and fouling)
            double conditionFactor = 1.0 + ((_sensorAge + _foulingLevel) / 100.0);
            double noise = (Random.NextDouble() * 2 - 1) * (Accuracy / 100.0) * measuredDO * conditionFactor;
            
            DissolvedOxygen = measuredDO + noise;
            
            // Ensure value is within valid range
            DissolvedOxygen = Math.Max(0, Math.Min(200, DissolvedOxygen)); // Allow super-saturation up to 200%
            
            // Update diagnostics
            DiagnosticData["DO"] = DissolvedOxygen;
            DiagnosticData["Temperature"] = Temperature;
            DiagnosticData["Drift"] = drift;
            DiagnosticData["FoulingLevel"] = _foulingLevel;
            
            if (SensorType == DOSensorType.Polarographic)
            {
                DiagnosticData["ElectrolyteDepleted"] = _electrolyteDepleted;
            }
            else
            {
                DiagnosticData["OpticalAgingLevel"] = _opticalAgingLevel;
            }
            
            // Update sensor condition (very slow aging)
            _sensorAge += 0.002 * elapsedTime.TotalHours; // 0.2% aging per 100 hours
            
            // Check for sensor issues
            if (_sensorAge > 80)
            {
                AddAlarm("SENSOR_OLD", "DO sensor nearing end of life", AlarmSeverity.Warning);
            }
            
            if (_foulingLevel > 40)
            {
                AddAlarm("SENSOR_FOULING", "DO sensor fouling detected", AlarmSeverity.Warning);
            }
            
            if (SensorType == DOSensorType.Polarographic && _electrolyteDepleted > 70)
            {
                AddAlarm("ELECTROLYTE_LOW", "DO sensor electrolyte depleted", AlarmSeverity.Minor);
            }
            
            if (SensorType == DOSensorType.Optical && _opticalAgingLevel > 75)
            {
                AddAlarm("OPTICAL_AGING", "DO optical element aging detected", AlarmSeverity.Warning);
            }
            
            // Check calibration age
            if (daysSinceCalibration > (SensorType == DOSensorType.Optical ? 90 : 30))
            {
                AddAlarm("CALIBRATION_DUE", "DO calibration overdue", AlarmSeverity.Warning);
            }
        }
        
        /// <summary>
        /// Set the actual process DO (for simulation)
        /// </summary>
        public void SetProcessDO(double doValue)
        {
            ActualDO = Math.Max(0, Math.Min(200, doValue));
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
        /// Set the ambient pressure
        /// </summary>
        public void SetAmbientPressure(double pressureMbar)
        {
            _ambientPressure = Math.Max(800, Math.Min(1200, pressureMbar));
            DiagnosticData["AmbientPressure"] = _ambientPressure;
        }
        
        /// <summary>
        /// Set the cell respiration rate (oxygen consumption)
        /// </summary>
        public void SetRespirationRate(double ratePerSecond)
        {
            _respirationRate = Math.Max(0, ratePerSecond);
            DiagnosticData["RespirationRate"] = _respirationRate;
        }
        
        /// <summary>
        /// Calibrate the DO sensor
        /// </summary>
        public void Calibrate(bool twoPoint, double zeroPointValue = 0.0)
        {
            _lastCalibration = DateTime.Now;
            
            if (twoPoint)
            {
                // Two-point calibration (zero and saturation)
                _zeroOffset = -zeroPointValue; // Zero calibration
                
                // Slope calibration - ideally should be 1.0, but allow for some error
                _slopeError = 0.98 + (Random.NextDouble() * 0.04); // 98-102% of ideal
            }
            else
            {
                // Single-point calibration (saturation only)
                // Assume the zero is correct, only adjust slope
                _slopeError = 0.97 + (Random.NextDouble() * 0.06); // 97-103% of ideal
            }
            
            DiagnosticData["LastCalibration"] = _lastCalibration;
            DiagnosticData["ZeroOffset"] = _zeroOffset;
            DiagnosticData["SlopeError"] = _slopeError;
            
            AddAlarm("CALIBRATION", "DO sensor calibrated", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Clean the sensor to remove fouling
        /// </summary>
        public void CleanSensor()
        {
            _foulingLevel = Math.Max(0, _foulingLevel - 95); // Remove 95% of fouling
            
            DiagnosticData["FoulingLevel"] = _foulingLevel;
            
            AddAlarm("SENSOR_CLEANED", "DO sensor cleaned", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Replace sensor or sensing element
        /// </summary>
        public void ReplaceSensor()
        {
            _lastCalibration = DateTime.Now;
            _sensorAge = 0;
            _foulingLevel = 0;
            
            if (SensorType == DOSensorType.Polarographic)
            {
                _electrolyteDepleted = 0;
                AddAlarm("MEMBRANE_REPLACED", "DO sensor membrane and electrolyte replaced", AlarmSeverity.Information);
            }
            else
            {
                _opticalAgingLevel = 0;
                AddAlarm("OPTICAL_REPLACED", "DO sensor optical element replaced", AlarmSeverity.Information);
            }
            
            // New sensors typically have good calibration
            _zeroOffset = (Random.NextDouble() * 2 - 1) * 0.2; // ±0.2% zero offset
            _slopeError = 0.99 + (Random.NextDouble() * 0.02); // 99-101% of ideal slope
            
            DiagnosticData["SensorAge"] = _sensorAge;
            DiagnosticData["FoulingLevel"] = _foulingLevel;
            
            if (SensorType == DOSensorType.Polarographic)
            {
                DiagnosticData["ElectrolyteDepleted"] = _electrolyteDepleted;
            }
            else
            {
                DiagnosticData["OpticalAgingLevel"] = _opticalAgingLevel;
            }
        }

        protected override void SimulateFault()
        {
            int faultType = Random.Next(5);
            
            switch (faultType)
            {
                case 0: // Common to both types - severe fouling
                    AddAlarm("SEVERE_FOULING", "Severe sensor fouling detected", AlarmSeverity.Major);
                    _foulingLevel = 90;
                    break;
                    
                case 1: // Type-specific failure
                    if (SensorType == DOSensorType.Polarographic)
                    {
                        AddAlarm("MEMBRANE_DAMAGED", "DO sensor membrane damaged", AlarmSeverity.Critical);
                        DissolvedOxygen = ActualDO * 0.2; // Reads very low
                    }
                    else // Optical
                    {
                        AddAlarm("OPTICAL_DAMAGE", "DO sensor optical damage detected", AlarmSeverity.Critical);
                        DissolvedOxygen = ActualDO * 0.3; // Reads very low
                    }
                    break;
                    
                case 2: // Air bubble
                    AddAlarm("AIR_BUBBLE", "Air bubble on sensor detected", AlarmSeverity.Minor);
                    DissolvedOxygen = 100 + (Random.NextDouble() * 30); // Reads high
                    break;
                    
                case 3: // Type-specific consumption
                    if (SensorType == DOSensorType.Polarographic)
                    {
                        AddAlarm("O2_CONSUMPTION", "Sensor oxygen consumption error", AlarmSeverity.Minor);
                        DissolvedOxygen = ActualDO * 0.7; // Reads low due to excess consumption at membrane
                    }
                    else // Optical
                    {
                        AddAlarm("LIGHT_INTERFERENCE", "Optical interference detected", AlarmSeverity.Minor);
                        DissolvedOxygen = ActualDO * 1.2; // Reads high due to light interference
                    }
                    break;
                    
                case 4: // Connection issue
                    AddAlarm("CONNECTION_ISSUE", "Sensor connection issue detected", AlarmSeverity.Major);
                    DissolvedOxygen = Random.Next(0, 10); // Very low erratic reading
                    break;
            }
        }
    }

    public enum DOSensorType
    {
        Polarographic,
        Optical
    }
}