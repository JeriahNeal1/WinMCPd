using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.DesktopAgent;

public sealed class DesktopUiAutomationService(StorageLayout layout)
{
    private static readonly string[] SafeDialogButtonNames = ["OK", "Continue", "Next", "Finish", "Save", "Open", "Cancel", "Yes", "No", "Retry"];

    private readonly ConcurrentDictionary<string, UiPlanResult> _plans = new();
    private readonly ConcurrentDictionary<string, string> _planLogs = new();

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

    public ResultEnvelope<object> MoveWindow(UiSelector selector, int x, int y)
    {
        var window = FindWindow(selector);
        if (window is null)
        {
            return ResultEnvelope<object>.Fail("window_not_found", "No matching window was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        var rect = window.Current.BoundingRectangle;
        MoveWindowNative(new IntPtr(window.Current.NativeWindowHandle), x, y, Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height), repaint: true);
        return ResultEnvelope<object>.Ok(new { hwnd = window.Current.NativeWindowHandle, x, y }, "Window moved.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> ResizeWindow(UiSelector selector, int width, int height)
    {
        var window = FindWindow(selector);
        if (window is null)
        {
            return ResultEnvelope<object>.Fail("window_not_found", "No matching window was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        var rect = window.Current.BoundingRectangle;
        MoveWindowNative(new IntPtr(window.Current.NativeWindowHandle), (int)rect.X, (int)rect.Y, Math.Max(1, width), Math.Max(1, height), repaint: true);
        return ResultEnvelope<object>.Ok(new { hwnd = window.Current.NativeWindowHandle, width, height }, "Window resized.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> MinimizeWindow(UiSelector selector) => ShowWindowBySelector(selector, ShowWindowCommand.Minimize, "Window minimized.");

    public ResultEnvelope<object> MaximizeWindow(UiSelector selector) => ShowWindowBySelector(selector, ShowWindowCommand.Maximize, "Window maximized.");

    public ResultEnvelope<object> CloseWindow(UiSelector selector)
    {
        var window = FindWindow(selector);
        if (window is null)
        {
            return ResultEnvelope<object>.Fail("window_not_found", "No matching window was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern) && pattern is WindowPattern windowPattern)
        {
            windowPattern.Close();
        }
        else
        {
            PostMessage(new IntPtr(window.Current.NativeWindowHandle), WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        return ResultEnvelope<object>.Ok(new { hwnd = window.Current.NativeWindowHandle }, "Window close requested.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> InspectWindow(UiSelector selector)
    {
        var window = FindWindow(selector);
        return window is null
            ? ResultEnvelope<object>.Fail("window_not_found", "No matching window was found.", OperationStatus.Failed, RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Ok(BuildWindowNode(window, includeControls: true), "Window inspected.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> FindControl(UiSelector selector)
    {
        var element = FindElement(selector);
        return element is null
            ? ResultEnvelope<object>.Fail("control_not_found", "No matching control was found.", OperationStatus.Failed, RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Ok(BuildControlNode(element, depth: 0), "Control found.", RiskLevel.ReadOnly);
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

    public ResultEnvelope<object> ScreenshotWindow(UiSelector selector)
    {
        var window = FindWindow(selector);
        if (window is null)
        {
            return ResultEnvelope<object>.Fail("window_not_found", "No matching window was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        Directory.CreateDirectory(layout.Screenshots);
        var rect = window.Current.BoundingRectangle;
        var bounds = new Rectangle((int)rect.X, (int)rect.Y, Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height));
        var path = Path.Combine(layout.Screenshots, $"window-{window.Current.NativeWindowHandle}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.png");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
        return ResultEnvelope<object>.Ok(new { path, hwnd = window.Current.NativeWindowHandle, width = bounds.Width, height = bounds.Height }, "Window screenshot saved.", RiskLevel.Medium);
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

    public ResultEnvelope<object> SelectItem(UiSelector selector)
    {
        var element = FindElement(selector);
        if (element is null)
        {
            return ResultEnvelope<object>.Fail("control_not_found", "No matching control was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && pattern is SelectionItemPattern selection)
        {
            selection.Select();
            return ResultEnvelope<object>.Ok(new { selector }, "Item selected.", RiskLevel.Medium);
        }

        return Click(selector);
    }

    public ResultEnvelope<object> ExpandCollapse(UiSelector selector, bool? expand = null)
    {
        var element = FindElement(selector);
        if (element is null)
        {
            return ResultEnvelope<object>.Fail("control_not_found", "No matching control was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) && pattern is ExpandCollapsePattern expandCollapse)
        {
            var state = expandCollapse.Current.ExpandCollapseState;
            if (expand ?? state == ExpandCollapseState.Collapsed)
            {
                expandCollapse.Expand();
            }
            else
            {
                expandCollapse.Collapse();
            }

            return ResultEnvelope<object>.Ok(new { selector }, "Expand/collapse completed.", RiskLevel.Medium);
        }

        return InvokeControl(selector);
    }

    public ResultEnvelope<object> Scroll(UiSelector selector, double horizontalPercent = 0, double verticalPercent = 0)
    {
        var element = FindElement(selector);
        if (element is null)
        {
            return ResultEnvelope<object>.Fail("control_not_found", "No matching control was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern) && pattern is ScrollPattern scroll)
        {
            var horizontal = scroll.Current.HorizontallyScrollable ? horizontalPercent : ScrollPattern.NoScroll;
            var vertical = scroll.Current.VerticallyScrollable ? verticalPercent : ScrollPattern.NoScroll;
            scroll.SetScrollPercent(horizontal, vertical);
            return ResultEnvelope<object>.Ok(new { selector, horizontal_percent = horizontal, vertical_percent = vertical }, "Scrolled.", RiskLevel.Medium);
        }

        var point = element.GetClickablePoint();
        SetCursorPos((int)point.X, (int)point.Y);
        MouseEvent(MouseEventFlags.Wheel, 0, 0, verticalPercent >= 0 ? -360 : 360);
        return ResultEnvelope<object>.Ok(new { selector, wheel = true }, "Mouse wheel scroll sent.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> SendKeysRaw(string keys)
    {
        SendKeys.SendWait(keys);
        return ResultEnvelope<object>.Ok(new { keys }, "Keys sent.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> DragDrop(UiSelector? selector, int? x, int? y, int toX, int toY)
    {
        int startX;
        int startY;
        if (selector is not null)
        {
            var element = FindElement(selector);
            if (element is null)
            {
                return ResultEnvelope<object>.Fail("control_not_found", "No matching drag source was found.", OperationStatus.Failed, RiskLevel.Medium);
            }

            var point = element.GetClickablePoint();
            startX = (int)point.X;
            startY = (int)point.Y;
        }
        else if (x is { } explicitX && y is { } explicitY)
        {
            startX = explicitX;
            startY = explicitY;
        }
        else
        {
            return ResultEnvelope<object>.Fail("drag_source_missing", "Drag/drop requires a selector or x/y coordinates.", OperationStatus.Failed, RiskLevel.Medium);
        }

        SetCursorPos(startX, startY);
        MouseEvent(MouseEventFlags.LeftDown);
        SetCursorPos(toX, toY);
        MouseEvent(MouseEventFlags.LeftUp);
        return ResultEnvelope<object>.Ok(new { x = startX, y = startY, to_x = toX, to_y = toY }, "Drag/drop sent.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> WaitForWindow(UiSelector selector, int timeoutMs = 5000) =>
        WaitFor("window", timeoutMs, () => FindWindow(selector) is { } window
            ? ResultEnvelope<object>.Ok(BuildWindowNode(window, includeControls: true), "Window found.", RiskLevel.ReadOnly)
            : null);

    public ResultEnvelope<object> WaitForControl(UiSelector selector, int timeoutMs = 5000) =>
        WaitFor("control", timeoutMs, () => FindElement(selector) is { } element
            ? ResultEnvelope<object>.Ok(BuildControlNode(element, depth: 0), "Control found.", RiskLevel.ReadOnly)
            : null);

    public ResultEnvelope<object> WaitForText(string text, UiSelector? selector = null, int timeoutMs = 5000) =>
        WaitFor("text", timeoutMs, () =>
        {
            var root = selector is null ? AutomationElement.RootElement : FindElement(selector);
            if (root is null)
            {
                return null;
            }

            var containsText = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Take(1000)
                .Any(e => SafeName(e).Contains(text, StringComparison.OrdinalIgnoreCase) || (TryReadValue(e)?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));

            return containsText
                ? ResultEnvelope<object>.Ok(new { text }, "Text found.", RiskLevel.ReadOnly)
                : null;
        });

    public ResultEnvelope<object> WaitForDialog(UiSelector? selector = null, int timeoutMs = 5000) =>
        WaitFor("dialog", timeoutMs, () =>
        {
            var dialogs = DetectDialogWindows(selector).ToArray();
            return dialogs.Length > 0
                ? ResultEnvelope<object>.Ok(new { dialogs }, "Dialog detected.", RiskLevel.ReadOnly)
                : null;
        });

    public ResultEnvelope<object> WaitUntilIdle(UiSelector? selector = null, int timeoutMs = 5000)
    {
        var window = selector is null ? FindWindow(new UiSelector(null, null, null, null, null, GetForegroundWindowNative().ToInt64(), null)) : FindWindow(selector);
        if (window is null)
        {
            Thread.Sleep(Math.Min(timeoutMs, 250));
            return ResultEnvelope<object>.Ok(new { waited_ms = Math.Min(timeoutMs, 250), input_idle = false }, "Idle wait completed with no target window.", RiskLevel.ReadOnly);
        }

        try
        {
            using var process = Process.GetProcessById(window.Current.ProcessId);
            var idle = process.WaitForInputIdle(timeoutMs);
            return ResultEnvelope<object>.Ok(new { pid = process.Id, input_idle = idle }, idle ? "Process input queue is idle." : "Timed out waiting for process input idle.", RiskLevel.ReadOnly);
        }
        catch (Exception ex)
        {
            return ResultEnvelope<object>.Fail("wait_idle_failed", ex.Message, OperationStatus.Failed, RiskLevel.ReadOnly);
        }
    }

    public ResultEnvelope<object> DetectCommonDialogs(UiSelector? selector = null)
    {
        var dialogs = DetectDialogWindows(selector).ToArray();
        return ResultEnvelope<object>.Ok(new { dialogs }, "Dialog scan complete.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> GetPlanStatus(string planId) =>
        _plans.TryGetValue(planId, out var result)
            ? ResultEnvelope<object>.Ok(result, "Plan status.", RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Fail("plan_not_found", "No plan with that id is known in this DesktopAgent session.", OperationStatus.Failed, RiskLevel.ReadOnly);

    public ResultEnvelope<object> ReadPlanLog(string planId) =>
        _planLogs.TryGetValue(planId, out var log)
            ? ResultEnvelope<object>.Ok(new { plan_id = planId, log }, "Plan log.", RiskLevel.ReadOnly)
            : ResultEnvelope<object>.Fail("plan_not_found", "No plan log with that id is known in this DesktopAgent session.", OperationStatus.Failed, RiskLevel.ReadOnly);

    public ResultEnvelope<object> StopCurrentPlan(string planId) =>
        ResultEnvelope<object>.Ok(new { plan_id = planId, stop_requested = false }, "Plan cancellation is cooperative through request cancellation in this milestone.", RiskLevel.Medium);

    public ResultEnvelope<UiPlanResult> ExecutePlan(UiPlanRequest request, CancellationToken cancellationToken)
    {
        var planId = Guid.NewGuid().ToString("n");
        var results = new List<UiPlanStepResult>();
        var log = new List<string>();
        using var timeoutCts = request.PlanTimeoutMs > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (timeoutCts is not null)
        {
            timeoutCts.CancelAfter(request.PlanTimeoutMs);
            cancellationToken = timeoutCts.Token;
        }

        for (var i = 0; i < request.Actions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = request.Actions[i];
            if (action.WaitBeforeMs > 0)
            {
                Task.Delay(action.WaitBeforeMs, cancellationToken).GetAwaiter().GetResult();
            }

            var beforeSnapshot = CaptureSnapshot(includeControls: true);
            if (ShouldPauseForSensitivePrompt(action, beforeSnapshot, out var beforeReason))
            {
                var waiting = new UiPlanResult(planId, OperationStatus.WaitingForUser, beforeReason, results, beforeSnapshot);
                _plans[planId] = waiting;
                _planLogs[planId] = string.Join(Environment.NewLine, log);
                return ResultEnvelope<UiPlanResult>.Ok(waiting, beforeReason, RiskLevel.CredentialSensitive);
            }

            ResultEnvelope<object> result = ResultEnvelope<object>.Fail("ui_action_not_attempted", "UI action was not attempted.", OperationStatus.Failed, RiskLevel.Medium);
            for (var attempt = 0; attempt <= Math.Max(0, action.RetryCount); attempt++)
            {
                result = ExecuteActionWithTimeout(action, cancellationToken);
                log.Add($"{DateTimeOffset.UtcNow:O} step={i} action={action.ActionType} attempt={attempt + 1} success={result.Success} message={result.Message}");
                if (result.Success || action.FallbackSelector is null)
                {
                    break;
                }

                var fallback = action with { Selector = action.FallbackSelector, FallbackSelector = null };
                result = ExecuteActionWithTimeout(fallback, cancellationToken);
                log.Add($"{DateTimeOffset.UtcNow:O} step={i} action={action.ActionType} fallback=true success={result.Success} message={result.Message}");
                if (result.Success)
                {
                    break;
                }
            }

            var snapshot = CaptureSnapshot(includeControls: true);
            results.Add(new UiPlanStepResult(i, action.ActionType, result.Success, result.Message ?? string.Empty, snapshot));
            if (ShouldPauseForSensitivePrompt(action, snapshot, out var afterReason))
            {
                var waiting = new UiPlanResult(planId, OperationStatus.WaitingForUser, afterReason, results, snapshot);
                _plans[planId] = waiting;
                _planLogs[planId] = string.Join(Environment.NewLine, log);
                return ResultEnvelope<UiPlanResult>.Ok(waiting, afterReason, RiskLevel.CredentialSensitive);
            }

            if (!result.Success && !string.Equals(action.FailureBehavior, "continue", StringComparison.OrdinalIgnoreCase))
            {
                var failed = new UiPlanResult(planId, result.Status, result.Message ?? "UI plan stopped.", results, snapshot);
                _plans[planId] = failed;
                _planLogs[planId] = string.Join(Environment.NewLine, log);
                return ResultEnvelope<UiPlanResult>.Ok(failed, "UI plan stopped.", RiskLevel.Medium);
            }

            if (action.WaitAfterMs > 0)
            {
                Task.Delay(action.WaitAfterMs, cancellationToken).GetAwaiter().GetResult();
            }
        }

        var completed = new UiPlanResult(planId, OperationStatus.Success, "UI plan completed.", results, CaptureSnapshot(includeControls: true));
        _plans[planId] = completed;
        _planLogs[planId] = string.Join(Environment.NewLine, log);
        return ResultEnvelope<UiPlanResult>.Ok(completed, "UI plan completed.", RiskLevel.Medium);
    }

    private ResultEnvelope<object> ExecuteActionWithTimeout(UiPlanAction action, CancellationToken cancellationToken)
    {
        if (action.TimeoutMs <= 0)
        {
            return ExecuteAction(action);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = Task.Run(() => ExecuteAction(action), timeoutCts.Token);
        if (task.Wait(action.TimeoutMs, cancellationToken))
        {
            return task.Result;
        }

        timeoutCts.Cancel();
        return ResultEnvelope<object>.Fail("ui_action_timeout", $"Action {action.ActionType} timed out after {action.TimeoutMs}ms.", OperationStatus.TimedOut, RiskLevel.Medium);
    }

    private ResultEnvelope<object> ExecuteAction(UiPlanAction action) =>
        action.ActionType switch
        {
            UiActionType.FocusWindow => FocusWindow(action.Selector ?? EmptySelector()),
            UiActionType.MoveWindow => MoveWindow(action.Selector ?? EmptySelector(), action.X ?? 0, action.Y ?? 0),
            UiActionType.ResizeWindow => ResizeWindow(action.Selector ?? EmptySelector(), action.Width ?? 1024, action.Height ?? 768),
            UiActionType.MinimizeWindow => MinimizeWindow(action.Selector ?? EmptySelector()),
            UiActionType.MaximizeWindow => MaximizeWindow(action.Selector ?? EmptySelector()),
            UiActionType.CloseWindow => CloseWindow(action.Selector ?? EmptySelector()),
            UiActionType.Click => Click(action.Selector ?? EmptySelector()),
            UiActionType.DoubleClick => Click(action.Selector ?? EmptySelector(), doubleClick: true),
            UiActionType.RightClick => Click(action.Selector ?? EmptySelector(), rightClick: true),
            UiActionType.InvokeControl => InvokeControl(action.Selector ?? EmptySelector()),
            UiActionType.SetControlValue => SetControlValue(action.Selector ?? EmptySelector(), action.Text ?? string.Empty),
            UiActionType.SelectItem => SelectItem(action.Selector ?? EmptySelector()),
            UiActionType.ExpandCollapse => ExpandCollapse(action.Selector ?? EmptySelector()),
            UiActionType.Scroll => Scroll(action.Selector ?? EmptySelector(), action.X ?? 0, action.Y ?? 50),
            UiActionType.TypeText => TypeText(action.Text ?? string.Empty),
            UiActionType.SendHotkey => SendHotkey(action.Hotkey ?? string.Empty),
            UiActionType.SendKeys => SendKeysRaw(action.Text ?? action.Hotkey ?? string.Empty),
            UiActionType.ClipboardSet => ClipboardSet(action.Text ?? string.Empty),
            UiActionType.WaitForWindow => WaitForWindow(action.Selector ?? EmptySelector(), action.TimeoutMs > 0 ? action.TimeoutMs : 5000),
            UiActionType.WaitForControl => WaitForControl(action.Selector ?? EmptySelector(), action.TimeoutMs > 0 ? action.TimeoutMs : 5000),
            UiActionType.WaitForText => WaitForText(action.Text ?? action.ExpectedResult ?? string.Empty, action.Selector, action.TimeoutMs > 0 ? action.TimeoutMs : 5000),
            UiActionType.WaitForDialog => WaitForDialog(action.Selector, action.TimeoutMs > 0 ? action.TimeoutMs : 5000),
            UiActionType.WaitUntilIdle => WaitUntilIdle(action.Selector, action.TimeoutMs > 0 ? action.TimeoutMs : 5000),
            UiActionType.Screenshot => ScreenshotDesktop(),
            UiActionType.ScreenshotWindow => ScreenshotWindow(action.Selector ?? EmptySelector()),
            UiActionType.DragDrop => DragDrop(action.Selector, action.X, action.Y, action.ToX ?? 0, action.ToY ?? 0),
            _ => ResultEnvelope<object>.Fail("ui_action_not_implemented", $"Action {action.ActionType} is scaffolded.", OperationStatus.NotImplemented, RiskLevel.Medium)
        };

    private ResultEnvelope<object> WaitFor(string subject, int timeoutMs, Func<ResultEnvelope<object>?> probe)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(1, timeoutMs));
        while (DateTimeOffset.UtcNow <= deadline)
        {
            var result = probe();
            if (result is not null)
            {
                return result;
            }

            Thread.Sleep(100);
        }

        return ResultEnvelope<object>.Fail($"ui_wait_{subject}_timeout", $"Timed out waiting for {subject}.", OperationStatus.TimedOut, RiskLevel.ReadOnly);
    }

    private ResultEnvelope<object> ShowWindowBySelector(UiSelector selector, ShowWindowCommand command, string message)
    {
        var window = FindWindow(selector);
        if (window is null)
        {
            return ResultEnvelope<object>.Fail("window_not_found", "No matching window was found.", OperationStatus.Failed, RiskLevel.Medium);
        }

        ShowWindow(new IntPtr(window.Current.NativeWindowHandle), command);
        return ResultEnvelope<object>.Ok(new { hwnd = window.Current.NativeWindowHandle, command = command.ToString() }, message, RiskLevel.Medium);
    }

    private IEnumerable<object> DetectDialogWindows(UiSelector? selector)
    {
        var snapshot = CaptureSnapshot(includeControls: true);
        var windows = selector is null
            ? snapshot.Windows
            : snapshot.Windows.Where(w => SelectorMatchesWindowNode(w, selector));

        foreach (var window in windows)
        {
            var text = (window.Title + " " + string.Join(' ', FlattenControls(window.RootControl).Select(c => c.Name))).Trim();
            var looksDialog =
                window.ClassName.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("User Account Control", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("verification code", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("multi-factor", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("administrator permission", StringComparison.OrdinalIgnoreCase) ||
                FlattenControls(window.RootControl).Any(c => c.ControlType.Contains("Button", StringComparison.OrdinalIgnoreCase) && SafeDialogButtonNames.Contains(c.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase));

            if (looksDialog)
            {
                yield return new
                {
                    window.Hwnd,
                    window.Title,
                    window.ClassName,
                    window.ProcessName,
                    sensitive = FlattenControls(window.RootControl).Any(c => c.IsSensitive),
                    suggested_actions = FlattenControls(window.RootControl)
                        .Where(c => c.ControlType.Contains("Button", StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(20)
                        .ToArray()
                };
            }
        }
    }

    private static bool ShouldPauseForSensitivePrompt(UiPlanAction action, DesktopSnapshot snapshot, out string reason)
    {
        var windows = snapshot.Windows;
        if (action.StopIfUacPromptAppears && windows.Any(w => w.Title.Contains("User Account Control", StringComparison.OrdinalIgnoreCase)))
        {
            reason = "UI plan paused because a UAC prompt appeared. The user must complete or cancel it.";
            return true;
        }

        var controls = windows.SelectMany(w => FlattenControls(w.RootControl)).ToArray();
        if (action.StopIfUnexpectedSensitiveFieldAppears && controls.Any(c => c.IsSensitive))
        {
            reason = "UI plan paused because a password/sensitive field appeared.";
            return true;
        }

        if (action.StopIfCredentialOrMfaPromptAppears)
        {
            var text = string.Join(' ', windows.Select(w => w.Title).Concat(controls.Select(c => c.Name ?? string.Empty)));
            if (controls.Any(c => c.IsSensitive) ||
                text.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("verification code", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("authenticator", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("MFA", StringComparison.OrdinalIgnoreCase))
            {
                reason = "UI plan paused because a credential or MFA prompt appeared. Ask the user to complete the login.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

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
        var minimized = false;
        var maximized = false;
        if (element.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern) && pattern is WindowPattern windowPattern)
        {
            minimized = windowPattern.Current.WindowVisualState == WindowVisualState.Minimized;
            maximized = windowPattern.Current.WindowVisualState == WindowVisualState.Maximized;
        }

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
            minimized,
            maximized,
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

    private static bool SelectorMatchesWindowNode(UiWindowNode window, UiSelector selector)
    {
        if (selector.Hwnd is { } hwnd && window.Hwnd != hwnd) return false;
        if (selector.ProcessId is { } pid && window.ProcessId != pid) return false;
        if (!string.IsNullOrWhiteSpace(selector.WindowTitle) && !window.Title.Contains(selector.WindowTitle, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(selector.WindowClass) && !string.Equals(window.ClassName, selector.WindowClass, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static IEnumerable<UiControlNode> FlattenControls(UiControlNode? root)
    {
        if (root is null)
        {
            yield break;
        }

        yield return root;
        foreach (var child in root.Children.SelectMany(FlattenControls))
        {
            yield return child;
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try { return element.Current.Name ?? string.Empty; }
        catch { return string.Empty; }
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
            (ScrollPattern.Pattern, "scroll"),
            (ScrollItemPattern.Pattern, "scroll_item"),
            (TextPattern.Pattern, "text"),
            (WindowPattern.Pattern, "window")
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
        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _)) actions.Add("select");
        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _)) actions.Add("expand_collapse");
        if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out _)) actions.Add("scroll");
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

    [DllImport("user32.dll", EntryPoint = "MoveWindow")]
    private static extern bool MoveWindowNative(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(MouseEventFlags dwFlags, int dx = 0, int dy = 0, int dwData = 0, UIntPtr dwExtraInfo = default);

    private const uint WmClose = 0x0010;

    private enum ShowWindowCommand
    {
        Minimize = 6,
        Maximize = 3,
        Restore = 9
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        Wheel = 0x0800
    }
}
