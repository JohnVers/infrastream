using InfraStream.CollectorEngine.Configuration;

namespace InfraStream.CollectorEngine.Ingress.Protocols;

/// <summary>
/// Selects the single ingress protocol strategy enabled by the configuration.
/// </summary>
/// <remarks>
/// <see cref="IngressOptionsValidator"/> guarantees that exactly one
/// protocol is enabled. This selector assumes the options are valid and
/// throws only on programmer errors (unknown <c>Mode</c>, unreachable
/// branches).
/// </remarks>
public static class IngressProtocolSelector
{
    /// <summary>
    /// Returns the strategy for the single enabled protocol.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the options are inconsistent (should not happen after
    /// validation).
    /// </exception>
    public static IIngressProtocol Select(InfraStreamOptions options)
    {
        var protocol = options.Protocols;

        if (protocol.Http1.Enabled)
        {
            return protocol.Http1.Mode switch
            {
                "ConnectionHandler" => new Http1ConnectionHandlerProtocol(),
                "Kestrel" => new Http1KestrelProtocol(),
                _ => throw new InvalidOperationException(
                    $"Unknown Protocols:Http1:Mode '{protocol.Http1.Mode}'. " +
                    "Validation should have rejected this.")
            };
        }

        if (protocol.Http2.Enabled)
            return new Http2KestrelProtocol();

        if (protocol.Grpc.Enabled)
            return new GrpcIngressProtocol();

        throw new InvalidOperationException(
            "No ingress protocol enabled. Validation should have rejected this.");
    }
}
