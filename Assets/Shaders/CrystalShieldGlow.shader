Shader "VotanicBow/CrystalShieldGlow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.35, 1, 1)
        _OutlineWidth ("Outline Width", Float) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry+1"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }

        // Solid hull outline — no transparency (reliable in CAVE / stereo).
        Pass
        {
            Name "SolidOutline"
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero
            ColorMask RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Color;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 n = normalize(v.normal);
                float4 vtx = v.vertex;
                vtx.xyz += n * _OutlineWidth;
                o.pos = UnityObjectToClipPos(vtx);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Color"
}
