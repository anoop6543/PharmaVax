using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// Represents a process unit that groups related equipment and control logic
	/// </summary>
	public class ProcessUnit
	{
		public string UnitId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public ProcessUnitState State { get; private set; }

		// Equipment references
		private readonly List<DeviceBase> _devices;
		private readonly List<IControlLoop> _controlLoops;
		private readonly List<Interlock> _interlocks;

		// Process values
		private readonly Dictionary<string, ProcessValue> _processValues;

		// Batch tracking
		public string CurrentBatchId { get; private set; }
		public string CurrentRecipePhase { get; private set; }

		public ProcessUnit(string unitId, string name, string description = "")
		{
			UnitId = unitId;
			Name = name;
			Description = description;
			State = ProcessUnitState.Idle;

			_devices = new List<DeviceBase>();
			_controlLoops = new List<IControlLoop>();
			_interlocks = new List<Interlock>();
			_processValues = new Dictionary<string, ProcessValue>();
		}

		#region Device Management

		public void AddDevice(DeviceBase device)
		{
			if (device != null && !_devices.Contains(device))
			{
				_devices.Add(device);
			}
		}

		public void RemoveDevice(DeviceBase device)
		{
			_devices.Remove(device);
		}

		public List<DeviceBase> GetDevices()
		{
			return new List<DeviceBase>(_devices);
		}

		#endregion

		#region Control Loop Management

		public void AddControlLoop(IControlLoop loop)
		{
			if (loop != null && !_controlLoops.Contains(loop))
			{
				_controlLoops.Add(loop);
			}
		}

		public void RemoveControlLoop(IControlLoop loop)
		{
			_controlLoops.Remove(loop);
		}

		#endregion

		#region Interlock Management

		public void AddInterlock(Interlock interlock)
		{
			if (interlock != null && !_interlocks.Contains(interlock))
			{
				_interlocks.Add(interlock);
			}
		}

		public async Task ExecuteInterlocksAsync()
		{
			foreach (var interlock in _interlocks)
			{
				if (interlock.IsEnabled)
				{
					await interlock.EvaluateAsync();
				}
			}
		}

		#endregion

		#region I/O Operations

		public async Task ReadInputsAsync()
		{
			// Read all device inputs
			var readTasks = new List<Task>();

			foreach (var device in _devices)
			{
				readTasks.Add(Task.Run(() =>
				{
					// Update process values from device
					UpdateProcessValuesFromDevice(device);
				}));
			}

			await Task.WhenAll(readTasks);
		}

		public async Task WriteOutputsAsync()
		{
			// Write all device outputs
			await Task.CompletedTask;
		}

		private void UpdateProcessValuesFromDevice(DeviceBase device)
		{
			// Extract relevant values from device diagnostic data
			foreach (var kvp in device.DiagnosticData)
			{
				string tagName = $"{UnitId}.{device.DeviceId}.{kvp.Key}";

				if (!_processValues.ContainsKey(tagName))
				{
					_processValues[tagName] = new ProcessValue
					{
						TagName = tagName,
						Description = $"{device.Name} - {kvp.Key}"
					};
				}

				if (kvp.Value is IConvertible)
				{
					_processValues[tagName].Value = Convert.ToDouble(kvp.Value);
					_processValues[tagName].Timestamp = DateTime.Now;
				}
			}
		}

		#endregion

		#region Process Value Access

		public double GetProcessValue(string tagName)
		{
			if (_processValues.TryGetValue(tagName, out var pv))
			{
				return pv.Value;
			}

			return double.NaN;
		}

		public bool SetProcessValue(string tagName, double value)
		{
			if (_processValues.TryGetValue(tagName, out var pv))
			{
				pv.Value = value;
				pv.Timestamp = DateTime.Now;
				return true;
			}

			return false;
		}

		public Dictionary<string, double> GetAllProcessValues()
		{
			var values = new Dictionary<string, double>();

			foreach (var kvp in _processValues)
			{
				values[kvp.Key] = kvp.Value.Value;
			}

			return values;
		}

		#endregion

		#region Historical Data

		public List<HistoricalDataPoint> GetHistoricalDataPoints(DateTime timestamp)
		{
			var dataPoints = new List<HistoricalDataPoint>();

			foreach (var pv in _processValues.Values)
			{
				dataPoints.Add(new HistoricalDataPoint
				{
					TagName = pv.TagName,
					Timestamp = timestamp,
					Value = pv.Value,
					Quality = pv.Quality,
					Units = pv.Units
				});
			}

			return dataPoints;
		}

		#endregion

		#region State Management

		public void SetState(ProcessUnitState state)
		{
			State = state;
		}

		public void SetBatchInfo(string batchId, string recipePhase)
		{
			CurrentBatchId = batchId;
			CurrentRecipePhase = recipePhase;
		}

		#endregion
	}

	public class ProcessValue
	{
		public string TagName { get; set; }
		public string Description { get; set; }
		public double Value { get; set; }
		public DateTime Timestamp { get; set; }
		public DataQuality Quality { get; set; } = DataQuality.Good;
		public string Units { get; set; }
		public double HighAlarmLimit { get; set; } = double.MaxValue;
		public double LowAlarmLimit { get; set; } = double.MinValue;
		public double HighHighAlarmLimit { get; set; } = double.MaxValue;
		public double LowLowAlarmLimit { get; set; } = double.MinValue;
	}

	public class Interlock
	{
		public string InterlockId { get; set; }
		public string Description { get; set; }
		public bool IsEnabled { get; set; }
		public InterlockType Type { get; set; }
		public Func<bool> Condition { get; set; }
		public Action<string> Action { get; set; }

		public async Task<bool> EvaluateAsync()
		{
			if (Condition != null && Condition())
			{
				// Interlock condition is true - take action
				Action?.Invoke($"Interlock {InterlockId} activated");
				return await Task.FromResult(true);
			}

			return await Task.FromResult(false);
		}
	}

	public enum ProcessUnitState
	{
		Idle,
		Running,
		Paused,
		Stopping,
		Fault,
		Maintenance
	}

	public enum InterlockType
	{
		Safety,
		Process,
		Equipment,
		Permissive
	}
}
