using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// Time-series data historian for storing and retrieving process data
	/// </summary>
	public class DataHistorian
	{
		private readonly ConcurrentDictionary<string, ConcurrentQueue<HistoricalDataPoint>> _dataStore;
		private readonly int _maxPointsPerTag;
		private readonly TimeSpan _retentionPeriod;
		private bool _isHealthy;

		public DataHistorian(int maxPointsPerTag = 100000, int retentionDays = 365)
		{
			_dataStore = new ConcurrentDictionary<string, ConcurrentQueue<HistoricalDataPoint>>();
			_maxPointsPerTag = maxPointsPerTag;
			_retentionPeriod = TimeSpan.FromDays(retentionDays);
			_isHealthy = true;
		}

		public async Task InitializeAsync()
		{
			// Initialize historian database connection
			await Task.CompletedTask;
		}

		public async Task ShutdownAsync()
		{
			// Flush all pending writes
			await Task.CompletedTask;
		}

		public async Task LogDataAsync(List<HistoricalDataPoint> dataPoints)
		{
			foreach (var point in dataPoints)
			{
				await LogDataPointAsync(point);
			}
		}

		public async Task LogDataPointAsync(HistoricalDataPoint dataPoint)
		{
			try
			{
				var queue = _dataStore.GetOrAdd(dataPoint.TagName, _ => new ConcurrentQueue<HistoricalDataPoint>());
				queue.Enqueue(dataPoint);

				// Trim old data
				while (queue.Count > _maxPointsPerTag)
				{
					queue.TryDequeue(out _);
				}

				await Task.CompletedTask;
			}
			catch
			{
				_isHealthy = false;
			}
		}

		public async Task<List<HistoricalDataPoint>> GetDataAsync(string tagName, DateTime startTime, DateTime endTime)
		{
			if (_dataStore.TryGetValue(tagName, out var queue))
			{
				var results = queue
					.Where(p => p.Timestamp >= startTime && p.Timestamp <= endTime)
					.OrderBy(p => p.Timestamp)
					.ToList();

				return await Task.FromResult(results);
			}

			return new List<HistoricalDataPoint>();
		}

		public async Task<HistoricalDataPoint> GetLatestValueAsync(string tagName)
		{
			if (_dataStore.TryGetValue(tagName, out var queue))
			{
				return await Task.FromResult(queue.LastOrDefault());
			}

			return null;
		}

		public async Task<Dictionary<string, HistoricalDataPoint>> GetLatestValuesAsync(List<string> tagNames)
		{
			var results = new Dictionary<string, HistoricalDataPoint>();

			foreach (var tagName in tagNames)
			{
				var latest = await GetLatestValueAsync(tagName);
				if (latest != null)
				{
					results[tagName] = latest;
				}
			}

			return results;
		}

		public bool IsHealthy()
		{
			return _isHealthy;
		}

		public async Task<List<string>> GetAllTagNamesAsync()
		{
			return await Task.FromResult(_dataStore.Keys.ToList());
		}

		public async Task<HistorianStatistics> GetStatisticsAsync()
		{
			var stats = new HistorianStatistics
			{
				TotalTags = _dataStore.Count,
				TotalDataPoints = _dataStore.Values.Sum(q => q.Count),
				OldestTimestamp = _dataStore.Values
					.Where(q => q.Count > 0)
					.Select(q => q.First().Timestamp)
					.DefaultIfEmpty(DateTime.MaxValue)
					.Min(),
				LatestTimestamp = _dataStore.Values
					.Where(q => q.Count > 0)
					.Select(q => q.Last().Timestamp)
					.DefaultIfEmpty(DateTime.MinValue)
					.Max()
			};

			return await Task.FromResult(stats);
		}
	}

	public class HistoricalDataPoint
	{
		public string TagName { get; set; }
		public DateTime Timestamp { get; set; }
		public double Value { get; set; }
		public DataQuality Quality { get; set; } = DataQuality.Good;
		public string Units { get; set; }
	}

	public class HistorianStatistics
	{
		public int TotalTags { get; set; }
		public int TotalDataPoints { get; set; }
		public DateTime OldestTimestamp { get; set; }
		public DateTime LatestTimestamp { get; set; }
	}

	public enum DataQuality
	{
		Good,
		Bad,
		Uncertain
	}
}
