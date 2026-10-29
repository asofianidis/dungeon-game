using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Depthbound.EditorTools
{
    /// <summary>Creates and assigns the project's Universal Render Pipeline asset.</summary>
    public static class UrpSetup
    {
        const string Folder = "Assets/Settings";
        const string RendererPath = Folder + "/Depthbound_Renderer.asset";
        const string AssetPath = Folder + "/Depthbound_URP.asset";

        public static bool IsConfigured => GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset;

        [MenuItem("Depthbound/Configure URP")]
        public static void Configure()
        {
            Directory.CreateDirectory(Folder);
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            // Moonlight shadows over a large view, plus enough per-pixel lights for the torch,
            // braziers, portals and glowing projectiles that can overlap one object.
            asset.shadowDistance = 35f;
            asset.maxAdditionalLightsCount = 8;
            var so = new SerializedObject(asset);
            void SetInt(string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Debug.LogWarning("URP setup: missing " + name); }
            void SetBool(string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning("URP setup: missing " + name); }
            SetBool("m_MainLightShadowsSupported", true);
            SetInt("m_MainLightShadowmapResolution", 2048);
            SetInt("m_AdditionalLightsRenderingMode", (int)LightRenderingMode.PerPixel);
            SetInt("m_AdditionalLightsPerObjectLimit", 8);
            SetBool("m_SoftShadowsSupported", true);
            SetInt("m_ShadowCascadeCount", 2);
            so.ApplyModifiedPropertiesWithoutUndo();

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);

            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            Debug.Log("Depthbound: URP configured (" + AssetPath + ")");
        }
    }
}
