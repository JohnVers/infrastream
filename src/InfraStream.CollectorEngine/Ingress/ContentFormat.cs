namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Wire format of the payload bytes.
/// </summary>
public enum ContentFormat
{
    /// <summary>UTF-8 JSON (HTTP ingress).</summary>
    Json = 0,

    /// <summary>Protobuf (gRPC ingress).</summary>
    Protobuf = 1,
}
