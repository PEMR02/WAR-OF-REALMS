Shader "Unlit/OutlineCullFront"
{
    // Dibuja solo las caras traseras (Cull Front) para usarse como borde/outline
    // en un mesh ligeramente escalado. Usado por FadeableByCamera y SelectableOutline.
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.5, 0.2, 0.7)
        _MainTex ("MainTex", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.33
        _UseTextureAlpha ("Use Texture Alpha", Float) = 0
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
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            float _UseTextureAlpha;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = _Color;
                if (_UseTextureAlpha > 0.5)
                {
                    fixed a = tex2D(_MainTex, i.uv).a;
                    clip(a - _Cutoff);
                    c.a *= a;
                }
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
