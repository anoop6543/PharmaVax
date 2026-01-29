using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Numerics;

namespace PharmaceuticalProcess.HardwareComponents.Actuators
{
    /// <summary>
    /// Base class for robotic end-effectors used in pharmaceutical processes
    /// </summary>
    public class Manipulator : DeviceBase
    {
        public override DeviceType Type => DeviceType.Actuator;
        
        // Configuration properties
        public ManipulatorToolType ToolType { get; private set; }
        public double Weight { get; private set; }                  // Weight in kg
        public double OpeningWidth { get; private set; }            // Maximum opening width in mm
        public double ClosedWidth { get; private set; }             // Width when closed in mm
        public double MaxForce { get; private set; }                // Maximum force in N
        public double CurrentForce { get; private set; }            // Current force in N
        
        // State properties
        public bool IsAttached { get; internal set; }               // Is the tool attached to a robot
        public bool IsActivated { get; private set; }               // Is the tool activated (gripping/sucking)
        public bool HasObject { get; private set; }                 // Is an object currently gripped
        public double GripPosition { get; private set; }            // Current position (0-100%)
        public double GripTarget { get; private set; }              // Target position (0-100%)
        
        // Internal state tracking
        private double _currentWidth;             // Current opening width in mm
        private double _movementSpeed;            // Movement speed in mm/s
        private bool _isMoving;                   // Is the gripper currently moving
        private double _maxAirFlow;               // Maximum air flow for vacuum in l/min
        private double _currentVacuum;            // Current vacuum level in kPa
        private double _cyclesToNextMaintenance;  // Cycles until maintenance is required
        
        /// <summary>
        /// Creates a new manipulator tool (end effector)
        /// </summary>
        /// <param name="deviceId">Unique device identifier</param>
        /// <param name="name">Human-readable device name</param>
        /// <param name="toolType">Type of end effector</param>
        /// <param name="weight">Tool weight in kg</param>
        /// <param name="maxForce">Maximum force in N</param>
        /// <param name="openingWidth">Maximum opening width in mm</param>
        public Manipulator(
            string deviceId, 
            string name,
            ManipulatorToolType toolType,
            double weight = 1.2,
            double maxForce = 100,
            double openingWidth = 80)
            : base(deviceId, name)
        {
            ToolType = toolType;
            Weight = weight;
            MaxForce = maxForce;
            OpeningWidth = openingWidth;
            ClosedWidth = toolType == ManipulatorToolType.VacuumGripper ? 0 : 5;
            IsAttached = false;
            IsActivated = false;
            HasObject = false;
            
            // Initialize width
            _currentWidth = OpeningWidth;
            GripPosition = 0;  // 0% = fully open
            GripTarget = 0;
            _isMoving = false;
            _movementSpeed = 50; // 50mm/s
            
            // Initialize vacuum parameters if vacuum gripper
            if (toolType == ManipulatorToolType.VacuumGripper)
            {
                _maxAirFlow = 25; // 25 l/min
                _currentVacuum = 0;
            }
            
            _cyclesToNextMaintenance = 10000; // 10,000 cycles until maintenance
            
            // Initialize diagnostics
            InitializeDiagnostics();
        }
        
        private void InitializeDiagnostics()
        {
            DiagnosticData["ToolType"] = ToolType.ToString();
            DiagnosticData["Weight"] = Weight;
            DiagnosticData["MaxForce"] = MaxForce;
            DiagnosticData["OpeningWidth"] = OpeningWidth;
            DiagnosticData["ClosedWidth"] = ClosedWidth;
            DiagnosticData["IsAttached"] = IsAttached;
            DiagnosticData["IsActivated"] = IsActivated;
            DiagnosticData["HasObject"] = HasObject;
            DiagnosticData["GripPosition"] = GripPosition;
            DiagnosticData["CurrentForce"] = CurrentForce;
            
            if (ToolType == ManipulatorToolType.VacuumGripper)
            {
                DiagnosticData["VacuumLevel"] = _currentVacuum;
                DiagnosticData["MaxAirFlow"] = _maxAirFlow;
            }
            
            DiagnosticData["CyclesToMaintenance"] = _cyclesToNextMaintenance;
        }
        
        public override bool Start()
        {
            if (base.Start())
            {
                // Reset state
                GripTarget = 0; // Open
                IsActivated = false;
                HasObject = false;
                
                return true;
            }
            return false;
        }
        
