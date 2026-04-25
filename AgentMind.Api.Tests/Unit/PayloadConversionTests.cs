using AgentMind.Api.Services;
using Qdrant.Client.Grpc;

namespace AgentMind.Api.Tests.Unit;

public class PayloadConversionTests
{
    [Fact]
    public void ConvertToQdrantValue_String_MapsToStringValue()
    {
        var result = VectorService.ConvertToQdrantValue("test");
        Assert.Equal("test", result.StringValue);
    }

    [Fact]
    public void ConvertToQdrantValue_Int_MapsToIntegerValue()
    {
        var result = VectorService.ConvertToQdrantValue(42);
        Assert.Equal(42, result.IntegerValue);
    }

    [Fact]
    public void ConvertToQdrantValue_Long_MapsToIntegerValue()
    {
        var result = VectorService.ConvertToQdrantValue(123456789L);
        Assert.Equal(123456789L, result.IntegerValue);
    }

    [Fact]
    public void ConvertToQdrantValue_Bool_MapsToBoolValue()
    {
        var result = VectorService.ConvertToQdrantValue(true);
        Assert.True(result.BoolValue);
    }

    [Fact]
    public void ConvertToQdrantValue_Double_MapsToDoubleValue()
    {
        var result = VectorService.ConvertToQdrantValue(3.14);
        Assert.Equal(3.14, result.DoubleValue);
    }

    [Fact]
    public void ConvertToQdrantValue_Float_MapsToDoubleValue()
    {
        var result = VectorService.ConvertToQdrantValue(2.5f);
        Assert.Equal(2.5, result.DoubleValue, precision: 5);
    }

    [Fact]
    public void ConvertToQdrantValue_DateTime_MapsToEpochInteger()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var result = VectorService.ConvertToQdrantValue(dt);
        var expectedEpoch = new DateTimeOffset(dt).ToUnixTimeSeconds();
        Assert.Equal(expectedEpoch, result.IntegerValue);
    }

    [Fact]
    public void ConvertToQdrantValue_DateTimeOffset_MapsToEpochInteger()
    {
        var dto = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var result = VectorService.ConvertToQdrantValue(dto);
        Assert.Equal(dto.ToUnixTimeSeconds(), result.IntegerValue);
    }

    [Fact]
    public void ConvertToQdrantValue_Null_ReturnsEmptyValue()
    {
        var result = VectorService.ConvertToQdrantValue(null);
        Assert.NotNull(result);
        Assert.Equal(Value.KindOneofCase.None, result.KindCase);
    }

    [Fact]
    public void ConvertFromQdrantValue_IntegerValue_ReturnsLong()
    {
        var value = new Value { IntegerValue = 100 };
        var result = VectorService.ConvertFromQdrantValue(value);
        Assert.Equal(100L, result);
    }

    [Fact]
    public void ConvertFromQdrantValue_DoubleValue_ReturnsDouble()
    {
        var value = new Value { DoubleValue = 1.5 };
        var result = VectorService.ConvertFromQdrantValue(value);
        Assert.Equal(1.5, result);
    }

    [Fact]
    public void ConvertFromQdrantValue_StringValue_ReturnsString()
    {
        var value = new Value { StringValue = "hello" };
        var result = VectorService.ConvertFromQdrantValue(value);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ConvertFromQdrantValue_BoolValue_ReturnsBool()
    {
        var value = new Value { BoolValue = true };
        var result = VectorService.ConvertFromQdrantValue(value);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConvertPayloadToQdrantValues_MixedTypes_ConvertsAll()
    {
        var payload = new Dictionary<string, object>
        {
            ["name"] = "test",
            ["count"] = 5,
            ["active"] = true,
            ["score"] = 9.5,
            ["timestamp"] = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = VectorService.ConvertPayloadToQdrantValues(payload);

        Assert.Equal(5, result.Count);
        Assert.Equal("test", result["name"].StringValue);
        Assert.Equal(5, result["count"].IntegerValue);
        Assert.True(result["active"].BoolValue);
        Assert.Equal(9.5, result["score"].DoubleValue);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), result["timestamp"].IntegerValue);
    }
}
