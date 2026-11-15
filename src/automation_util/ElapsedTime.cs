using System;

namespace game_automation
{
    public class ElapsedTime
    {
        private readonly TimeSpan delta;
        private DateTime lastTrueTime;

        public ElapsedTime(TimeSpan delta)
        {
            this.delta = delta;
            lastTrueTime = DateTime.Now;
        }

        public void reset()
        {
            lastTrueTime = DateTime.Now;
        }

        public bool CheckTime()
        {
            return (DateTime.Now - lastTrueTime) >= delta;
        }
    }
}
