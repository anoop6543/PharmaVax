using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PharmaceuticalProcess.HardwareComponents.Actuators
{
    /// <summary>
    /// Simulates a robotic arm used for precise handling of pharmaceutical components in sterile environments
    /// </summary>
    public class RoboticArm : DeviceBase
    {
        public override DeviceType Type => DeviceType.Actuator;

        // Configuration properties
        public RobotType RobotType { get; private set; }
        public double ReachRadius { get; private set; }   // Maximum reach in mm
        public double Payload { get; private set; }       // Maximum payload in kg
        public double PositionalAccuracy { get; private set; } // Accuracy in mm
        public int AxisCount { get; private set; }        // Number of robot axes

        // State properties
        public RobotOperationMode OperationMode { get; private set; }
        public RobotStatus RobotOperationalStatus { get; private set; }
        public bool IsHomed { get; private set; }
        public Vector3 CurrentPosition { get; private set; }     // X,Y,Z in mm
        public Vector3 CurrentOrientation { get; private set; }  // Roll, Pitch, Yaw in degrees
        public Vector3 TargetPosition { get; private set; }      // Target X,Y,Z
        public Vector3 TargetOrientation { get; private set; }   // Target orientation
        public double CurrentSpeed { get; private set; }         // % of maximum speed
        public double MoveCompletion { get; private set; }       // % completion of current move
        public Manipulator AttachedTool { get; private set; }    // Current end-effector
        public bool IsSafetyPerimeterActive { get; private set; }

        // Internal state tracking
        private Queue<RobotMovement> _moveQueue;
        private RobotMovement _currentMove;
        private bool _isMoving;
        private double _moveStartTime;
        private Vector3 _moveStartPosition;
        private Vector3 _moveStartOrientation;
        private double _motionPlanningTime;
        private readonly Dictionary<string, RobotPosition> _teachPositions;
        private double _maintenanceCountdown;
        private double _cyclesToNextCalibration;
        private double _totalMoveDistance;
        private double _wearFactor;
        private readonly List<VisionSystem> _connectedVisionSystems;
        
        // Tracking for dynamics simulation
        private double _torqueUtilization;    // % of maximum torque
        private double _accelerationRate;     // Current acceleration in %/s²
        private double _decelerationRate;     // Current deceleration in %/s²
        private double _powerConsumption;     // kW
        private double _temperature;          // °C

        /// <summary>
        /// Creates a new robotic arm simulation
        /// </summary>
        /// <param name="deviceId">Unique device identifier</param>
        /// <param name="name">Human-readable device name</param>
        /// <param name="robotType">Type of robot</param>
        /// <param name="reachRadius">Maximum reach in mm</param>
        /// <param name="payload">Maximum payload in kg</param>
        /// <param name="accuracy">Positional accuracy in mm</param>
        /// <param name="axisCount">Number of robot axes</param>
        public RoboticArm(
            string deviceId,
            string name,
            RobotType robotType = RobotType.Articulated6Axis, 
            double reachRadius = 1200,
            double payload = 5.0,
            double accuracy = 0.1,
            int axisCount = 6)
            : base(deviceId, name)
        {
            RobotType = robotType;
            ReachRadius = reachRadius;
            Payload = payload;
            PositionalAccuracy = accuracy;
            AxisCount = axisCount;
            
            // Initialize properties
            CurrentPosition = new Vector3(0, 0, 500);       // Default position above base
            CurrentOrientation = new Vector3(0, 0, 0);      // Default orientation
            TargetPosition = CurrentPosition;
            TargetOrientation = CurrentOrientation;
            RobotOperationalStatus = RobotStatus.PoweredOff;
            OperationMode = RobotOperationMode.Automatic;
            IsHomed = false;
            CurrentSpeed = 50;                              // Default 50% speed
            IsSafetyPerimeterActive = true;
            _isMoving = false;
            _moveQueue = new Queue<RobotMovement>();
            _teachPositions = new Dictionary<string, RobotPosition>();
            _maintenanceCountdown = 5000;                   // Hours until maintenance
            _cyclesToNextCalibration = 1000;                // Cycles until calibration
            _wearFactor = 0.0;                              // 0% wear (new)
            _connectedVisionSystems = new List<VisionSystem>();
            
            // Dynamics simulation
            _accelerationRate = 120;      // 120%/s² acceleration
            _decelerationRate = 150;      // 150%/s² deceleration
            _temperature = 25.0;          // 25°C initial temperature
            _powerConsumption = 0.0;      // 0kW when idle
            _torqueUtilization = 0.0;     // 0% torque utilization
            
            // Initialize diagnostic data
            InitializeDiagnostics();
        }

        private void InitializeDiagnostics()
        {
            DiagnosticData["RobotType"] = RobotType.ToString();
            DiagnosticData["ReachRadius"] = ReachRadius;
            DiagnosticData["Payload"] = Payload;
            DiagnosticData["PositionalAccuracy"] = PositionalAccuracy;
            DiagnosticData["AxisCount"] = AxisCount;
            DiagnosticData["OperationMode"] = OperationMode.ToString();
            DiagnosticData["RobotStatus"] = RobotOperationalStatus.ToString();
            DiagnosticData["IsHomed"] = IsHomed;
            DiagnosticData["CurrentSpeed"] = CurrentSpeed;
            DiagnosticData["IsSafetyPerimeterActive"] = IsSafetyPerimeterActive;
            DiagnosticData["Position"] = $"X:{CurrentPosition.X:F2}, Y:{CurrentPosition.Y:F2}, Z:{CurrentPosition.Z:F2}";
            DiagnosticData["Orientation"] = $"R:{CurrentOrientation.X:F2}, P:{CurrentOrientation.Y:F2}, Y:{CurrentOrientation.Z:F2}";
            DiagnosticData["AttachedTool"] = AttachedTool?.Name ?? "None";
            DiagnosticData["PowerConsumption"] = _powerConsumption;
            DiagnosticData["Temperature"] = _temperature;
            DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;
            DiagnosticData["WearFactor"] = _wearFactor;
        }

        public override void Initialize()
        {
            base.Initialize();
            
            RobotOperationalStatus = RobotStatus.PoweredOff;
            IsHomed = false;
            _isMoving = false;
            _moveQueue.Clear();
            CurrentPosition = new Vector3(0, 0, 500);
            CurrentOrientation = new Vector3(0, 0, 0);
            IsSafetyPerimeterActive = true;
            _powerConsumption = 0.0;
            _temperature = 25.0;
            
            UpdateDiagnostics();
        }
        
        public override bool Start()
        {
            if (base.Start())
            {
                RobotOperationalStatus = RobotStatus.PoweringUp;
                _powerConsumption = 0.3; // Base power consumption when powered on
                AddAlarm("ROBOT_POWERING", $"Robot {Name} powering up", AlarmSeverity.Information);
                return true;
            }
            return false;
        }
        
        public override void Stop()
        {
            if (_isMoving)
            {
                AddAlarm("ROBOT_HALTING", "Robot halting movement immediately", AlarmSeverity.Warning);
            }
            
            _isMoving = false;
            _moveQueue.Clear();
            RobotOperationalStatus = RobotStatus.PoweredOff;
            _powerConsumption = 0.0;
            
            base.Stop();
        }
        
        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);
            
            if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
                return;

            // Update power-up sequence
            if (RobotOperationalStatus == RobotStatus.PoweringUp)
            {
                UpdatePowerUpSequence(elapsedTime);
            }
            
            // If robot is homing
            if (RobotOperationalStatus == RobotStatus.Homing)
            {
                UpdateHomingSequence(elapsedTime);
            }

            // Process current robot movement
            if (_isMoving && RobotOperationalStatus == RobotStatus.Ready)
            {
                UpdateRobotMovement(elapsedTime);
            }
            // Process movement queue if robot is ready and not moving
            else if (!_isMoving && RobotOperationalStatus == RobotStatus.Ready && _moveQueue.Count > 0)
            {
                StartNextMovement();
            }
            
            // Update dynamics (temperature, power consumption)
            UpdateRobotDynamics(elapsedTime);
            
            // Update maintenance counters
            if (RobotOperationalStatus == RobotStatus.Ready || RobotOperationalStatus == RobotStatus.Busy)
            {
                _maintenanceCountdown -= elapsedTime.TotalHours;
            }
            
            // Check for alarm conditions
            CheckAlarmConditions();
            
            // Update diagnostics
            UpdateDiagnostics();
        }
        
        private void UpdatePowerUpSequence(TimeSpan elapsedTime)
        {
            // Simulate power-up sequence taking 5 seconds
            _motionPlanningTime += elapsedTime.TotalSeconds;
            
            if (_motionPlanningTime >= 5.0)
            {
                _motionPlanningTime = 0.0;
                RobotOperationalStatus = RobotStatus.Ready;
                AddAlarm("ROBOT_READY", $"Robot {Name} powered up and ready", AlarmSeverity.Information);
            }
        }
        
        private void UpdateHomingSequence(TimeSpan elapsedTime)
        {
            // Simulate homing sequence taking 10 seconds
            _motionPlanningTime += elapsedTime.TotalSeconds;
            
            // Update position during homing - moving toward home position
            if (_motionPlanningTime <= 10.0)
            {
                double progress = _motionPlanningTime / 10.0;
                CurrentPosition = new Vector3(
                    (1 - progress) * CurrentPosition.X,
                    (1 - progress) * CurrentPosition.Y,
                    500 + (1 - progress) * (CurrentPosition.Z - 500)
                );
                
                // Smooth orientation to zero
                CurrentOrientation = new Vector3(
                    (1 - progress) * CurrentOrientation.X,
                    (1 - progress) * CurrentOrientation.Y,
                    (1 - progress) * CurrentOrientation.Z
                );
            }
            
            if (_motionPlanningTime >= 10.0)
            {
                _motionPlanningTime = 0.0;
                RobotOperationalStatus = RobotStatus.Ready;
                IsHomed = true;
                CurrentPosition = new Vector3(0, 0, 500); // Home position above base
                CurrentOrientation = new Vector3(0, 0, 0); // Home orientation
                _powerConsumption = 0.3; // Base power consumption when idle
                AddAlarm("ROBOT_HOMED", $"Robot {Name} homing complete", AlarmSeverity.Information);
            }
        }
        
        private void UpdateRobotMovement(TimeSpan elapsedTime)
        {
            if (_currentMove == null)
            {
                _isMoving = false;
                return;
            }
            
            // Calculate move progress
            _motionPlanningTime += elapsedTime.TotalSeconds;
            double linearDistance = Vector3.Distance(_moveStartPosition, _currentMove.TargetPosition);
            double moveSpeed = (_currentMove.Speed / 100.0) * 250.0; // mm/s at 100% speed
            double moveDuration = linearDistance / moveSpeed;
            moveDuration = Math.Max(moveDuration, 0.5); // Minimum move duration for very short moves
            
            // Add acceleration and deceleration ramps (trapezoidal motion profile)
            moveDuration += 0.4; // Add time for acceleration and deceleration
            double progress = Math.Min(_motionPlanningTime / moveDuration, 1.0);
            
            // Calculate smooth motion curve with acceleration and deceleration
            double smoothedProgress = CalculateSmoothMotionProfile(progress);
            MoveCompletion = progress * 100.0;
            
            // Update position with smooth interpolation
            CurrentPosition = new Vector3(
                _moveStartPosition.X + (float)(smoothedProgress * (_currentMove.TargetPosition.X - _moveStartPosition.X)),
                _moveStartPosition.Y + (float)(smoothedProgress * (_currentMove.TargetPosition.Y - _moveStartPosition.Y)),
                _moveStartPosition.Z + (float)(smoothedProgress * (_currentMove.TargetPosition.Z - _moveStartPosition.Z))
            );

            // Update orientation with smooth interpolation
            CurrentOrientation = new Vector3(
                _moveStartOrientation.X + (float)(smoothedProgress * (_currentMove.TargetOrientation.X - _moveStartOrientation.X)),
                _moveStartOrientation.Y + (float)(smoothedProgress * (_currentMove.TargetOrientation.Y - _moveStartOrientation.Y)),
                _moveStartOrientation.Z + (float)(smoothedProgress * (_currentMove.TargetOrientation.Z - _moveStartOrientation.Z))
            );
            
            // Calculate load-dependent values
            double distanceFactor = linearDistance / ReachRadius;
            _torqueUtilization = 20 + (30 * distanceFactor) + (_currentMove.EstimatedLoad / Payload) * 50;
            _torqueUtilization *= (smoothedProgress < 0.5) ? (smoothedProgress * 2.0) : (2.0 * (1.0 - smoothedProgress));
            _powerConsumption = 0.3 + (_torqueUtilization / 100.0) * 2.2; // 0.3-2.5kW based on torque
            
            // Check if motion is complete
            if (progress >= 1.0)
            {
                CompleteCurrentMovement();
            }
        }
        
        private double CalculateSmoothMotionProfile(double progress)
        {
            // S-curve motion profile using sine acceleration
            if (progress < 0.2)
            {
                // Acceleration phase (0-20% of time)
                return 0.5 * (1 - Math.Cos(Math.PI * progress / 0.2)) * 0.2;
            }
            else if (progress < 0.8)
            {
                // Constant velocity phase (20-80% of time)
                return 0.2 + (progress - 0.2) * (0.6 / 0.6);
            }
            else
            {
                // Deceleration phase (80-100% of time)
                return 0.8 + 0.5 * (1 - Math.Cos(Math.PI * (progress - 0.8) / 0.2)) * 0.2;
            }
        }
        
        private void StartNextMovement()
        {
            if (_moveQueue.Count == 0 || RobotOperationalStatus != RobotStatus.Ready)
                return;

            _currentMove = _moveQueue.Dequeue();
            _isMoving = true;
            _moveStartTime = 0.0;
            _motionPlanningTime = 0.0;
            _moveStartPosition = CurrentPosition;
            _moveStartOrientation = CurrentOrientation;
            RobotOperationalStatus = RobotStatus.Busy;
            
            // Plan the motion (simulated time delay for path planning)
            _moveStartTime = 0.1 + (Vector3.Distance(_moveStartPosition, _currentMove.TargetPosition) / ReachRadius) * 0.2;
            
            // Update wear statistics
            _cyclesToNextCalibration--;
            _totalMoveDistance += Vector3.Distance(_moveStartPosition, _currentMove.TargetPosition);
            _wearFactor = Math.Min(100.0, _totalMoveDistance / 1000000.0 * 100.0); // 1,000,000 mm = 100% wear
            
            // Update target values
            TargetPosition = _currentMove.TargetPosition;
            TargetOrientation = _currentMove.TargetOrientation;
            
            // Verify tool is correct
            if (_currentMove.RequiredTool != null && 
                (AttachedTool == null || AttachedTool.ToolType != _currentMove.RequiredTool))
            {
                AddAlarm("TOOL_MISMATCH", 
                    $"Required tool {_currentMove.RequiredTool} not attached for current operation", 
                    AlarmSeverity.Warning);
            }
            
            // Log movement
            AddAlarm("ROBOT_MOVING", 
                $"Moving to {_currentMove.OperationName}: X={_currentMove.TargetPosition.X:F1}, Y={_currentMove.TargetPosition.Y:F1}, Z={_currentMove.TargetPosition.Z:F1}", 
                AlarmSeverity.Information);
        }
        
        private void CompleteCurrentMovement()
        {
            if (_currentMove == null)
                return;
                
            // Snap exactly to target position
            CurrentPosition = _currentMove.TargetPosition;
            CurrentOrientation = _currentMove.TargetOrientation;
            
            // Fire completion message
            AddAlarm("MOVE_COMPLETE", $"Completed move to {_currentMove.OperationName}", AlarmSeverity.Information);
            
            // Execute the operation callback if provided
            _currentMove.OperationCompleteAction?.Invoke();
            
            // Reset movement state
            _isMoving = false;
            _motionPlanningTime = 0.0;
            _currentMove = null;
            MoveCompletion = 0.0;
            RobotOperationalStatus = RobotStatus.Ready;
        }
        
        private void UpdateRobotDynamics(TimeSpan elapsedTime)
        {
            // Update temperature based on power consumption
            double ambientTemp = 25.0;
            double maxTemp = 75.0;
            double heatFactor = Math.Min(1.0, _powerConsumption / 3.0); // Normalized heat generation
            double coolingFactor = Math.Max(0.1, (_temperature - ambientTemp) / (maxTemp - ambientTemp));
            
            // Temperature rises with power consumption, falls based on difference from ambient
            _temperature += (heatFactor * 5.0 - coolingFactor * 2.0) * elapsedTime.TotalSeconds;
            _temperature = Math.Max(ambientTemp, Math.Min(maxTemp, _temperature));
        }
        
        private void CheckAlarmConditions()
        {
            // Check temperature
            if (_temperature > 70)
            {
                AddAlarm("HIGH_TEMP", $"Robot temperature high: {_temperature:F1}°C", AlarmSeverity.Warning);
            }
            
            // Check maintenance
            if (_maintenanceCountdown <= 0)
            {
                AddAlarm("MAINTENANCE_DUE", "Robot maintenance required", AlarmSeverity.Warning);
            }
            
            // Check calibration
            if (_cyclesToNextCalibration <= 0)
            {
                AddAlarm("CALIBRATION_DUE", "Robot calibration required", AlarmSeverity.Warning);
                PositionalAccuracy *= 1.5; // Degraded accuracy
            }
            
            // Check positioning errors based on wear
            if (_wearFactor > 80)
            {
                AddAlarm("EXCESSIVE_WEAR", "Robot shows signs of excessive wear", AlarmSeverity.Warning);
                PositionalAccuracy *= 1.0 + (_wearFactor - 80) / 100.0; // Degraded accuracy
            }
        }
        
        private void UpdateDiagnostics()
        {
            DiagnosticData["RobotStatus"] = RobotOperationalStatus.ToString();
            DiagnosticData["IsHomed"] = IsHomed;
            DiagnosticData["CurrentSpeed"] = CurrentSpeed;
            DiagnosticData["Position"] = $"X:{CurrentPosition.X:F2}, Y:{CurrentPosition.Y:F2}, Z:{CurrentPosition.Z:F2}";
            DiagnosticData["Orientation"] = $"R:{CurrentOrientation.X:F2}, P:{CurrentOrientation.Y:F2}, Y:{CurrentOrientation.Z:F2}";
            DiagnosticData["IsMoving"] = _isMoving;
            DiagnosticData["MoveProgress"] = MoveCompletion;
            DiagnosticData["TorqueUtilization"] = _torqueUtilization;
            DiagnosticData["PowerConsumption"] = _powerConsumption;
            DiagnosticData["Temperature"] = _temperature;
            DiagnosticData["MaintenanceCountdown"] = _maintenanceCountdown;
            DiagnosticData["CyclesToCalibration"] = _cyclesToNextCalibration;
            DiagnosticData["WearFactor"] = _wearFactor;
            DiagnosticData["PositionalAccuracy"] = PositionalAccuracy;
            DiagnosticData["QueuedMoves"] = _moveQueue.Count;
            if (AttachedTool != null)
            {
                DiagnosticData["AttachedTool"] = AttachedTool.Name;
                DiagnosticData["ToolType"] = AttachedTool.ToolType.ToString();
            }
        }
        
        #region Public Control Methods
        
        /// <summary>
        /// Homes the robot to its reference position
        /// </summary>
        public bool Home()
        {
            if (RobotOperationalStatus == RobotStatus.PoweredOff)
            {
                AddAlarm("HOME_FAILED", "Cannot home robot: power is off", AlarmSeverity.Warning);
                return false;
            }
            
            if (_isMoving)
            {
                AddAlarm("HOME_FAILED", "Cannot home robot: robot is moving", AlarmSeverity.Warning);
                return false;
            }
            
            // Clear the move queue
            _moveQueue.Clear();
            _currentMove = null;
            _isMoving = false;
            
            // Start homing sequence
            RobotOperationalStatus = RobotStatus.Homing;
            _motionPlanningTime = 0;
            AddAlarm("HOMING", "Robot homing sequence initiated", AlarmSeverity.Information);
            
            return true;
        }
        
        /// <summary>
        /// Moves the robot to a specific position and orientation
        /// </summary>
        public bool MoveTo(Vector3 position, Vector3 orientation, double speed = 50, string operationName = "Position", ManipulatorToolType? requiredTool = null)
        {
            if (!IsRobotOperational())
                return false;
                
            // Validate position is within reach
            if (Vector3.Distance(Vector3.Zero, position) > ReachRadius)
            {
                AddAlarm("MOVE_ERROR", $"Position X:{position.X}, Y:{position.Y}, Z:{position.Z} is outside robot reach", AlarmSeverity.Warning);
                return false;
            }
            
            // Validate speed
            speed = Math.Max(1, Math.Min(100, speed));
            
            // Create and queue the move
            var move = new RobotMovement
            {
                TargetPosition = position,
                TargetOrientation = orientation,
                Speed = speed,
                EstimatedLoad = AttachedTool?.Weight ?? 0,
                OperationName = operationName,
                RequiredTool = requiredTool
            };
            
            _moveQueue.Enqueue(move);
            AddAlarm("MOVE_QUEUED", $"Move to {operationName} queued, position: X:{position.X:F1}, Y:{position.Y:F1}, Z:{position.Z:F1}", AlarmSeverity.Information);
            
            return true;
        }
        
        /// <summary>
        /// Moves the robot to a taught position by name
        /// </summary>
        public bool MoveToTeachPosition(string positionName, double speed = 50)
        {
            if (!_teachPositions.ContainsKey(positionName))
            {
                AddAlarm("UNKNOWN_POSITION", $"Teach position '{positionName}' not found", AlarmSeverity.Warning);
                return false;
            }
            
            var position = _teachPositions[positionName];
            return MoveTo(position.Position, position.Orientation, speed, positionName, position.RequiredTool);
        }
        
        /// <summary>
        /// Stores the current position as a named teach point
        /// </summary>
        public bool TeachPosition(string positionName, ManipulatorToolType? requiredTool = null)
        {
            if (!IsRobotOperational())
                return false;
                
            _teachPositions[positionName] = new RobotPosition 
            { 
                Position = CurrentPosition, 
                Orientation = CurrentOrientation,
                RequiredTool = requiredTool
            };
            
            AddAlarm("POSITION_TAUGHT", $"Position '{positionName}' stored", AlarmSeverity.Information);
            return true;
        }
        
        /// <summary>
        /// Attaches a tool to the robot
        /// </summary>
        public bool AttachTool(Manipulator tool)
        {
            if (tool == null)
            {
                AddAlarm("TOOL_ERROR", "Cannot attach null tool", AlarmSeverity.Warning);
                return false;
            }
            
            if (!IsRobotOperational())
                return false;
                
            if (_isMoving)
            {
                AddAlarm("TOOL_ERROR", "Cannot change tools while robot is moving", AlarmSeverity.Warning);
                return false;
            }
            
            // Detach current tool first
            if (AttachedTool != null)
            {
                DetachTool();
            }
            
            // Attach new tool
            AttachedTool = tool;
            tool.IsAttached = true;
            
            AddAlarm("TOOL_ATTACHED", $"Tool {tool.Name} attached", AlarmSeverity.Information);
            return true;
        }
        
        /// <summary>
        /// Detaches the current tool
        /// </summary>
        public bool DetachTool()
        {
            if (!IsRobotOperational())
                return false;
                
            if (_isMoving)
            {
                AddAlarm("TOOL_ERROR", "Cannot change tools while robot is moving", AlarmSeverity.Warning);
                return false;
            }
            
            if (AttachedTool == null)
            {
                // No tool to detach
                return true;
            }
            
            // Detach tool
            AttachedTool.IsAttached = false;
            var detachedToolName = AttachedTool.Name;
            AttachedTool = null;
            
            AddAlarm("TOOL_DETACHED", $"Tool {detachedToolName} detached", AlarmSeverity.Information);
            return true;
        }
        
        /// <summary>
        /// Sets the operation mode of the robot
        /// </summary>
        public void SetOperationMode(RobotOperationMode mode)
        {
            if (RobotOperationalStatus == RobotStatus.PoweredOff)
            {
                AddAlarm("MODE_FAILED", "Cannot change mode: robot is powered off", AlarmSeverity.Warning);
                return;
            }
            
            // If switching to manual mode, clear the queue
            if (mode == RobotOperationMode.Manual && OperationMode != RobotOperationMode.Manual)
            {
                _moveQueue.Clear();
                if (_isMoving)
                {
                    AddAlarm("MOVE_CANCELED", "Current movement canceled due to mode change", AlarmSeverity.Warning);
                    _isMoving = false;
                    RobotOperationalStatus = RobotStatus.Ready;
                }
            }
            
            OperationMode = mode;
            AddAlarm("MODE_CHANGED", $"Operation mode changed to {mode}", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Connects a vision system to the robot for vision-guided operations
        /// </summary>
        public void ConnectVisionSystem(VisionSystem visionSystem)
        {
            if (visionSystem == null)
                return;
                
            if (!_connectedVisionSystems.Contains(visionSystem))
            {
                _connectedVisionSystems.Add(visionSystem);
                AddAlarm("VISION_CONNECTED", $"Vision system {visionSystem.Name} connected to robot", AlarmSeverity.Information);
            }
        }
        
        /// <summary>
        /// Performs a vision-guided pick operation
        /// </summary>
        public bool PerformVisionGuidedPick(string targetObject, double approachHeight = 50, double speed = 30)
        {
            if (!IsRobotOperational() || AttachedTool == null || _connectedVisionSystems.Count == 0)
            {
                AddAlarm("VISION_PICK_FAILED", "Vision pick prerequisites not met", AlarmSeverity.Warning);
                return false;
            }
            
            // Ask vision system for object location
            Vector3? objectLocation = null;
            foreach (var vision in _connectedVisionSystems)
            {
                if (vision.Status == DeviceStatus.Running)
                {
                    objectLocation = vision.GetObjectPosition(targetObject);
                    if (objectLocation.HasValue)
                        break;
                }
            }
            
            if (!objectLocation.HasValue)
            {
                AddAlarm("VISION_PICK_FAILED", $"Object {targetObject} not found by vision system", AlarmSeverity.Warning);
                return false;
            }
            
            // Create a sequence of moves for the pick operation
            Vector3 approachPosition = new Vector3(objectLocation.Value.X, objectLocation.Value.Y, objectLocation.Value.Z + approachHeight);
            Vector3 pickPosition = objectLocation.Value;
            Vector3 departPosition = approachPosition;
            
            // Queue the movements
            MoveTo(approachPosition, CurrentOrientation, speed, $"Approach {targetObject}");
            
            // Add gripper-specific orientation
            Vector3 pickOrientation = AttachedTool.ToolType switch
            {
                ManipulatorToolType.VacuumGripper => new Vector3(0, 0, 0),
                ManipulatorToolType.MechanicalGripper => new Vector3(0, 90, 0),
                _ => CurrentOrientation
            };
            
            // Move to pick position
            MoveTo(pickPosition, pickOrientation, speed * 0.5, $"Pick {targetObject}");
            
            // Activate the tool
            _moveQueue.Enqueue(new RobotMovement
            {
                TargetPosition = pickPosition,
                TargetOrientation = pickOrientation,
                Speed = speed * 0.5,
                OperationName = "Grip object",
                OperationCompleteAction = () => AttachedTool?.Activate()
            });
            
            // Wait for tool activation
            _moveQueue.Enqueue(new RobotMovement
            {
                TargetPosition = pickPosition,
                TargetOrientation = pickOrientation,
                Speed = speed * 0.5,
                OperationName = "Wait for grip",
                EstimatedLoad = AttachedTool?.Weight ?? 0 + 0.2, // Add object weight
                OperationCompleteAction = () => System.Threading.Thread.Sleep(500)
            });
            
            // Move to depart position with object
            MoveTo(departPosition, pickOrientation, speed, $"Depart with {targetObject}");
            
            return true;
        }
        
        /// <summary>
        /// Performs a vision-guided place operation
        /// </summary>
        public bool PerformVisionGuidedPlace(string targetLocation, double approachHeight = 50, double speed = 30)
        {
            if (!IsRobotOperational() || AttachedTool == null || _connectedVisionSystems.Count == 0)
            {
                AddAlarm("VISION_PLACE_FAILED", "Vision place prerequisites not met", AlarmSeverity.Warning);
                return false;
            }
            
            // Ask vision system for target location
            Vector3? placeLocation = null;
            foreach (var vision in _connectedVisionSystems)
            {
                if (vision.Status == DeviceStatus.Running)
                {
                    placeLocation = vision.GetObjectPosition(targetLocation);
                    if (placeLocation.HasValue)
                        break;
                }
            }
            
            if (!placeLocation.HasValue)
            {
                AddAlarm("VISION_PLACE_FAILED", $"Location {targetLocation} not found by vision system", AlarmSeverity.Warning);
                return false;
            }
            
            // Create a sequence of moves for the place operation
            Vector3 approachPosition = new Vector3(placeLocation.Value.X, placeLocation.Value.Y, placeLocation.Value.Z + approachHeight);
            Vector3 placePosition = placeLocation.Value;
            Vector3 departPosition = approachPosition;
            
            // Queue the movements
            MoveTo(approachPosition, CurrentOrientation, speed, $"Approach {targetLocation}");
            MoveTo(placePosition, CurrentOrientation, speed * 0.5, $"Place at {targetLocation}");
            
            // Release the object
            _moveQueue.Enqueue(new RobotMovement
            {
                TargetPosition = placePosition,
                TargetOrientation = CurrentOrientation,
                Speed = speed * 0.5,
                OperationName = "Release object",
                OperationCompleteAction = () => AttachedTool?.Deactivate()
            });
            
            // Wait for tool deactivation
            _moveQueue.Enqueue(new RobotMovement
            {
                TargetPosition = placePosition,
                TargetOrientation = CurrentOrientation,
                Speed = speed * 0.5,
                OperationName = "Wait for release",
                OperationCompleteAction = () => System.Threading.Thread.Sleep(500)
            });
            
            // Move to depart position without object
            MoveTo(departPosition, CurrentOrientation, speed, $"Depart from {targetLocation}");
            
            return true;
        }
        
        /// <summary>
        /// Performs maintenance on the robot, resetting wear counters
        /// </summary>
        public void PerformMaintenance()
        {
            if (_isMoving || RobotOperationalStatus != RobotStatus.Ready && RobotOperationalStatus != RobotStatus.PoweredOff)
            {
                AddAlarm("MAINTENANCE_FAILED", "Cannot perform maintenance in current state", AlarmSeverity.Warning);
                return;
            }
            
            // Reset maintenance counters
            _maintenanceCountdown = 5000;
            _wearFactor *= 0.2; // Reduce wear to 20% of current value
            
            // Log maintenance
            AddAlarm("MAINTENANCE_PERFORMED", "Robot maintenance performed", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Performs calibration on the robot, resetting accuracy
        /// </summary>
        public void PerformCalibration()
        {
            if (_isMoving || RobotOperationalStatus != RobotStatus.Ready)
            {
                AddAlarm("CALIBRATION_FAILED", "Cannot perform calibration in current state", AlarmSeverity.Warning);
                return;
            }
            
            // Reset calibration counters
            _cyclesToNextCalibration = 1000;
            PositionalAccuracy = RobotType switch
            {
                RobotType.Delta => 0.05,
                RobotType.SCARA => 0.02,
                RobotType.Articulated6Axis => 0.1,
                RobotType.Cartesian => 0.03,
                _ => 0.1
            };
            
            // Log calibration
            AddAlarm("CALIBRATION_PERFORMED", $"Robot calibration performed. Accuracy: {PositionalAccuracy:F3}mm", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Checks if the robot is operational for movement commands
        /// </summary>
        private bool IsRobotOperational()
        {
            if (RobotOperationalStatus == RobotStatus.PoweredOff)
            {
                AddAlarm("ROBOT_NOT_READY", "Robot is powered off", AlarmSeverity.Warning);
                return false;
            }
            
            if (!IsHomed)
            {
                AddAlarm("ROBOT_NOT_HOMED", "Robot has not been homed", AlarmSeverity.Warning);
                return false;
            }
            
            if (RobotOperationalStatus != RobotStatus.Ready && RobotOperationalStatus != RobotStatus.Busy)
            {
                AddAlarm("ROBOT_NOT_READY", $"Robot is not ready: {RobotOperationalStatus}", AlarmSeverity.Warning);
                return false;
            }
            
            return true;
        }
        
        #endregion

        protected override void SimulateFault()
        {
            if (Status == DeviceStatus.Fault)
                return;
            
            int faultType = Random.Next(5);
            
            switch (faultType)
            {
                case 0: // Communication error
                    AddAlarm("ROBOT_COMM_ERROR", "Robot communication error", AlarmSeverity.Major);
                    Status = DeviceStatus.Fault;
                    if (_isMoving)
                    {
                        _isMoving = false;
                        RobotOperationalStatus = RobotStatus.Error;
                    }
                    break;
                    
                case 1: // Servo fault
                    AddAlarm("ROBOT_SERVO_FAULT", "Robot servo drive fault", AlarmSeverity.Critical);
                    Status = DeviceStatus.Fault;
                    RobotOperationalStatus = RobotStatus.Error;
                    IsHomed = false;
                    break;
                    
                case 2: // Collision detection
                    AddAlarm("ROBOT_COLLISION", "Collision detected", AlarmSeverity.Critical);
                    Status = DeviceStatus.Fault;
                    _isMoving = false;
                    RobotOperationalStatus = RobotStatus.EmergencyStop;
                    break;
                    
                case 3: // Position error
                    AddAlarm("ROBOT_POSITION_ERROR", "Position error exceeds threshold", AlarmSeverity.Major);
                    Status = DeviceStatus.Fault;
                    PositionalAccuracy *= 3; // Severe degradation in accuracy
                    RobotOperationalStatus = RobotStatus.Error;
                    break;
                    
                case 4: // Overheating
                    AddAlarm("ROBOT_OVERHEAT", "Robot motor overheat", AlarmSeverity.Major);
                    Status = DeviceStatus.Fault;
                    _temperature = 85.0;
                    RobotOperationalStatus = RobotStatus.Error;
                    break;
            }
        }
    }
    
    /// <summary>
    /// Represents a movement command for the robot
    /// </summary>
    public class RobotMovement
    {
        public Vector3 TargetPosition { get; set; }
        public Vector3 TargetOrientation { get; set; }
        public double Speed { get; set; }
        public double EstimatedLoad { get; set; }
        public string OperationName { get; set; }
        public ManipulatorToolType? RequiredTool { get; set; }
        public Action OperationCompleteAction { get; set; }
    }
    
    /// <summary>
    /// Represents a stored robot position
    /// </summary>
    public class RobotPosition
    {
        public Vector3 Position { get; set; }
        public Vector3 Orientation { get; set; }
        public ManipulatorToolType? RequiredTool { get; set; }
    }
    
    /// <summary>
    /// Defines the types of robots
    /// </summary>
    public enum RobotType
    {
        Articulated6Axis,
        SCARA,
        Delta,
        Cartesian,
        Collaborative
    }
    
    /// <summary>
    /// Defines the operational status of the robot
    /// </summary>
    public enum RobotStatus
    {
        PoweredOff,
        PoweringUp,
        Ready,
        Homing,
        Busy,
        Paused,
        Error,
        EmergencyStop
    }
    
    /// <summary>
    /// Defines the operation modes for the robot
    /// </summary>
    public enum RobotOperationMode
    {
        Automatic,
        Manual,
        Collaborative,
        ServiceMode
    }
}