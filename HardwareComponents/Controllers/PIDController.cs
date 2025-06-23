using PharmaceuticalProcess.HardwareComponents.Core;
using System;
using System.Collections.Generic;

namespace PharmaceuticalProcess.HardwareComponents.Controllers
{
	/// <summary>
	/// Implements a PID controller for pharmaceutical process control applications
	/// </summary>
	public class PIDController : DeviceBase
	{
		public override DeviceType Type => DeviceType.Controller;

		// Control parameters
		public double Setpoint { get; private set; }
		public double ProcessVariable { get; private set; }
		public double Output { get; private set; }

		// PID parameters
		public double ProportionalGain { get; private set; }
		public double IntegralGain { get; private set; }
		public double DerivativeGain { get; private set; }
		public double DerivativeFilterTime { get; private set; }

		// Limits
		public double OutputHighLimit { get; private set; }
		public double OutputLowLimit { get; private set; }
		public double RateHighLimit { get; private set; } // Maximum rate of change for output

		// Controller modes
		public ControllerMode Mode { get; private set; }
		public bool DirectActing { get; private set; } // true for direct action, false for reverse

		// Advanced features
		public double FeedForwardValue { get; private set; }
		public double ManualOutput { get; private set; }
		public double SetpointRampRate { get; private set; } // Units per second, 0 = no ramping

		// Internal state variables
		private double _targetSetpoint;
		private double _previousError;
		private double _integral;
		private double _previousProcessVariable;
		private double _previousOutput;
		private double _previousDerivative;
		private DateTime _lastUpdateTime;
		private bool _firstExecution;

		// Performance metrics
		private double _varianceSum;
		private int _varianceCount;
		private double _lastSetpointChange;
		private DateTime _setpointChangeTime;

		/// <summary>
		/// Creates a new PID controller with specified parameters
		/// </summary>
		public PIDController(
			string deviceId,
			string name,
			double proportionalGain = 1.0,
			double integralGain = 0.1,
			double derivativeGain = 0.0,
			bool directActing = true)
			: base(deviceId, name)
		{
			ProportionalGain = proportionalGain;
			IntegralGain = integralGain;
			DerivativeGain = derivativeGain;
			DerivativeFilterTime = 0.1; // Default filter time constant of 0.1 seconds

			OutputHighLimit = 100.0;
			OutputLowLimit = 0.0;
			RateHighLimit = double.MaxValue; // No rate limiting by default

			DirectActing = directActing;
			Mode = ControllerMode.Manual;

			// Initialize internal state
			Output = 0.0;
			ManualOutput = 0.0;
			Setpoint = 0.0;
			ProcessVariable = 0.0;
			_targetSetpoint = 0.0;
			_integral = 0.0;
			_previousError = 0.0;
			_previousProcessVariable = 0.0;
			_previousOutput = 0.0;
			_previousDerivative = 0.0;
			_firstExecution = true;
			SetpointRampRate = 0.0; // No ramping by default

			// Initialize diagnostic data
			DiagnosticData["ProportionalGain"] = ProportionalGain;
			DiagnosticData["IntegralGain"] = IntegralGain;
			DiagnosticData["DerivativeGain"] = DerivativeGain;
			DiagnosticData["Mode"] = Mode.ToString();
			DiagnosticData["DirectActing"] = DirectActing;
			DiagnosticData["OutputHighLimit"] = OutputHighLimit;
			DiagnosticData["OutputLowLimit"] = OutputLowLimit;
		}

		public override void Initialize()
		{
			base.Initialize();

			_lastUpdateTime = DateTime.Now;
			_firstExecution = true;
			_integral = 0.0;
			_varianceSum = 0.0;
			_varianceCount = 0;
		}

