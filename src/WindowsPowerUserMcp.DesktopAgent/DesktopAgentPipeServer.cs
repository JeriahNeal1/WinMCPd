using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WindowsPowerUserMcp.Core;
using WindowsPowerUserMcp.Tray;

namespace WindowsPowerUserMcp.DesktopAgent;

public sealed class DesktopAgentPipeServer(
    string pipeName,
    DesktopUiAutomationService ui,
    TrayController tray,
    MainWindow dashboard)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(pipe, cancellationToken), CancellationToken.None);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            var request = JsonSerializer.Deserialize<BrokerRequest>(requestLine, JsonDefaults.Options);
            if (request is null)
            {
                await WriteAsync(writer, new BrokerResponse("unknown", false, null, "bad_request", "DesktopAgent could not parse request.", DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
                return;
            }

            object result;
            try
            {
                result = await InvokeAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = ResultEnvelope<object>.Fail("desktop_agent_exception", ex.Message, OperationStatus.Failed, RiskLevel.Medium);
            }

            await WriteAsync(writer, new BrokerResponse(request.RequestId, true, JsonDefaults.ToElement(result), null, null, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<object> InvokeAsync(string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        dashboard.Log(toolName);
        tray.SetStatus(TrayBackendStatus.Busy, toolName);
        try
        {
            return toolName switch
            {
                "ui_get_desktop_snapshot" => Task.FromResult<object>(ui.GetDesktopSnapshot()),
                "ui_list_windows" => Task.FromResult<object>(ui.ListWindows()),
                "ui_get_foreground_window" => Task.FromResult<object>(ui.GetForegroundWindow()),
                "ui_focus_window" => Task.FromResult<object>(ui.FocusWindow(GetSelector(args))),
                "ui_move_window" => Task.FromResult<object>(ui.MoveWindow(GetSelector(args), GetInt(args, "x", 0), GetInt(args, "y", 0))),
                "ui_resize_window" => Task.FromResult<object>(ui.ResizeWindow(GetSelector(args), GetInt(args, "width", 1024), GetInt(args, "height", 768))),
                "ui_minimize_window" => Task.FromResult<object>(ui.MinimizeWindow(GetSelector(args))),
                "ui_maximize_window" => Task.FromResult<object>(ui.MaximizeWindow(GetSelector(args))),
                "ui_close_window" => Task.FromResult<object>(ui.CloseWindow(GetSelector(args))),
                "ui_inspect_window" => Task.FromResult<object>(ui.InspectWindow(GetSelector(args))),
                "ui_find_control" => Task.FromResult<object>(ui.FindControl(GetSelector(args))),
                "ui_screenshot_desktop" => Task.FromResult<object>(ui.ScreenshotDesktop()),
                "ui_screenshot_window" => Task.FromResult<object>(ui.ScreenshotWindow(GetSelector(args))),
                "ui_clipboard_set" => Task.FromResult<object>(ui.ClipboardSet(GetString(args, "text"))),
                "ui_clipboard_get_redacted" => Task.FromResult<object>(ui.ClipboardGetRedacted()),
                "ui_type_text" => Task.FromResult<object>(ui.TypeText(GetString(args, "text"))),
                "ui_send_hotkey" => Task.FromResult<object>(ui.SendHotkey(GetString(args, "hotkey"))),
                "ui_send_keys" => Task.FromResult<object>(ui.SendKeysRaw(GetString(args, "text") is { Length: > 0 } keys ? keys : GetString(args, "hotkey"))),
                "ui_click" => Task.FromResult<object>(ui.Click(GetSelector(args))),
                "ui_double_click" => Task.FromResult<object>(ui.Click(GetSelector(args), doubleClick: true)),
                "ui_right_click" => Task.FromResult<object>(ui.Click(GetSelector(args), rightClick: true)),
                "ui_invoke_control" => Task.FromResult<object>(ui.InvokeControl(GetSelector(args))),
                "ui_set_control_value" => Task.FromResult<object>(ui.SetControlValue(GetSelector(args), GetString(args, "value"))),
                "ui_select_item" => Task.FromResult<object>(ui.SelectItem(GetSelector(args))),
                "ui_expand_collapse" => Task.FromResult<object>(ui.ExpandCollapse(GetSelector(args))),
                "ui_scroll" => Task.FromResult<object>(ui.Scroll(GetSelector(args), GetInt(args, "x", 0), GetInt(args, "y", 50))),
                "ui_drag_drop" => Task.FromResult<object>(ui.DragDrop(GetSelector(args), GetOptionalInt(args, "x"), GetOptionalInt(args, "y"), GetInt(args, "to_x", 0), GetInt(args, "to_y", 0))),
                "ui_wait_for_window" => Task.FromResult<object>(ui.WaitForWindow(GetSelector(args), GetInt(args, "timeout_ms", 5000))),
                "ui_wait_for_control" => Task.FromResult<object>(ui.WaitForControl(GetSelector(args), GetInt(args, "timeout_ms", 5000))),
                "ui_wait_for_text" => Task.FromResult<object>(ui.WaitForText(GetString(args, "text"), GetSelector(args), GetInt(args, "timeout_ms", 5000))),
                "ui_wait_for_dialog" => Task.FromResult<object>(ui.WaitForDialog(GetSelector(args), GetInt(args, "timeout_ms", 5000))),
                "ui_wait_until_idle" => Task.FromResult<object>(ui.WaitUntilIdle(GetSelector(args), GetInt(args, "timeout_ms", 5000))),
                "ui_detect_common_dialogs" => Task.FromResult<object>(ui.DetectCommonDialogs(GetSelector(args))),
                "ui_execute_plan" => Task.FromResult<object>(ui.ExecutePlan(GetPlanRequest(args), cancellationToken)),
                "ui_stop_current_plan" => Task.FromResult<object>(ui.StopCurrentPlan(GetString(args, "plan_id"))),
                "ui_get_plan_status" => Task.FromResult<object>(ui.GetPlanStatus(GetString(args, "plan_id"))),
                "ui_read_plan_log" => Task.FromResult<object>(ui.ReadPlanLog(GetString(args, "plan_id"))),
                _ => Task.FromResult<object>(ResultEnvelope<object>.Fail("ui_tool_not_implemented", $"DesktopAgent tool '{toolName}' is scaffolded in this milestone.", OperationStatus.NotImplemented, RiskLevel.Medium))
            };
        }
        finally
        {
            tray.SetStatus(TrayBackendStatus.Running, "DesktopAgent UI pipe ready");
        }
    }

    private static UiSelector GetSelector(JsonElement args)
    {
        if (args.TryGetProperty("selector", out var selector))
        {
            return JsonDefaults.FromElement<UiSelector>(selector) ?? new UiSelector(null, null, null, null, null, null, null);
        }

        return new UiSelector(
            GetOptionalString(args, "window_title"),
            GetOptionalString(args, "window_class"),
            GetOptionalString(args, "automation_id"),
            GetOptionalString(args, "name"),
            GetOptionalString(args, "control_type"),
            GetOptionalLong(args, "hwnd"),
            GetOptionalInt(args, "process_id"));
    }

    private static string GetString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) ? property.GetString() ?? string.Empty : string.Empty;

    private static string? GetOptionalString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;

    private static int? GetOptionalInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetInt32() : null;

    private static long? GetOptionalLong(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetInt64() : null;

    private static int GetInt(JsonElement args, string name, int fallback) =>
        args.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property.GetInt32() : fallback;

    private static UiPlanRequest GetPlanRequest(JsonElement args)
    {
        if (args.TryGetProperty("plan", out var plan) && plan.ValueKind == JsonValueKind.Object)
        {
            return JsonDefaults.FromElement<UiPlanRequest>(plan) ?? new UiPlanRequest(null, []);
        }

        return JsonDefaults.FromElement<UiPlanRequest>(args) ?? new UiPlanRequest(null, []);
    }

    private static Task WriteAsync(StreamWriter writer, BrokerResponse response, CancellationToken cancellationToken) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonDefaults.Options).AsMemory(), cancellationToken);
}
