using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Actuators
{
    /// <summary>
    /// Represents a motor controller for equipment such as bioreactor agitators, pumps, and mixing systems
    /// </summary>
    public class MotorController : DeviceBase
    {
        public override DeviceType Type => DeviceType.Actuator;

        // Motor control properties
        public double Speed { get; private set; }                // Current speed in percentage (0-100%)
        public double TargetSpeed { get; private set; }          // Target speed in percentage
        public double Acceleration { get; private set; }         // Acceleration rate in %/second
        public double Deceleration { get; private set; }         // Deceleration rate in %/second
        public double CurrentTorque { get; private set; }        // Current torque as percentage of maximum
        public double PowerConsumption { get; private set; }     // Power consumption in kW
        public double MaxPower { get; private set; }             // Maximum power rating in kW
        public double MaxSpeed { get; private set; }             // Maximum speed in RPM
        public MotorType MotorType { get; private set; }         // Type of motor
        public MotorControllerMode ControlMode { get; private set; } // Control mode

        // Internal state variables
        private double _actualSpeed;           // Actual physical speed (for simulation)
        private double _actualTorque;          // Actual physical torque (for simulation)
        private double _loadFactor = 1.0;      // Load factor (1.0 = nominal load)
        private double _efficiency = 0.92;     // Motor efficiency
        private double _temperatureFactor = 1.0; // Temperature impact on efficiency
        private double _vibrationLevel = 0.0;   // Vibration level in percentage
        private double _temperature = 25.0;     // Motor temperature in Celsius
        private double _maxTemperature = 85.0;  // Maximum safe operating temperature
        private double _temperatureRiseRate;    // Temperature rise rate per kW
        private double _temperatureCoolRate;    // Temperature cooling rate per second
        private int _startCount = 0;            // Number of motor starts (for maintenance tracking)
        private double _runningHours = 0.0;     // Total running hours

        /// <summary>
        /// Creates a new motor controller for simulation
        /// </summary>
        /// <param name="deviceId">Unique device identifier</param>
        /// <param name="name">Human-readable device name</param>
        /// <param name="motorType">Type of motor being controlled</param>
        /// <param name="maxPower">Maximum power rating in kW</param>
        /// <param name="maxSpeed">Maximum speed in RPM</param>
        /// <param name="acceleration">Acceleration rate in %/second</param>
        /// <param name="deceleration">Deceleration rate in %/second</param>
        /// <param name="controlMode">Control mode for the motor</param>
        public MotorController(
            string deviceId,
            string name,
            MotorType motorType = MotorType.ACInduction,
            double maxPower = 5.0,
            double maxSpeed = 1800,
            double acceleration = 10.0,
            double deceleration = 10.0,
            MotorControllerMode controlMode = MotorControllerMode.SpeedControl)
            : base(deviceId, name)
        {
            MotorType = motorType;
            MaxPower = maxPower;
            MaxSpeed = maxSpeed;
            Acceleration = acceleration;
            Deceleration = deceleration;
            ControlMode = controlMode;

            // Initialize motor state
            Speed = 0;
            TargetSpeed = 0;
            _actualSpeed = 0;
            CurrentTorque = 0;
            _actualTorque = 0;
            PowerConsumption = 0;

            // Configure motor-specific parameters
            ConfigureMotorParameters();

            // Set initial diagnostic data
            DiagnosticData["MotorType"] = MotorType.ToString();
            DiagnosticData["MaxPower"] = MaxPower;
            DiagnosticData["MaxSpeed"] = MaxSpeed;
            DiagnosticData["Acceleration"] = Acceleration;
            DiagnosticData["Deceleration"] = Deceleration;
            DiagnosticData["ControlMode"] = ControlMode.ToString();
            DiagnosticData["Efficiency"] = _efficiency * 100;
            DiagnosticData["StartCount"] = _startCount;
            DiagnosticData["RunningHours"] = _runningHours;
            DiagnosticData["Temperature"] = _temperature;
        }

        /// <summary>
        /// Sets motor-specific parameters based on motor type
        /// </summary>
        private void ConfigureMotorParameters()
        {
            switch (MotorType)
            {
                case MotorType.ACInduction:
                    _efficiency = 0.92;
                    _temperatureRiseRate = 1.5;     // °C per kW per hour
                    _temperatureCoolRate = 0.05;    // °C per second when idle
                    break;

                case MotorType.PermanentMagnet:
                    _efficiency = 0.96;
                    _temperatureRiseRate = 1.2;     // °C per kW per hour
                    _temperatureCoolRate = 0.04;    // °C per second when idle
                    break;

                case MotorType.StepperMotor:
                    _efficiency = 0.70;
                    _temperatureRiseRate = 2.0;     // °C per kW per hour
                    _temperatureCoolRate = 0.03;    // °C per second when idle
                    break;

                case MotorType.ServoMotor:
                    _efficiency = 0.95;
                    _temperatureRiseRate = 1.3;     // °C per kW per hour
                    _temperatureCoolRate = 0.045;   // °C per second when idle
                    break;

                case MotorType.DCMotor:
                    _efficiency = 0.85;
                    _temperatureRiseRate = 1.8;     // °C per kW per hour
                    _temperatureCoolRate = 0.04;    // °C per second when idle
                    break;

                default:
                    _efficiency = 0.90;
                    _temperatureRiseRate = 1.5;     // °C per kW per hour
                    _temperatureCoolRate = 0.04;    // °C per second when idle
                    break;
            }
        }

        public override bool Start()
        {
            if (base.Start())
            {
                _startCount++;
                DiagnosticData["StartCount"] = _startCount;
                AddAlarm("MOTOR_STARTED", $"Motor {Name} started", AlarmSeverity.Information);
                return true;
            }
            return false;
        }

        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);

            if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
                return;

            // Update running hours counter
            _runningHours += elapsedTime.TotalHours;
            DiagnosticData["RunningHours"] = _runningHours;

            // Calculate speed change based on acceleration/deceleration rates
            double speedDifference = TargetSpeed - _actualSpeed;
            double maxChange;
            
            if (speedDifference > 0)
            {
                // Accelerating
                maxChange = Acceleration * elapsedTime.TotalSeconds;
            }
            else
            {
                // Decelerating
                maxChange = Deceleration * elapsedTime.TotalSeconds;
            }

            double actualChange = Math.Sign(speedDifference) * Math.Min(Math.Abs(speedDifference), maxChange);
            
            // Update actual speed
            _actualSpeed += actualChange;

            // Apply small random fluctuations to simulate real motor behavior
            double fluctuation = (Random.NextDouble() * 2 - 1) * 0.5;
            Speed = Math.Min(Math.Max(_actualSpeed + fluctuation, 0), 100);

            // Calculate torque based on load and speed
            UpdateTorqueAndPower(elapsedTime);

            // Update temperature based on power consumption
            UpdateTemperature(elapsedTime);

            // Update vibration level
            UpdateVibrationLevel(elapsedTime);

            // Update diagnostic data
            UpdateDiagnostics();

            // Check for alarm conditions
            CheckAlarmConditions();
        }

        /// <summary>
        /// Updates torque and power consumption based on current conditions
        /// </summary>
        private void UpdateTorqueAndPower(TimeSpan elapsedTime)
        {
            // Calculate base torque based on load factor and motor type
            double speedFactor = _actualSpeed / 100.0;
            double baseTorque;

            switch (MotorType)
            {
                case MotorType.ACInduction:
                    // AC induction motors have reduced torque at very low speeds
                    if (speedFactor < 0.1)
                        baseTorque = speedFactor * 10 * _loadFactor;
                    else
                        baseTorque = _loadFactor;
                    break;

                case MotorType.StepperMotor:
                    // Stepper motors have high torque at low speeds, decreasing with speed
                    baseTorque = _loadFactor * (1.0 - 0.3 * speedFactor);
                    break;

                case MotorType.ServoMotor:
                case MotorType.PermanentMagnet:
                    // Servo and PM motors maintain torque across speed range
                    baseTorque = _loadFactor;
                    break;

                case MotorType.DCMotor:
                    // DC motors have slightly reduced torque at higher speeds
                    baseTorque = _loadFactor * (1.0 - 0.1 * speedFactor);
                    break;

                default:
                    baseTorque = _loadFactor;
                    break;
            }

            // Add torque fluctuations
            double torqueNoise = (Random.NextDouble() * 2 - 1) * 0.05 * baseTorque;
            _actualTorque = baseTorque + torqueNoise;
            _actualTorque = Math.Max(0, Math.Min(_actualTorque, 2.0)); // Limit to 200% of rated torque

            // Calculate current torque as percentage
            CurrentTorque = _actualTorque * 100;

            // Calculate power consumption based on speed, torque, and efficiency
            PowerConsumption = MaxPower * speedFactor * _actualTorque / (_efficiency * _temperatureFactor);
        }

        /// <summary>
        /// Updates motor temperature based on power consumption and cooling
        /// </summary>
        private void UpdateTemperature(TimeSpan elapsedTime)
        {
            // Calculate temperature rise from power dissipation
            double powerLoss = PowerConsumption * (1 - _efficiency);
            double temperatureRise = powerLoss * _temperatureRiseRate * elapsedTime.TotalHours;

            // Calculate cooling effect
            double cooling;
            if (_actualSpeed < 5)
            {
                // Motor stopped or very slow - natural cooling
                cooling = _temperatureCoolRate * elapsedTime.TotalSeconds;
            }
            else
            {
                // Motor running - cooling depends on speed (air flow)
                double speedFactor = _actualSpeed / 100.0;
                cooling = _temperatureCoolRate * speedFactor * 0.5 * elapsedTime.TotalSeconds;
            }

            // Adjust temperature (rise minus cooling)
            _temperature += temperatureRise - cooling * (_temperature - 25.0) / 60.0;

            // Ensure temperature stays within realistic bounds
            _temperature = Math.Max(25.0, Math.Min(_temperature, 120.0));

            // Update temperature factor for efficiency calculation
            if (_temperature > 80.0)
            {
                // Efficiency drops with high temperature
                _temperatureFactor = 1.0 - ((_temperature - 80.0) / 100.0);
                _temperatureFactor = Math.Max(0.7, _temperatureFactor);
            }
            else
            {
                _temperatureFactor = 1.0;
            }
        }

        /// <summary>
        /// Updates motor vibration level based on speed, load, and running time
        /// </summary>
        private void UpdateVibrationLevel(TimeSpan elapsedTime)
        {
            // Base vibration depends on speed
            double speedFactor = _actualSpeed / 100.0;
            double baseVibration = speedFactor * 10.0; // 0-10% base vibration from speed

            // Add load contribution
            double loadVibration = speedFactor * (_loadFactor - 1.0) * 15.0;
            loadVibration = Math.Max(0, loadVibration); // Only overload increases vibration

            // Add wear factor based on running hours
            double wearVibration = Math.Min(10.0, _runningHours / 1000.0 * 5.0);

            // Add resonance effects at certain speeds
            double resonanceVibration = 0.0;
            if (speedFactor > 0.4 && speedFactor < 0.5) // Resonance zone around 40-50%
            {
                resonanceVibration = 15.0 * (1.0 - Math.Abs(0.45 - speedFactor) / 0.05);
                resonanceVibration = Math.Max(0, resonanceVibration);
            }
            else if (speedFactor > 0.85 && speedFactor < 0.95) // Resonance zone around 85-95%
            {
                resonanceVibration = 10.0 * (1.0 - Math.Abs(0.9 - speedFactor) / 0.05);
                resonanceVibration = Math.Max(0, resonanceVibration);
            }

            // Add random vibration component
            double randomVibration = Random.NextDouble() * 2.0 * speedFactor;

            // Calculate total vibration
            _vibrationLevel = baseVibration + loadVibration + wearVibration + resonanceVibration + randomVibration;
            _vibrationLevel = Math.Min(100, _vibrationLevel);
        }

        /// <summary>
        /// Updates diagnostic data for display and monitoring
        /// </summary>
        private void UpdateDiagnostics()
        {
            DiagnosticData["Speed"] = Speed;
            DiagnosticData["TargetSpeed"] = TargetSpeed;
            DiagnosticData["ActualRPM"] = (MaxSpeed * _actualSpeed / 100.0);
            DiagnosticData["Torque"] = CurrentTorque;
            DiagnosticData["PowerConsumption"] = PowerConsumption;
            DiagnosticData["Temperature"] = _temperature;
            DiagnosticData["VibrationLevel"] = _vibrationLevel;
            DiagnosticData["RunningHours"] = _runningHours;
            DiagnosticData["TemperatureFactor"] = _temperatureFactor;
        }

        /// <summary>
        /// Checks for alarm conditions based on current motor state
        /// </summary>
        private void CheckAlarmConditions()
        {
            // Check for high temperature
            if (_temperature > _maxTemperature * 0.85)
            {
                AddAlarm("MOTOR_HIGH_TEMP", $"Motor temperature high: {_temperature:F1}°C", AlarmSeverity.Warning);
            }

            if (_temperature > _maxTemperature)
            {
                AddAlarm("MOTOR_OVER_TEMP", $"Motor temperature critical: {_temperature:F1}°C", AlarmSeverity.Critical);
                EmergencyStop("Motor temperature exceeded maximum");
            }

            // Check for high vibration
            if (_vibrationLevel > 70)
            {
                AddAlarm("MOTOR_HIGH_VIBRATION", $"High vibration detected: {_vibrationLevel:F1}%", AlarmSeverity.Warning);
            }

            if (_vibrationLevel > 90)
            {
                AddAlarm("MOTOR_EXCESSIVE_VIBRATION", $"Excessive vibration: {_vibrationLevel:F1}%", AlarmSeverity.Critical);
                EmergencyStop("Excessive vibration detected");
            }

            // Check for overload
            if (CurrentTorque > 150)
            {
                AddAlarm("MOTOR_OVERLOAD", $"Motor overload: {CurrentTorque:F1}% of rated torque", AlarmSeverity.Warning);
            }

            if (CurrentTorque > 180)
            {
                AddAlarm("MOTOR_SEVERE_OVERLOAD", $"Severe motor overload: {CurrentTorque:F1}% of rated torque", AlarmSeverity.Critical);
                EmergencyStop("Severe overload detected");
            }

            // Check for stall condition
            if (_actualSpeed < 5 && TargetSpeed > 20 && CurrentTorque > 100)
            {
                AddAlarm("MOTOR_STALL", "Motor stall detected", AlarmSeverity.Major);
            }

            // Check for maintenance
            if (_runningHours > 8000 && _runningHours % 1000 < 1)
            {
                AddAlarm("MOTOR_MAINTENANCE", $"Motor maintenance recommended: {_runningHours:F0} hours", AlarmSeverity.Information);
            }
        }

        /// <summary>
        /// Performs an emergency stop of the motor
        /// </summary>
        private void EmergencyStop(string reason)
        {
            TargetSpeed = 0;
            Deceleration *= 3; // Faster deceleration for emergency
            Status = DeviceStatus.Fault;
            AddAlarm("MOTOR_EMERGENCY_STOP", $"Emergency stop: {reason}", AlarmSeverity.Critical);
        }

        #region Public Control Methods

        /// <summary>
        /// Sets the target speed of the motor
        /// </summary>
        /// <param name="speed">Target speed as percentage (0-100%)</param>
        public void SetSpeed(double speed)
        {
            TargetSpeed = Math.Min(Math.Max(speed, 0), 100);
            DiagnosticData["TargetSpeed"] = TargetSpeed;
        }

        /// <summary>
        /// Sets the load factor of the motor
        /// </summary>
        /// <param name="loadFactor">Load factor (normally 0-2, where 1.0 is nominal load)</param>
        public void SetLoadFactor(double loadFactor)
        {
            _loadFactor = Math.Max(loadFactor, 0);
            DiagnosticData["LoadFactor"] = _loadFactor;
        }

        /// <summary>
        /// Triggers an emergency stop of the motor
        /// </summary>
        public void TriggerEmergencyStop()
        {
            EmergencyStop("Manual emergency stop triggered");
        }

        /// <summary>
        /// Resets the motor after a fault condition
        /// </summary>
        /// <returns>True if reset was successful</returns>
        public bool Reset()
        {
            if (Status == DeviceStatus.Fault)
            {
                if (_temperature < _maxTemperature * 0.9 && _vibrationLevel < 60)
                {
                    Status = DeviceStatus.Ready;
                    AddAlarm("MOTOR_RESET", "Motor controller reset successful", AlarmSeverity.Information);
                    return true;
                }
                else
                {
                    AddAlarm("MOTOR_RESET_FAILED", "Cannot reset: motor conditions still outside safe limits", AlarmSeverity.Warning);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Records that maintenance was performed on the motor
        /// </summary>
        public void PerformMaintenance()
        {
            // Simulate effects of maintenance
            _vibrationLevel *= 0.5;  // Reduce vibration
            _runningHours = Math.Floor(_runningHours / 1000) * 1000; // Reset to nearest thousand hours
            
            AddAlarm("MOTOR_MAINTENANCE_PERFORMED", "Motor maintenance performed", AlarmSeverity.Information);
            DiagnosticData["RunningHours"] = _runningHours;
            DiagnosticData["VibrationLevel"] = _vibrationLevel;
            
            if (Status == DeviceStatus.Maintenance)
            {
                Status = DeviceStatus.Ready;
            }
        }

        #endregion

        protected override void SimulateFault()
        {
            int faultType = Random.Next(5);

            switch (faultType)
            {
                case 0: // Bearing issue
                    _vibrationLevel += 25.0;
                    AddAlarm("MOTOR_BEARING_FAULT", "Motor bearing issue detected", AlarmSeverity.Warning);
                    break;

                case 1: // Winding overheat
                    _temperature += 15.0;
                    _temperatureFactor = 0.8;
                    AddAlarm("MOTOR_WINDING_HOT", "Motor winding overheating", AlarmSeverity.Major);
                    break;

                case 2: // Phase imbalance
                    AddAlarm("MOTOR_PHASE_IMBALANCE", "Motor phase imbalance detected", AlarmSeverity.Major);
                    _efficiency *= 0.8;
                    _vibrationLevel += 15.0;
                    break;

                case 3: // Cooling system issue
                    _temperatureCoolRate *= 0.5;
                    AddAlarm("MOTOR_COOLING_FAULT", "Motor cooling system issue", AlarmSeverity.Warning);
                    break;

                case 4: // Catastrophic failure
                    AddAlarm("MOTOR_FAILURE", "Catastrophic motor failure", AlarmSeverity.Critical);
                    Status = DeviceStatus.Fault;
                    TargetSpeed = 0;
                    _temperature += 30.0;
                    _vibrationLevel = 100.0;
                    break;
            }
        }
    }

    /// <summary>
    /// Defines the types of motors supported by the motor controller
    /// </summary>
    public enum MotorType
    {
        ACInduction,        // Standard AC induction motor
        PermanentMagnet,    // Permanent magnet synchronous motor
        StepperMotor,       // Stepper motor for precise positioning
        ServoMotor,         // Servo motor with feedback
        DCMotor             // DC motor
    }

    /// <summary>
    /// Defines the control modes for the motor controller
    /// </summary>
    public enum MotorControllerMode
    {
        SpeedControl,       // Basic speed control
        TorqueControl,      // Torque control mode
        PositionControl,    // Position control (for servo motors)
        VectorControl,      // Field-oriented vector control
        PID                 // PID control with feedback
    }
}