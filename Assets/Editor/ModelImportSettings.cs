using UnityEditor;

namespace Depthbound.EditorTools
{
    /// <summary>
    /// Import rules for the Blender-built models: bake Blender's Z-up axis conversion into the
    /// transforms (so bones and parts have clean orientations) and keep skeletons for skinning.
    /// </summary>
    public class ModelImportSettings : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Models/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.optimizeGameObjects = false;
            importer.importNormals = ModelImporterNormals.Import;
        }

        /// <summary>Blender takes arrive as "Armature|Walk"; name them "Walk" and set loop modes.</summary>
        void OnPreprocessAnimation()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Models/")) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                bool loop = n == "Idle" || n == "Walk";
                c.loopTime = loop;
                c.wrapMode = loop ? UnityEngine.WrapMode.Loop : n == "Death" ? UnityEngine.WrapMode.ClampForever : UnityEngine.WrapMode.Once;
            }
            importer.clipAnimations = clips;
        }
    }
}
