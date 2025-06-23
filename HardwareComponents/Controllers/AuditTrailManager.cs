using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.HardwareComponents.Controllers
{
	/// <summary>
	/// Manages the audit trail for GMP-compliant pharmaceutical manufacturing
	/// </summary>
	public class AuditTrailManager
	{
		public string DeviceId { get; private set; }
		public int EntryCount { get; private set; }
		public DateTime StartTime { get; private set; }

		private List<AuditEntry> _auditEntries;
		private bool _archivingEnabled;
		private int _maxEntries;
		private string _archivePath;

		public AuditTrailManager(string deviceId, int maxEntries = 10000, bool archivingEnabled = true)
		{
			DeviceId = deviceId;
			_maxEntries = maxEntries;
			_archivingEnabled = archivingEnabled;

			StartTime = DateTime.Now;
			_auditEntries = new List<AuditEntry>();
			_archivePath = Path.Combine("AuditLogs", deviceId);

			// Create initial system entry
			LogAction("System", "Audit Trail Initialized", "System startup", true);
		}

		public void LogAction(string user, string action, string reason, bool success)
		{
			var entry = new AuditEntry
			{
				Timestamp = DateTime.Now,
				DeviceId = DeviceId,
				User = user,
				Action = action,
				Reason = reason,
				Success = success
			};

			_auditEntries.Add(entry);
			EntryCount++;

			// Archive if needed
			if (_archivingEnabled && _auditEntries.Count >= _maxEntries)
			{
				Task.Run(() => ArchiveEntries());
			}
		}

		public IReadOnlyList<AuditEntry> GetRecentEntries(int count)
		{
			int entryCount = Math.Min(count, _auditEntries.Count);
			return _auditEntries.GetRange(_auditEntries.Count - entryCount, entryCount);
		}

		public IReadOnlyList<AuditEntry> GetEntriesByUser(string user, int maxCount = 100)
		{
			return _auditEntries
				.FindAll(e => e.User.Equals(user, StringComparison.OrdinalIgnoreCase))
				.GetRange(0, Math.Min(maxCount, _auditEntries.Count));
		}

		public IReadOnlyList<AuditEntry> GetEntriesByTimeRange(DateTime start, DateTime end, int maxCount = 100)
		{
			var filteredEntries = _auditEntries.FindAll(e => e.Timestamp >= start && e.Timestamp <= end);
			return filteredEntries.GetRange(0, Math.Min(maxCount, filteredEntries.Count));
		}

		private async Task ArchiveEntries()
		{
			// Ensure directory exists
			Directory.CreateDirectory(_archivePath);

			// Create archive filename based on date
			string fileName = $"AuditLog_{DeviceId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
			string filePath = Path.Combine(_archivePath, fileName);

			var entriesToArchive = new List<AuditEntry>(_auditEntries);

			// Clear entries after copying them for archiving
			_auditEntries.Clear();
			_auditEntries.Capacity = _maxEntries;

			// Archive asynchronously
			try
			{
				StringBuilder csv = new StringBuilder();
				csv.AppendLine("Timestamp,DeviceId,User,Action,Reason,Success");

				foreach (var entry in entriesToArchive)
				{
					csv.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
								   $"\"{entry.DeviceId}\"," +
								   $"\"{entry.User}\"," +
								   $"\"{entry.Action}\"," +
								   $"\"{entry.Reason}\"," +
								   $"{entry.Success}");
				}

				await File.WriteAllTextAsync(filePath, csv.ToString());

				// Keep the most recent entries in memory
				int keepCount = Math.Min(100, entriesToArchive.Count);
				if (keepCount > 0)
				{
					_auditEntries.AddRange(entriesToArchive.GetRange(entriesToArchive.Count - keepCount, keepCount));
				}
			}
			catch (Exception ex)
			{
				// In a real system, this would log to a redundant system
				Console.WriteLine($"Error archiving audit trail: {ex.Message}");
			}
		}
	}

	public class AuditEntry
	{
		public DateTime Timestamp { get; set; }
		public string DeviceId { get; set; }
		public string User { get; set; }
		public string Action { get; set; }
		public string Reason { get; set; }
		public bool Success { get; set; }

		public override string ToString()
		{
			return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {User} {(Success ? "successfully" : "failed to")} perform {Action}. Reason: {Reason}";
		}
	}
}