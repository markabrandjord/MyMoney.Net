using NUnit.Framework;
using Walkabout.StockQuotes;

namespace Walkabout.Tests
{
    [TestFixture]
    public class StockQuoteThrottleTests
    {
        [Test]
        public void GetSleep_UnderAllLimits_ReturnsZero()
        {
            var throttle = new StockQuoteThrottle
            {
                Settings = new OnlineServiceSettings
                {
                    ApiRequestsPerMonthLimit = 1000,
                    ApiRequestsPerDayLimit = 100,
                    ApiRequestsPerMinuteLimit = 10
                }
            };
            Assert.That(throttle.GetSleep(), Is.EqualTo(0));
        }

        [Test]
        public void GetSleep_MonthlyLimitExceeded_ThrowsWithMonthlyLimitReached()
        {
            var throttle = new StockQuoteThrottle
            {
                Settings = new OnlineServiceSettings { ApiRequestsPerMonthLimit = 1 }
            };
            throttle.RecordCall();
            var ex = Assert.Throws<StockQuoteThrottledException>(() => throttle.GetSleep());
            Assert.That(ex.MonthlyLimitReached, Is.True);
        }
    }
}
