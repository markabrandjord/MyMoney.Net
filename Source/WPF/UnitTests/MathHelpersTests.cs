using NUnit.Framework;
using System.Windows;
using Walkabout.Utilities;

namespace Walkabout.Tests
{
    public class MathHelpersTests
    {
        [Test]
        public void LinearRegression_PerfectlyLinearSeries_ReturnsExactSlopeAndIntercept()
        {
            // x implied as 1..N; y = 1 + 2x for x=1..5 gives y = [3, 5, 7, 9, 11].
            MathHelpers.LinearRegression(new double[] { 3, 5, 7, 9, 11 }, out double a, out double b);

            Assert.That(a, Is.EqualTo(1.0).Within(0.0001));
            Assert.That(b, Is.EqualTo(2.0).Within(0.0001));
        }

        [Test]
        public void LinearRegression_NoisySeries_ReturnsHandComputedSlopeAndIntercept()
        {
            // x implied as 1..5, y = [2, 4, 5, 4, 5]. Hand-computed OLS: meanX=3, meanY=4,
            // covariance-sum=6, variance-sum(x)=10, b=6/10=0.6, a=4-0.6*3=2.2.
            MathHelpers.LinearRegression(new double[] { 2, 4, 5, 4, 5 }, out double a, out double b);

            Assert.That(a, Is.EqualTo(2.2).Within(0.0001));
            Assert.That(b, Is.EqualTo(0.6).Within(0.0001));
        }

        [Test]
        public void Covariance_PerfectlyLinearPoints_ReturnsHandComputedSum()
        {
            // (1,3), (2,5), (3,7): meanX=2, meanY=5. Covariance is a raw sum of products of
            // deviations (not divided by count, per this method's own implementation) =
            // (-1*-2) + (0*0) + (1*2) = 4.
            var points = new[] { new Point(1, 3), new Point(2, 5), new Point(3, 7) };

            Assert.That(MathHelpers.Covariance(points), Is.EqualTo(4.0).Within(0.0001));
        }

        [Test]
        public void LinearRegression_PointBasedOverload_MatchesHandComputedValues()
        {
            // Same (1,3), (2,5), (3,7) - perfectly linear y = 1 + 2x, so a=1, b=2.
            var points = new[] { new Point(1, 3), new Point(2, 5), new Point(3, 7) };

            MathHelpers.LinearRegression(points, out double a, out double b);

            Assert.That(a, Is.EqualTo(1.0).Within(0.0001));
            Assert.That(b, Is.EqualTo(2.0).Within(0.0001));
        }
    }
}
