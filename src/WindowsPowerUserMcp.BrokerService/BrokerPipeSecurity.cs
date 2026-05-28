using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.BrokerService;

public static class BrokerPipeSecurity
{
    public static NamedPipeServerStream CreateServerStream(WindowsPowerUserMcpOptions options)
    {
        if (options.IpcCurrentUserOnly && options.IpcAllowedUserSids.Length == 0)
        {
            return new NamedPipeServerStream(
                options.IpcPipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        return NamedPipeServerStreamAcl.Create(
            options.IpcPipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            BuildPipeSecurity(options));
    }

    public static PipeSecurity BuildPipeSecurity(WindowsPowerUserMcpOptions options)
    {
        var security = new PipeSecurity();
        var rights = PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance;
        var current = WindowsIdentity.GetCurrent().User;
        if (current is not null)
        {
            security.AddAccessRule(new PipeAccessRule(current, rights, AccessControlType.Allow));
        }

        foreach (var sidText in options.IpcAllowedUserSids.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sidText), rights, AccessControlType.Allow));
        }

        if (options.IpcAllowBuiltinAdministrators)
        {
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), rights, AccessControlType.Allow));
        }

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return security;
    }

    public static IReadOnlyList<string> GetAllowedSidStrings(WindowsPowerUserMcpOptions options)
    {
        var sids = new List<string>();
        var current = WindowsIdentity.GetCurrent().User?.Value;
        if (!string.IsNullOrWhiteSpace(current))
        {
            sids.Add(current);
        }

        sids.AddRange(options.IpcAllowedUserSids.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (options.IpcAllowBuiltinAdministrators)
        {
            sids.Add(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value);
        }

        return sids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
