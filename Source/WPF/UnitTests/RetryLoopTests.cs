using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class RetryLoopTests
    {
        [Test]
        public void FirstAttemptSucceeds_ReturnsTrueWithoutPrompting()
        {
            bool prompted = false;
            bool result = RetryLoop.Run(
                tryAction: () => true,
                promptOnFailure: () => { prompted = true; return RetryLoop.PromptResult.Cancel; });

            Assert.That(result, Is.True);
            Assert.That(prompted, Is.False);
        }

        [Test]
        public void FailsThenRetrySucceeds_ReturnsTrue()
        {
            int attempts = 0;
            bool result = RetryLoop.Run(
                tryAction: () => { attempts++; return attempts >= 2; },
                promptOnFailure: () => RetryLoop.PromptResult.Retry);

            Assert.That(result, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void FailsThenCancel_ReturnsFalseAndStopsRetrying()
        {
            int attempts = 0;
            bool result = RetryLoop.Run(
                tryAction: () => { attempts++; return false; },
                promptOnFailure: () => RetryLoop.PromptResult.Cancel);

            Assert.That(result, Is.False);
            Assert.That(attempts, Is.EqualTo(1));
        }
    }
}
