using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Queues;
using Xunit;

namespace CollectorEngine.Tests.Queues;

public sealed class TelemetryBatchPayloadTests
{
    [Fact]
    public void Defaults_AreIdentityAndEmpty()
    {
        var payload = TelemetryBatchPool.Rent(16);

        try
        {
            Assert.Equal(ContentEncoding.Identity, payload.ContentEncoding);
            Assert.Equal(16, payload.Length);
            Assert.Empty(payload.NodeId);
            Assert.Empty(payload.Environment);
        }
        finally
        {
            payload.Dispose();
        }
    }

    [Fact]
    public void Properties_CanBeSetAndRead()
    {
        var payload = TelemetryBatchPool.Rent(16);
        try
        {
            payload.NodeId = "node-1";
            payload.Environment = "prod";
            payload.ContentEncoding = ContentEncoding.Brotli;

            Assert.Equal("node-1", payload.NodeId);
            Assert.Equal("prod", payload.Environment);
            Assert.Equal(ContentEncoding.Brotli, payload.ContentEncoding);
        }
        finally
        {
            payload.Dispose();
        }
    }

    [Fact]
    public void Dispose_ResetsState()
    {
        var payload = TelemetryBatchPool.Rent(16);
        payload.NodeId = "node-1";
        payload.Environment = "prod";
        payload.ContentEncoding = ContentEncoding.Gzip;

        // Rent again — the pool may return the same instance.
        // We cannot guarantee identity, but the new instance must be reset.
        payload.Dispose();

        var second = TelemetryBatchPool.Rent(16);
        try
        {
            Assert.Equal(ContentEncoding.Identity, second.ContentEncoding);
            Assert.Empty(second.NodeId);
            Assert.Empty(second.Environment);
        }
        finally
        {
            second.Dispose();
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var payload = TelemetryBatchPool.Rent(16);
        payload.Dispose();
        payload.Dispose(); // must not throw
    }

    [Fact]
    public void Span_And_Memory_HaveRequestedLength()
    {
        var payload = TelemetryBatchPool.Rent(32);
        try
        {
            Assert.Equal(32, payload.Span.Length);
            Assert.Equal(32, payload.Memory.Length);
            Assert.Equal(32, payload.WritableSpan.Length);
            Assert.True(payload.Array.Length >= 32);
        }
        finally
        {
            payload.Dispose();
        }
    }
}
