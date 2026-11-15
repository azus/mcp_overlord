using game_automation;
using mcp_overlord.Configuration;

namespace mcp_overlord.Services
{
    /// <summary>Result of a mouse tool.</summary>
    public sealed class MouseActionResult
    {
        /// <summary>Tool error, or null.</summary>
        public string? Error { get; init; }

        /// <summary>Tool name.</summary>
        public string Action { get; init; } = "";

        /// <summary>Button for click, down, and up.</summary>
        public string? Button { get; init; }

        /// <summary>Window X in <c>GetWindowRect</c> pixels.</summary>
        public int WindowX { get; init; }

        /// <summary>Window Y in <c>GetWindowRect</c> pixels.</summary>
        public int WindowY { get; init; }

        /// <summary>Hold that was applied, when the tool uses one.</summary>
        public int? AppliedHoldMilliseconds { get; init; }

        /// <summary>True when holdMs was clamped.</summary>
        public bool HoldWasClamped { get; init; }

        /// <summary>Wheel notches that were applied.</summary>
        public int? AppliedDelta { get; init; }

        /// <summary>True when delta was clamped.</summary>
        public bool DeltaWasClamped { get; init; }
    }

    /// <summary>Result of a keyboard tool.</summary>
    public sealed class KeyActionResult
    {
        /// <summary>Tool error, or null.</summary>
        public string? Error { get; init; }

        /// <summary>Tool name.</summary>
        public string Action { get; init; } = "";

        /// <summary>Key name, when the tool uses one.</summary>
        public string? Key { get; init; }

        /// <summary>Hold that was applied.</summary>
        public int? AppliedHoldMilliseconds { get; init; }

        /// <summary>True when holdMs was clamped.</summary>
        public bool HoldWasClamped { get; init; }

        /// <summary>Number of characters typed.</summary>
        public int? Length { get; init; }
    }

    /// <summary>Result of <c>focus_window</c>.</summary>
    public sealed class FocusResult
    {
        /// <summary>Tool error, or null.</summary>
        public string? Error { get; init; }

        /// <summary>True when the window was brought to the foreground.</summary>
        public bool Focused { get; init; }

        /// <summary>Set when focus failed. Input may still land at the window's screen position.</summary>
        public string? Message { get; init; }
    }

    /// <summary>
    /// Focuses a window and sends mouse and keyboard gestures through the per-window gate.
    /// </summary>
    public sealed class InputService
    {
        /// <summary>Default time a click or key stays down.</summary>
        public const int DefaultHoldMilliseconds = 50;

        /// <summary>Maximum holdMs.</summary>
        public const int MaxHoldMilliseconds = 2000;

        /// <summary>Maximum absolute wheel notches.</summary>
        public const int MaxScrollNotches = 30;

        /// <summary>Maximum type_text length.</summary>
        public const int MaxTextLength = 500;

        private readonly WindowLocator _locator;
        private readonly ClickBackends _backends;
        private readonly IKeyboardSender _keyboard;
        private readonly FocusHelper _focus;
        private readonly IntegrityChecker _integrity;
        private readonly InputGate _gate;
        private readonly ScreenshotScaleStore _scales;
        private readonly IWorkstationLock _workstationLock;
        private readonly object _heldGate = new object();
        private readonly List<HeldButton> _heldButtons = new List<HeldButton>();

        /// <summary>
        /// Creates the service.
        /// </summary>
        public InputService(
            WindowLocator locator,
            ClickBackends backends,
            IKeyboardSender keyboard,
            FocusHelper focus,
            IntegrityChecker integrity,
            InputGate gate,
            ScreenshotScaleStore scales,
            IWorkstationLock workstationLock)
        {
            _locator = locator;
            _backends = backends;
            _keyboard = keyboard;
            _focus = focus;
            _integrity = integrity;
            _gate = gate;
            _scales = scales;
            _workstationLock = workstationLock;
        }

        /// <summary>Moves the cursor. No button change.</summary>
        public MouseActionResult MouseMove(ApplicationEntry application, double x, double y, string? coordinateSpace)
        {
            return Mouse(application, "mouse_move", x, y, coordinateSpace, null, null, false, null, (helper, hwnd, point, hold) =>
            {
                helper.MoveTo(hwnd, point.ClientX, point.ClientY);
            });
        }

