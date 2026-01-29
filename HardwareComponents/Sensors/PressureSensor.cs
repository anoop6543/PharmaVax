namespace PharmaceuticalProcess.HardwareComponents.Sensors
{
    /// <summary>
    /// Simulates a pressure sensor for monitoring differential pressure in cleanroom environments.
    /// </summary>
    public class PressureSensor : DeviceBase
    {
        public override DeviceType Type => DeviceType.Sensor;

        public double Pressure { get; private set; } // Pascal
        public double Accuracy { get; private set; } // ±Pascal

        public PressureSensor(string deviceId, string name, double accuracy = 0.5)
            : base(deviceId, name)
        {
            Pressure = 45.0; // Default to 45 Pa
            Accuracy = accuracy;

            DiagnosticData["Pressure"] = Pressure;
            DiagnosticData["Accuracy"] = Accuracy;
        }

        public void SetPressure(double pressure)
        {
            // Add measurement variation based on accuracy
            double variation = (Random.NextDouble() * 2.0 - 1.0) * Accuracy;
            Pressure = pressure + variation;
            Pressure = Math.Max(0, Pressure);

            DiagnosticData["Pressure"] = Pressure;
        }

        public override void Update(TimeSpan elapsedTime)
        {
            base.Update(elapsedTime);

            if (Status != DeviceStatus.Running)
                return;

            // Add random fluctuation
            Pressure *= 1.0 + ((Random.NextDouble() * 0.02) - 0.01); // ±1% variation

            DiagnosticData["Pressure"] = Pressure;
        }

        protected override void SimulateFault()
        {
            // Simulate sensor fault
            int faultType = Random.Next(2);

            switch (faultType)
            {
                case 0: // Sensor drift
                    Pressure *= 0.8; // Reads 80% of actual pressure
                    AddAlarm("SENSOR_DRIFT", "Pressure sensor reading abnormally low", AlarmSeverity.Warning);
                    break;
                case 1: // Erratic readings
                    Pressure = Random.NextDouble() * 100 + 20;
                    AddAlarm("ERRATIC_READINGS", "Pressure sensor showing erratic values", AlarmSeverity.Major);
                    break;
            }

            DiagnosticData["Pressure"] = Pressure;
        }
    }
}
