using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.Controllers
{
	/// <summary>
	/// Manages user access and permission control for regulatory compliance
	/// </summary>
	public class UserAccessManager
	{
		private Dictionary<string, UserRole> _userRoles;
		private Dictionary<string, List<string>> _rolePermissions;
		private Dictionary<string, DateTime> _activeUsers;

		public UserAccessManager()
		{
			_userRoles = new Dictionary<string, UserRole>(StringComparer.OrdinalIgnoreCase);
			_rolePermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			_activeUsers = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

			// Initialize default roles and permissions
			InitializeDefaultRoles();
		}

		private void InitializeDefaultRoles()
		{
			// Define standard roles
			_rolePermissions["Operator"] = new List<string>
			{
				"StartBatch", "StopBatch", "PauseBatch", "ViewProcessData", "AcknowledgeAlarms"
			};

			_rolePermissions["Supervisor"] = new List<string>
			{
				"StartBatch", "StopBatch", "PauseBatch", "ViewProcessData", "AcknowledgeAlarms",
				"ModifyProcessParameters", "AbortBatch", "ApproveDeviation"
			};

			_rolePermissions["QualityControl"] = new List<string>
			{
				"ViewProcessData", "ApproveDeviation", "ReviewBatchRecords",
				"ReleaseProduct", "InitiateDeviation"
			};

			_rolePermissions["Administrator"] = new List<string>
			{
				"StartBatch", "StopBatch", "PauseBatch", "ViewProcessData", "AcknowledgeAlarms",
				"ModifyProcessParameters", "AbortBatch", "ApproveDeviation",
				"ConfigureSystem", "ManageUsers", "ModifyRecipes", "OverrideInterlocks"
			};

			_rolePermissions["Maintenance"] = new List<string>
			{
				"ViewProcessData", "AcknowledgeAlarms", "CalibrateInstruments",
				"ServiceEquipment", "DiagnosticMode"
			};
		}

		public bool AddUser(string username, UserRole role)
		{
			if (string.IsNullOrEmpty(username))
				return false;

			_userRoles[username] = role;
			return true;
		}

		public bool RemoveUser(string username)
		{
			if (string.IsNullOrEmpty(username) || !_userRoles.ContainsKey(username))
				return false;

			_userRoles.Remove(username);
			if (_activeUsers.ContainsKey(username))
				_activeUsers.Remove(username);

			return true;
		}

		public bool ValidateAccess(string username, string permission)
		{
			// Check if user exists and is active
			if (string.IsNullOrEmpty(username) || !_userRoles.ContainsKey(username))
				return false;

			if (!_activeUsers.ContainsKey(username))
				return false;

			// Get user's role
			UserRole role = _userRoles[username];
			string roleName = role.ToString();

			// Check if role has permission
			if (!_rolePermissions.ContainsKey(roleName))
				return false;

			return _rolePermissions[roleName].Contains(permission);
		}

		public bool LogUserIn(string username)
		{
			if (string.IsNullOrEmpty(username) || !_userRoles.ContainsKey(username))
				return false;

			_activeUsers[username] = DateTime.Now;
			return true;
		}

		public bool LogUserOut(string username)
		{
			if (string.IsNullOrEmpty(username) || !_activeUsers.ContainsKey(username))
				return false;

			_activeUsers.Remove(username);
			return true;
		}

		public bool IsUserLoggedIn(string username)
		{
			return !string.IsNullOrEmpty(username) && _activeUsers.ContainsKey(username);
		}

		public UserRole? GetUserRole(string username)
		{
			if (string.IsNullOrEmpty(username) || !_userRoles.ContainsKey(username))
				return null;

			return _userRoles[username];
		}

		public void AddPermissionToRole(string roleName, string permission)
		{
			if (!_rolePermissions.ContainsKey(roleName))
				_rolePermissions[roleName] = new List<string>();

			if (!_rolePermissions[roleName].Contains(permission))
				_rolePermissions[roleName].Add(permission);
		}

		public void RemovePermissionFromRole(string roleName, string permission)
		{
			if (!_rolePermissions.ContainsKey(roleName))
				return;

			_rolePermissions[roleName].Remove(permission);
		}

		public List<string> GetActiveUsers()
		{
			return new List<string>(_activeUsers.Keys);
		}
	}

	public enum UserRole
	{
		Operator,
		Supervisor,
		QualityControl,
		Administrator,
		Maintenance
	}
}