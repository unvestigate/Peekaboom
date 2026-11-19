cbuffer SceneConstants : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 CameraPosition;
    float4 LightDirection;
    float4 Shading;
};

struct VertexInput
{
    float3 position : POSITION;
    float4 color : COLOR;
};

struct PixelInput
{
    float4 position : SV_Position;
    float4 color : COLOR;
};

PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    output.position = mul(float4(input.position, 1.0), ViewProjection);
    output.color = input.color;
    return output;
}

float4 PSMain(PixelInput input) : SV_Target
{
    return float4(input.color.rgb, 1.0);
}
