using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.Core
{
	/// <summary>
	/// Base implementation for all hardware devices
	/// </summary>
	public abstract class DeviceBase : IDeviceBase
	{
		public string DeviceId { get; protected set; }
		public string Name { get; protected set; }
		public DeviceStatus Status { get; protected set; } = DeviceStatus.Offline;
		public abstract DeviceType Type { get; }

		protected List<DeviceAlarm> ActiveAlarms { get; } = new List<DeviceAlarm>();
		protected DateTime StartTime;
		protected Random Random = new Random();

		// Properties for simulation
		protected bool HasPower { get; set; } = true;
		protected int SimulatedFaultProbability { get; set; } = 0; // Percentage chance of fault per update
		protected DateTime LastUpdateTime { get; set; }
		protected Dictionary<string, object> DiagnosticData { get; set; } = new Dictionary<string, object>();

		protected DeviceBase(string deviceId, string name)
		{
			DeviceId = deviceId;
			Name = name;
			LastUpdateTime = DateTime.Now;
		}

		public virtual void Initialize()
		{
			Status = DeviceStatus.Initializing;
			ClearAlarms();
			DiagnosticData["InitializedAt"] = DateTime.Now;

			Status = DeviceStatus.Ready;
		}

		public virtual bool Start()
		{
			if (Status == DeviceStatus.Ready || Status == DeviceStatus.Fault)
			{
				Status = DeviceStatus.Running;
				StartTime = DateTime.Now;
				return true;
			}
			return false;
		}

		public virtual bool Stop()
		{
			if (Status == DeviceStatus.Running || Status == DeviceStatus.Warning)
			{
				Status = DeviceStatus.Ready;
				return true;
			}
			return false;
		}

		public virtual DeviceState GetState()
		{
			var state = new DeviceState
			{
				Status = Status,
				LastStatusChange = LastUpdateTime,
				OperatingTime = Status == DeviceStatus.Running ?
					DateTime.Now - StartTime : TimeSpan.Zero,
				Parameters = new Dictionary<string, object>(DiagnosticData)
			};

			return state;
		}

		public virtual Dictionary<string, object> GetDiagnostics()
		{
			return new Dictionary<string, object>(DiagnosticData);
		}

		public List<DeviceAlarm> GetActiveAlarms()
		{
			return new List<DeviceAlarm>(ActiveAlarms);
		}

		protected void AddAlarm(string alarmId, string description, AlarmSeverity severity)
		{
			var alarm = new DeviceAlarm
			{
				AlarmId = alarmId,
				Description = description,
				Severity = severity,
				Timestamp = DateTime.Now,
				Acknowledged = false
			};

			ActiveAlarms.Add(alarm);

			// Update status based on alarm severity
			if (severity == AlarmSeverity.Critical)
				Status = DeviceStatus.Fault;
			else if (severity == AlarmSeverity.Major || severity == AlarmSeverity.Minor)
				Status = DeviceStatus.Alarm;
			else if (severity == AlarmSeverity.Warning && Status == DeviceStatus.Running)
				Status = DeviceStatus.Warning;
		}

		protected void ClearAlarms()
		{
			ActiveAlarms.Clear();
		}

		protected virtual void CheckForFaults()
		{
			// Simulate random faults based on probability
			if (Random.Next(100) < SimulatedFaultProbability)
			{
				SimulateFault();
			}
		}

		protected virtual void SimulateFault()
		{
			// Override in derived classes
		}

		public virtual void Update(TimeSpan elapsedTime)
		{
			LastUpdateTime = DateTime.Now;

			if (!HasPower)
			{
				Status = DeviceStatus.Offline;
				return;
			}

			CheckForFaults();
		}
	}
}