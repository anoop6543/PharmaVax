using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// Main Distributed Control System controller that manages all process control operations
	/// </summary>
	public class DCSController
	{
		// Core components
		private readonly ConcurrentDictionary<string, IControlLoop> _controlLoops;
		private readonly ConcurrentDictionary<string, ProcessUnit> _processUnits;
		private readonly AlarmManager _alarmManager;
		private readonly DataHistorian _historian;
		private readonly RecipeManager _recipeManager;
		private readonly BatchManager _batchManager;
		private readonly AuditTrailManager _auditTrail;
		private readonly CommunicationManager _commManager;

		// System state
		public DCSOperatingMode OperatingMode { get; private set; }
		public bool IsRedundant { get; private set; }
		public DCSController RedundantController { get; set; }
		public SystemHealthStatus HealthStatus { get; private set; }

		// Scan cycle parameters
		private readonly int _scanCycleMs;
		private CancellationTokenSource _scanCancellationToken;
		private Task _scanTask;
		private DateTime _lastScanTime;
		private double _scanCycleTime;

		// Performance metrics
		public double AverageScanTime { get; private set; }
		public double MaxScanTime { get; private set; }
		public int MissedScans { get; private set; }
		public DateTime StartTime { get; private set; }
		public TimeSpan Uptime => DateTime.Now - StartTime;

		public DCSController(int scanCycleMs = 100, bool enableRedundancy = false)
		{
			_scanCycleMs = scanCycleMs;
			IsRedundant = enableRedundancy;

			// Initialize collections
			_controlLoops = new ConcurrentDictionary<string, IControlLoop>();
			_processUnits = new ConcurrentDictionary<string, ProcessUnit>();

			// Initialize managers
			_alarmManager = new AlarmManager();
			_historian = new DataHistorian();
			_recipeManager = new RecipeManager();
			_batchManager = new BatchManager();
			_auditTrail = new AuditTrailManager();
			_commManager = new CommunicationManager();

			// Set initial state
			OperatingMode = DCSOperatingMode.Stopped;
			HealthStatus = SystemHealthStatus.Healthy;
			StartTime = DateTime.Now;
		}

		/// <summary>
		/// Start the DCS system
		/// </summary>
		public async Task<bool> StartAsync()
		{
			if (OperatingMode != DCSOperatingMode.Stopped)
			{
				_auditTrail.LogEvent("DCS_START_FAILED", "DCS already running", "SYSTEM", AuditEventType.SystemEvent);
				return false;
			}

			try
			{
				// Initialize all managers
				await _alarmManager.InitializeAsync();
				await _historian.InitializeAsync();
				await _commManager.InitializeAsync();

				// Start scan cycle
				_scanCancellationToken = new CancellationTokenSource();
				_scanTask = Task.Run(() => ScanCycleAsync(_scanCancellationToken.Token));

				OperatingMode = DCSOperatingMode.Running;
				_auditTrail.LogEvent("DCS_STARTED", "DCS system started", "SYSTEM", AuditEventType.SystemEvent);

				return true;
			}
			catch (Exception ex)
			{
				_auditTrail.LogEvent("DCS_START_ERROR", $"DCS start failed: {ex.Message}", "SYSTEM", AuditEventType.Error);
				return false;
			}
		}

		/// <summary>
		/// Stop the DCS system
		/// </summary>
		public async Task<bool> StopAsync()
		{
			if (OperatingMode == DCSOperatingMode.Stopped)
			{
				return false;
			}

			try
			{
				// Cancel scan cycle
				_scanCancellationToken?.Cancel();

				// Wait for scan task to complete
				if (_scanTask != null)
				{
					await _scanTask;
				}

				// Stop all control loops
				foreach (var loop in _controlLoops.Values)
				{
					loop.Stop();
				}

				// Shutdown managers
				await _historian.ShutdownAsync();
				await _commManager.ShutdownAsync();

				OperatingMode = DCSOperatingMode.Stopped;
				_auditTrail.LogEvent("DCS_STOPPED", "DCS system stopped", "SYSTEM", AuditEventType.SystemEvent);

				return true;
			}
			catch (Exception ex)
			{
				_auditTrail.LogEvent("DCS_STOP_ERROR", $"DCS stop failed: {ex.Message}", "SYSTEM", AuditEventType.Error);
				return false;
			}
		}

		/// <summary>
		/// Main scan cycle that executes all control logic
		/// </summary>
		private async Task ScanCycleAsync(CancellationToken cancellationToken)
		{
			var scanTimer = new System.Diagnostics.Stopwatch();
			var scanTimes = new Queue<double>();

			while (!cancellationToken.IsCancellationRequested)
			{
				scanTimer.Restart();
				var scanStartTime = DateTime.Now;

				try
				{
					// Execute scan cycle
					await ExecuteScanCycleAsync();

					// Update scan metrics
					scanTimer.Stop();
					_scanCycleTime = scanTimer.Elapsed.TotalMilliseconds;

					scanTimes.Enqueue(_scanCycleTime);
					if (scanTimes.Count > 100) scanTimes.Dequeue();

					AverageScanTime = scanTimes.Average();
					MaxScanTime = Math.Max(MaxScanTime, _scanCycleTime);

					// Check for missed scans
					if (_scanCycleTime > _scanCycleMs * 1.5)
					{
						MissedScans++;
						_alarmManager.RaiseAlarm("SCAN_OVERRUN", $"Scan cycle overrun: {_scanCycleTime:F1}ms",
							AlarmPriority.Medium, AlarmCategory.System);
					}

					// Update last scan time
					_lastScanTime = scanStartTime;

					// Wait for next scan cycle
					var waitTime = _scanCycleMs - (int)_scanCycleTime;
					if (waitTime > 0)
					{
						await Task.Delay(waitTime, cancellationToken);
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					_alarmManager.RaiseAlarm("SCAN_ERROR", $"Scan cycle error: {ex.Message}",
						AlarmPriority.High, AlarmCategory.System);

					// Continue with next scan
					await Task.Delay(_scanCycleMs, cancellationToken);
				}
			}
		}

		/// <summary>
		/// Execute one complete scan cycle
		/// </summary>
		private async Task ExecuteScanCycleAsync()
		{
			var scanTime = DateTime.Now;

			// Phase 1: Read all inputs
			await ReadInputsAsync();

			// Phase 2: Execute all control loops
			await ExecuteControlLoopsAsync(scanTime);

			// Phase 3: Execute interlocks and safety logic
			await ExecuteInterlocksAsync();

			// Phase 4: Execute batch/recipe logic
			await ExecuteBatchLogicAsync(scanTime);

			// Phase 5: Write all outputs
			await WriteOutputsAsync();

			// Phase 6: Update alarms
			await _alarmManager.UpdateAlarmsAsync();

			// Phase 7: Log data to historian
			await LogHistoricalDataAsync(scanTime);

			// Phase 8: Update HMI/SCADA
			await UpdateHMIAsync();
		}

		private async Task ReadInputsAsync()
		{
			var readTasks = _processUnits.Values.Select(unit => unit.ReadInputsAsync());
			await Task.WhenAll(readTasks);
		}

		private async Task ExecuteControlLoopsAsync(DateTime scanTime)
		{
			var controlTasks = _controlLoops.Values
				.Where(loop => loop.IsEnabled)
				.Select(loop => Task.Run(() => loop.Execute(scanTime)));

			await Task.WhenAll(controlTasks);
		}

		private async Task ExecuteInterlocksAsync()
		{
			// Execute safety interlocks for all process units
			foreach (var unit in _processUnits.Values)
			{
				await unit.ExecuteInterlocksAsync();
			}
		}

		private async Task ExecuteBatchLogicAsync(DateTime scanTime)
		{
			if (_batchManager.IsRunning)
			{
				await _batchManager.ExecuteAsync(scanTime);
			}
		}

		private async Task WriteOutputsAsync()
		{
			var writeTasks = _processUnits.Values.Select(unit => unit.WriteOutputsAsync());
			await Task.WhenAll(writeTasks);
		}

		private async Task LogHistoricalDataAsync(DateTime timestamp)
		{
			// Collect all process values
			var dataPoints = new List<HistoricalDataPoint>();

			foreach (var unit in _processUnits.Values)
			{
				dataPoints.AddRange(unit.GetHistoricalDataPoints(timestamp));
			}

			foreach (var loop in _controlLoops.Values)
			{
				dataPoints.AddRange(loop.GetHistoricalDataPoints(timestamp));
			}

			// Log to historian
			await _historian.LogDataAsync(dataPoints);
		}

		private async Task UpdateHMIAsync()
		{
			// Update communication manager with latest values
			await _commManager.PublishUpdateAsync();
		}

		#region Control Loop Management

		/// <summary>
		/// Add a control loop to the DCS
		/// </summary>
		public bool AddControlLoop(IControlLoop controlLoop)
		{
			if (controlLoop == null || string.IsNullOrEmpty(controlLoop.LoopId))
				return false;

			if (_controlLoops.TryAdd(controlLoop.LoopId, controlLoop))
			{
				_auditTrail.LogEvent("LOOP_ADDED", $"Control loop added: {controlLoop.LoopId}",
					"SYSTEM", AuditEventType.Configuration);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Remove a control loop from the DCS
		/// </summary>
		public bool RemoveControlLoop(string loopId)
		{
			if (_controlLoops.TryRemove(loopId, out var loop))
			{
				loop.Stop();
				_auditTrail.LogEvent("LOOP_REMOVED", $"Control loop removed: {loopId}",
					"SYSTEM", AuditEventType.Configuration);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Get a control loop by ID
		/// </summary>
		public IControlLoop GetControlLoop(string loopId)
		{
			_controlLoops.TryGetValue(loopId, out var loop);
			return loop;
		}

		#endregion

		#region Process Unit Management

		/// <summary>
		/// Add a process unit to the DCS
		/// </summary>
		public bool AddProcessUnit(ProcessUnit unit)
		{
			if (unit == null || string.IsNullOrEmpty(unit.UnitId))
				return false;

			if (_processUnits.TryAdd(unit.UnitId, unit))
			{
				_auditTrail.LogEvent("UNIT_ADDED", $"Process unit added: {unit.UnitId}",
					"SYSTEM", AuditEventType.Configuration);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Get a process unit by ID
		/// </summary>
		public ProcessUnit GetProcessUnit(string unitId)
		{
			_processUnits.TryGetValue(unitId, out var unit);
			return unit;
		}

		#endregion

		#region Batch Management

		/// <summary>
		/// Start a batch using a recipe
		/// </summary>
		public async Task<bool> StartBatchAsync(string recipeName, string batchId, Dictionary<string, object> parameters)
		{
			if (_batchManager.IsRunning)
			{
				_alarmManager.RaiseAlarm("BATCH_START_FAILED", "A batch is already running",
					AlarmPriority.Medium, AlarmCategory.Process);
				return false;
			}

			var recipe = _recipeManager.GetRecipe(recipeName);
			if (recipe == null)
			{
				_alarmManager.RaiseAlarm("RECIPE_NOT_FOUND", $"Recipe not found: {recipeName}",
					AlarmPriority.High, AlarmCategory.Process);
				return false;
			}

			return await _batchManager.StartBatchAsync(recipe, batchId, parameters);
		}

		/// <summary>
		/// Stop the current batch
		/// </summary>
		public async Task<bool> StopBatchAsync(string reason)
		{
			return await _batchManager.StopBatchAsync(reason);
		}

		#endregion

		#region Alarm Management

		/// <summary>
		/// Get all active alarms
		/// </summary>
		public List<Alarm> GetActiveAlarms()
		{
			return _alarmManager.GetActiveAlarms();
		}

		/// <summary>
		/// Acknowledge an alarm
		/// </summary>
		public bool AcknowledgeAlarm(string alarmId, string userId, string comment = "")
		{
			var result = _alarmManager.AcknowledgeAlarm(alarmId, userId, comment);

			if (result)
			{
				_auditTrail.LogEvent("ALARM_ACK", $"Alarm acknowledged: {alarmId}",
					userId, AuditEventType.AlarmAcknowledgement, comment);
			}

			return result;
		}

		#endregion

		#region Data Access

		/// <summary>
		/// Get historical data for a tag
		/// </summary>
		public async Task<List<HistoricalDataPoint>> GetHistoricalDataAsync(string tagName, DateTime startTime, DateTime endTime)
		{
			return await _historian.GetDataAsync(tagName, startTime, endTime);
		}

		/// <summary>
		/// Get the audit trail
		/// </summary>
		public List<AuditEntry> GetAuditTrail(DateTime? startTime = null, DateTime? endTime = null, string userId = null)
		{
			return _auditTrail.GetAuditTrail(startTime, endTime, userId);
		}

		#endregion

		#region System Health

		/// <summary>
		/// Perform system health check
		/// </summary>
		public SystemHealthReport PerformHealthCheck()
		{
			var report = new SystemHealthReport
			{
				Timestamp = DateTime.Now,
				OverallStatus = SystemHealthStatus.Healthy,
				AverageScanTime = AverageScanTime,
				MaxScanTime = MaxScanTime,
				MissedScans = MissedScans,
				Uptime = Uptime,
				ActiveAlarmCount = _alarmManager.GetActiveAlarms().Count,
				ControlLoopCount = _controlLoops.Count,
				ProcessUnitCount = _processUnits.Count
			};

			// Check for issues
			if (AverageScanTime > _scanCycleMs * 0.8)
			{
				report.OverallStatus = SystemHealthStatus.Degraded;
				report.Issues.Add("Average scan time approaching cycle time limit");
			}

			if (MissedScans > 10)
			{
				report.OverallStatus = SystemHealthStatus.Degraded;
				report.Issues.Add($"{MissedScans} missed scans detected");
			}

			// Check historian health
			if (!_historian.IsHealthy())
			{
				report.OverallStatus = SystemHealthStatus.Degraded;
				report.Issues.Add("Historian not responding");
			}

			// Check communication health
			if (!_commManager.IsHealthy())
			{
				report.OverallStatus = SystemHealthStatus.Degraded;
				report.Issues.Add("Communication issues detected");
			}

			// Check redundancy if enabled
			if (IsRedundant && RedundantController != null)
			{
				if (RedundantController.OperatingMode == DCSOperatingMode.Stopped)
				{
					report.OverallStatus = SystemHealthStatus.Degraded;
					report.Issues.Add("Redundant controller offline");
				}
			}

			HealthStatus = report.OverallStatus;
			return report;
		}

		#endregion
	}

	#region Enums and Supporting Types

	public enum DCSOperatingMode
	{
		Stopped,
		Starting,
		Running,
		Stopping,
		Maintenance,
		Fault
	}

	public enum SystemHealthStatus
	{
		Healthy,
		Degraded,
		Critical,
		Offline
	}

	public class SystemHealthReport
	{
		public DateTime Timestamp { get; set; }
		public SystemHealthStatus OverallStatus { get; set; }
		public double AverageScanTime { get; set; }
		public double MaxScanTime { get; set; }
		public int MissedScans { get; set; }
		public TimeSpan Uptime { get; set; }
		public int ActiveAlarmCount { get; set; }
		public int ControlLoopCount { get; set; }
		public int ProcessUnitCount { get; set; }
		public List<string> Issues { get; set; } = new List<string>();
	}

	#endregion
}
