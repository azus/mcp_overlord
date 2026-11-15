namespace game_automation_in_csharp.util
{
    public class AtomicBool
    {
        private bool _value;
        private readonly object _lock = new object();

        public AtomicBool(bool initialValue = false)
        {
            _value = initialValue;
        }

        public bool Value
        {
            get
            {
                lock (_lock)
                {
                    return _value;
                }
            }
            set
            {
                lock (_lock)
                {
                    _value = value;
                }
            }
        }
    }
}
