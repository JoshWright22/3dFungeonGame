// Two-sided version of the standard opaque surface, for dungeon pieces that are seen from both
// directions - doorways above all, since walking through one is the entire point of it.
//
// The Synty kit meshes only have faces on one side, and stacking two mirrored copies cannot fix
// that: each copy is a slab with thickness, so one copy's body always ends up between the viewer
// and the other copy's textured face. Rendering both faces of a single piece is the fix.
//
// Built-in pipeline: this project has no render pipeline asset assigned, so URP's Lit shader
// (which has a Render Face toggle) renders magenta here.
Shader "Delver/Dungeon Two Sided"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.0
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        // The whole point of this shader.
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            // Positive on front faces, negative on back faces.
            float facing : VFACE;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;

            // Only back faces are touched. They inherit the front face's normal, which points
            // away from the viewer and would light them as though they were in shadow, so flip
            // it. Front faces are left alone - overriding their normal too flattens the mesh's
            // own shading and the stonework loses all its relief.
            if (IN.facing < 0) o.Normal = float3(0, 0, -1);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