		public override void Update(TimeSpan elapsedTime)
		{
			base.Update(elapsedTime);

			if (Status != DeviceStatus.Running)
				return;

			// Update diagnostic data
			DiagnosticData["Setpoint"] = Setpoint;
			DiagnosticData["ProcessVariable"] = ProcessVariable;
			DiagnosticData["Output"] = Output;
			DiagnosticData["Error"] = DirectActing ? Setpoint - ProcessVariable : ProcessVariable - Setpoint;

			// Handle setpoint ramping
			if (SetpointRampRate > 0 && Setpoint != _targetSetpoint)
			{
				double maxChange = SetpointRampRate * elapsedTime.TotalSeconds;
				if (Math.Abs(_targetSetpoint - Setpoint) <= maxChange)
				{
					Setpoint = _targetSetpoint;
				}
				else
				{
					if (_targetSetpoint > Setpoint)
						Setpoint += maxChange;
					else
						Setpoint -= maxChange;
				}

				DiagnosticData["Setpoint"] = Setpoint;
			}

			// For auto mode, calculate control output
			if (Mode == ControllerMode.Auto)
			{
				CalculateOutput(elapsedTime);
			}
			else if (Mode == ControllerMode.Manual)
			{
				Output = ManualOutput;
				// Reset integral to prevent bump when switching to auto
				_integral = Output - CalculateProportionalTerm() - CalculateDerivativeTerm(elapsedTime);
			}
			else if (Mode == ControllerMode.Cascade)
			{
				// In cascade mode, another controller will provide the setpoint
				// The rest of the calculation is the same as Auto mode
				CalculateOutput(elapsedTime);
			}

			// Update previous state for next calculation
			_previousProcessVariable = ProcessVariable;
			_previousOutput = Output;
			_lastUpdateTime = DateTime.Now;
			_firstExecution = false;

			// Track process variance for diagnostics (error squared)
			double error = DirectActing ? Setpoint - ProcessVariable : ProcessVariable - Setpoint;
			_varianceSum += error * error;
			_varianceCount++;

			if (_varianceCount > 1000) // Prevent overflow by periodically resetting
			{
				DiagnosticData["AverageVariance"] = Math.Sqrt(_varianceSum / _varianceCount);
				_varianceSum = 0;
				_varianceCount = 0;
			}
		}

		/// <summary>
		/// Sets the process variable input to the controller
		/// </summary>
		public void SetProcessVariable(double value)
		{
			ProcessVariable = value;

			// If this is the first execution, initialize previous PV to current PV
			if (_firstExecution)
			{
				_previousProcessVariable = value;
			}
		}

		/// <summary>
		/// Sets the setpoint for the controller
		/// </summary>
		public void SetSetpoint(double value)
		{
			// Enforce limits if configured
			value = Math.Max(OutputLowLimit, Math.Min(OutputHighLimit, value));

			// Record for performance metrics when setpoint changes significantly
			if (Math.Abs(value - Setpoint) > 0.001)
			{
				_lastSetpointChange = value - Setpoint;
				_setpointChangeTime = DateTime.Now;
			}

			// Store as target for ramping
			_targetSetpoint = value;

			// If no ramping, set immediately
			if (SetpointRampRate <= 0)
			{
				Setpoint = value;
			}
		}

		/// <summary>
		/// Sets the controller to manual mode with specified output
		/// </summary>
		public void SetManual(double manualOutput)
		{
			// Constrain the manual output to limits
			manualOutput = Math.Max(OutputLowLimit, Math.Min(OutputHighLimit, manualOutput));

			// Only update if there's a change to avoid bumps
			if (Mode != ControllerMode.Manual || ManualOutput != manualOutput)
			{
				ManualOutput = manualOutput;
				// If switching from auto, we want to maintain the current output for bumpless transfer
				if (Mode == ControllerMode.Auto || Mode == ControllerMode.Cascade)
				{
					ManualOutput = Output;
				}
				Mode = ControllerMode.Manual;
				DiagnosticData["Mode"] = Mode.ToString();
			}
		}

		/// <summary>
		/// Sets the controller to automatic mode
		/// </summary>
		public void SetAuto()
		{
			if (Mode != ControllerMode.Auto)
			{
				// For bumpless transfer, set integral term to achieve current output
				_integral = Output - CalculateProportionalTerm() - CalculateDerivativeTerm(DateTime.Now - _lastUpdateTime);
				Mode = ControllerMode.Auto;
				DiagnosticData["Mode"] = Mode.ToString();
			}
		}

