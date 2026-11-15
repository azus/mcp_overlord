using System.ComponentModel;
using mcp_overlord.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace mcp_overlord.Tools
{
    /// <summary>Workstation lock tools. There is no unlock tool.</summary>
    [McpServerToolType]
    public sealed class SessionTools
    {
        private readonly InputService _input;
        private readonly IWorkstationLock _workstationLock;

        /// <summary>
        /// Creates the tools.
        /// </summary>
        /// <param name="input">Releases buttons this server still considers down before the screen locks.</param>
        /// <param name="workstationLock">Reads and sets the workstation lock.</param>
        public SessionTools(InputService input, IWorkstationLock workstationLock)
        {
            _input = input;
            _workstationLock = workstationLock;
        }

        /// <summary>Reports whether the workstation screen is locked.</summary>
        [McpServerTool(Name = "is_locked", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("Report whether the workstation screen is locked. There is no unlock tool.")]
        public CallToolResult IsLocked()
        {
            return ToolResponses.Text(ToolJson.Serialize(new
            {
                locked = _workstationLock.IsLocked()
            }));
        }

        /// <summary>Locks the workstation screen.</summary>
        [McpServerTool(Name = "lock", OpenWorld = false),
         Description("Lock the workstation screen. Mouse and keyboard tools then fail until someone unlocks the screen at the console. This server cannot unlock the screen.")]
        public CallToolResult Lock()
        {
            _input.ReleaseHeldButtons();
            if (!_workstationLock.TryLock(out string? error))
            {
                return ToolResponses.Error(ToolErrors.LockFailed(error));
            }

            return ToolResponses.Text(ToolJson.Serialize(new
            {
                locked = true
            }));
        }
    }
}
