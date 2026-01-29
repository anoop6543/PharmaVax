public class VibrationAnalyzer
{
    private List<double> _vibrationHistory = new List<double>();
    private Dictionary<string, double[]> _faultSignatures = new Dictionary<string, double[]>();
    
    public void AddVibrationSample(double vibrationLevel)
    {
        _vibrationHistory.Add(vibrationLevel);
        if (_vibrationHistory.Count > 1000) // Keep reasonable history size
            _vibrationHistory.RemoveAt(0);
    }
    
    public Dictionary<string, double> AnalyzeVibrationPattern()
    {
        // Perform Fast Fourier Transform on vibration data to detect frequency components
        double[] spectrum = CalculateFFT(_vibrationHistory.ToArray());
        
        // Compare against known fault signatures
        Dictionary<string, double> faultProbabilities = new Dictionary<string, double>();
        foreach (var signature in _faultSignatures)
        {
            double similarity = CalculateCosineSimilarity(spectrum, signature.Value);
            faultProbabilities.Add(signature.Key, similarity);
        }
        
        return faultProbabilities;
    }
    
    /// <summary>
    /// Calculates the Fast Fourier Transform to convert time-domain signal to frequency-domain
    /// </summary>
    /// <param name="signal">The time-domain vibration signal</param>
    /// <returns>Frequency spectrum magnitude values</returns>
    private double[] CalculateFFT(double[] signal)
    {
        // Check if we need to pad the signal to a power of 2 (FFT requirement)
        int n = signal.Length;
        int powerOf2 = (int)Math.Pow(2, Math.Ceiling(Math.Log(n, 2)));
        
        if (n != powerOf2)
        {
            // Pad with zeros to make length a power of 2
            Array.Resize(ref signal, powerOf2);
        }
        
        // FFT requires complex numbers (real and imaginary parts)
        double[] real = (double[])signal.Clone();
        double[] imag = new double[real.Length];
        
        // Perform in-place FFT
        int numBits = (int)Math.Log(real.Length, 2);
        
        // Bit-reversal permutation
        for (int i = 0; i < real.Length; i++)
        {
            int j = ReverseBits(i, numBits);
            if (j > i)
            {
                // Swap values
                double tempReal = real[i];
                double tempImag = imag[i];
                real[i] = real[j];
                imag[i] = imag[j];
                real[j] = tempReal;
                imag[j] = tempImag;
            }
        }
        
        // Cooley-Tukey FFT algorithm
        for (int size = 2; size <= real.Length; size *= 2)
        {
            double angle = -2 * Math.PI / size;
            double wReal = Math.Cos(angle);
            double wImag = Math.Sin(angle);
            
            for (int i = 0; i < real.Length; i += size)
            {
                double tReal = 1.0;
                double tImag = 0.0;
                
                for (int k = 0; k < size / 2; k++)
                {
                    int evenIndex = i + k;
                    int oddIndex = i + k + size / 2;
                    
                    // Even part
                    double evenReal = real[evenIndex];
                    double evenImag = imag[evenIndex];
                    
                    // Odd part
                    double oddReal = real[oddIndex];
                    double oddImag = imag[oddIndex];
                    
                    // Twiddle factor * odd
                    double multReal = tReal * oddReal - tImag * oddImag;
                    double multImag = tReal * oddImag + tImag * oddReal;
                    
                    // Butterfly operation
                    real[oddIndex] = evenReal - multReal;
                    imag[oddIndex] = evenImag - multImag;
                    real[evenIndex] = evenReal + multReal;
                    imag[evenIndex] = evenImag + multImag;
                    
                    // Update twiddle factor
                    double nextTReal = tReal * wReal - tImag * wImag;
                    double nextTImag = tReal * wImag + tImag * wReal;
                    tReal = nextTReal;
                    tImag = nextTImag;
                }
            }
        }
        
        // Calculate magnitude spectrum (sqrt(real² + imag²))
        double[] magnitude = new double[real.Length / 2]; // Only need first half due to symmetry
        for (int i = 0; i < magnitude.Length; i++)
        {
            magnitude[i] = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / real.Length;
        }
        
        return magnitude;
    }
    
    /// <summary>
    /// Helper method for FFT: Reverses the bits of a binary number
    /// </summary>
    private int ReverseBits(int n, int numBits)
    {
        int result = 0;
        for (int i = 0; i < numBits; i++)
        {
            result = (result << 1) | (n & 1);
            n >>= 1;
        }
        return result;
    }
    
    /// <summary>
    /// Calculates the cosine similarity between two vectors
    /// </summary>
    /// <param name="a">First vector (typically the measured spectrum)</param>
    /// <param name="b">Second vector (typically the fault signature)</param>
    /// <returns>Similarity value between -1.0 and 1.0</returns>
    private double CalculateCosineSimilarity(double[] a, double[] b)
    {
        // Ensure vectors are the same length
        if (a.Length != b.Length)
        {
            // We need vectors of the same length - resize the smaller one
            if (a.Length > b.Length)
            {
                Array.Resize(ref b, a.Length);
            }
            else
            {
                Array.Resize(ref a, b.Length);
            }
        }
        
        // Calculate dot product
        double dotProduct = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
        }
        
        // Calculate magnitudes
        double magnitudeA = 0.0;
        double magnitudeB = 0.0;
        
        for (int i = 0; i < a.Length; i++)
        {
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }
        
        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);
        
        // Avoid division by zero
        if (magnitudeA == 0.0 || magnitudeB == 0.0)
            return 0.0;
            
        // Calculate cosine similarity
        return dotProduct / (magnitudeA * magnitudeB);
    }

    public double EstimateInsulationLifeReduction(double operatingTemperature, TimeSpan duration)
    {
        // Arrhenius equation for thermal aging
        // Every 10°C increase above rated temperature cuts insulation life in half
        double baseTemperature = 85.0; // Class B insulation reference
        double accelerationFactor = Math.Pow(2, (operatingTemperature - baseTemperature) / 10.0);

        // Calculate equivalent aging hours
        return duration.TotalHours * accelerationFactor;
    }
}