        /// <summary>Moves, presses, waits, and releases. <paramref name="doubleClick"/> repeats the click once.</summary>
        public MouseActionResult MouseClick(
            ApplicationEntry application,
            double x,
            double y,
            string? button,
            bool doubleClick,
            int? holdMs,
            string? coordinateSpace)
        {
            return Mouse(application, "mouse_click", x, y, coordinateSpace, button, holdMs, false, null, (helper, hwnd, point, hold) =>
            {
                MouseButton mouseButton = ParseButton(button, out _);
                helper.Click(hwnd, point.ClientX, point.ClientY, mouseButton, hold, doubleClick);
            });
        }

        /// <summary>Moves and presses a button. The button stays down until mouse_up or a later click.</summary>
        public MouseActionResult MouseDown(ApplicationEntry application, double x, double y, string? button, string? coordinateSpace)
        {
            return Mouse(application, "mouse_down", x, y, coordinateSpace, button, null, true, null, (helper, hwnd, point, hold) =>
            {
                MouseButton mouseButton = ParseButton(button, out _);
                helper.ButtonDown(hwnd, point.ClientX, point.ClientY, mouseButton);
                RememberHeld(hwnd, mouseButton, point.ClientX, point.ClientY, helper);
            });
        }

        /// <summary>Moves and releases a button.</summary>
        public MouseActionResult MouseUp(ApplicationEntry application, double x, double y, string? button, string? coordinateSpace)
        {
            return Mouse(application, "mouse_up", x, y, coordinateSpace, button, null, true, null, (helper, hwnd, point, hold) =>
            {
                MouseButton mouseButton = ParseButton(button, out _);
                helper.ButtonUp(hwnd, point.ClientX, point.ClientY, mouseButton);
                ForgetHeld(hwnd, mouseButton);
            });
        }

        /// <summary>Moves and scrolls. Positive delta scrolls up. The range is clamped to -30…30.</summary>
        public MouseActionResult MouseScroll(ApplicationEntry application, double x, double y, int delta, string? coordinateSpace)
        {
            int applied = Math.Clamp(delta, -MaxScrollNotches, MaxScrollNotches);
            bool clampedDelta = applied != delta;
            return Mouse(application, "mouse_scroll", x, y, coordinateSpace, null, null, false, (applied, clampedDelta), (helper, hwnd, point, hold) =>
            {
                helper.Scroll(hwnd, point.ClientX, point.ClientY, applied);
            });
        }

        /// <summary>Focuses, presses a key, waits, and releases it.</summary>
        public KeyActionResult KeyPress(ApplicationEntry application, string key, int? holdMs)
        {
            return Key(application, "key_press", key, holdMs, (hwnd, virtualKey, hold) =>
            {
                _keyboard.Press(hwnd, virtualKey, hold);
            });
        }

        /// <summary>Focuses and sends a key down.</summary>
        public KeyActionResult KeyDown(ApplicationEntry application, string key)
        {
            return Key(application, "key_down", key, null, (hwnd, virtualKey, hold) =>
            {
                _keyboard.KeyDown(hwnd, virtualKey);
            });
        }

        /// <summary>Focuses and sends a key up.</summary>
        public KeyActionResult KeyUp(ApplicationEntry application, string key)
        {
            return Key(application, "key_up", key, null, (hwnd, virtualKey, hold) =>
            {
                _keyboard.KeyUp(hwnd, virtualKey);
            });
        }

        /// <summary>Focuses and types text. Length must be 1–500.</summary>
        public KeyActionResult TypeText(ApplicationEntry application, string? text)
        {
            if (_workstationLock.IsLocked())
            {
                return new KeyActionResult
                {
                    Error = ToolErrors.ScreenLocked,
                    Action = "type_text"
                };
            }

            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            int length = text?.Length ?? 0;
            if (text == null || length < 1 || length > MaxTextLength)
            {
                return new KeyActionResult
                {
                    Error = ToolErrors.TextLength(length),
                    Action = "type_text"
                };
            }

            if (!TryPrepare(application, out WindowLookup? window, out string? error))
            {
                return new KeyActionResult { Error = error, Action = "type_text" };
            }

            using (_gate.Acquire(window!.Hwnd))
            {
                _focus.TryFocus(window.Hwnd);
                _keyboard.TypeUnicode(window.Hwnd, text);
            }

            return new KeyActionResult
            {
                Action = "type_text",
                Length = length
            };
        }

