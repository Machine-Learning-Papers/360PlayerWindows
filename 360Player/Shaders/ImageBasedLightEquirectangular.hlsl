﻿//reuse//code//ApplyAnEffectFxFile//
uniform extern float4x4 WorldViewProj : WORLDVIEWPROJECTION;
extern float gammaFactor;

/////////////
// GLOBALS //
/////////////
//matrix worldMatrix;
//matrix viewMatrix;
//matrix projectionMatrix;

//float4 AmbientColor = float4(1, 1, 1, 1);

//////////////
// TYPEDEFS //
//////////////
struct VertexInputType
{
    float4 Position : SV_Position;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
};

struct PixelInputType
{
    float4 position : SV_POSITION;
    //float4 color : COLOR;
    float2 TexCoord : TEXCOORD0;
	float3 Normal : NORMAL;
};

Texture2D<float4> UserTex : register(t0);
SamplerState UserTexSampler : register(s0);


////////////////////////////////////////////////////////////////////////////////
// Vertex Shader
////////////////////////////////////////////////////////////////////////////////
PixelInputType ColorVertexShader(VertexInputType input)
{
    PixelInputType output;
    
    
    // Change the position vector to be 4 units for proper matrix calculations.
    input.Position.w = 1.0f;

    // Calculate the position of the vertex against the world, view, and projection matrices.
    //output.position = mul(input.position, worldMatrix);
    //output.position = mul(output.position, viewMatrix);
    //output.position = mul(output.position, projectionMatrix);
    output.position = mul(input.Position, WorldViewProj);
    
    // Store the input color for the pixel shader to use.
    //output.color = input.color;
    //output.color = input.Textoord;
    output.TexCoord = input.TexCoord;

	output.Normal = input.Normal;

    return output;
}
//
//// http://gamedev.stackexchange.com/a/32688/44395
//float2 rand_2_10(in float2 uv)
//{
//    float noiseX = (frac(sin(dot(uv, float2(12.9898, 78.233) * 2.0)) * 43758.5453));
//    float noiseY = sqrt(1 - noiseX * noiseX);
//    return float2(noiseX, noiseY);
//}
//float rand_1_05(in float2 uv)
//{
//    float2 noise = (frac(sin(dot(uv, float2(12.9898, 78.233) * 2.0)) * 43758.5453));
//    return abs(noise.x + noise.y) * 0.5;

//}


///aaaa

float Pixels[13] =
{
    -6,
   -5,
   -4,
   -3,
   -2,
   -1,
    0,
    1,
    2,
    3,
    4,
    5,
    6,
};

float BlurWeights[13] =
{
    0.002216,
   0.008764,
   0.026995,
   0.064759,
   0.120985,
   0.176033,
   0.199471,
   0.176033,
   0.120985,
   0.064759,
   0.026995,
   0.008764,
   0.002216,
};

#define M1_2PI 0.15915494309
#define MPI 3.14
////////////////////////////////////////////////////////////////////////////////
// Pixel Shader
////////////////////////////////////////////////////////////////////////////////
float4 ColorPixelShader(PixelInputType input) : SV_Target
{

	//return float4(2,1,1,1) - float4(input.Normal, 1);

	float3 normal = input.Normal;
	//return float4(normal.y, normal.y, normal.y, 1);

	float pitch = (normal.y-1)/2;
	float yaw = (atan2(normal.x, -normal.z) + MPI) * M1_2PI;

	float imageBasedLight = saturate(normal.y);
	//if (imageBasedLight > .8) imageBasedLight = 1;
	//else imageBasedLight = 0;

	float2 TexCoord; // = input.Normal.xy;
	TexCoord.y = -pitch;
	TexCoord.x = yaw;

	//return float4(imageBasedLight, imageBasedLight, imageBasedLight, 1);

    return pow(UserTex.Sample(UserTexSampler, TexCoord), gammaFactor) ;
}



////////////////////////////////////////////////////////////////////////////////
// Technique
////////////////////////////////////////////////////////////////////////////////
technique10 ColorTechnique
{
    pass pass0
    {
        SetVertexShader(CompileShader(vs_4_0, ColorVertexShader()));
        SetPixelShader(CompileShader(ps_4_0, ColorPixelShader()));
        SetGeometryShader(NULL);
    }
}
