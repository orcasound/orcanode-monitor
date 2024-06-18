namespace OrcanodeMonitor.Core
{
    public class WelfordVariance
    {
        private double mean;
        private double M2;
        private int count;

        public void Add(double value)
        {
            count++;
            var delta = value - mean;
            mean += delta / count;
            var delta2 = value - mean;
            M2 += delta * delta2;
        }

        public double Mean => mean;

        public double Variance => count < 2 ? double.NaN : M2 / count;

        public double StandardDeviation => Math.Sqrt(Variance);
    }
}