        /// <summary>Restores the window if it is minimized, then tries to focus it.</summary>
        public FocusResult Focus(ApplicationEntry application)
        {
            if (!TryPrepare(application, out WindowLookup? window, out string? error))
            {
                return new FocusResult { Error = error };
            }

            bool focused;
            using (_gate.Acquire(window!.Hwnd))
            {
                focused = _focus.TryFocus(window.Hwnd);
            }

            return new FocusResult
            {
                Focused = focused,
                Message = focused
                    ? null
                    : "The window could not be brought to the foreground. Input may still be delivered at the window's screen position."
            };
        }

        /// <summary>
        /// Releases every button this service still considers down. Called while the process is stopping,
        /// and before <c>lock</c> while the desktop is still interactive.
        /// Does nothing when the workstation is locked, so a button up is not sent to the lock screen.
        /// </summary>
        public void ReleaseHeldButtons()
        {
            if (_workstationLock.IsLocked())
            {
                return;
            }

            HeldButton[] held;
            lock (_heldGate)
            {
                held = _heldButtons.ToArray();
                _heldButtons.Clear();
            }

            foreach (HeldButton button in held)
            {
                using (_gate.Acquire(button.Hwnd))
                {
                    button.Helper.ButtonUp(button.Hwnd, button.ClientX, button.ClientY, button.Button);
                }
            }
        }

        /// <summary>Clamps holdMs to 0–2000. A missing value becomes 50.</summary>
        public static int ClampHold(int? holdMs, out bool wasClamped)
        {
            int value = holdMs ?? DefaultHoldMilliseconds;
            int clamped = Math.Clamp(value, 0, MaxHoldMilliseconds);
            wasClamped = clamped != value;
            return clamped;
        }

        private MouseActionResult Mouse(
            ApplicationEntry application,
            string action,
            double x,
            double y,
            string? coordinateSpace,
            string? button,
            int? holdMs,
            bool requiresButton,
            (int Applied, bool Clamped)? delta,
            Action<ClickHelper, IntPtr, MappedPoint, int> send)
        {
            if (_workstationLock.IsLocked())
            {
                return new MouseActionResult
                {
                    Error = ToolErrors.ScreenLocked,
                    Action = action
                };
            }

            if (!string.IsNullOrEmpty(button) && !TryParseButton(button, out _))
            {
                return new MouseActionResult { Error = ToolErrors.UnknownButton(button), Action = action };
            }

            if (!TryParseSpace(coordinateSpace, out string space))
            {
                return new MouseActionResult { Error = ToolErrors.UnknownCoordinateSpace(coordinateSpace ?? ""), Action = action };
            }

            if (!TryPrepare(application, out WindowLookup? window, out string? error))
            {
                return new MouseActionResult { Error = error, Action = action };
            }

            bool hasFrame = _scales.TryGet(application.Name, out ScreenshotFrame frame);
            if (!CoordinateMapper.TryMap(
                x,
                y,
                space,
                hasFrame ? frame : null,
                window!.Frame,
                out MappedPoint point))
            {
                return new MouseActionResult
                {
                    Error = ToolErrors.PointOutsideWindow(
                        point.WindowX,
                        point.WindowY,
                        window.Frame.FrameWidth,
                        window.Frame.FrameHeight),
                    Action = action
                };
            }

            int hold = ClampHold(holdMs, out bool holdClamped);
            ClickHelper helper = _backends.Resolve(application.ClickBackend);

            using (_gate.Acquire(window.Hwnd))
            {
                _focus.TryFocus(window.Hwnd);
                send(helper, window.Hwnd, point, hold);
            }

            return new MouseActionResult
            {
                Action = action,
                Button = requiresButton || action == "mouse_click" ? NormalizeButton(button) : null,
                WindowX = point.WindowX,
                WindowY = point.WindowY,
                AppliedHoldMilliseconds = holdMs.HasValue || action == "mouse_click" ? hold : null,
                HoldWasClamped = holdClamped,
                AppliedDelta = delta?.Applied,
                DeltaWasClamped = delta?.Clamped ?? false
            };
        }

