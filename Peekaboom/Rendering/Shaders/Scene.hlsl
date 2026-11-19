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
    float3 world : TEXCOORD0;
    float4 color : TEXCOORD1;
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
    output.world = world;
    output.color = input.color;
    return output;
}

float4 PSMain(PixelInput input) : SV_Target
{
    float3 toCamera = normalize(CameraPosition.xyz - input.world);
    float3 facet = cross(ddy(input.world), ddx(input.world));
    float facetLength = length(facet);
    float3 normal = facetLength > 1e-12 ? facet / facetLength : toCamera;
    if (dot(normal, toCamera) < 0.0)
    {
        normal = -normal;
    }

    float key = saturate(dot(normal, LightDirection.xyz));
    float head = saturate(dot(normal, toCamera));
    float light = 0.32 + 0.48 * key + 0.25 * head;
    float3 color = input.color.rgb * lerp(1.0, light, Shading.x) * Shading.y;
    color = lerp(color, Highlight.rgb, Shading.z);
    return float4(color, 1.0);
}
