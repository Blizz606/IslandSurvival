Shader "IslandSurvival/NightVisionMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Center ("Player Screen Position", Vector) = (0.5, 0.5, 0, 0)
        _Aspect ("Screen Aspect", Float) = 1.7778
        _Radius ("Visible Radius", Float) = 0.14
        _Feather ("Edge Softness", Float) = 0.025
        _InnerDarkness ("Inner Darkness", Range(0,1)) = 0.12
        _PixelSize ("Edge Pixel Size", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            float4 _Center;
            float _Aspect;
            float _Radius;
            float _Feather;
            float _InnerDarkness;
            float _PixelSize;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // Snap the mask to a small screen-pixel grid to match the game art.
                float2 pixelUV = (floor(i.uv * _ScreenParams.xy / _PixelSize) + 0.5) * _PixelSize / _ScreenParams.xy;
                float2 offset = pixelUV - _Center.xy;
                offset.x *= _Aspect;
                float distanceFromPlayer = length(offset);
                float falloff = smoothstep(_Radius * 0.18, _Radius + max(_Feather, 0.0001), distanceFromPlayer);
                float darkness = lerp(_InnerDarkness, 1.0, falloff);
                return fixed4(0, 0, 0, darkness * i.color.a);
            }
            ENDCG
        }
    }
}
