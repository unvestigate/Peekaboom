cbuffer SceneConstants : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 CameraPosition;
    float4 LightDirection;
    float4 Shading;
    float4 Highlight;
};

struct VertexInput
{
    float4 position : POSITION;
    float4 world0 : TEXCOORD0;
    float4 world1 : TEXCOORD1;
    float4 world2 : TEXCOORD2;
    float4 color : TEXCOORD3;
    float4 shape : TEXCOORD4;
};

struct PixelInput
{
    float4 position : SV_Position;
    nointerpolation uint id : TEXCOORD0;
    float3 world : TEXCOORD1;
};

struct PixelOutput
{
    uint id : SV_Target0;
    float4 world : SV_Target1;
};

PixelInput VSMain(VertexInput input)
{
    float3 local = input.position.xyz;
    if (input.shape.z > 0.5)
    {
        local = local * input.shape.x + float3(input.position.w * input.shape.y, 0.0, 0.0);
    }

    float4 p = float4(local, 1.0);
    float3 world = float3(dot(input.world0, p), dot(input.world1, p), dot(input.world2, p));

    PixelInput output;
    output.position = mul(float4(world, 1.0), ViewProjection);
    output.id = (uint)(input.shape.w + 0.5);
    output.world = world;
    return output;
}

PixelOutput PSMain(PixelInput input)
{
    PixelOutput output;
    output.id = input.id;
    output.world = float4(input.world, 1.0);
    return output;
}
