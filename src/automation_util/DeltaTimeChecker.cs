using System;

namespace game_automation
{
    public class DeltaTimeChecker
    {
        private readonly TimeSpan delta;
        private DateTime lastTrueTime;

        public DeltaTimeChecker(TimeSpan delta)
        {
            this.delta = delta;
            // Initialize the last time to the current time minus 5 minutes to return true on the first check
            lastTrueTime = DateTime.Now;
        }

        public bool CheckTime()
        {
            // Check if the current time is at least 5 minutes greater than the last time it returned true
            if ((DateTime.Now - lastTrueTime) >= delta)
            {
                // Update the last time to the current time
                lastTrueTime = DateTime.Now;
                return true;
            }
            return false;
        }
    }
}
