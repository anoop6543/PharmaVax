public class RemainingUsefulLifeEstimator
{
    // Parameters based on motor type, load history, and environment
    private double _baselineLifeHours;
    private double _vibrationImpactFactor;
    private double _temperatureImpactFactor;
    private double _startStopImpactFactor;
    
    // Calculate remaining useful life in hours
    public double CalculateRUL(double runningHours, double avgVibration, 
                              double avgTemperature, int startCount)
    {
        // Exponential degradation model with multiple factors
        double vibrationDegradation = Math.Exp(avgVibration * _vibrationImpactFactor);
        double temperatureDegradation = Math.Exp((avgTemperature-60)/10 * _temperatureImpactFactor);
        double startStopDegradation = 1 + (startCount * _startStopImpactFactor);
        
        double usedLifePercentage = (runningHours / _baselineLifeHours) * 
                                   vibrationDegradation * 
                                   temperatureDegradation *
                                   startStopDegradation;
        
        return Math.Max(0, _baselineLifeHours * (1 - usedLifePercentage));
    }
}