Shader "Unlit/GridAlwaysOnTop"
{
    // Grilla que se dibuja siempre encima del terreno (ZTest Always).
    // Asigna este shader a un material y úsalo en RTS Map Generator → Grid Line Material Override.
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.06)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
    Fallback Off
}