		/// <summary>
		/// Sets the controller to cascade mode where setpoint comes from another controller
		/// </summary>
		public void SetCascade()
		{
			if (Mode != ControllerMode.Cascade)
			{
				// For bumpless transfer, set integral term to achieve current output
				_integral = Output - CalculateProportionalTerm() - CalculateDerivativeTerm(DateTime.Now - _lastUpdateTime);
				Mode = ControllerMode.Cascade;
				DiagnosticData["Mode"] = Mode.ToString();
			}
		}

		/// <summary>
		/// Sets the feed forward value for advanced control strategies
		/// </summary>
		public void SetFeedForward(double value)
		{
			FeedForwardValue = value;
			DiagnosticData["FeedForwardValue"] = value;
		}

		/// <summary>
		/// Configure the PID gains
		/// </summary>
		public void SetPIDGains(double proportionalGain, double integralGain, double derivativeGain)
		{
			// Store old integral effect to recalculate with new gain
			double oldIntegralEffect = IntegralGain * _integral;

			ProportionalGain = proportionalGain;
			IntegralGain = integralGain;
			DerivativeGain = derivativeGain;

			// Adjust integral to maintain same effect with new gain
			if (IntegralGain != 0)
			{
				_integral = oldIntegralEffect / IntegralGain;
			}
			else
			{
				_integral = 0;
			}

			DiagnosticData["ProportionalGain"] = ProportionalGain;
			DiagnosticData["IntegralGain"] = IntegralGain;
			DiagnosticData["DerivativeGain"] = DerivativeGain;
		}

		/// <summary>
		/// Set the output limits for the controller
		/// </summary>
		public void SetOutputLimits(double lowLimit, double highLimit)
		{
			if (lowLimit < highLimit)
			{
				OutputLowLimit = lowLimit;
				OutputHighLimit = highLimit;

				// Constrain current output to new limits
				Output = Math.Max(OutputLowLimit, Math.Min(OutputHighLimit, Output));

				DiagnosticData["OutputLowLimit"] = OutputLowLimit;
				DiagnosticData["OutputHighLimit"] = OutputHighLimit;
			}
		}

		/// <summary>
		/// Set the setpoint ramp rate (units per second)
		/// </summary>
		public void SetSetpointRampRate(double ratePerSecond)
		{
			if (ratePerSecond >= 0)
			{
				SetpointRampRate = ratePerSecond;
				DiagnosticData["SetpointRampRate"] = ratePerSecond;
			}
		}

		/// <summary>
		/// Get performance metrics for the controller
		/// </summary>
		public Dictionary<string, double> GetPerformanceMetrics()
		{
			var metrics = new Dictionary<string, double>();

			// Calculate IAE (Integral of Absolute Error) - lower is better
			if (_varianceCount > 0)
			{
				metrics["RMSE"] = Math.Sqrt(_varianceSum / _varianceCount); // Root Mean Square Error
			}

			// Calculate settling time if we have a recent setpoint change
			if (_lastSetpointChange != 0 && _setpointChangeTime != DateTime.MinValue)
			{
				// Check if we're within 5% of the setpoint now
				double error = Math.Abs((Setpoint - ProcessVariable) / _lastSetpointChange);
				if (error < 0.05)
				{
					metrics["SettlingTime"] = (DateTime.Now - _setpointChangeTime).TotalSeconds;
					_lastSetpointChange = 0; // Reset so we don't keep calculating
				}
			}

			metrics["IntegralTerm"] = _integral;

			return metrics;
		}