        private KeyActionResult Key(
            ApplicationEntry application,
            string action,
            string key,
            int? holdMs,
            Action<IntPtr, byte, int> send)
        {
            if (_workstationLock.IsLocked())
            {
                return new KeyActionResult
                {
                    Error = ToolErrors.ScreenLocked,
                    Action = action,
                    Key = key
                };
            }

            if (!KeyNameMap.TryGetVirtualKey(key, out byte virtualKey))
            {
                return new KeyActionResult
                {
                    Error = ToolErrors.UnknownKey(key ?? ""),
                    Action = action
                };
            }

            if (!TryPrepare(application, out WindowLookup? window, out string? error))
            {
                return new KeyActionResult { Error = error, Action = action, Key = key };
            }

            int hold = ClampHold(holdMs, out bool holdClamped);
            using (_gate.Acquire(window!.Hwnd))
            {
                _focus.TryFocus(window.Hwnd);
                send(window.Hwnd, virtualKey, hold);
            }

            return new KeyActionResult
            {
                Action = action,
                Key = key,
                AppliedHoldMilliseconds = action == "key_press" ? hold : null,
                HoldWasClamped = holdClamped
            };
        }

        private bool TryPrepare(ApplicationEntry application, out WindowLookup? window, out string? error)
        {
            window = _locator.Find(application, true, true);
            if (!window.Found)
            {
                error = ToolErrors.WindowNotAvailable(application.Name, application.IsLaunchable);
                window = null;
                return false;
            }

            if (_integrity.TargetHasHigherIntegrity(window.ProcessId))
            {
                error = ToolErrors.HigherIntegrity();
                window = null;
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryParseSpace(string? coordinateSpace, out string space)
        {
            if (string.IsNullOrWhiteSpace(coordinateSpace) || string.Equals(coordinateSpace, "image", StringComparison.OrdinalIgnoreCase))
            {
                space = "image";
                return true;
            }

            if (string.Equals(coordinateSpace, "window_rect", StringComparison.OrdinalIgnoreCase))
            {
                space = "window_rect";
                return true;
            }

            space = "";
            return false;
        }

        private static bool TryParseButton(string? button, out MouseButton mouseButton)
        {
            mouseButton = MouseButton.Left;
            if (string.IsNullOrWhiteSpace(button) || string.Equals(button, "left", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(button, "right", StringComparison.OrdinalIgnoreCase))
            {
                mouseButton = MouseButton.Right;
                return true;
            }

            if (string.Equals(button, "middle", StringComparison.OrdinalIgnoreCase))
            {
                mouseButton = MouseButton.Middle;
                return true;
            }

            return false;
        }

        private static MouseButton ParseButton(string? button, out bool recognized)
        {
            recognized = TryParseButton(button, out MouseButton mouseButton);
            return mouseButton;
        }

        private static string NormalizeButton(string? button)
        {
            if (string.Equals(button, "right", StringComparison.OrdinalIgnoreCase))
            {
                return "right";
            }

            if (string.Equals(button, "middle", StringComparison.OrdinalIgnoreCase))
            {
                return "middle";
            }

            return "left";
        }

        private void RememberHeld(IntPtr hwnd, MouseButton button, int clientX, int clientY, ClickHelper helper)
        {
            lock (_heldGate)
            {
                ForgetHeldCore(hwnd, button);
                _heldButtons.Add(new HeldButton(hwnd, button, clientX, clientY, helper));
            }
        }

        private void ForgetHeld(IntPtr hwnd, MouseButton button)
        {
            lock (_heldGate)
            {
                ForgetHeldCore(hwnd, button);
            }
        }

        private void ForgetHeldCore(IntPtr hwnd, MouseButton button)
        {
            _heldButtons.RemoveAll(held => held.Hwnd == hwnd && held.Button == button);
        }

        private sealed class HeldButton
        {
            public HeldButton(IntPtr hwnd, MouseButton button, int clientX, int clientY, ClickHelper helper)
            {
                Hwnd = hwnd;
                Button = button;
                ClientX = clientX;
                ClientY = clientY;
                Helper = helper;
            }

            public IntPtr Hwnd { get; }

            public MouseButton Button { get; }

            public int ClientX { get; }

            public int ClientY { get; }

            public ClickHelper Helper { get; }
        }
    }
}
