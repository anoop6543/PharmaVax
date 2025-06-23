using PharmaceuticalProcess.HardwareComponents.Core;
using System;

namespace PharmaceuticalProcess.HardwareComponents.Actuators
{
	public class ValveController : DeviceBase
	{
		public override DeviceType Type => DeviceType.Actuator;

		public double Position { get; private set; } // Current position 0-100%
		public double TargetPosition { get; private set; } // Target position 0-100%
		public double PositionAccuracy { get; private set; } // Accuracy in %
		public double SpeedFactor { get; private set; } // Speed of position change in %/second
		public bool HasPositionFeedback { get; private set; }
		public bool HasEndSwitches { get; private set; }

		private double _actualPosition; // Physical position (for simulation)
		private bool _endSwitchOpen = false;
		private bool _endSwitchClosed = true;
		private double _hysteresis = 0.5; // Hysteresis in %
		private double _stickiness = 0; // Chance the valve gets stuck (0-100%)
		private bool _isStuck = false;

		public ValveController(
			string deviceId,
			string name,
			bool hasPositionFeedback = true,
			bool hasEndSwitches = true,
			double positionAccuracy = 0.5,
			double speedFactor = 10.0)
			: base(deviceId, name)
		{
			HasPositionFeedback = hasPositionFeedback;
			HasEndSwitches = hasEndSwitches;
			PositionAccuracy = positionAccuracy;
			SpeedFactor = speedFactor;

			// Initialize at closed position
			Position = 0;
			_actualPosition = 0;
			TargetPosition = 0;

			DiagnosticData["HasPositionFeedback"] = HasPositionFeedback;
			DiagnosticData["HasEndSwitches"] = HasEndSwitches;
			DiagnosticData["PositionAccuracy"] = PositionAccuracy;
			DiagnosticData["EndSwitchOpen"] = _endSwitchOpen;
			DiagnosticData["EndSwitchClosed"] = _endSwitchClosed;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running || _isStuck)
				return;

			// Calculate position change based on speed factor
			double positionDifference = TargetPosition - _actualPosition;
			double maxChange = SpeedFactor * elapsedTime.TotalSeconds;
			double actualChange = Math.Sign(positionDifference) * Math.Min(Math.Abs(positionDifference), maxChange);

			// Update actual position
			_actualPosition += actualChange;

			// Add accuracy error to reported position
			if (HasPositionFeedback)
			{
				double error = (Random.NextDouble() * 2 - 1) * PositionAccuracy;
				Position = Math.Min(Math.Max(_actualPosition + error, 0), 100);
			}
			else
			{
				// If no position feedback, position is just an estimate
				Position = TargetPosition;
			}

			// Update end switches
			if (HasEndSwitches)
			{
				_endSwitchOpen = _actualPosition >= (100 - _hysteresis);
				_endSwitchClosed = _actualPosition <= _hysteresis;

				DiagnosticData["EndSwitchOpen"] = _endSwitchOpen;
				DiagnosticData["EndSwitchClosed"] = _endSwitchClosed;
			}

			// Update diagnostic data
			DiagnosticData["Position"] = Position;
			DiagnosticData["TargetPosition"] = TargetPosition;
			DiagnosticData["ActualPosition"] = _actualPosition;

			// Check if valve should get stuck
			if (Random.NextDouble() * 100 < _stickiness)
			{
				_isStuck = true;
				AddAlarm("VALVE_STUCK", $"Valve stuck at {Position:F1}%", AlarmSeverity.Major);
			}

			// Check for valve movement problems
			if (!_isStuck && Math.Abs(_actualPosition - TargetPosition) < 0.1 && Math.Abs(positionDifference) > 5)
			{
				AddAlarm("VALVE_POSITION_ERROR",
					$"Valve position error: {Position:F1}% vs target {TargetPosition:F1}%",
					AlarmSeverity.Minor);
			}
		}

		public void SetPosition(double targetPosition)
		{
			// Clamp value between 0-100%
			TargetPosition = Math.Min(Math.Max(targetPosition, 0), 100);
		}

		public void Emergency(bool openPosition)
		{
			// Emergency open or close
			TargetPosition = openPosition ? 100 : 0;

			// Increase speed for emergency operation
			SpeedFactor *= 2;

			AddAlarm("VALVE_EMERGENCY",
				$"Valve emergency {(openPosition ? "open" : "close")}",
				AlarmSeverity.Major);
		}

		public void Maintenance()
		{
			Status = DeviceStatus.Maintenance;
			_isStuck = false;
			_stickiness = 0; // Reset stickiness
		}

		protected override void SimulateFault()
		{
			int faultType = Random.Next(3);

			switch (faultType)
			{
				case 0: // Valve stuck
					_isStuck = true;
					AddAlarm("VALVE_STUCK", $"Valve stuck at {Position:F1}%", AlarmSeverity.Major);
					break;
				case 1: // Position feedback failure
					if (HasPositionFeedback)
					{
						AddAlarm("POSITION_FEEDBACK_FAILURE", "Position feedback failure", AlarmSeverity.Minor);
						Position = Random.NextDouble() * 100; // Random incorrect position
					}
					break;
				case 2: // End switch failure
					if (HasEndSwitches)
					{
						AddAlarm("END_SWITCH_FAILURE", "End switch failure", AlarmSeverity.Warning);
						_endSwitchOpen = Random.Next(2) == 0;
						_endSwitchClosed = Random.Next(2) == 0;
						DiagnosticData["EndSwitchOpen"] = _endSwitchOpen;
						DiagnosticData["EndSwitchClosed"] = _endSwitchClosed;
					}
					break;
			}
		}
	}
}