namespace WindowsPowerUserMcp.Core;

public enum RiskLevel
{
    ReadOnly,
    Low,
    Medium,
    High,
    Destructive,
    SecuritySensitive,
    CredentialSensitive
}

public enum OperationStatus
{
    Success,
    Failed,
    TimedOut,
    Cancelled,
    WaitingForUser,
    NotImplemented,
    BrokerUnavailable
}

public enum TaskStatus
{
    Active,
    Waiting,
    Blocked,
    Complete,
    Failed,
    Cancelled
}

public enum TrackedProcessStatus
{
    Starting,
    Running,
    Exited,
    TimedOut,
    Cancelled,
    Failed,
    RequiresUserIntervention
}

public enum ContinuationConditionType
{
    Time,
    ProcessExit,
    FileCreated,
    FileChanged,
    WindowDetected,
    DialogDetected,
    ManualUserContinue
}

public enum TrayBackendStatus
{
    Stopped,
    Starting,
    Running,
    Busy,
    WaitingForUser,
    Error
}

public enum ToolImplementationStatus
{
    Implemented,
    Scaffolded,
    DelegatedToDesktopAgent
}

public sealed record ResultEnvelope<T>(
    bool Success,
    OperationStatus Status,
    string? ErrorCode,
    string? Message,
    RiskLevel RiskLevel,
    T? Data,
    string? AuditId,
    IReadOnlyDictionary<string, string>? Artifacts,
    IReadOnlyList<string>? RedactionsApplied,
    DateTimeOffset Timestamp)
{
    public static ResultEnvelope<T> Ok(
        T data,
        string? message = null,
        RiskLevel riskLevel = RiskLevel.Low,
        string? auditId = null,
        IReadOnlyDictionary<string, string>? artifacts = null,
        IReadOnlyList<string>? redactionsApplied = null) =>
        new(true, OperationStatus.Success, null, message, riskLevel, data, auditId, artifacts, redactionsApplied, DateTimeOffset.UtcNow);

    public static ResultEnvelope<T> Fail(
        string errorCode,
        string message,
        OperationStatus status = OperationStatus.Failed,
        RiskLevel riskLevel = RiskLevel.Medium,
        string? auditId = null,
        IReadOnlyDictionary<string, string>? artifacts = null,
        IReadOnlyList<string>? redactionsApplied = null) =>
        new(false, status, errorCode, message, riskLevel, default, auditId, artifacts, redactionsApplied, DateTimeOffset.UtcNow);
}

public sealed record ToolDescriptor(
    string Name,
    string Description,
    RiskLevel RiskLevel,
    ToolImplementationStatus ImplementationStatus,
    string Component);

public sealed record BrokerStatus(
    bool Running,
    string Component,
    string Version,
    string PipeName,
    bool IsElevated,
    string DataRoot,
    string ServiceDataRoot,
    DateTimeOffset Timestamp,
    IReadOnlyList<ToolDescriptor> Tools);

public sealed record PathInfo(
    string Path,
    bool Exists,
    bool IsFile,
    bool IsDirectory,
    long? Length,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Attributes);

public sealed record CommandExecutionResult(
    string CommandLineRedacted,
    string? WorkingDirectory,
    int? ExitCode,
    TimeSpan Duration,
    bool TimedOut,
    string StdoutTail,
    string StderrTail,
    string StdoutLogPath,
    string StderrLogPath);

public sealed record TrackedProcessRecord(
    string Id,
    string? TaskId,
    int? Pid,
    string ProcessName,
    string CommandLineRedacted,
    string? WorkingDirectory,
    DateTimeOffset StartedAt,
    DateTimeOffset? ExitedAt,
    int? ExitCode,
    TrackedProcessStatus Status,
    string StdoutLogPath,
    string StderrLogPath,
    string? ExpectedCompletionSignal,
    string? ContinuationPrompt,
    bool RequiresUserIntervention);

public sealed record TaskRecord(
    string Id,
    string Title,
    string Goal,
    TaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CurrentSummary,
    string? NextSteps,
    string? ContinuationPrompt,
    string? CompletionEvidence,
    string? OwnerUser,
    int Priority);

public sealed record TaskEventRecord(
    string Id,
    string TaskId,
    DateTimeOffset Timestamp,
    string Actor,
    string EventType,
    string Summary,
    string? ToolName,
    string? CommandLineRedacted,
    string? WorkingDirectory,
    string? ResultStatus,
    string? StdoutLogPath,
    string? StderrLogPath,
    string? ArtifactsJson,
    string? RedactionsAppliedJson);

public sealed record PendingContinuationRecord(
    string Id,
    string TaskId,
    ContinuationConditionType ConditionType,
    string ConditionPayloadJson,
    string Prompt,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? TriggeredAt);

public sealed record ApprovalRecord(
    string Id,
    string? TaskId,
    DateTimeOffset Timestamp,
    string Operation,
    RiskLevel RiskLevel,
    string ApprovalSource,
    string ApprovalTextSummary);

public sealed record AuditEventRecord(
    string Id,
    DateTimeOffset Timestamp,
    string Severity,
    string Component,
    string Operation,
    RiskLevel RiskLevel,
    string User,
    string Result,
    string Summary,
    string? DetailsJsonRedacted);
