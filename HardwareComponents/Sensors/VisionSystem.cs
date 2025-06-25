using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
    /// <summary>
    /// Advanced machine vision system for pharmaceutical automation applications
    /// </summary>
    public class VisionSystem : DeviceBase
    {
        public override DeviceType Type => DeviceType.Sensor;
        
        // Vision system properties
        public VisionSystemType SystemType { get; private set; }
        public double Resolution { get; private set; }        // Megapixels
        public double FieldOfView { get; private set; }       // Field of view in mm
        public double WorkingDistance { get; private set; }   // Distance to subject in mm
        public double PositionalAccuracy { get; private set; } // Accuracy in mm
        public VisionIlluminationType IlluminationType { get; private set; }
        public bool Is3DCapable { get; private set; }
        
        // State properties
        public bool IsCalibrated { get; private set; }
        public double LastInspectionTime { get; private set; } // Time in ms
        public int ObjectsDetected { get; private set; }
        public double LastDetectionConfidence { get; private set; } // 0-100%
        public bool IsCapturing { get; private set; }
        
        // Internal tracking
        private readonly Dictionary<string, ObjectTemplate> _objectLibrary;
        private readonly Dictionary<string, Vector3> _lastDetectedPositions;
        private double _processingTime;
        private double _lightIntensity;
        private double _calibrationAge;
        private double _defectDetectionThreshold;
        private readonly List<VisionInspectionResult> _inspectionHistory;
        private Dictionary<VisionDefectType, int> _defectCounts;
        private int _totalInspections;
        private int _passedInspections;
        
        /// <summary>
        /// Creates a new vision system for automation and inspection
        /// </summary>
        /// <param name="deviceId">Unique device identifier</param>
        /// <param name="name">Human-readable device name</param>
        /// <param name="systemType">Type of vision system</param>
        /// <param name="resolution">Camera resolution in megapixels</param>
        /// <param name="fieldOfView">Field of view in mm</param>
        /// <param name="is3DCapable">Whether system supports 3D vision</param>
        public VisionSystem(
            string deviceId,
            string name,
            VisionSystemType systemType = VisionSystemType.QualityInspection,
            double resolution = 5.0,
            double fieldOfView = 300,
            bool is3DCapable = false)
            : base(deviceId, name)
        {
            SystemType = systemType;
            Resolution = resolution;
            FieldOfView = fieldOfView;
            Is3DCapable = is3DCapable;
            WorkingDistance = 400;
            PositionalAccuracy = CalculateAccuracy(resolution, fieldOfView);
            IlluminationType = VisionIlluminationType.RingLight;
            
            IsCalibrated = false;
            LastInspectionTime = 0;
            ObjectsDetected = 0;
            LastDetectionConfidence = 0;
            IsCapturing = false;
            
            _processingTime = 0;
            _lightIntensity = 100;
            _calibrationAge = 0;
            _defectDetectionThreshold = 85; // 85% threshold
            _objectLibrary = new Dictionary<string, ObjectTemplate>();
            _lastDetectedPositions = new Dictionary<string, Vector3>();
            _inspectionHistory = new List<VisionInspectionResult>();
            _defectCounts = new Dictionary<VisionDefectType, int>();
            foreach (VisionDefectType defect in Enum.GetValues(typeof(VisionDefectType)))
            {
                _defectCounts[defect] = 0;
            }
            _totalInspections = 0;
            _passedInspections = 0;
            
            // Initialize diagnostics
            InitializeDiagnostics();
        }

        private double CalculateAccuracy(double resolution, double fieldOfView)
        {
            // Simple model: accuracy improves with higher resolution and smaller field of view
            double pixelSize = fieldOfView / Math.Sqrt(resolution * 1000000);
            
            // Accuracy is typically a fraction of the pixel size depending on system quality
            double accuracyFactor = 1.5; // Typical multiplier for vision systems
            
            return pixelSize * accuracyFactor;
        }
        
        private void InitializeDiagnostics()
        {
            DiagnosticData["SystemType"] = SystemType.ToString();
            DiagnosticData["Resolution"] = Resolution;
            DiagnosticData["FieldOfView"] = FieldOfView;
            DiagnosticData["WorkingDistance"] = WorkingDistance;
            DiagnosticData["PositionalAccuracy"] = PositionalAccuracy;
            DiagnosticData["IlluminationType"] = IlluminationType.ToString();
            DiagnosticData["Is3DCapable"] = Is3DCapable;
            DiagnosticData["IsCalibrated"] = IsCalibrated;
            DiagnosticData["LastInspectionTime"] = LastInspectionTime;
            DiagnosticData["ObjectsDetected"] = ObjectsDetected;
            DiagnosticData["LastDetectionConfidence"] = LastDetectionConfidence;
            DiagnosticData["DefectDetectionThreshold"] = _defectDetectionThreshold;
            DiagnosticData["PassRate"] = _totalInspections > 0 ? (double)_passedInspections / _totalInspections * 100.0 : 0.0;
        }

        public override void Initialize()
        {
            base.Initialize();
            
            IsCapturing = false;
            ObjectsDetected = 0;
            LastDetectionConfidence = 0;
            _processingTime = 0;
            
            UpdateDiagnostics();
        }

        public override bool Start()
        {
            if (base.Start())
            {
                // Simulate camera startup and initialization
                AddAlarm("VISION_STARTUP", "Vision system starting up", AlarmSeverity.Information);
                IsCapturing = true;
                return true;
            }
            return false;
        }

        public override bool Stop()
        {
            IsCapturing = false;
            AddAlarm("VISION_STOPPED", "Vision system stopped", AlarmSeverity.Information);
            return base.Stop();
        }

        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);
            
            if (Status != DeviceStatus.Running && Status != DeviceStatus.Warning)
                return;
                
            // Increase calibration age
            _calibrationAge += elapsedTime.TotalHours;
            
            // Check if calibration has expired
            if (_calibrationAge > 168) // 1 week
            {
                if (IsCalibrated)
                {
                    AddAlarm("CALIBRATION_EXPIRED", "Vision system calibration has expired", AlarmSeverity.Warning);
                    IsCalibrated = false;
                }
                
                // Degrade positional accuracy as calibration ages
                PositionalAccuracy *= 1.01;
            }
            
            // Simulate random objects detected in the field of view
            if (IsCapturing && Random.NextDouble() < 0.1 * elapsedTime.TotalSeconds)
            {
                SimulateObjectDetection();
            }
            
            // Update diagnostics
            UpdateDiagnostics();
        }
        
        private void SimulateObjectDetection()
        {
            // Only for demonstration - simulates detecting random objects
            if (_objectLibrary.Count > 0 && Random.NextDouble() < 0.3)
            {
                // Select a random object from library
                int index = Random.Next(_objectLibrary.Count);
                string objectName = _objectLibrary.Keys.ElementAt(index);
                ObjectTemplate template = _objectLibrary[objectName];
                
                // Calculate detection confidence based on system quality and calibration
                double baseConfidence = IsCalibrated ? 90.0 : 70.0;
                double confidenceVariation = IsCalibrated ? 10.0 : 25.0;
                LastDetectionConfidence = Math.Min(100.0, baseConfidence + (Random.NextDouble() * 2.0 - 1.0) * confidenceVariation);
                
                // Generate random position within field of view
                double margin = FieldOfView * 0.1;
                double x = (Random.NextDouble() * (FieldOfView - 2*margin)) - (FieldOfView/2 - margin);
                double y = (Random.NextDouble() * (FieldOfView - 2*margin)) - (FieldOfView/2 - margin);
                double z = WorkingDistance - (Random.NextDouble() * 20.0); // ±10mm from working distance
                
                // Add position noise based on accuracy
                double positionNoise = IsCalibrated ? PositionalAccuracy : PositionalAccuracy * 2.0;
                x += (Random.NextDouble() * 2.0 - 1.0) * positionNoise;
                y += (Random.NextDouble() * 2.0 - 1.0) * positionNoise;
                z += (Random.NextDouble() * 2.0 - 1.0) * positionNoise;
                
                // Update detected object position
                _lastDetectedPositions[objectName] = new Vector3((float)x, (float)y, (float)z);
                
                ObjectsDetected++;
                
                AddAlarm("OBJECT_DETECTED", $"Detected {objectName} with {LastDetectionConfidence:F1}% confidence", AlarmSeverity.Information);
            }
        }
        
        private void UpdateDiagnostics()
        {
            DiagnosticData["IsCapturing"] = IsCapturing;
            DiagnosticData["ObjectsDetected"] = ObjectsDetected;
            DiagnosticData["LastDetectionConfidence"] = LastDetectionConfidence;
            DiagnosticData["CalibrationAge"] = _calibrationAge;
            DiagnosticData["IsCalibrated"] = IsCalibrated;
            DiagnosticData["PositionalAccuracy"] = PositionalAccuracy;
            DiagnosticData["TotalInspections"] = _totalInspections;
            DiagnosticData["PassedInspections"] = _passedInspections;
        }
        
        #region Public Methods
        
        /// <summary>
        /// Performs a visual inspection of an item
        /// </summary>
        /// <param name="itemType">Type of item to inspect</param>
        /// <param name="referenceObject">Optional reference object name to compare against</param>
        /// <returns>Inspection result with details</returns>
        public VisionInspectionResult Inspect(string itemType, string? referenceObject = null)
        {
            if (Status != DeviceStatus.Running)
            {
                return new VisionInspectionResult
                {
                    Passed = false,
                    ConfidenceLevel = 0,
                    InspectionTime = 0,
                    DefectType = VisionDefectType.SystemError,
                    ItemType = itemType,
                    Message = "Vision system not running"
                };
            }
            
            // Track start time for performance measurement
            DateTime startTime = DateTime.Now;
            
            // Initialize result
            VisionInspectionResult result = new VisionInspectionResult
            {
                ItemType = itemType
            };
            
            // Base confidence level depends on system calibration and resolution
            double baseConfidence = IsCalibrated ? 95.0 : 80.0;
            baseConfidence *= Math.Min(1.0, Resolution / 5.0); // Scale by resolution
            
            // Add random variation
            double confidence = baseConfidence + (Random.NextDouble() * 10.0 - 5.0);
            confidence = Math.Min(100.0, Math.Max(0.0, confidence));
            
            // Check reference template
            bool hasTemplate = false;
            if (referenceObject != null && _objectLibrary.ContainsKey(referenceObject))
            {
                hasTemplate = true;
                // Higher confidence with template matching
                confidence += 5.0;
                confidence = Math.Min(100.0, confidence);
            }
            
            // Determine if item passes inspection (with random defect chance)
            bool hasDefect = DetermineIfDefectPresent(itemType);
            
            // Calculate inspection time based on complexity
            double processingFactor = hasTemplate ? 1.5 : 1.0; // Template matching takes longer
            _processingTime = (100.0 / Resolution) * processingFactor * (Random.NextDouble() * 0.4 + 0.8);
            
            // Simulate processing time delay for realism
            System.Threading.Thread.Sleep((int)(_processingTime * 10)); // Scale up for visibility
            
            // Record inspection time
            TimeSpan inspectionTime = DateTime.Now - startTime;
            LastInspectionTime = inspectionTime.TotalMilliseconds;
            
            // Fill in result details
            result.Passed = !hasDefect;
            result.ConfidenceLevel = confidence;
            result.InspectionTime = LastInspectionTime;
            result.DefectType = hasDefect ? GetRandomDefectType() : VisionDefectType.None;
            
            if (hasDefect)
            {
                result.Message = $"Detected {result.DefectType} in {itemType}";
                _defectCounts[result.DefectType]++;
            }
            else
            {
                result.Message = $"{itemType} passed inspection";
            }
            
            // Update inspection statistics
            _totalInspections++;
            if (result.Passed)
            {
                _passedInspections++;
            }
            
            // Add to inspection history
            _inspectionHistory.Add(result);
            
            // Log inspection event
            AddAlarm(
                result.Passed ? "INSPECTION_PASS" : "INSPECTION_FAIL",
                result.Message,
                result.Passed ? AlarmSeverity.Information : AlarmSeverity.Warning
            );
            
            return result;
        }
        
        /// <summary>
        /// Performs camera calibration routine
        /// </summary>
        /// <returns>True if calibration was successful</returns>
        public bool Calibrate()
        {
            if (Status != DeviceStatus.Running)
            {
                AddAlarm("CALIBRATION_FAILED", "Cannot calibrate: vision system not running", AlarmSeverity.Warning);
                return false;
            }
            
            // Simulate calibration process
            System.Threading.Thread.Sleep(2000);
            
            // 90% chance of successful calibration
            bool success = Random.NextDouble() < 0.9;
            
            if (success)
            {
                IsCalibrated = true;
                _calibrationAge = 0;
                
                // Reset positional accuracy to optimal level
                PositionalAccuracy = CalculateAccuracy(Resolution, FieldOfView);
                
                AddAlarm("CALIBRATION_SUCCESS", "Vision system calibration completed successfully", AlarmSeverity.Information);
            }
            else
            {
                AddAlarm("CALIBRATION_FAILED", "Vision system calibration failed", AlarmSeverity.Warning);
            }
            
            // Update calibration status in diagnostics
            DiagnosticData["IsCalibrated"] = IsCalibrated;
            DiagnosticData["CalibrationAge"] = _calibrationAge;
            DiagnosticData["PositionalAccuracy"] = PositionalAccuracy;
            
            return success;
        }
        
        /// <summary>
        /// Sets the illumination intensity
        /// </summary>
        /// <param name="intensity">Intensity from 0-100%</param>
        public void SetIllumination(double intensity)
        {
            _lightIntensity = Math.Max(0, Math.Min(100, intensity));
            AddAlarm("LIGHTING_CHANGED", $"Illumination set to {_lightIntensity:F0}%", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Changes the illumination type
        /// </summary>
        public void SetIlluminationType(VisionIlluminationType type)
        {
            IlluminationType = type;
            AddAlarm("LIGHTING_TYPE_CHANGED", $"Illumination type set to {type}", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Sets the defect detection threshold
        /// </summary>
        /// <param name="threshold">Threshold from 0-100%</param>
        public void SetDefectDetectionThreshold(double threshold)
        {
            _defectDetectionThreshold = Math.Max(0, Math.Min(100, threshold));
            DiagnosticData["DefectDetectionThreshold"] = _defectDetectionThreshold;
            AddAlarm("THRESHOLD_CHANGED", $"Defect detection threshold set to {_defectDetectionThreshold:F0}%", AlarmSeverity.Information);
        }
        
        /// <summary>
        /// Trains the vision system to recognize a new object
        /// </summary>
        /// <param name="objectName">Name identifier for the object</param>
        /// <param name="dimensions">Object dimensions (width, height, depth) in mm</param>
        /// <param name="color">Object color description</param>
        /// <returns>True if training was successful</returns>
        public bool TrainObject(string objectName, Vector3 dimensions, string color)
        {
            if (Status != DeviceStatus.Running)
            {
                AddAlarm("TRAINING_FAILED", "Cannot train: vision system not running", AlarmSeverity.Warning);
                return false;
            }
            
            // Create object template
            var template = new ObjectTemplate
            {
                Name = objectName,
                Dimensions = dimensions,
                Color = color,
                TrainingDate = DateTime.Now
            };
            
            // Add to library (replace if exists)
            _objectLibrary[objectName] = template;
            
            // Simulate training time
            System.Threading.Thread.Sleep(1000);
            
            AddAlarm("OBJECT_TRAINED", $"Successfully trained vision system for '{objectName}'", AlarmSeverity.Information);
            return true;
        }
        
        /// <summary>
        /// Gets the position of a recognized object
        /// </summary>
        /// <param name="objectName">Name of object to locate</param>
        /// <returns>Object position in mm or null if not found</returns>
        public Vector3? GetObjectPosition(string objectName)
        {
            // Check if we have this object in our detected positions
            if (_lastDetectedPositions.TryGetValue(objectName, out Vector3 position))
            {
                // Apply some random noise to position based on calibration status
                double positionNoise = IsCalibrated ? PositionalAccuracy : PositionalAccuracy * 2.0;
                Vector3 noisyPosition = new Vector3(
                    position.X + (float)((Random.NextDouble() * 2.0 - 1.0) * positionNoise),
                    position.Y + (float)((Random.NextDouble() * 2.0 - 1.0) * positionNoise),
                    position.Z + (float)((Random.NextDouble() * 2.0 - 1.0) * positionNoise)
                );
                
                AddAlarm("POSITION_REQUEST", $"Retrieved position for {objectName}", AlarmSeverity.Information);
                return noisyPosition;
            }
            
            // If not in cache, simulate looking for object (with low probability of success)
            if (Random.NextDouble() < 0.1 && _objectLibrary.ContainsKey(objectName))
            {
                // Generate random position within field of view
                double margin = FieldOfView * 0.1;
                double x = (Random.NextDouble() * (FieldOfView - 2*margin)) - (FieldOfView/2 - margin);
                double y = (Random.NextDouble() * (FieldOfView - 2*margin)) - (FieldOfView/2 - margin);
                double z = WorkingDistance - (Random.NextDouble() * 20.0);
                
                Vector3 newPosition = new Vector3((float)x, (float)y, (float)z);
                _lastDetectedPositions[objectName] = newPosition;
                
                AddAlarm("OBJECT_FOUND", $"Found {objectName} in field of view", AlarmSeverity.Information);
                return newPosition;
            }
            
            AddAlarm("OBJECT_NOT_FOUND", $"Object {objectName} not found in field of view", AlarmSeverity.Minor);
            return null;
        }
        
        /// <summary>
        /// Gets inspection statistics
        /// </summary>
        /// <returns>Dictionary of inspection metrics</returns>
        public Dictionary<string, object> GetInspectionStatistics()
        {
            Dictionary<string, object> stats = new Dictionary<string, object>();
            
            stats["TotalInspections"] = _totalInspections;
            stats["PassedInspections"] = _passedInspections;
            stats["FailedInspections"] = _totalInspections - _passedInspections;
            
            if (_totalInspections > 0)
            {
                stats["PassRate"] = (double)_passedInspections / _totalInspections * 100.0;
            }
            else
            {
                stats["PassRate"] = 0.0;
            }
            
            // Add defect counts
            foreach (var defect in _defectCounts)
            {
                stats[$"Defect_{defect.Key}"] = defect.Value;
            }
            
            // Add average inspection time if we have data
            if (_inspectionHistory.Count > 0)
            {
                stats["AverageInspectionTimeMs"] = _inspectionHistory.Average(r => r.InspectionTime);
            }
            
            return stats;
        }
        
        /// <summary>
        /// Clears inspection history and statistics
        /// </summary>
        public void ClearInspectionHistory()
        {
            _inspectionHistory.Clear();
            _totalInspections = 0;
            _passedInspections = 0;
            
            foreach (VisionDefectType defect in Enum.GetValues(typeof(VisionDefectType)))
            {
                _defectCounts[defect] = 0;
            }
            
            AddAlarm("HISTORY_CLEARED", "Inspection history and statistics cleared", AlarmSeverity.Information);
        }
        
        #endregion
        
        #region Private Helper Methods
        
        private bool DetermineIfDefectPresent(string itemType)
        {
            // Base defect rate depends on item type
            double baseDefectRate = 0.05; // 5% for generic items
            
            // Adjust based on specific item types
            switch (itemType.ToLower())
            {
                case "vial":
                    baseDefectRate = 0.03;
                    break;
                case "stopper":
                    baseDefectRate = 0.04;
                    break;
                case "cap":
                    baseDefectRate = 0.02;
                    break;
                case "label":
                    baseDefectRate = 0.07;
                    break;
            }
            
            // Adjust for system quality and calibration
            if (IsCalibrated)
            {
                baseDefectRate *= 0.8; // Lower defect detection with calibrated system
            }
            else
            {
                baseDefectRate *= 1.5; // Higher with uncalibrated system (more false positives)
            }
            
            // Adjust for threshold
            double thresholdFactor = (100.0 - _defectDetectionThreshold) / 20.0;
            baseDefectRate *= (1.0 + thresholdFactor);
            
            // Final random check
            return Random.NextDouble() < baseDefectRate;
        }
        
        private VisionDefectType GetRandomDefectType()
        {
            // Get all defect types except 'None' and 'SystemError'
            var defectTypes = Enum.GetValues(typeof(VisionDefectType))
                .Cast<VisionDefectType>()
                .Where(d => d != VisionDefectType.None && d != VisionDefectType.SystemError)
                .ToList();
            
            // Select random defect type
            return defectTypes[Random.Next(defectTypes.Count)];
        }
        
        #endregion
        
        protected override void SimulateFault()
        {
            if (Status == DeviceStatus.Fault)
                return;
            
            int faultType = Random.Next(4);
            
            switch (faultType)
            {
                case 0: // Camera exposure issues
                    AddAlarm("EXPOSURE_ERROR", "Camera exposure error", AlarmSeverity.Warning);
                    LastDetectionConfidence *= 0.6;
                    break;
                    
                case 1: // Lighting failure
                    AddAlarm("LIGHTING_FAILURE", "Vision system lighting failure", AlarmSeverity.Major);
                    _lightIntensity *= 0.3;
                    Status = DeviceStatus.Fault;
                    break;
                    
                case 2: // Focus drift
                    AddAlarm("FOCUS_ERROR", "Camera focus error", AlarmSeverity.Warning);
                    PositionalAccuracy *= 2.5;
                    break;
                    
                case 3: // Communication error
                    AddAlarm("CAMERA_COMM_ERROR", "Camera communication error", AlarmSeverity.Major);
                    IsCapturing = false;
                    Status = DeviceStatus.Fault;
                    break;
            }
        }
    }
    
    /// <summary>
    /// Types of vision systems based on application
    /// </summary>
    public enum VisionSystemType
    {
        QualityInspection,
        ObjectIdentification,
        BarcodeScan,
        ColorVerification,
        ContaminationDetection,
        DimensionalMeasurement,
        OCR,
        RobotGuidance
    }
    
    /// <summary>
    /// Types of illumination for vision systems
    /// </summary>
    public enum VisionIlluminationType
    {
        RingLight,
        DomeLight,
        BackLight,
        DirectionalLight,
        DarkField,
        Coaxial,
        StructuredLight,
        MultiSpectral,
        Strobed
    }
    
    /// <summary>
    /// Types of defects that can be detected
    /// </summary>
    public enum VisionDefectType
    {
        None,
        DimensionalError,
        ColorVariation,
        Contamination,
        Surface,
        Missing,
        Misalignment,
        Deformation,
        TextError,
        Scratch,
        BarcodeError,
        SystemError
    }
    
    /// <summary>
    /// Represents an object template for vision recognition
    /// </summary>
    public class ObjectTemplate
    {
        public string Name { get; init; } = string.Empty;
        public Vector3 Dimensions { get; set; } // Width, Height, Depth in mm
        public string Color { get; init; } = string.Empty;
        public DateTime TrainingDate { get; set; }
    }
    
    /// <summary>
    /// Result of a vision inspection operation
    /// </summary>
    public class VisionInspectionResult
    {
        public bool Passed { get; set; }
        public double ConfidenceLevel { get; set; } // 0-100%
        public double InspectionTime { get; set; } // ms
        public VisionDefectType DefectType { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}