// FigmaImageShader — UGUI shader for FigmaImage component.
// Supports: solid fill, linear/radial gradient (up to 16 stops),
// SDF shapes (rounded rect, ellipse, star), stroke, arc range.
// Built-in Render Pipeline only.
Shader "Figma/FigmaImageShader"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _FillColor ("Fill Color", Color) = (1,1,1,1)
        _StrokeColor ("Stroke Color", Color) = (0,0,0,1)
        _StrokeWidth ("Stroke Width", Float) = 0
        _CornerRadius ("Corner Radius (TR,BR,TL,BL)", Vector) = (0,0,0,0)
        _ArcAngleRangeInnerRadius ("Arc Angle Range + Inner Radius", Vector) = (0,6.2832,0,0)

        _GradientNumStops ("Gradient Stop Count", Float) = 0

        // UGUI stencil / masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil     ("Stencil ID", Float) = 0
        _StencilOp   ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float) = 255
        _ColorMask   ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ LINEAR_GRADIENT RADIAL_GRADIENT
            #pragma multi_compile_local _ STROKE
            #pragma multi_compile_local _ SHAPE_RECTANGLE SHAPE_ELLIPSE SHAPE_STAR
            #pragma multi_compile_local _ ARC_ANGLE_RANGE
            #pragma multi_compile_local _ CLAMP_TEXTURE

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            // ─── Uniforms ─────────────────────────────────
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _FillColor;
            fixed4 _StrokeColor;
            float _StrokeWidth;
            float4 _CornerRadius; // TR, BR, TL, BL
            float4 _ArcAngleRangeInnerRadius; // x=startAngle, y=endAngle, z=innerRadius

            #define MAX_STOPS 16
            fixed4 _GradientColors[MAX_STOPS];
            float _GradientStops[MAX_STOPS];
            float _GradientNumStops;
            float _GradientHandlePositions[6]; // 3 Vector2s flattened

            float4 _ClipRect;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = v.vertex;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            // ─── Gradient Sampling ────────────────────────
            fixed4 sampleGradient(float t)
            {
                t = saturate(t);
                int numStops = (int)_GradientNumStops;
                if (numStops <= 0) return _FillColor;
                if (numStops == 1) return _GradientColors[0];

                fixed4 col = _GradientColors[0];
                for (int i = 1; i < numStops && i < MAX_STOPS; i++)
                {
                    float prev = _GradientStops[i - 1];
                    float curr = _GradientStops[i];
                    float range = max(curr - prev, 0.0001);
                    float blend = saturate((t - prev) / range);
                    col = lerp(col, _GradientColors[i], blend);
                }
                return col;
            }

            // ─── SDF Functions (Inigo Quilez) ─────────────
            // Rounded box SDF
            float sdRoundedBox(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x = (p.y > 0.0) ? r.x : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            // Ellipse SDF
            float sdEllipse(float2 p, float2 ab)
            {
                float2 pa = abs(p);
                if (pa.x > pa.y) { pa = pa.yx; ab = ab.yx; }
                float l = ab.y * ab.y - ab.x * ab.x;
                float m = ab.x * pa.x / l;
                float n = ab.y * pa.y / l;
                float m2 = m * m;
                float n2 = n * n;
                float c = (m2 + n2 - 1.0) / 3.0;
                float c3 = c * c * c;
                float d = c3 + m2 * n2;
                float q = d + m2 * n2;
                float g = m + m * n2;
                float co;
                if (d < 0.0)
                {
                    float h = acos(q / c3) / 3.0;
                    float s = cos(h) + 2.0;
                    float t2 = sign(l) * sqrt(abs(s)) * sqrt(abs(-c));
                    float rx = sqrt(abs(m2 - c * (s + 2.0 * t2)));
                    co = (rx + 2.0 * g / (3.0 * rx + 0.0001)) * 0.5;
                }
                else
                {
                    float h = 2.0 * m * n * sqrt(abs(d));
                    float s2 = sign(q + h) * pow(abs(q + h), 1.0 / 3.0);
                    float u = sign(q - h) * pow(abs(q - h), 1.0 / 3.0);
                    float rx = abs(s2 + u) * 0.5 - m;
                    co = (rx + 2.0 * g / max(3.0 * rx, 0.0001)) * 0.5;
                }
                co = clamp(co, 0.0, 1.0);
                float2 r2 = ab * float2(co, sqrt(max(1.0 - co * co, 0.0)));
                return length(r2 - pa) * sign(pa.y - r2.y);
            }

            // 5-point star SDF
            float sdStar5(float2 p, float r, float rf)
            {
                const float2 k1 = float2(0.809016994, -0.587785252);
                const float2 k2 = float2(-k1.x, k1.y);
                p.x = abs(p.x);
                p -= 2.0 * max(dot(k1, p), 0.0) * k1;
                p -= 2.0 * max(dot(k2, p), 0.0) * k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0, 1);
                float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
                return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
            }

            // ─── Fragment ─────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 texColor = tex2D(_MainTex, uv);

                // ── Fill color ──
                fixed4 fillColor = _FillColor;

                #if defined(LINEAR_GRADIENT)
                {
                    float2 p0 = float2(_GradientHandlePositions[0], _GradientHandlePositions[1]);
                    float2 p1 = float2(_GradientHandlePositions[2], _GradientHandlePositions[3]);
                    float2 axis = p1 - p0;
                    float len2 = dot(axis, axis);
                    float t = (len2 > 0.0001) ? dot(uv - p0, axis) / len2 : 0.0;
                    fillColor = sampleGradient(t);
                }
                #elif defined(RADIAL_GRADIENT)
                {
                    float2 center = float2(_GradientHandlePositions[0], _GradientHandlePositions[1]);
                    float2 edge = float2(_GradientHandlePositions[2], _GradientHandlePositions[3]);
                    float radius = length(edge - center);
                    float t = (radius > 0.0001) ? length(uv - center) / radius : 0.0;
                    fillColor = sampleGradient(t);
                }
                #endif

                // ── Texture blend ──
                #if defined(CLAMP_TEXTURE)
                    float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                    texColor = lerp(fixed4(0,0,0,0), texColor, inside);
                #endif

                fixed4 baseColor = texColor * fillColor;

                // ── SDF shape masking ──
                #if defined(SHAPE_RECTANGLE) || defined(SHAPE_ELLIPSE) || defined(SHAPE_STAR)
                {
                    // Map UV to centered coordinates (-halfSize to +halfSize)
                    float2 size = float2(1.0, 1.0); // normalized
                    float2 halfSize = size * 0.5;
                    float2 p = uv - halfSize;

                    float dist = 0;

                    #if defined(SHAPE_RECTANGLE)
                        // Corner radius already normalized 0-1 from C#, scale to half-size
                        float4 cr = _CornerRadius * min(halfSize.x, halfSize.y);
                        dist = sdRoundedBox(p, halfSize, cr);
                    #elif defined(SHAPE_ELLIPSE)
                        #if defined(ARC_ANGLE_RANGE)
                            // Arc: use angle range
                            float angle = atan2(p.y, p.x);
                            float startAngle = _ArcAngleRangeInnerRadius.x;
                            float endAngle = _ArcAngleRangeInnerRadius.y;
                            float innerRadius = _ArcAngleRangeInnerRadius.z;
                            float r = length(p / halfSize);
                            float inArc = step(startAngle, angle) * step(angle, endAngle);
                            float inRing = step(innerRadius, r) * step(r, 1.0);
                            dist = (inArc * inRing > 0.5) ? -0.01 : 0.01;
                        #else
                            dist = sdEllipse(p, halfSize);
                        #endif
                    #elif defined(SHAPE_STAR)
                        dist = sdStar5(p, halfSize.x, 0.4);
                    #endif

                    // Anti-alias edge
                    float pixelSize = fwidth(dist);
                    float shapeMask = 1.0 - smoothstep(-pixelSize, pixelSize, dist);

                    #if defined(STROKE)
                        float strokeOuter = dist;
                        float strokeInner = dist + _StrokeWidth * 0.01;
                        float strokeMask = smoothstep(-pixelSize, pixelSize, -strokeOuter)
                                         * smoothstep(-pixelSize, pixelSize, strokeInner);
                        baseColor = lerp(baseColor, fixed4(_StrokeColor.rgb, 1), strokeMask * _StrokeColor.a);
                    #endif

                    baseColor.a *= shapeMask;
                }
                #endif

                // ── Final output ──
                baseColor *= i.color;

                // UGUI clip rect
                baseColor.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);

                clip(baseColor.a - 0.001);
                return baseColor;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
