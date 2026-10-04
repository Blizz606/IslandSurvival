Shader "IslandSurvival/Tree Dither Fade URP2D"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fade ("Visible Pixel Coverage", Range(0,1)) = 1
        _DitherScale ("Dither Screen Pixel Size", Float) = 2
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "TreeDither2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex DitherVertex
            #pragma fragment DitherFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Fade;
                float _DitherScale;
            CBUFFER_END

            Varyings DitherVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings output = CommonUnlitVertex(input);
                // Unity 6 passes SpriteRenderer tint/alpha separately from vertex color.
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            float BayerThreshold(uint2 pixel)
            {
                const float values[16] =
                {
                    0, 8, 2, 10,
                    12, 4, 14, 6,
                    3, 11, 1, 9,
                    15, 7, 13, 5
                };
                uint index = (pixel.x & 3u) + ((pixel.y & 3u) * 4u);
                // Half-step thresholds ensure 0 removes ALL pixels and 1 keeps ALL.
                return (values[index] + 0.5) / 16.0;
            }

            half4 DitherFragment(Varyings input) : SV_Target
            {
                half4 color = CommonUnlitFragment(input, input.color);
                clip(color.a - 0.001);
                uint2 pixel = (uint2)floor(input.positionCS.xy / max(_DitherScale, 1.0));
                clip(saturate(_Fade) - BayerThreshold(pixel));
                return color;
            }
            ENDHLSL
        }
    }
}
