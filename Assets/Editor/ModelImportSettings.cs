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
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.optimizeGameObjects = false;
            importer.importNormals = ModelImporterNormals.Import;
        }
    }
}