        public override void Stop()
        {
            // Release any gripped object
            Deactivate();
            base.Stop();
        }
        
        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);
            
            if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
                return;
                
            // Update gripper movement
            if (_isMoving)
            {
                UpdateGripperMovement(elapsedTime);
            }
            
            // Update vacuum level for vacuum grippers
            if (ToolType == ManipulatorToolType.VacuumGripper)
            {
                UpdateVacuumLevel(elapsedTime);
            }
            
            // Update diagnostics
            UpdateDiagnostics();
        }

        private void UpdateGripperMovement(TimeSpan elapsedTime)
        {
            if (Math.Abs(GripPosition - GripTarget) < 0.1)
            {
                _isMoving = false;
                GripPosition = GripTarget;
                return;
            }
            
            // Calculate movement direction and distance
            double direction = GripTarget > GripPosition ? 1 : -1;
            double maxMove = _movementSpeed * elapsedTime.TotalSeconds;
            double actualMove = Math.Min(Math.Abs(GripTarget - GripPosition), maxMove);
            
            // Update position
            GripPosition += direction * actualMove;
            
            // Calculate corresponding width
            _currentWidth = OpeningWidth - (GripPosition / 100.0) * (OpeningWidth - ClosedWidth);
            
            // Check for object contact
            if (HasObject && GripPosition > 50)
            {
                // Calculate force based on how far we're "squeezing"
                double overTravel = Math.Max(0, GripPosition - 70);
                CurrentForce = MaxForce * (overTravel / 30.0);
            }
            else
            {
                CurrentForce = 0;
            }
            
            // Update diagnostics
            DiagnosticData["GripPosition"] = GripPosition;
            DiagnosticData["CurrentWidth"] = _currentWidth;
            DiagnosticData["CurrentForce"] = CurrentForce;
        }

        private void UpdateVacuumLevel(TimeSpan elapsedTime)
        {
            if (IsActivated)
            {
                // Build up vacuum
                double targetVacuum = HasObject ? 85 : 90;
                double vacuumBuildRate = 150; // kPa per second
                
                double change = vacuumBuildRate * elapsedTime.TotalSeconds;
                _currentVacuum = Math.Min(_currentVacuum + change, targetVacuum);
                
                // If we're nearly at vacuum but don't have an object, simulate a leak
                if (_currentVacuum > 80 && !HasObject)
                {
                    _currentVacuum = 82 + (Random.NextDouble() * 4);
                    
                    // Random chance of picking up a simulated object
                    if (Random.NextDouble() < 0.01)
                    {
                        HasObject = true;
                        AddAlarm("VACUUM_GRIP", "Object acquired with vacuum", AlarmSeverity.Information);
                    }
                }
            }
            else
            {
                // Release vacuum
                double vacuumReleaseRate = 300; // kPa per second
                double change = vacuumReleaseRate * elapsedTime.TotalSeconds;
                _currentVacuum = Math.Max(_currentVacuum - change, 0);
                
                if (_currentVacuum < 20 && HasObject)
                {
                    HasObject = false;
                    AddAlarm("VACUUM_RELEASE", "Object released from vacuum", AlarmSeverity.Information);
                }
            }
            
            DiagnosticData["VacuumLevel"] = _currentVacuum;
        }
        
        private void UpdateDiagnostics()
        {
            DiagnosticData["IsAttached"] = IsAttached;
            DiagnosticData["IsActivated"] = IsActivated;
            DiagnosticData["HasObject"] = HasObject;
            DiagnosticData["GripPosition"] = GripPosition;
            DiagnosticData["CurrentForce"] = CurrentForce;
            DiagnosticData["CyclesToMaintenance"] = _cyclesToNextMaintenance;
            
            if (ToolType == ManipulatorToolType.VacuumGripper)
            {
                DiagnosticData["VacuumLevel"] = _currentVacuum;
            }
        }
        
        #region Public Control Methods
        
        /// <summary>
        /// Activates the gripper (close mechanical gripper or enable vacuum)
        /// </summary>
        public void Activate()
        {
            if (Status != DeviceStatus.Running)
            {
                AddAlarm("TOOL_NOT_READY", "Tool is not ready for activation", AlarmSeverity.Warning);
                return;
            }
            
            IsActivated = true;
            _cyclesToNextMaintenance--;
            
            if (ToolType == ManipulatorToolType.VacuumGripper)
            {
                AddAlarm("VACUUM_ON", "Vacuum generator activated", AlarmSeverity.Information);
            }
            else
            {
                // For mechanical grippers, close to gripping position
                GripTarget = 80; // 80% closed
                _isMoving = true;
                AddAlarm("GRIPPER_CLOSING", "Mechanical gripper closing", AlarmSeverity.Information);
                
                // Simulate object detection
                if (Random.NextDouble() < 0.9) // 90% chance of successful grip
                {
                    HasObject = true;
                }
            }
        }
        
        /// <summary>
        /// Deactivates the gripper (open mechanical gripper or disable vacuum)
        /// </summary>
        public void Deactivate()
        {
            IsActivated = false;
            
            if (ToolType == ManipulatorToolType.VacuumGripper)
            {
                AddAlarm("VACUUM_OFF", "Vacuum generator deactivated", AlarmSeverity.Information);
            }
            else
            {
                // For mechanical grippers, open
                GripTarget = 0; // Fully open
                _isMoving = true;
                AddAlarm("GRIPPER_OPENING", "Mechanical gripper opening", AlarmSeverity.Information);
                
                // Release object
                HasObject = false;
            }
        }
        
        /// <summary>
        /// Sets the target position for adjustable grippers
        /// </summary>
        /// <param name="position">Position from 0 (open) to 100 (closed) percent</param>
        public void SetPosition(double position)
        {
            position = Math.Min(Math.Max(position, 0), 100);
            GripTarget = position;
            _isMoving = true;
            
            AddAlarm("GRIP_POSITION", $"Gripper position set to {position:F1}%", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Sets the force for the gripper
        /// </summary>
        /// <param name="forcePercent">Force from 0 to 100 percent of maximum</param>
        public void SetForce(double forcePercent)
        {
            if (ToolType == ManipulatorToolType.VacuumGripper)
            {
                AddAlarm("INVALID_OPERATION", "Cannot set force for vacuum gripper", AlarmSeverity.Warning);
                return;
            }
            
            forcePercent = Math.Min(Math.Max(forcePercent, 10), 100);
            MaxForce = forcePercent;
            
            AddAlarm("GRIP_FORCE", $"Gripper force set to {forcePercent:F1}%", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Perform maintenance on the tool
        /// </summary>
        public void PerformMaintenance()
        {
            // Reset maintenance counter
            _cyclesToNextMaintenance = 10000;
            
            AddAlarm("TOOL_MAINTENANCE", "Tool maintenance performed", AlarmSeverity.Information);
        }
        
        #endregion
        
        protected override void SimulateFault()
        {
            int faultType = Random.Next(3);
            
            switch (faultType)
            {
                case 0: // Pneumatic pressure loss (for vacuum or pneumatic grippers)
                    if (ToolType == ManipulatorToolType.VacuumGripper)
                    {
                        AddAlarm("VACUUM_LOSS", "Vacuum pressure loss detected", AlarmSeverity.Major);
                        _currentVacuum *= 0.5;
                        if (HasObject && _currentVacuum < 40)
                        {
                            HasObject = false;
                            AddAlarm("OBJECT_LOST", "Object lost due to vacuum failure", AlarmSeverity.Critical);
                        }
                    }
                    else
                    {
                        AddAlarm("PNEUMATIC_PRESSURE", "Pneumatic pressure issue detected", AlarmSeverity.Major);
                        _movementSpeed *= 0.5;
                        CurrentForce *= 0.7;
                    }
                    break;
                    
                case 1: // Mechanical jam
                    AddAlarm("GRIPPER_JAM", "Mechanical jam detected in gripper", AlarmSeverity.Major);
                    _isMoving = false;
                    GripTarget = GripPosition;
                    Status = DeviceStatus.Fault;
                    break;
                    
                case 2: // Sensor failure
                    AddAlarm("SENSOR_FAILURE", "Gripper position sensor failure", AlarmSeverity.Warning);
                    // Simulate erratic position readings
                    GripPosition += (Random.NextDouble() * 20) - 10;
                    GripPosition = Math.Min(Math.Max(GripPosition, 0), 100);
                    break;
            }
        }
    }
    
    /// <summary>
    /// Types of end effectors for robotic manipulation
    /// </summary>
    public enum ManipulatorToolType
    {
        MechanicalGripper,
        VacuumGripper,
        DualGripper,
        SyringeTool,
        SterilizedPicker,
        VialHandler,
        LidHandler,
        CustomTool
    }
}