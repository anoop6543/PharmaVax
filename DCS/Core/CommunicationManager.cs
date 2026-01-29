using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// Manages communication between DCS and external systems (HMI, SCADA, OPC UA clients)
	/// </summary>
	public class CommunicationManager
	{
		private readonly ConcurrentDictionary<string, TagValue> _tagCache;
		private readonly List<ICommunicationInterface> _interfaces;
		private bool _isHealthy;
		private DateTime _lastUpdateTime;

		public CommunicationManager()
		{
			_tagCache = new ConcurrentDictionary<string, TagValue>();
			_interfaces = new List<ICommunicationInterface>();
			_isHealthy = true;
		}

		public async Task InitializeAsync()
		{
			// Initialize all communication interfaces
			foreach (var intf in _interfaces)
			{
				await intf.InitializeAsync();
			}
		}

		public async Task ShutdownAsync()
		{
			// Shutdown all communication interfaces
			foreach (var intf in _interfaces)
			{
				await intf.ShutdownAsync();
			}
		}

		public void AddInterface(ICommunicationInterface commInterface)
		{
			if (commInterface != null && !_interfaces.Contains(commInterface))
			{
				_interfaces.Add(commInterface);
			}
		}

		public void RemoveInterface(ICommunicationInterface commInterface)
		{
			_interfaces.Remove(commInterface);
		}

		public async Task PublishUpdateAsync()
		{
			_lastUpdateTime = DateTime.Now;

			// Publish updates to all registered interfaces
			var publishTasks = _interfaces.Select(intf => intf.PublishDataAsync(_tagCache));
			await Task.WhenAll(publishTasks);
		}

		public void UpdateTag(string tagName, double value, DataQuality quality = DataQuality.Good)
		{
			var tag = _tagCache.GetOrAdd(tagName, _ => new TagValue { TagName = tagName });
			tag.Value = value;
			tag.Quality = quality;
			tag.Timestamp = DateTime.Now;
		}

		public void UpdateTags(Dictionary<string, double> tags)
		{
			foreach (var kvp in tags)
			{
				UpdateTag(kvp.Key, kvp.Value);
			}
		}

		public TagValue GetTag(string tagName)
		{
			_tagCache.TryGetValue(tagName, out var tag);
			return tag;
		}

		public Dictionary<string, TagValue> GetAllTags()
		{
			return new Dictionary<string, TagValue>(_tagCache);
		}

		public bool IsHealthy()
		{
			// Check if all interfaces are healthy
			if (!_interfaces.All(intf => intf.IsHealthy()))
			{
				_isHealthy = false;
			}

			// Check if updates are recent
			if ((DateTime.Now - _lastUpdateTime).TotalSeconds > 10)
			{
				_isHealthy = false;
			}

			return _isHealthy;
		}

		public CommunicationStatistics GetStatistics()
		{
			return new CommunicationStatistics
			{
				TotalTags = _tagCache.Count,
				ActiveInterfaces = _interfaces.Count(i => i.IsHealthy()),
				TotalInterfaces = _interfaces.Count,
				LastUpdateTime = _lastUpdateTime,
				IsHealthy = _isHealthy
			};
		}
	}

	public class TagValue
	{
		public string TagName { get; set; }
		public double Value { get; set; }
		public DateTime Timestamp { get; set; }
		public DataQuality Quality { get; set; }
		public string Description { get; set; }
		public string Units { get; set; }
	}

	public interface ICommunicationInterface
	{
		string InterfaceName { get; }
		bool IsHealthy();
		Task InitializeAsync();
		Task ShutdownAsync();
		Task PublishDataAsync(ConcurrentDictionary<string, TagValue> tags);
	}

	public class CommunicationStatistics
	{
		public int TotalTags { get; set; }
		public int ActiveInterfaces { get; set; }
		public int TotalInterfaces { get; set; }
		public DateTime LastUpdateTime { get; set; }
		public bool IsHealthy { get; set; }
	}

	/// <summary>
	/// OPC UA server interface for industrial communication
	/// </summary>
	public class OPCUAInterface : ICommunicationInterface
	{
		public string InterfaceName => "OPC UA Server";
		private bool _isRunning;

		public bool IsHealthy()
		{
			return _isRunning;
		}

		public async Task InitializeAsync()
		{
			// Initialize OPC UA server
			_isRunning = true;
			await Task.CompletedTask;
		}

		public async Task ShutdownAsync()
		{
			_isRunning = false;
			await Task.CompletedTask;
		}

		public async Task PublishDataAsync(ConcurrentDictionary<string, TagValue> tags)
		{
			// Publish tags to OPC UA clients
			await Task.CompletedTask;
		}
	}

	/// <summary>
	/// Modbus TCP interface for PLC communication
	/// </summary>
	public class ModbusInterface : ICommunicationInterface
	{
		public string InterfaceName => "Modbus TCP";
		private bool _isConnected;

		public bool IsHealthy()
		{
			return _isConnected;
		}

		public async Task InitializeAsync()
		{
			// Initialize Modbus TCP connection
			_isConnected = true;
			await Task.CompletedTask;
		}

		public async Task ShutdownAsync()
		{
			_isConnected = false;
			await Task.CompletedTask;
		}

		public async Task PublishDataAsync(ConcurrentDictionary<string, TagValue> tags)
		{
			// Write tags to Modbus registers
			await Task.CompletedTask;
		}
	}

	/// <summary>
	/// Web API interface for HMI/SCADA systems
	/// </summary>
	public class WebAPIInterface : ICommunicationInterface
	{
		public string InterfaceName => "Web API";
		private bool _isRunning;

		public bool IsHealthy()
		{
			return _isRunning;
		}

		public async Task InitializeAsync()
		{
			// Start Web API server
			_isRunning = true;
			await Task.CompletedTask;
		}

		public async Task ShutdownAsync()
		{
			_isRunning = false;
			await Task.CompletedTask;
		}

		public async Task PublishDataAsync(ConcurrentDictionary<string, TagValue> tags)
		{
			// Make tags available via REST API
			await Task.CompletedTask;
		}
	}
}
