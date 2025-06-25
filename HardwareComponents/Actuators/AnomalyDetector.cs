public class AnomalyDetector
{
    private Queue<double> _temperatureWindow = new Queue<double>();
    private Queue<double> _vibrationWindow = new Queue<double>();
    private int _windowSize = 100;
    
    public AnomalyStatus DetectAnomalies(double currentTemperature, double currentVibration)
    {
        // Add new values to window
        _temperatureWindow.Enqueue(currentTemperature);
        _vibrationWindow.Enqueue(currentVibration);
        
        // Keep window at fixed size
        if (_temperatureWindow.Count > _windowSize)
            _temperatureWindow.Dequeue();
        if (_vibrationWindow.Count > _windowSize)
            _vibrationWindow.Dequeue();
            
        // Calculate statistics
        double tempAvg = _temperatureWindow.Average();
        double tempStdDev = CalculateStdDev(_temperatureWindow, tempAvg);
        double vibAvg = _vibrationWindow.Average();
        double vibStdDev = CalculateStdDev(_vibrationWindow, vibAvg);
        
        // Check if current values are statistical anomalies (more than 3 sigma)
        bool tempAnomaly = Math.Abs(currentTemperature - tempAvg) > 3 * tempStdDev;
        bool vibAnomaly = Math.Abs(currentVibration - vibAvg) > 3 * vibStdDev;
        
        return new AnomalyStatus(tempAnomaly, vibAnomaly);
    }
    
    private double CalculateStdDev(IEnumerable<double> values, double mean)
    {
        double sum = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sum / values.Count());
    }
}