using Microsoft.WSL.Containers;

namespace porthole_cli.Commands;

internal static class RunArgumentParser
{
    internal readonly record struct VolumeMount(string HostPath, string ContainerPath, bool ReadOnly);

    public static VolumeMount ParseVolume(string raw)
    {
        // Docker-style mount format: hostPath:containerPath[:ro|rw].
        // Handle Windows drive prefixes like C:\repo:/workspace.
        int firstSeparator = raw.IndexOf(':');
        int secondSeparator = raw.IndexOf(':', firstSeparator + 1);

        if (firstSeparator <= 0 || secondSeparator <= firstSeparator)
        {
            throw new InvalidOperationException($"Invalid volume mapping '{raw}'. Expected hostPath:containerPath[:ro|rw].");
        }

        string hostPath = raw[..secondSeparator].Trim();
        string remainder = raw[(secondSeparator + 1)..].Trim();

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
