using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// Manages all alarms in the DCS system with priority handling and acknowledgement
	/// </summary>
	public class AlarmManager
	{
		private readonly ConcurrentDictionary<string, Alarm> _activeAlarms;
		private readonly ConcurrentQueue<AlarmEvent> _alarmHistory;
		private readonly int _maxHistorySize;
		private readonly Dictionary<string, AlarmConfiguration> _alarmConfigurations;

		public AlarmManager(int maxHistorySize = 10000)
		{
			_activeAlarms = new ConcurrentDictionary<string, Alarm>();
			_alarmHistory = new ConcurrentQueue<AlarmEvent>();
			_maxHistorySize = maxHistorySize;
			_alarmConfigurations = new Dictionary<string, AlarmConfiguration>();
		}

		public async Task InitializeAsync()
		{
			// Load alarm configurations
			await Task.CompletedTask;
		}

		public void RaiseAlarm(string alarmId, string message, AlarmPriority priority, AlarmCategory category,
							  string source = "", Dictionary<string, object> additionalData = null)
		{
			var alarm = new Alarm
			{
				AlarmId = alarmId,
				Message = message,
				Priority = priority,
				Category = category,
				Source = source,
				ActivationTime = DateTime.Now,
				Status = AlarmStatus.Active,
				AdditionalData = additionalData ?? new Dictionary<string, object>()
			};

			// Add or update active alarm
			var isNew = _activeAlarms.TryAdd(alarmId, alarm);

			// Log alarm event
			var alarmEvent = new AlarmEvent
			{
				EventId = Guid.NewGuid().ToString(),
				AlarmId = alarmId,
				EventType = isNew ? AlarmEventType.Activated : AlarmEventType.Reactivated,
				Timestamp = DateTime.Now,
				Message = message,
				Priority = priority
			};

			LogAlarmEvent(alarmEvent);
		}

		public bool AcknowledgeAlarm(string alarmId, string userId, string comment = "")
		{
			if (_activeAlarms.TryGetValue(alarmId, out var alarm))
			{
				alarm.Status = AlarmStatus.Acknowledged;
				alarm.AcknowledgedBy = userId;
				alarm.AcknowledgementTime = DateTime.Now;
				alarm.AcknowledgementComment = comment;

				// Log alarm event
				var alarmEvent = new AlarmEvent
				{
					EventId = Guid.NewGuid().ToString(),
					AlarmId = alarmId,
					EventType = AlarmEventType.Acknowledged,
					Timestamp = DateTime.Now,
					Message = $"Acknowledged by {userId}",
					Priority = alarm.Priority,
					UserId = userId,
					Comment = comment
				};

				LogAlarmEvent(alarmEvent);
				return true;
			}

			return false;
		}

		public bool ClearAlarm(string alarmId, string reason = "")
		{
			if (_activeAlarms.TryRemove(alarmId, out var alarm))
			{
				alarm.Status = AlarmStatus.Cleared;
				alarm.ClearTime = DateTime.Now;

				// Log alarm event
				var alarmEvent = new AlarmEvent
				{
					EventId = Guid.NewGuid().ToString(),
					AlarmId = alarmId,
					EventType = AlarmEventType.Cleared,
					Timestamp = DateTime.Now,
					Message = $"Cleared: {reason}",
					Priority = alarm.Priority
				};

				LogAlarmEvent(alarmEvent);
				return true;
			}

			return false;
		}

		public List<Alarm> GetActiveAlarms()
		{
			return _activeAlarms.Values
				.OrderByDescending(a => a.Priority)
				.ThenBy(a => a.ActivationTime)
				.ToList();
		}

		public List<Alarm> GetActiveAlarmsByPriority(AlarmPriority priority)
		{
			return _activeAlarms.Values
				.Where(a => a.Priority == priority)
				.OrderBy(a => a.ActivationTime)
				.ToList();
		}

		public List<AlarmEvent> GetAlarmHistory(DateTime? startTime = null, DateTime? endTime = null, string alarmId = null)
		{
			var query = _alarmHistory.AsEnumerable();

			if (startTime.HasValue)
				query = query.Where(e => e.Timestamp >= startTime.Value);

			if (endTime.HasValue)
				query = query.Where(e => e.Timestamp <= endTime.Value);

			if (!string.IsNullOrEmpty(alarmId))
				query = query.Where(e => e.AlarmId == alarmId);

			return query.OrderByDescending(e => e.Timestamp).ToList();
		}

		public async Task UpdateAlarmsAsync()
		{
			// Check for alarms that should auto-clear
			var alarmsToCheck = _activeAlarms.Values.ToList();

			foreach (var alarm in alarmsToCheck)
			{
				if (_alarmConfigurations.TryGetValue(alarm.AlarmId, out var config))
				{
					if (config.AutoClear && alarm.Status == AlarmStatus.Acknowledged)
					{
						var timeSinceAck = DateTime.Now - alarm.AcknowledgementTime;
						if (timeSinceAck.HasValue && timeSinceAck.Value.TotalMinutes > config.AutoClearDelayMinutes)
						{
							ClearAlarm(alarm.AlarmId, "Auto-cleared");
						}
					}
				}
			}

			await Task.CompletedTask;
		}

		private void LogAlarmEvent(AlarmEvent alarmEvent)
		{
			_alarmHistory.Enqueue(alarmEvent);

			// Trim history if too large
			while (_alarmHistory.Count > _maxHistorySize)
			{
				_alarmHistory.TryDequeue(out _);
			}
		}

		public void ConfigureAlarm(string alarmId, AlarmConfiguration configuration)
		{
			_alarmConfigurations[alarmId] = configuration;
		}
	}

	public class Alarm
	{
		public string AlarmId { get; set; }
		public string Message { get; set; }
		public AlarmPriority Priority { get; set; }
		public AlarmCategory Category { get; set; }
		public string Source { get; set; }
		public AlarmStatus Status { get; set; }
		public DateTime ActivationTime { get; set; }
		public DateTime? AcknowledgementTime { get; set; }
		public string AcknowledgedBy { get; set; }
		public string AcknowledgementComment { get; set; }
		public DateTime? ClearTime { get; set; }
		public Dictionary<string, object> AdditionalData { get; set; }
	}

	public class AlarmEvent
	{
		public string EventId { get; set; }
		public string AlarmId { get; set; }
		public AlarmEventType EventType { get; set; }
		public DateTime Timestamp { get; set; }
		public string Message { get; set; }
		public AlarmPriority Priority { get; set; }
		public string UserId { get; set; }
		public string Comment { get; set; }
	}

	public class AlarmConfiguration
	{
		public string AlarmId { get; set; }
		public AlarmPriority Priority { get; set; }
		public bool RequiresAcknowledgement { get; set; }
		public bool AutoClear { get; set; }
		public double AutoClearDelayMinutes { get; set; }
		public bool EnableAudible { get; set; }
		public string AlarmGroup { get; set; }
	}

	public enum AlarmPriority
	{
		Critical = 1,
		High = 2,
		Medium = 3,
		Low = 4,
		Information = 5
	}

	public enum AlarmCategory
	{
		Safety,
		Process,
		Equipment,
		Quality,
		System,
		Communication,
		Operator
	}

	public enum AlarmStatus
	{
		Active,
		Acknowledged,
		Cleared,
		Suppressed
	}

	public enum AlarmEventType
	{
		Activated,
		Reactivated,
		Acknowledged,
		Cleared,
		Suppressed,
		Unsuppressed
	}
}
