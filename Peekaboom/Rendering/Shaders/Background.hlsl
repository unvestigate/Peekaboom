static const float3 TOP_COLOR = float3(0.235, 0.247, 0.267);
static const float3 BOTTOM_COLOR = float3(0.118, 0.122, 0.133);

struct VertexOutput
{
    float4 position : SV_Position;
    float height : TEXCOORD0;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);

    VertexOutput output;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.height = uv.y;
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    return float4(lerp(TOP_COLOR, BOTTOM_COLOR, saturate(input.height)), 1.0);
}
