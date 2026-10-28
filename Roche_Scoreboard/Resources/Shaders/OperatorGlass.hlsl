// Original Roche operator material. Background only: foreground text is never filtered.
sampler2D source : register(s0);
float4 tint : register(c0);
float2 size : register(c1);
float radius : register(c2);
float2 light : register(c3);
float activity : register(c4);
float2 optics : register(c5);
float edgeDepth : register(c6);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // The host supplies strictly positive dimensions.
    float2 dimensions = size;
    float r = min(radius, min(dimensions.x, dimensions.y) * .5);
    float2 p = (uv - .5) * dimensions;
    float2 q = abs(p) - (dimensions * .5 - r);
    float2 corner = max(q, 0);
    float cornerLength = length(corner);
    float distance = cornerLength + min(max(q.x, q.y), 0) - r;
    float mask = saturate(.5 - distance);
    // Straight edges bend perpendicular to the edge, not diagonally. The old
    // epsilon-only normal made every flat side behave like a rounded corner.
    float2 normal = normalize((corner + step(q.yx, q.xy) * .001) * sign(p + .0001));
    float bevel = saturate(distance * edgeDepth + 1);
    float2 sampleUv = saturate(uv - normal * bevel * bevel * optics.x / dimensions);
    float3 background = tex2D(source, sampleUv).rgb;
    float3 result = lerp(background, tint.rgb, tint.a);
    float rim = saturate(1 - abs(distance + 1.3) * .9);
    float direction = saturate(-normal.x * .55 - normal.y * .8);
    float2 lightOffset = uv - light;
    float glow = saturate(1 - dot(lightOffset, lightOffset) * 3);
    result += rim * (.10 + direction * optics.y);
    result += glow * activity * .08;
    return float4(saturate(result) * mask, mask);
}
