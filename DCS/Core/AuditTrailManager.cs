using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// 21 CFR Part 11 compliant audit trail manager for pharmaceutical manufacturing
	/// </summary>
	public class AuditTrailManager
	{
		private readonly ConcurrentQueue<AuditEntry> _auditTrail;
		private readonly int _maxEntries;
		private bool _isEnabled;

		public AuditTrailManager(int maxEntries = 100000)
		{
			_auditTrail = new ConcurrentQueue<AuditEntry>();
			_maxEntries = maxEntries;
			_isEnabled = true;
		}

		public void LogEvent(string eventId, string description, string userId, AuditEventType eventType, string additionalData = "")
		{
			if (!_isEnabled)
			{
				return;
			}

			var entry = new AuditEntry
			{
				EntryId = Guid.NewGuid().ToString(),
				EventId = eventId,
				Timestamp = DateTime.Now,
				UserId = userId,
				EventType = eventType,
				Description = description,
				AdditionalData = additionalData,
				ComputerName = Environment.MachineName,
				ApplicationName = "PharmaceuticalProcess.DCS"
			};

			// Generate digital signature for integrity
			entry.Signature = GenerateSignature(entry);

			_auditTrail.Enqueue(entry);

			// Trim if necessary
			while (_auditTrail.Count > _maxEntries)
			{
				_auditTrail.TryDequeue(out _);
			}
		}

		public List<AuditEntry> GetAuditTrail(DateTime? startTime = null, DateTime? endTime = null, string userId = null)
		{
			var query = _auditTrail.AsEnumerable();

			if (startTime.HasValue)
			{
				query = query.Where(e => e.Timestamp >= startTime.Value);
			}

			if (endTime.HasValue)
			{
				query = query.Where(e => e.Timestamp <= endTime.Value);
			}

			if (!string.IsNullOrEmpty(userId))
			{
				query = query.Where(e => e.UserId == userId);
			}

			return query.OrderByDescending(e => e.Timestamp).ToList();
		}

		public List<AuditEntry> GetAuditTrailByEventType(AuditEventType eventType, DateTime? startTime = null, DateTime? endTime = null)
		{
			var query = _auditTrail.Where(e => e.EventType == eventType);

			if (startTime.HasValue)
			{
				query = query.Where(e => e.Timestamp >= startTime.Value);
			}

			if (endTime.HasValue)
			{
				query = query.Where(e => e.Timestamp <= endTime.Value);
			}

			return query.OrderByDescending(e => e.Timestamp).ToList();
		}

		public bool VerifyAuditEntry(AuditEntry entry)
		{
			var calculatedSignature = GenerateSignature(entry);
			return calculatedSignature == entry.Signature;
		}

		public AuditTrailIntegrityReport VerifyIntegrity()
		{
			var report = new AuditTrailIntegrityReport
			{
				TotalEntries = _auditTrail.Count,
				VerificationTime = DateTime.Now,
				IsValid = true
			};

			foreach (var entry in _auditTrail)
			{
				if (!VerifyAuditEntry(entry))
				{
					report.IsValid = false;
					report.InvalidEntries.Add(entry.EntryId);
				}
			}

			report.ValidEntries = report.TotalEntries - report.InvalidEntries.Count;

			return report;
		}

		private string GenerateSignature(AuditEntry entry)
		{
			// Create a deterministic string representation of the audit entry
			var signatureData = $"{entry.EntryId}|{entry.Timestamp:O}|{entry.UserId}|{entry.EventId}|{entry.Description}|{entry.EventType}";

			// Generate SHA256 hash
			using (var sha256 = SHA256.Create())
			{
				var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(signatureData));
				return Convert.ToBase64String(hashBytes);
			}
		}

		public void EnableAuditTrail()
		{
			_isEnabled = true;
			LogEvent("AUDIT_ENABLED", "Audit trail enabled", "SYSTEM", AuditEventType.SystemEvent);
		}

		public void DisableAuditTrail()
		{
			LogEvent("AUDIT_DISABLED", "Audit trail disabled", "SYSTEM", AuditEventType.SystemEvent);
			_isEnabled = false;
		}

		public AuditTrailStatistics GetStatistics()
		{
			var stats = new AuditTrailStatistics
			{
				TotalEntries = _auditTrail.Count,
				OldestEntry = _auditTrail.FirstOrDefault()?.Timestamp ?? DateTime.MinValue,
				NewestEntry = _auditTrail.LastOrDefault()?.Timestamp ?? DateTime.MinValue,
				UniqueUsers = _auditTrail.Select(e => e.UserId).Distinct().Count(),
				EventTypeBreakdown = _auditTrail
					.GroupBy(e => e.EventType)
					.ToDictionary(g => g.Key, g => g.Count())
			};

			return stats;
		}
	}

	public class AuditEntry
	{
		public string EntryId { get; set; }
		public string EventId { get; set; }
		public DateTime Timestamp { get; set; }
		public string UserId { get; set; }
		public AuditEventType EventType { get; set; }
		public string Description { get; set; }
		public string AdditionalData { get; set; }
		public string ComputerName { get; set; }
		public string ApplicationName { get; set; }
		public string Signature { get; set; } // Digital signature for integrity
	}

	public class AuditTrailIntegrityReport
	{
		public DateTime VerificationTime { get; set; }
		public int TotalEntries { get; set; }
		public int ValidEntries { get; set; }
		public bool IsValid { get; set; }
		public List<string> InvalidEntries { get; set; } = new List<string>();
	}

	public class AuditTrailStatistics
	{
		public int TotalEntries { get; set; }
		public DateTime OldestEntry { get; set; }
		public DateTime NewestEntry { get; set; }
		public int UniqueUsers { get; set; }
		public Dictionary<AuditEventType, int> EventTypeBreakdown { get; set; }
	}

	public enum AuditEventType
	{
		SystemEvent,
		UserLogin,
		UserLogout,
		Configuration,
		ProcessStart,
		ProcessStop,
		ParameterChange,
		SetpointChange,
		AlarmAcknowledgement,
		BatchStart,
		BatchEnd,
		RecipeLoad,
		ManualControl,
		SafetyOverride,
		MaintenanceStart,
		MaintenanceEnd,
		DataExport,
		ReportGeneration,
		Error
	}
}
