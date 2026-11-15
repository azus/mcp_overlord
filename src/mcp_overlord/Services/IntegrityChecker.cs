namespace mcp_overlord.Services
{
    /// <summary>
    /// Refuses input when the target process runs at a higher integrity level than this process.
    /// Neither mouse backend can cross that boundary, and this check does not try to.
    /// </summary>
    public sealed class IntegrityChecker
    {
        private readonly IIntegrityApi _api;

        /// <summary>
        /// Creates the checker.
        /// </summary>
        /// <param name="api">Integrity level reader.</param>
        public IntegrityChecker(IIntegrityApi api)
        {
            _api = api;
        }

        /// <summary>
        /// True when the target integrity RID is strictly higher than this process.
        /// An unreadable level does not block input.
        /// </summary>
        /// <param name="processId">Target process id.</param>
        /// <returns>True when input would be blocked by UIPI.</returns>
        public bool TargetHasHigherIntegrity(int processId)
        {
            int target = _api.GetIntegrityLevel(processId);
            int current = _api.GetCurrentIntegrityLevel();
            if (target < 0 || current < 0)
            {
                return false;
            }

            return target > current;
        }
    }
}
