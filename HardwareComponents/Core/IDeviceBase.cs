namespace PharmaceuticalProcess.HardwareComponents.Core
{
	/// <summary>
	/// Base interface for all hardware devices in the system
	/// </summary>
	public interface IDeviceBase
	{
		string DeviceId { get; }
		string Name { get; }
		DeviceStatus Status { get; }
		DeviceType Type { get; }

		void Initialize();
		bool Start();
		bool Stop();
		DeviceState GetState();
		Dictionary<string, object> GetDiagnostics();
		List<DeviceAlarm> GetActiveAlarms();
	}

	public enum DeviceStatus
	{
		Offline,
		Initializing,
		Ready,
		Running,
		Warning,
		Alarm,
		Maintenance,
		Fault
	}

	public enum DeviceType
	{
		Controller,
		Sensor,
		Actuator,
		IOModule,
		ProcessEquipment,
		NetworkDevice
	}

	public class DeviceState
	{
		public DeviceStatus Status { get; set; }
		public DateTime LastStatusChange { get; set; }
		public TimeSpan OperatingTime { get; set; }
		public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
		public bool HasFaults => FaultCodes.Count > 0;
		public List<string> FaultCodes { get; set; } = new List<string>();
	}

	public class DeviceAlarm
	{
		public string AlarmId { get; set; }
		public string Description { get; set; }
		public AlarmSeverity Severity { get; set; }
		public DateTime Timestamp { get; set; }
		public bool Acknowledged { get; set; }
	}

	public enum AlarmSeverity
	{
		Information,
		Warning,
		Minor,
		Major,
		Critical
	}
}