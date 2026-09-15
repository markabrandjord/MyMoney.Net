using System;

namespace Walkabout.Data
{
    public static class RetryLoop
    {
        public enum PromptResult
        {
            Retry,
            Cancel
        }

        public static bool Run(Func<bool> tryAction, Func<PromptResult> promptOnFailure)
        {
            while (true)
            {
                if (tryAction())
                {
                    return true;
                }

                if (promptOnFailure() == PromptResult.Cancel)
                {
                    return false;
                }
            }
        }
    }
}
