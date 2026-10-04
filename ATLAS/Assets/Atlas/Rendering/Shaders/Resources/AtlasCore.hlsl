#ifndef ATLAS_CORE_INCLUDED
#define ATLAS_CORE_INCLUDED

// The shared look of every space (Master Plan sections 8.1 and 8.3, decision D23): the single
// per-material block, the camera-wide globals and the look functions. Included after URP's Core.hlsl
// and before AtlasWarp.hlsl, AtlasPortalClip.hlsl and AtlasGates.hlsl, which read what is declared here.

// The one per-material block. Order and types are a contract frozen at G0; MaterialPool sets every value.
CBUFFER_START(UnityPerMaterial)
    float4x4 _Anchor;         // tile, room or object anchor of the warp. Identity in this build
    float4   _BaseTint;       // rgb multiplies the palette colour (linear); a = opacity 0..1, dithered
    float4   _LineColor;      // rgb line colour (linear); a unused
    float4   _LineParams;     // x minor width, y major width (multiples of _AtlasLineWidth); z minor strength, w major strength
    float4   _LineMajor;      // xy = period of major lines in line-coordinate units (0 = none); zw unused
    float4   _PortalPlane;    // world-space plane (n.xyz, d); fragments with dot(n, p) + d < 0 are clipped
    float4   _Gate0;          // sight-line gates: (post.x, post.z, sign, unused)
    float4   _Gate1;
    float4   _Gate2;
    float4   _Gate3;
    float4   _SurfaceParams;  // x emissive gain; y fog weight 0..1; z near-fade distance in metres (0 = off); w fade weight
    float    _WarpMode;       // 0 none, 1 hyperbolic, 2 spherical
CBUFFER_END

// Camera-wide values, set by AtlasLook and AtlasPalette. One value for both eyes.
TEXTURE2D(_AtlasPalette);
SAMPLER(sampler_AtlasPalette);
float4 _HeadPosWS;        // xyz = head position (the eye midpoint), world, metres
float  _HypL;             // metres per kernel unit
float4 _AtlasFogColor;    // rgb linear; also the background at and below the horizon
float4 _AtlasFogParams;   // x = fog start in metres from the head on the floor; y = 1 / (end - start)
float4 _AtlasSkyTop;      // rgb linear: the background at the zenith
float4 _AtlasShade;       // x = darkest shade a vertex can have; y = global emissive gain
float  _AtlasLineWidth;   // line width unit, pixels of the eye texture
float  _AtlasFade;        // 1 = world visible, 0 = world black
float  _AtlasTime;        // seconds, modulo 3600

// Rows 12 to 15 of the palette (v >= 0.75) are the emissive accents.
#define ATLAS_EMISSIVE_V 0.75

// Linear rgb of the palette at uv0. The palette has no mip chain.
half3 AtlasPaletteColor(float2 uv0)
{
    return SAMPLE_TEXTURE2D_LOD(_AtlasPalette, sampler_AtlasPalette, uv0, 0.0).rgb;
}

// True when uv0 lies in the emissive block of the palette.
bool AtlasIsEmissive(float2 uv0)
{
    return uv0.y >= ATLAS_EMISSIVE_V;
}

// Coverage of the lines at the integers of coord, per axis, for a line widthPixels wide.
// coordPerPixel is fwidth(coord). A line narrower than one pixel is drawn one pixel wide and faded, and where
// the lines come closer than two pixels apart the mask settles on their average coverage, so nothing shimmers
// and both eyes agree.
float2 AtlasLineAxes(float2 coord, float2 coordPerPixel, float widthPixels)
{
    float2 safePerPixel = max(coordPerPixel, 1e-6);
    float2 distancePixels = abs(frac(coord - 0.5) - 0.5) / safePerPixel;
    float drawnWidth = max(widthPixels, 1.0);
    float2 coverage = saturate(0.5 * drawnWidth + 0.5 - distancePixels) * saturate(widthPixels / drawnWidth);
    float2 average = saturate(widthPixels * safePerPixel);
    coverage = lerp(coverage, average, saturate(safePerPixel * 2.0 - 1.0));
    // A coordinate that does not change on screen carries no line (a face generated without line coordinates).
    return coverage * step(1e-7, coordPerPixel);
}

