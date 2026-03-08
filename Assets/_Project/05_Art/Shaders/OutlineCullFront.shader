Shader "Unlit/OutlineCullFront"
{
    // Dibuja solo las caras traseras (Cull Front) para usarse como borde/outline
    // en un mesh ligeramente escalado. Usado por FadeableByCamera y SelectableOutline.
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.5, 0.2, 0.7)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+1" "RenderType" = "Transparent" }
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
    Fallback Off
}
