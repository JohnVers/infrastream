using InfraStream.CollectorEngine.Configuration;

namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Validates <see cref="InfraStreamOptions"/> protocol configuration.
/// </summary>
public static class IngressOptionsValidator
{
    private const int DefaultHttp1Port = 5005;

    private static readonly string[] AllowedHttp1Modes = { "ConnectionHandler", "Kestrel" };

    public static IReadOnlyList<string> Validate(InfraStreamOptions options)
    {
        var errors = new List<string>();

        ValidateExactlyOneProtocol(options, errors);
        ValidatePorts(options, errors);
        ValidateHttp1Mode(options, errors);
        ValidateHttp1PortResolution(options, errors);

        return errors;
    }

    private static void ValidateExactlyOneProtocol(
        InfraStreamOptions options,
        List<string> errors)
    {
        var p = options.Protocols;
        int enabledCount =
            (p.Http1.Enabled ? 1 : 0) +
            (p.Http2.Enabled ? 1 : 0) +
            (p.Grpc.Enabled ? 1 : 0);

        if (enabledCount != 1)
        {
            errors.Add(
                $"Exactly one ingress protocol must be enabled " +
                $"(Protocols:Http1, Protocols:Http2, or Protocols:Grpc); " +
                $"got {enabledCount}.");
        }
    }

    private static void ValidatePorts(
        InfraStreamOptions options,
        List<string> errors)
    {
        var ports = new Dictionary<int, string>();

        void CheckPort(int port, string name)
        {
            if (port <= 0 || port > 65535)
            {
                errors.Add($"{name} port must be in 1..65535 (got {port}).");
                return;
            }

            if (ports.TryGetValue(port, out var existing))
            {
                errors.Add($"{name} port {port} conflicts with {existing}.");
                return;
            }

            ports[port] = name;
        }

        CheckPort(options.ManagementPort, "ManagementPort");

        var p = options.Protocols;
        if (p.Http1.Enabled) CheckPort(ResolveHttp1Port(options), "Http1");
        if (p.Http2.Enabled) CheckPort(p.Http2.Port, "Http2");
        if (p.Grpc.Enabled) CheckPort(p.Grpc.Port, "Grpc");
    }

    private static void ValidateHttp1Mode(
        InfraStreamOptions options,
        List<string> errors)
    {
        if (!options.Protocols.Http1.Enabled)
            return;

        var mode = options.Protocols.Http1.Mode;

        if (string.IsNullOrEmpty(mode) ||
            !AllowedHttp1Modes.Contains(mode, StringComparer.Ordinal))
        {
            errors.Add(
                "Protocols:Http1:Mode must be 'ConnectionHandler' or 'Kestrel' " +
                $"(got '{mode}').");
        }
    }

    private static void ValidateHttp1PortResolution(
        InfraStreamOptions options,
        List<string> errors)
    {
        var http1 = options.Protocols.Http1;

        if (!http1.Enabled || http1.Port == 0)
            return;

        if (options.IngressPort > 0 && options.IngressPort != http1.Port)
        {
            errors.Add(
                $"IngressPort ({options.IngressPort}) conflicts with " +
                $"Protocols:Http1:Port ({http1.Port}). " +
                "Use one, or set them to the same value.");
        }
    }

    internal static int ResolveHttp1Port(InfraStreamOptions options)
    {
        if (options.Protocols.Http1.Port > 0)
            return options.Protocols.Http1.Port;

        if (options.IngressPort > 0)
            return options.IngressPort;

        return DefaultHttp1Port;
    }
}
