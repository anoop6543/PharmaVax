using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// ISA-88 compliant batch manager for pharmaceutical manufacturing
	/// </summary>
	public class BatchManager
	{
		public bool IsRunning { get; private set; }
		public string CurrentBatchId { get; private set; }
		public Recipe CurrentRecipe { get; private set; }
		public BatchPhase CurrentPhase { get; private set; }
		public int CurrentPhaseIndex { get; private set; }
		public BatchState State { get; private set; }

		private DateTime _batchStartTime;
		private DateTime _phaseStartTime;
		private readonly ConcurrentQueue<BatchEvent> _batchHistory;
		private Dictionary<string, object> _batchParameters;

		public BatchManager()
		{
			IsRunning = false;
			State = BatchState.Idle;
			_batchHistory = new ConcurrentQueue<BatchEvent>();
			CurrentPhaseIndex = -1;
		}

		public async Task<bool> StartBatchAsync(Recipe recipe, string batchId, Dictionary<string, object> parameters)
		{
			if (IsRunning)
			{
				return false;
			}

			CurrentRecipe = recipe;
			CurrentBatchId = batchId;
			_batchParameters = parameters ?? new Dictionary<string, object>();
			CurrentPhaseIndex = 0;
			State = BatchState.Running;
			IsRunning = true;
			_batchStartTime = DateTime.Now;

			// Log batch start
			LogBatchEvent(BatchEventType.BatchStarted, $"Batch {batchId} started with recipe {recipe.Name}");

			// Start first phase
			if (recipe.Phases.Count > 0)
			{
				await StartPhaseAsync(recipe.Phases[0]);
			}

			return true;
		}

		public async Task<bool> StopBatchAsync(string reason)
		{
			if (!IsRunning)
			{
				return false;
			}

			State = BatchState.Stopping;
			LogBatchEvent(BatchEventType.BatchStopped, $"Batch {CurrentBatchId} stopped: {reason}");

			IsRunning = false;
			State = BatchState.Idle;
			CurrentPhase = null;
			CurrentPhaseIndex = -1;

			await Task.CompletedTask;
			return true;
		}

		public async Task ExecuteAsync(DateTime scanTime)
		{
			if (!IsRunning || CurrentPhase == null)
			{
				return;
			}

			// Check if current phase is complete
			var phaseElapsed = (scanTime - _phaseStartTime).TotalMinutes;

			if (await IsPhaseCompleteAsync(phaseElapsed))
			{
				// Move to next phase
				await CompleteCurrentPhaseAsync();

				CurrentPhaseIndex++;

				if (CurrentPhaseIndex < CurrentRecipe.Phases.Count)
				{
					await StartPhaseAsync(CurrentRecipe.Phases[CurrentPhaseIndex]);
				}
				else
				{
					// Batch complete
					await CompleteBatchAsync();
				}
			}
		}

		private async Task StartPhaseAsync(BatchPhase phase)
		{
			CurrentPhase = phase;
			_phaseStartTime = DateTime.Now;

			LogBatchEvent(BatchEventType.PhaseStarted, $"Phase {phase.Name} started");

			// Execute phase operations
			foreach (var operation in phase.Operations)
			{
				await ExecuteOperationAsync(operation);
			}
		}

		private async Task<bool> IsPhaseCompleteAsync(double elapsedMinutes)
		{
			if (CurrentPhase == null)
			{
				return true;
			}

			// Check duration
			if (CurrentPhase.Duration > 0 && elapsedMinutes >= CurrentPhase.Duration)
			{
				return true;
			}

			// Check completion criteria
			if (CurrentPhase.CompletionCriteria != null)
			{
				return await Task.FromResult(CurrentPhase.CompletionCriteria());
			}

			return false;
		}

		private async Task CompleteCurrentPhaseAsync()
		{
			if (CurrentPhase != null)
			{
				LogBatchEvent(BatchEventType.PhaseCompleted, $"Phase {CurrentPhase.Name} completed");
			}

			await Task.CompletedTask;
		}

		private async Task CompleteBatchAsync()
		{
			State = BatchState.Completed;
			IsRunning = false;

			var batchDuration = (DateTime.Now - _batchStartTime).TotalMinutes;
			LogBatchEvent(BatchEventType.BatchCompleted, $"Batch {CurrentBatchId} completed in {batchDuration:F1} minutes");

			await Task.CompletedTask;
		}

		private async Task ExecuteOperationAsync(BatchOperation operation)
		{
			LogBatchEvent(BatchEventType.OperationStarted, $"Operation {operation.Name} started");

			// Execute the operation action
			operation.Action?.Invoke(_batchParameters);

			LogBatchEvent(BatchEventType.OperationCompleted, $"Operation {operation.Name} completed");

			await Task.CompletedTask;
		}

		private void LogBatchEvent(BatchEventType eventType, string message)
		{
			var batchEvent = new BatchEvent
			{
				EventId = Guid.NewGuid().ToString(),
				BatchId = CurrentBatchId,
				EventType = eventType,
				Timestamp = DateTime.Now,
				Message = message,
				PhaseName = CurrentPhase?.Name ?? "N/A"
			};

			_batchHistory.Enqueue(batchEvent);
		}

		public List<BatchEvent> GetBatchHistory()
		{
			return _batchHistory.ToList();
		}

		public BatchReport GenerateBatchReport()
		{
			return new BatchReport
			{
				BatchId = CurrentBatchId,
				RecipeName = CurrentRecipe?.Name ?? "N/A",
				StartTime = _batchStartTime,
				EndTime = DateTime.Now,
				State = State,
				Events = GetBatchHistory(),
				Parameters = new Dictionary<string, object>(_batchParameters)
			};
		}
	}

	public class Recipe
	{
		public string RecipeId { get; set; }
		public string Name { get; set; }
		public string Version { get; set; }
		public string Description { get; set; }
		public List<BatchPhase> Phases { get; set; } = new List<BatchPhase>();
		public Dictionary<string, object> DefaultParameters { get; set; } = new Dictionary<string, object>();
	}

	public class BatchPhase
	{
		public string Name { get; set; }
		public string Description { get; set; }
		public double Duration { get; set; } // Minutes, 0 = wait for completion criteria
		public Func<bool> CompletionCriteria { get; set; }
		public List<BatchOperation> Operations { get; set; } = new List<BatchOperation>();
	}

	public class BatchOperation
	{
		public string Name { get; set; }
		public string Description { get; set; }
		public Action<Dictionary<string, object>> Action { get; set; }
		public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
	}

	public class BatchEvent
	{
		public string EventId { get; set; }
		public string BatchId { get; set; }
		public BatchEventType EventType { get; set; }
		public DateTime Timestamp { get; set; }
		public string Message { get; set; }
		public string PhaseName { get; set; }
	}

	public class BatchReport
	{
		public string BatchId { get; set; }
		public string RecipeName { get; set; }
		public DateTime StartTime { get; set; }
		public DateTime EndTime { get; set; }
		public BatchState State { get; set; }
		public List<BatchEvent> Events { get; set; }
		public Dictionary<string, object> Parameters { get; set; }
	}

	public enum BatchState
	{
		Idle,
		Running,
		Paused,
		Stopping,
		Completed,
		Aborted,
		Fault
	}

	public enum BatchEventType
	{
		BatchStarted,
		BatchCompleted,
		BatchAborted,
		PhaseStarted,
		PhaseCompleted,
		OperationStarted,
		OperationCompleted,
		ParameterChanged,
		AlarmRaised
	}
}
