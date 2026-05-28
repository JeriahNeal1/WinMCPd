namespace WindowsPowerUserMcp.Core;

public enum UiActionType
{
    FocusWindow,
    MoveWindow,
    ResizeWindow,
    MinimizeWindow,
    MaximizeWindow,
    CloseWindow,
    InvokeControl,
    SetControlValue,
    SelectItem,
    ExpandCollapse,
    Scroll,
    Click,
    DoubleClick,
    RightClick,
    TypeText,
    SendHotkey,
    SendKeys,
    ClipboardSet,
    WaitForWindow,
    WaitForControl,
    WaitForText,
    WaitForDialog,
    WaitUntilIdle,
    Screenshot,
    ScreenshotWindow,
    DragDrop
}

public sealed record MonitorInfo(
    string DeviceName,
    int X,
    int Y,
    int Width,
    int Height,
    bool Primary);

public sealed record UiControlNode(
    string RuntimeId,
    string? AutomationId,
    string? Name,
    string ControlType,
    string? ClassName,
    string? FrameworkId,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsKeyboardFocusable,
    bool HasKeyboardFocus,
    IReadOnlyList<string> SupportedPatterns,
    string? SafeValue,
    bool IsSensitive,
    double Confidence,
    IReadOnlyList<string> SuggestedActions,
    IReadOnlyList<UiControlNode> Children);

public sealed record UiWindowNode(
    string RuntimeId,
    long Hwnd,
    string Title,
    string ClassName,
    string? ProcessName,
    int? ProcessId,
    int X,
    int Y,
    int Width,
    int Height,
    bool Visible,
    bool Enabled,
    bool Focused,
    bool Minimized,
    bool Maximized,
    UiControlNode? RootControl);

public sealed record DesktopSnapshot(
    string SnapshotId,
    DateTimeOffset Timestamp,
    string UserName,
    IReadOnlyList<MonitorInfo> Monitors,
    UiWindowNode? ForegroundWindow,
    IReadOnlyList<UiWindowNode> Windows);

public sealed record UiSelector(
    string? WindowTitle,
    string? WindowClass,
    string? AutomationId,
    string? Name,
    string? ControlType,
    long? Hwnd,
    int? ProcessId);

public sealed record UiPlanAction(
    UiActionType ActionType,
    UiSelector? Selector,
    UiSelector? FallbackSelector,
    int WaitBeforeMs,
    int WaitAfterMs,
    int TimeoutMs,
    string? Text,
    string? Hotkey,
    string? ExpectedResult,
    int RetryCount,
    string FailureBehavior,
    bool ScreenshotBefore,
    bool ScreenshotAfter,
    bool SafeTextEntryMode,
    bool StopIfUnexpectedSensitiveFieldAppears,
    bool StopIfUacPromptAppears,
    bool StopIfCredentialOrMfaPromptAppears,
    bool StopIfUserInterventionRequired,
    int? X = null,
    int? Y = null,
    int? ToX = null,
    int? ToY = null,
    int? Width = null,
    int? Height = null);

public sealed record UiPlanRequest(
    string? TaskId,
    IReadOnlyList<UiPlanAction> Actions,
    int PlanTimeoutMs = 0);

public sealed record UiPlanStepResult(
    int Index,
    UiActionType ActionType,
    bool Success,
    string Message,
    DesktopSnapshot? Snapshot);

public sealed record UiPlanResult(
    string PlanId,
    OperationStatus Status,
    string Message,
    IReadOnlyList<UiPlanStepResult> Steps,
    DesktopSnapshot? FinalSnapshot);
