using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.DesktopAgent;

public sealed class DesktopUiAutomationService(StorageLayout layout)
{
    public ResultEnvelope<DesktopSnapshot> GetDesktopSnapshot() =>
        ResultEnvelope<DesktopSnapshot>.Ok(CaptureSnapshot(includeControls: true), "Desktop snapshot.", RiskLevel.ReadOnly);

    public ResultEnvelope<object> ListWindows() =>
        ResultEnvelope<object>.Ok(CaptureSnapshot(includeControls: false).Windows, "Window list.", RiskLevel.ReadOnly);

    public ResultEnvelope<object> GetForegroundWindow()
    {
        var foreground = CaptureSnapshot(includeControls: true).ForegroundWindow;
        return ResultEnvelope<object>.Ok(new { foreground_window = foreground }, "Foreground window.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> FocusWindow(UiSelector selector)
    {
        var window = FindWindow(selector);
        if (window is null)
        {
            return ResultEnvelope<object>.Fail("window_not_found", "No matching window was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        SetForegroundWindow(new IntPtr(window.Current.NativeWindowHandle));
        return ResultEnvelope<object>.Ok(new { hwnd = window.Current.NativeWindowHandle, title = window.Current.Name }, "Window focused.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> ScreenshotDesktop()
    {
        Directory.CreateDirectory(layout.Screenshots);
        var bounds = Screen.AllScreens.Aggregate(Rectangle.Empty, (current, screen) => Rectangle.Union(current, screen.Bounds));
        var path = Path.Combine(layout.Screenshots, $"desktop-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.png");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
        return ResultEnvelope<object>.Ok(new { path, width = bounds.Width, height = bounds.Height }, "Desktop screenshot saved.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> ClipboardSet(string text)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text));
        return ResultEnvelope<object>.Ok(new { bytes = text.Length }, "Clipboard text set.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> ClipboardGetRedacted()
    {
        var text = System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty);
        if (LooksSecret(text))
        {
            return ResultEnvelope<object>.Ok(new { text = "[REDACTED]", redacted = true }, "Clipboard contained sensitive-looking text and was redacted.", RiskLevel.CredentialSensitive);
        }

        return ResultEnvelope<object>.Ok(new { text, redacted = false }, "Clipboard text read.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> TypeText(string text)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text));
        SendKeys.SendWait("^v");
        return ResultEnvelope<object>.Ok(new { characters = text.Length }, "Text pasted into focused control.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> SendHotkey(string hotkey)
    {
        SendKeys.SendWait(ToSendKeys(hotkey));
        return ResultEnvelope<object>.Ok(new { hotkey }, "Hotkey sent.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> Click(UiSelector selector, bool doubleClick = false, bool rightClick = false)
    {
        var element = FindElement(selector);
        if (element is null)
        {
            return ResultEnvelope<object>.Fail("control_not_found", "No matching control/window was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        var point = element.GetClickablePoint();
        SetCursorPos((int)point.X, (int)point.Y);
        if (rightClick)
        {
            MouseEvent(MouseEventFlags.RightDown | MouseEventFlags.RightUp);
        }
        else
        {
            MouseEvent(MouseEventFlags.LeftDown | MouseEventFlags.LeftUp);
            if (doubleClick)
            {
                MouseEvent(MouseEventFlags.LeftDown | MouseEventFlags.LeftUp);
            }
        }

        return ResultEnvelope<object>.Ok(new { x = point.X, y = point.Y, double_click = doubleClick, right_click = rightClick }, "Click sent.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> InvokeControl(UiSelector selector)
    {
        var element = FindElement(selector);
        if (element is null)
        {
            return ResultEnvelope<object>.Fail("control_not_found", "No matching control was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) && pattern is InvokePattern invoke)
        {
            invoke.Invoke();
            return ResultEnvelope<object>.Ok(new { selector }, "Control invoked.", RiskLevel.Medium);
        }

        return Click(selector);
    }

    public ResultEnvelope<object> SetControlValue(UiSelector selector, string value)
    {
        var element = FindElement(selector);
        if (element is null)
        {
            return ResultEnvelope<object>.Fail("control_not_found", "No matching control was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        if (IsPassword(element))
        {
            return ResultEnvelope<object>.Fail("sensitive_field", "Refusing to set a password/sensitive field. Ask the user to complete credential entry.", OperationStatus.WaitingForUser, RiskLevel.CredentialSensitive);
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
        {
            valuePattern.SetValue(value);
            return ResultEnvelope<object>.Ok(new { selector, characters = value.Length }, "Control value set.", RiskLevel.Medium);
        }

        FocusWindow(selector);
        return TypeText(value);
    }

    public ResultEnvelope<UiPlanResult> ExecutePlan(UiPlanRequest request, CancellationToken cancellationToken)
    {
        var planId = Guid.NewGuid().ToString("n");
        var results = new List<UiPlanStepResult>();
        for (var i = 0; i < request.Actions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = request.Actions[i];
            if (action.WaitBeforeMs > 0)
            {
                Thread.Sleep(action.WaitBeforeMs);
            }

            var result = ExecuteAction(action);
            var snapshot = CaptureSnapshot(includeControls: false);
            results.Add(new UiPlanStepResult(i, action.ActionType, result.Success, result.Message ?? string.Empty, snapshot));
            if (!result.Success && !string.Equals(action.FailureBehavior, "continue", StringComparison.OrdinalIgnoreCase))
            {
                return ResultEnvelope<UiPlanResult>.Ok(new UiPlanResult(planId, OperationStatus.Failed, result.Message ?? "UI plan stopped.", results, snapshot), "UI plan stopped.", RiskLevel.Medium);
            }

            if (action.WaitAfterMs > 0)
            {
                Thread.Sleep(action.WaitAfterMs);
            }
        }

        return ResultEnvelope<UiPlanResult>.Ok(new UiPlanResult(planId, OperationStatus.Success, "UI plan completed.", results, CaptureSnapshot(includeControls: true)), "UI plan completed.", RiskLevel.Medium);
    }

    private ResultEnvelope<object> ExecuteAction(UiPlanAction action) =>
        action.ActionType switch
        {
            UiActionType.FocusWindow => FocusWindow(action.Selector ?? EmptySelector()),
            UiActionType.Click => Click(action.Selector ?? EmptySelector()),
            UiActionType.DoubleClick => Click(action.Selector ?? EmptySelector(), doubleClick: true),
            UiActionType.RightClick => Click(action.Selector ?? EmptySelector(), rightClick: true),
            UiActionType.InvokeControl => InvokeControl(action.Selector ?? EmptySelector()),
            UiActionType.SetControlValue => SetControlValue(action.Selector ?? EmptySelector(), action.Text ?? string.Empty),
            UiActionType.TypeText => TypeText(action.Text ?? string.Empty),
            UiActionType.SendHotkey => SendHotkey(action.Hotkey ?? string.Empty),
            UiActionType.Screenshot => ScreenshotDesktop(),
            _ => ResultEnvelope<object>.Fail("ui_action_not_implemented", $"Action {action.ActionType} is scaffolded.", OperationStatus.NotImplemented, RiskLevel.Medium)
        };

    private DesktopSnapshot CaptureSnapshot(bool includeControls)
    {
        var foreground = GetForegroundWindowNative();
        var windows = AutomationElement.RootElement
            .FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Select(e => BuildWindowNode(e, includeControls))
            .Where(w => !string.IsNullOrWhiteSpace(w.Title))
            .ToArray();

        var foregroundWindow = windows.FirstOrDefault(w => w.Hwnd == foreground.ToInt64());
        var monitors = Screen.AllScreens.Select(s => new MonitorInfo(s.DeviceName, s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, s.Primary)).ToArray();
        return new DesktopSnapshot(Guid.NewGuid().ToString("n"), DateTimeOffset.UtcNow, Environment.UserName, monitors, foregroundWindow, windows);
    }

    private UiWindowNode BuildWindowNode(AutomationElement element, bool includeControls)
    {
        var rect = element.Current.BoundingRectangle;
        return new UiWindowNode(
            RuntimeId(element),
            element.Current.NativeWindowHandle,
            element.Current.Name ?? string.Empty,
            element.Current.ClassName ?? string.Empty,
            TryGetProcessName(element.Current.ProcessId),
            element.Current.ProcessId,
            (int)rect.X,
            (int)rect.Y,
            (int)Math.Max(0, rect.Width),
            (int)Math.Max(0, rect.Height),
            !element.Current.IsOffscreen,
            element.Current.IsEnabled,
            element.Current.HasKeyboardFocus,
            false,
            false,
            includeControls ? BuildControlNode(element, depth: 0) : null);
    }

    private UiControlNode BuildControlNode(AutomationElement element, int depth)
    {
        var rect = element.Current.BoundingRectangle;
        var sensitive = IsPassword(element);
        IReadOnlyList<UiControlNode> children = depth >= 4
            ? []
            : element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition).Cast<AutomationElement>().Take(100).Select(e => BuildControlNode(e, depth + 1)).ToArray();

        return new UiControlNode(
            RuntimeId(element),
            element.Current.AutomationId,
            element.Current.Name,
            element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal),
            element.Current.ClassName,
            element.Current.FrameworkId,
            (int)rect.X,
            (int)rect.Y,
            (int)Math.Max(0, rect.Width),
            (int)Math.Max(0, rect.Height),
            element.Current.IsEnabled,
            element.Current.IsOffscreen,
            element.Current.IsKeyboardFocusable,
            element.Current.HasKeyboardFocus,
            SupportedPatterns(element),
            sensitive ? null : TryReadValue(element),
            sensitive,
            1.0,
            SuggestedActions(element),
            children);
    }

    private AutomationElement? FindWindow(UiSelector selector) =>
        AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition)
            .Cast<AutomationElement>()
            .FirstOrDefault(e => Matches(e, selector, windowOnly: true));

    private AutomationElement? FindElement(UiSelector selector)
    {
        if (selector.Hwnd is { } hwnd)
        {
            return AutomationElement.FromHandle(new IntPtr(hwnd));
        }

        var window = FindWindow(selector);
        if (window is not null && selector.AutomationId is null && selector.Name is null && selector.ControlType is null)
        {
            return window;
        }

        var root = window ?? AutomationElement.RootElement;
        return root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition)
            .Cast<AutomationElement>()
            .FirstOrDefault(e => Matches(e, selector, windowOnly: false));
    }

    private static bool Matches(AutomationElement element, UiSelector selector, bool windowOnly)
    {
        if (selector.Hwnd is { } hwnd && element.Current.NativeWindowHandle != hwnd) return false;
        if (selector.ProcessId is { } pid && element.Current.ProcessId != pid) return false;
        if (!string.IsNullOrWhiteSpace(selector.WindowTitle) && windowOnly && !element.Current.Name.Contains(selector.WindowTitle, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(selector.WindowClass) && !string.Equals(element.Current.ClassName, selector.WindowClass, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(selector.AutomationId) && !string.Equals(element.Current.AutomationId, selector.AutomationId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(selector.Name) && !element.Current.Name.Contains(selector.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(selector.ControlType) && !element.Current.ControlType.ProgrammaticName.Contains(selector.ControlType, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static UiSelector EmptySelector() => new(null, null, null, null, null, null, null);

    private static string RuntimeId(AutomationElement element)
    {
        try { return string.Join(".", element.GetRuntimeId()); }
        catch { return element.Current.NativeWindowHandle.ToString(); }
    }

    private static IReadOnlyList<string> SupportedPatterns(AutomationElement element)
    {
        var patterns = new List<string>();
        foreach (var (pattern, name) in new[]
        {
            (InvokePattern.Pattern, "invoke"),
            (ValuePattern.Pattern, "value"),
            (SelectionItemPattern.Pattern, "selection_item"),
            (ExpandCollapsePattern.Pattern, "expand_collapse"),
            (ScrollItemPattern.Pattern, "scroll_item"),
            (TextPattern.Pattern, "text")
        })
        {
            if (element.TryGetCurrentPattern(pattern, out _))
            {
                patterns.Add(name);
            }
        }

        return patterns;
    }

    private static IReadOnlyList<string> SuggestedActions(AutomationElement element)
    {
        var actions = new List<string>();
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out _)) actions.Add("invoke");
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _) && !IsPassword(element)) actions.Add("set_value");
        if (element.Current.IsKeyboardFocusable) actions.Add("focus");
        return actions;
    }

    private static string? TryReadValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern
                ? valuePattern.Current.Value
                : null;
        }
        catch { return null; }
    }

    private static bool IsPassword(AutomationElement element)
    {
        try { return (bool)element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, ignoreDefaultValue: true); }
        catch { return false; }
    }

    private static bool LooksSecret(string text) =>
        text.Length > 24 && (text.Contains("token", StringComparison.OrdinalIgnoreCase) || text.Contains("password", StringComparison.OrdinalIgnoreCase) || text.Count(c => c == '.') >= 2);

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch { return null; }
    }

    private static string ToSendKeys(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var key = parts.LastOrDefault() ?? hotkey;
        var prefix = string.Concat(parts.SkipLast(1).Select(p => p.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => "^",
            "ALT" => "%",
            "SHIFT" => "+",
            _ => string.Empty
        }));
        return prefix + key.ToLowerInvariant();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindowNative();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(MouseEventFlags dwFlags, int dx = 0, int dy = 0, int dwData = 0, UIntPtr dwExtraInfo = default);

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010
    }
}
