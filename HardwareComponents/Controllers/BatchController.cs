using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.Controllers
{
	/// <summary>
	/// Controls batch execution for pharmaceutical manufacturing processes
	/// </summary>
	public class BatchController : DeviceBase
	{
		public override DeviceType Type => DeviceType.Controller;

		public string CurrentBatchId { get; private set; }
		public BatchStatus BatchStatus { get; private set; }
		public Dictionary<string, Recipe> RecipeLibrary { get; private set; }
		public Dictionary<string, BatchProcess> BatchHistory { get; private set; }

		private Recipe _activeRecipe;
		private ProcessPhase _currentPhase;
		private int _currentPhaseIndex;
		private bool _phaseTransitionRequested;
		private DateTime _batchStartTime;

		// Integration points
		public PLCController ProcessController { get; private set; }
		public AuditTrailManager AuditManager { get; private set; }

		// Events
		public event EventHandler<BatchEventArgs> BatchStatusChanged;
		public event EventHandler<PhaseEventArgs> PhaseStatusChanged;

		public BatchController(
			string deviceId,
			string name,
			PLCController processController)
			: base(deviceId, name)
		{
			ProcessController = processController;
			RecipeLibrary = new Dictionary<string, Recipe>();
			BatchHistory = new Dictionary<string, BatchProcess>();
			BatchStatus = BatchStatus.Idle;

			// Use same audit manager as the PLC for consistency
			AuditManager = processController.AuditManager;

			// Add diagnostic data
			DiagnosticData["CurrentBatchId"] = string.Empty;
			DiagnosticData["BatchStatus"] = BatchStatus.ToString();
			DiagnosticData["ActiveRecipe"] = string.Empty;
			DiagnosticData["CurrentPhase"] = string.Empty;
		}

		public override void Initialize()
		{
			base.Initialize();

			// Initialize batch state
			CurrentBatchId = string.Empty;
			_activeRecipe = null;
			_currentPhase = null;
			_currentPhaseIndex = -1;
			BatchStatus = BatchStatus.Idle;

			// Update diagnostics
			DiagnosticData["BatchStatus"] = BatchStatus.ToString();
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Handle current batch execution
			if (BatchStatus == BatchStatus.InProcess)
			{
				// Update current phase
				if (_currentPhase != null)
				{
					_currentPhase.Update(elapsedTime);
					DiagnosticData["PhaseProgress"] = _currentPhase.Progress;
					DiagnosticData["PhaseStatus"] = _currentPhase.Status.ToString();

					// Check if phase is complete
					if (_currentPhase.Status == PhaseStatus.Complete || _phaseTransitionRequested)
					{
						_phaseTransitionRequested = false;
						MoveToNextPhase();
					}
				}

				// Update batch progress
				if (_activeRecipe != null)
				{
					double totalPhases = _activeRecipe.Phases.Count;
					double completedPhases = _currentPhaseIndex;
					double currentProgress = _currentPhase?.Progress ?? 0;
					double overallProgress = (completedPhases + (currentProgress / 100.0)) / totalPhases * 100.0;

					DiagnosticData["BatchProgress"] = overallProgress;
					DiagnosticData["BatchDuration"] = (DateTime.Now - _batchStartTime).TotalMinutes;
				}
			}
		}

		public bool LoadRecipe(string recipeId)
		{
			if (BatchStatus != BatchStatus.Idle)
			{
				AddAlarm("BATCH_ACTIVE", "Cannot load recipe when batch is active", AlarmSeverity.Warning);
				return false;
			}

			if (!RecipeLibrary.TryGetValue(recipeId, out Recipe recipe))
			{
				AddAlarm("RECIPE_NOT_FOUND", $"Recipe {recipeId} not found", AlarmSeverity.Minor);
				return false;
			}

			_activeRecipe = recipe;
			DiagnosticData["ActiveRecipe"] = recipeId;

			AuditManager.LogAction("System", $"Load Recipe {recipeId}", "Production preparation", true);
			return true;
		}

		public BatchProcess CreateBatch(string batchId)
		{
			if (_activeRecipe == null)
			{
				AddAlarm("NO_RECIPE", "No recipe loaded", AlarmSeverity.Minor);
				return null;
			}

			if (BatchStatus != BatchStatus.Idle)
			{
				AddAlarm("BATCH_ACTIVE", "Cannot create batch when another is active", AlarmSeverity.Warning);
				return null;
			}

			if (BatchHistory.ContainsKey(batchId))
			{
				AddAlarm("DUPLICATE_BATCH", $"Batch ID {batchId} already exists", AlarmSeverity.Warning);
				return null;
			}

			// Create new batch
			var batch = new BatchProcess(batchId, _activeRecipe);
			BatchHistory[batchId] = batch;

			// Update state
			CurrentBatchId = batchId;
			DiagnosticData["CurrentBatchId"] = batchId;

			AuditManager.LogAction("System", $"Create Batch {batchId}", "Production start", true);
			return batch;
		}

		public bool StartBatch()
		{
			if (_activeRecipe == null)
			{
				AddAlarm("NO_RECIPE", "No recipe loaded", AlarmSeverity.Minor);
				return false;
			}

			if (string.IsNullOrEmpty(CurrentBatchId))
			{
				AddAlarm("NO_BATCH_ID", "No batch ID assigned", AlarmSeverity.Minor);
				return false;
			}

			if (BatchStatus != BatchStatus.Idle && BatchStatus != BatchStatus.Paused)
			{
				AddAlarm("INVALID_STATE", "Cannot start batch in current state", AlarmSeverity.Warning);
				return false;
			}

			// Start the batch
			BatchStatus = BatchStatus.InProcess;
			_batchStartTime = DateTime.Now;
			DiagnosticData["BatchStatus"] = BatchStatus.ToString();
			DiagnosticData["BatchStartTime"] = _batchStartTime;

			// Start first phase if needed
			if (_currentPhaseIndex < 0)
			{
				_currentPhaseIndex = 0;
				_currentPhase = _activeRecipe.Phases[0];
				_currentPhase.Start();
				DiagnosticData["CurrentPhase"] = _currentPhase.Name;

				OnPhaseStatusChanged(_currentPhase.Name, PhaseStatus.Running);
			}

			OnBatchStatusChanged(CurrentBatchId, BatchStatus);
			AuditManager.LogAction("Operator", $"Start Batch {CurrentBatchId}", "Production execution", true);
			return true;
		}

		public bool PauseBatch()
		{
			if (BatchStatus != BatchStatus.InProcess)
			{
				AddAlarm("INVALID_STATE", "Cannot pause batch in current state", AlarmSeverity.Warning);
				return false;
			}

			BatchStatus = BatchStatus.Paused;
			DiagnosticData["BatchStatus"] = BatchStatus.ToString();

			if (_currentPhase != null)
			{
				_currentPhase.Pause();
			}

			OnBatchStatusChanged(CurrentBatchId, BatchStatus);
			AuditManager.LogAction("Operator", $"Pause Batch {CurrentBatchId}", "Production suspended", true);
			return true;
		}

		public bool AbortBatch(string reason)
		{
			if (BatchStatus != BatchStatus.InProcess && BatchStatus != BatchStatus.Paused)
			{
				AddAlarm("INVALID_STATE", "Cannot abort batch in current state", AlarmSeverity.Warning);
				return false;
			}

			BatchStatus = BatchStatus.Aborted;
			DiagnosticData["BatchStatus"] = BatchStatus.ToString();
			DiagnosticData["AbortReason"] = reason;

			if (_currentPhase != null)
			{
				_currentPhase.Abort();
			}

			OnBatchStatusChanged(CurrentBatchId, BatchStatus);
			AuditManager.LogAction("Supervisor", $"Abort Batch {CurrentBatchId}", reason, true);
			return true;
		}

		public bool CompleteBatch()
		{
			if (BatchStatus != BatchStatus.InProcess)
			{
				AddAlarm("INVALID_STATE", "Cannot complete batch in current state", AlarmSeverity.Warning);
				return false;
			}

			if (_currentPhase != null && _currentPhase.Status != PhaseStatus.Complete)
			{
				AddAlarm("ACTIVE_PHASE", "Cannot complete batch with active phase", AlarmSeverity.Warning);
				return false;
			}

			BatchStatus = BatchStatus.Completed;
			DiagnosticData["BatchStatus"] = BatchStatus.ToString();
			DiagnosticData["BatchEndTime"] = DateTime.Now;

			OnBatchStatusChanged(CurrentBatchId, BatchStatus);
			AuditManager.LogAction("Operator", $"Complete Batch {CurrentBatchId}", "Production completed", true);
			return true;
		}

		public bool MoveToNextPhase()
		{
			if (BatchStatus != BatchStatus.InProcess)
			{
				_phaseTransitionRequested = true;
				return false;
			}

			if (_activeRecipe == null || _currentPhaseIndex < 0)
			{
				AddAlarm("INVALID_STATE", "No active recipe or phase", AlarmSeverity.Warning);
				return false;
			}

			// Complete current phase
			if (_currentPhase != null && _currentPhase.Status != PhaseStatus.Complete)
			{
				_currentPhase.Complete();
				OnPhaseStatusChanged(_currentPhase.Name, PhaseStatus.Complete);
			}

			// Check if this was the last phase
			if (_currentPhaseIndex >= _activeRecipe.Phases.Count - 1)
			{
				// Complete the batch automatically
				return CompleteBatch();
			}

			// Move to next phase
			_currentPhaseIndex++;
			_currentPhase = _activeRecipe.Phases[_currentPhaseIndex];
			DiagnosticData["CurrentPhase"] = _currentPhase.Name;

			// Start the next phase
			_currentPhase.Start();
			OnPhaseStatusChanged(_currentPhase.Name, PhaseStatus.Running);

			AuditManager.LogAction("System", $"Phase transition to {_currentPhase.Name}", "Process execution", true);
			return true;
		}

		public bool AddRecipe(Recipe recipe)
		{
			if (recipe == null || string.IsNullOrEmpty(recipe.RecipeId))
				return false;

			if (RecipeLibrary.ContainsKey(recipe.RecipeId))
				return false;

			RecipeLibrary[recipe.RecipeId] = recipe;
			AuditManager.LogAction("Administrator", $"Add Recipe {recipe.RecipeId}", "Configuration update", true);
			return true;
		}

		protected virtual void OnBatchStatusChanged(string batchId, BatchStatus status)
		{
			BatchStatusChanged?.Invoke(this, new BatchEventArgs(batchId, status));
		}

		protected virtual void OnPhaseStatusChanged(string phaseName, PhaseStatus status)
		{
			PhaseStatusChanged?.Invoke(this, new PhaseEventArgs(phaseName, status));
		}

		protected override void SimulateFault()
		{
			if (BatchStatus != BatchStatus.InProcess)
				return;

			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Minor deviation
					AddAlarm("PARAMETER_DEVIATION", "Process parameter deviation detected", AlarmSeverity.Minor);
					break;

				case 1: // Batch hold required
					AddAlarm("HOLD_REQUIRED", "Process exception requires batch hold", AlarmSeverity.Warning);
					PauseBatch();
					break;

				case 2: // Critical fault
					AddAlarm("CRITICAL_DEVIATION", "Critical process deviation detected", AlarmSeverity.Critical);
					AbortBatch("Critical process deviation");
					break;
			}
		}
	}

	public class Recipe
	{
		public string RecipeId { get; private set; }
		public string Name { get; private set; }
		public string Version { get; private set; }
		public string Author { get; private set; }
		public DateTime CreationDate { get; private set; }
		public List<ProcessPhase> Phases { get; private set; }
		public Dictionary<string, object> Parameters { get; set; }
		public Dictionary<string, MaterialRequirement> Materials { get; set; }

		public Recipe(string recipeId, string name, string version)
		{
			RecipeId = recipeId;
			Name = name;
			Version = version;
			Author = "System";
			CreationDate = DateTime.Now;
			Phases = new List<ProcessPhase>();
			Parameters = new Dictionary<string, object>();
			Materials = new Dictionary<string, MaterialRequirement>();
		}

		public bool AddPhase(ProcessPhase phase)
		{
			if (phase == null)
				return false;

			Phases.Add(phase);
			return true;
		}

		public bool Validate()
		{
			// Basic validation - check if we have phases
			return Phases.Count > 0;
		}
	}

	public class ProcessPhase
	{
		public string Name { get; private set; }
		public PhaseStatus Status { get; private set; }
		public double Progress { get; private set; }
		public Dictionary<string, object> Parameters { get; set; }

		private DateTime _startTime;
		private TimeSpan _expectedDuration;

		public ProcessPhase(string name, TimeSpan expectedDuration)
		{
			Name = name;
			Status = PhaseStatus.Idle;
			Progress = 0.0;
			_expectedDuration = expectedDuration;
			Parameters = new Dictionary<string, object>();
		}

		public void Start()
		{
			if (Status == PhaseStatus.Idle || Status == PhaseStatus.Paused)
			{
				Status = PhaseStatus.Running;
				if (Status != PhaseStatus.Paused) // Don't reset start time if resuming
					_startTime = DateTime.Now;
			}
		}

		public void Pause()
		{
			if (Status == PhaseStatus.Running)
			{
				Status = PhaseStatus.Paused;
			}
		}

		public void Complete()
		{
			Status = PhaseStatus.Complete;
			Progress = 100.0;
		}

		public void Abort()
		{
			Status = PhaseStatus.Aborted;
		}

		public void Update(TimeSpan elapsedTime)
		{
			if (Status != PhaseStatus.Running)
				return;

			// Update progress based on elapsed time
			if (_expectedDuration.TotalSeconds > 0)
			{
				TimeSpan elapsed = DateTime.Now - _startTime;
				Progress = Math.Min(100.0, (elapsed.TotalSeconds / _expectedDuration.TotalSeconds) * 100.0);

				// Auto-complete if time elapsed
				if (Progress >= 100.0)
				{
					Complete();
				}
			}
		}
	}

	public class BatchProcess
	{
		public string BatchId { get; private set; }
		public Recipe Recipe { get; private set; }
		public BatchStatus Status { get; private set; }
		public DateTime CreationTime { get; private set; }
		public DateTime? StartTime { get; private set; }
		public DateTime? EndTime { get; private set; }
		public Dictionary<string, object> Results { get; private set; }

		public BatchProcess(string batchId, Recipe recipe)
		{
			BatchId = batchId;
			Recipe = recipe;
			Status = BatchStatus.Created;
			CreationTime = DateTime.Now;
			Results = new Dictionary<string, object>();
		}

		public void Start()
		{
			if (Status == BatchStatus.Created || Status == BatchStatus.Paused)
			{
				Status = BatchStatus.InProcess;
				if (!StartTime.HasValue) // Only set start time if not already started
					StartTime = DateTime.Now;
			}
		}

		public void Complete()
		{
			Status = BatchStatus.Completed;
			EndTime = DateTime.Now;
		}

		public void Fail(string reason)
		{
			Status = BatchStatus.Failed;
			EndTime = DateTime.Now;
			Results["FailureReason"] = reason;
		}

		public void Abort(string reason)
		{
			Status = BatchStatus.Aborted;
			EndTime = DateTime.Now;
			Results["AbortReason"] = reason;
		}

		public void AddResult(string name, object value)
		{
			Results[name] = value;
		}
	}

	public class MaterialRequirement
	{
		public string MaterialId { get; set; }
		public string Description { get; set; }
		public double Quantity { get; set; }
		public string Unit { get; set; }

		public MaterialRequirement(string materialId, string description, double quantity, string unit)
		{
			MaterialId = materialId;
			Description = description;
			Quantity = quantity;
			Unit = unit;
		}
	}

	public enum BatchStatus
	{
		Idle,
		Created,
		InProcess,
		Paused,
		Completed,
		Failed,
		Aborted,
		Rejected,
		Released
	}

	public enum PhaseStatus
	{
		Idle,
		Running,
		Paused,
		Complete,
		Aborted,
		Failed
	}

	public class BatchEventArgs : EventArgs
	{
		public string BatchId { get; }
		public BatchStatus Status { get; }

		public BatchEventArgs(string batchId, BatchStatus status)
		{
			BatchId = batchId;
			Status = status;
		}
	}

	public class PhaseEventArgs : EventArgs
	{
		public string PhaseName { get; }
		public PhaseStatus Status { get; }

		public PhaseEventArgs(string phaseName, PhaseStatus status)
		{
			PhaseName = phaseName;
			Status = status;
		}
	}
}