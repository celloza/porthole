using Microsoft.WSL.Containers;

namespace porthole_cli.Commands;

internal static class RunArgumentParser
{
    internal readonly record struct VolumeMount(string HostPath, string ContainerPath, bool ReadOnly);

    public static VolumeMount ParseVolume(string raw)
    {
        // Docker-style mount format: hostPath:containerPath[:ro|rw].
        // Handle Windows drive prefixes like C:\repo:/workspace and named volumes like data:/workspace.
        int separator = GetHostContainerSeparator(raw);

        if (separator <= 0)
        {
            throw new InvalidOperationException($"Invalid volume mapping '{raw}'. Expected hostPath:containerPath[:ro|rw].");
        }

        string hostPath = raw[..separator].Trim();
        string remainder = raw[(separator + 1)..].Trim();

        string containerPath;
        bool readOnly = false;
        int modeSeparator = remainder.LastIndexOf(':');
        if (modeSeparator > 0)
        {
            containerPath = remainder[..modeSeparator].Trim();
            string mode = remainder[(modeSeparator + 1)..].Trim();
            readOnly = string.Equals(mode, "ro", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            containerPath = remainder;
        }

        if (string.IsNullOrWhiteSpace(hostPath) || string.IsNullOrWhiteSpace(containerPath))
        {
            throw new InvalidOperationException($"Invalid volume mapping '{raw}'. Expected hostPath:containerPath[:ro|rw].");
        }

        return new VolumeMount(hostPath, containerPath, readOnly);
    }

    private static int GetHostContainerSeparator(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return -1;
        }

        bool hasWindowsDrivePrefix = raw.Length >= 3
            && char.IsLetter(raw[0])
            && raw[1] == ':'
            && (raw[2] == '\\' || raw[2] == '/');

        return hasWindowsDrivePrefix
            ? raw.IndexOf(':', 2)
            : raw.IndexOf(':');
    }

    public static ContainerPortMapping ParsePortMapping(string raw)
    {
        // Supported forms: hostPort:containerPort, hostIp:hostPort:containerPort, and /tcp|/udp suffixes.
        string normalized = raw.Trim();
        PortProtocol protocol = PortProtocol.TCP;

        int protocolSeparator = normalized.LastIndexOf('/');
        if (protocolSeparator > 0)
        {
            string protocolToken = normalized[(protocolSeparator + 1)..];
            protocol = string.Equals(protocolToken, "udp", StringComparison.OrdinalIgnoreCase)
                ? PortProtocol.UDP
                : PortProtocol.TCP;
            normalized = normalized[..protocolSeparator];
        }

        string[] parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"Invalid port mapping '{raw}'. Expected hostPort:containerPort or hostIp:hostPort:containerPort.");
        }

        string hostPortToken = parts.Length == 2 ? parts[0] : parts[1];
        string containerPortToken = parts[^1];

        if (!ushort.TryParse(hostPortToken, out ushort hostPort) || !ushort.TryParse(containerPortToken, out ushort containerPort))
        {
            throw new InvalidOperationException($"Invalid port mapping '{raw}'. Ports must be numeric.");
        }

        return new ContainerPortMapping(hostPort, containerPort, protocol);
    }
}