		// PID algorithm implementation
		private void CalculateOutput(TimeSpan elapsedTime)
		{
			// Don't update if no time has passed or on first execution
			if (elapsedTime.TotalSeconds <= 0 || _firstExecution)
			{
				return;
			}

			// Calculate the proportional term
			double proportionalTerm = CalculateProportionalTerm();

			// Calculate the derivative term
			double derivativeTerm = CalculateDerivativeTerm(elapsedTime);

			// Calculate the integral term with anti-windup
			double error = DirectActing ? Setpoint - ProcessVariable : ProcessVariable - Setpoint;
			_integral += error * elapsedTime.TotalSeconds;

			// Calculate raw output
			double integralTerm = IntegralGain * _integral;
			double rawOutput = proportionalTerm + integralTerm + derivativeTerm + FeedForwardValue;

			// Apply output limits
			double limitedOutput = Math.Max(OutputLowLimit, Math.Min(OutputHighLimit, rawOutput));

			// Anti-windup - if output is limited, reduce integral accumulation
			if (limitedOutput != rawOutput && IntegralGain != 0)
			{
				// Back-calculate integral to match limited output
				_integral = (limitedOutput - proportionalTerm - derivativeTerm - FeedForwardValue) / IntegralGain;
			}

			// Apply rate limiting if configured
			if (RateHighLimit < double.MaxValue)
			{
				double maxChange = RateHighLimit * elapsedTime.TotalSeconds;
				if (Math.Abs(limitedOutput - _previousOutput) > maxChange)
				{
					if (limitedOutput > _previousOutput)
						limitedOutput = _previousOutput + maxChange;
					else
						limitedOutput = _previousOutput - maxChange;
				}
			}

			// Store final output
			Output = limitedOutput;

			// Update diagnostics
			DiagnosticData["ProportionalTerm"] = proportionalTerm;
			DiagnosticData["IntegralTerm"] = integralTerm;
			DiagnosticData["DerivativeTerm"] = derivativeTerm;
			DiagnosticData["RawOutput"] = rawOutput;
		}

		private double CalculateProportionalTerm()
		{
			double error = DirectActing ? Setpoint - ProcessVariable : ProcessVariable - Setpoint;
			return ProportionalGain * error;
		}

		private double CalculateDerivativeTerm(TimeSpan elapsedTime)
		{
			// If no derivative action or first execution, return 0
			if (DerivativeGain == 0 || _firstExecution)
				return 0;

			// Calculate derivative based on process variable change, not error
			// This avoids derivative kick when setpoint changes
			double pvChange = ProcessVariable - _previousProcessVariable;
			double derivativeRate = (DirectActing ? -1 : 1) * pvChange / elapsedTime.TotalSeconds;

			// Apply first-order filter to derivative to reduce noise sensitivity
			double derivativeTerm = DerivativeGain * derivativeRate;
			double alpha = elapsedTime.TotalSeconds / (DerivativeFilterTime + elapsedTime.TotalSeconds);
			derivativeTerm = (1 - alpha) * _previousDerivative + alpha * derivativeTerm;

			_previousDerivative = derivativeTerm;
			return derivativeTerm;
		}

		protected override void SimulateFault()
		{
			// Simulate various PID controller faults
			int faultType = Random.Next(5);

			switch (faultType)
			{
				case 0: // Output stuck
					AddAlarm("OUTPUT_STUCK", "Controller output stuck at current value", AlarmSeverity.Major);
					if (Mode == ControllerMode.Auto)
					{
						Mode = ControllerMode.Manual; // Force to manual mode
													  // Don't update ManualOutput so it stays at last value
					}
					break;

				case 1: // Oscillation
					AddAlarm("OSCILLATION", "Control loop oscillation detected", AlarmSeverity.Warning);
					// Increase the integral contribution to induce oscillation
					_integral *= 1.5;
					break;

				case 2: // Incorrect parameters
					AddAlarm("TUNING_ERROR", "Poor controller tuning detected", AlarmSeverity.Minor);
					// Temporary gain changes to simulate poor tuning
					ProportionalGain *= 3.0;
					break;

				case 3: // Process variable noise
					AddAlarm("PV_NOISE", "Excessive noise in process variable", AlarmSeverity.Minor);
					// Simulate by adding noise to the derivative term
					_previousDerivative += (Random.NextDouble() * 2 - 1) * 10;
					break;

				case 4: // Configuration error
					AddAlarm("CONFIG_ERROR", "Controller configuration error", AlarmSeverity.Warning);
					DirectActing = !DirectActing; // Temporarily reverse the action
					break;
			}
		}
	}

	/// <summary>
	/// Operating modes for PID controllers
	/// </summary>
	public enum ControllerMode
	{
		Manual,     // Output is manually set by operator
		Auto,       // Output is calculated based on setpoint and process variable
		Cascade     // Setpoint is received from another controller
	}
}