// Coverage 0..1 of the grid and edge lines at these line coordinates. Anti-aliased with fwidth.
// lineWeight is COLOR.g. Returns 0 when both strengths in _LineParams are 0.
half AtlasLineMask(float2 lineCoord, half lineWeight)
{
    float2 minorPerPixel = fwidth(lineCoord);
    float2 minorAxes = AtlasLineAxes(lineCoord, minorPerPixel, _LineParams.x * _AtlasLineWidth);
    float minorMask = max(minorAxes.x, minorAxes.y) * _LineParams.z;

    // A period of 0 switches the major lines of that axis off.
    float2 majorOn = step(1e-6, _LineMajor.xy);
    float2 majorScale = majorOn / max(_LineMajor.xy, 1e-6);
    float2 majorAxes = AtlasLineAxes(lineCoord * majorScale, minorPerPixel * majorScale, _LineParams.y * _AtlasLineWidth) * majorOn;
    float majorMask = max(majorAxes.x, majorAxes.y) * _LineParams.w;

    return half(max(minorMask, majorMask) * lineWeight);
}

// Fog amount 0..1 from the horizontal distance between positionWS and _HeadPosWS, scaled by _SurfaceParams.y.
// Height does not count, so a copy of the room above the walker stays visible (Master Plan section 8.5).
half AtlasFogFactor(float3 positionWS)
{
    float floorDistance = length(positionWS.xz - _HeadPosWS.xz);
    return half(saturate((floorDistance - _AtlasFogParams.x) * _AtlasFogParams.y) * _SurfaceParams.y);
}

// Ordered dither: clips the fragment when opacity is below a 4 x 4 Bayer threshold at this pixel.
// Opacity 1 is never clipped and opacity 0 always is.
void AtlasDitherClip(float4 positionCS, half opacity)
{
    static const float thresholds[16] =
    {
         0.5 / 16.0,  8.5 / 16.0,  2.5 / 16.0, 10.5 / 16.0,
        12.5 / 16.0,  4.5 / 16.0, 14.5 / 16.0,  6.5 / 16.0,
         3.5 / 16.0, 11.5 / 16.0,  1.5 / 16.0,  9.5 / 16.0,
        15.5 / 16.0,  7.5 / 16.0, 13.5 / 16.0,  5.5 / 16.0
    };
    uint column = uint(positionCS.x) % 4u;
    uint row = uint(positionCS.y) % 4u;
    clip(opacity - thresholds[row * 4u + column]);
}

// The background gradient for a view direction (unit, world): _AtlasFogColor at and below the horizon,
// _AtlasSkyTop at the zenith, both scaled by _AtlasFade.
half3 AtlasBackground(float3 viewDirWS)
{
    float rise = smoothstep(0.0, 0.7, viewDirWS.y);
    return half3(lerp(_AtlasFogColor.rgb, _AtlasSkyTop.rgb, rise) * _AtlasFade);
}

// The whole look for one fragment. Returns the final colour, alpha 1.
//   positionWS  world position, metres          uv0          palette coordinate
//   lineCoord   TEXCOORD1                       vertexColor  COLOR (r shade, g line weight)
//   positionCS  SV_POSITION, for the dither
half4 AtlasShade(float3 positionWS, float2 uv0, float2 lineCoord, half4 vertexColor, float4 positionCS)
{
    // Derivatives are taken before the clip so that they are defined for every fragment of the quad.
    half lineMask = AtlasLineMask(lineCoord, vertexColor.g);

    float opacity = _BaseTint.a;
    float nearFade = saturate(distance(positionWS, _WorldSpaceCameraPos.xyz) / max(_SurfaceParams.z, 1e-4));
    opacity *= lerp(1.0, nearFade, step(1e-4, _SurfaceParams.z));
    AtlasDitherClip(positionCS, half(opacity));

    float3 color = AtlasPaletteColor(uv0) * _BaseTint.rgb;

    // Emissive accents carry no shade and no lines; everything else takes the baked shade, then the lines.
    float3 emissive = color * (_SurfaceParams.x * _AtlasShade.y);
    float3 shaded = color * lerp(_AtlasShade.x, 1.0, vertexColor.r);
    shaded = lerp(shaded, _LineColor.rgb, lineMask);
    color = AtlasIsEmissive(uv0) ? emissive : shaded;

    // Everything fades to the one fog colour, which is also the horizon of the background.
    color = lerp(color, _AtlasFogColor.rgb, AtlasFogFactor(positionWS));
    color *= lerp(1.0, _AtlasFade, _SurfaceParams.w);
    return half4(color, 1.0);
}

#endif
