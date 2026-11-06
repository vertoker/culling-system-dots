using System;
using System.IO;
using CullingSystemDOTS.Dynamic.Gpu;
using CullingSystemDOTS.Samples.DynamicCity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CullingSystemDOTS.Delivery.Editor
{
    public static class DynamicCityFixtureBuilder
    {
        public static string Create(string folder)
        {
            string absolute=Path.GetFullPath(folder),assets=Path.GetFullPath(Application.dataPath)+Path.DirectorySeparatorChar;
            if(!folder.StartsWith("Assets/",StringComparison.Ordinal) || !absolute.StartsWith(assets,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use a sample destination inside Assets.");
            string content=folder+"/Content",path=folder+"/DynamicCity.unity";
            if(Directory.Exists(content) || File.Exists(path))throw new InvalidOperationException("Refusing to overwrite existing sample content.");
            var host=SceneManager.GetActiveScene();Scene owned=default;
            try
            {
                owned=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                Directory.CreateDirectory(content);AssetDatabase.Refresh();
                var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);Mesh mesh;
                try{mesh=UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);}finally{UnityEngine.Object.DestroyImmediate(primitive);}
                mesh.name="Dynamic City cube";AssetDatabase.CreateAsset(mesh,content+"/Cube.asset");
                var shader=Shader.Find("CullingSystemDOTS/Samples/Dynamic City Ordinary");if(shader==null || !shader.isSupported)throw new InvalidOperationException("Compile the sample ordinary shader first.");
                var palette=new Material[16];
                for(int i=0;i<palette.Length;i++)
                {
                    Color color=Color.HSVToRGB(i/16f,.6f,.85f);var material=new Material(shader){name="City color "+i,enableInstancing=true};
                    material.SetVector("_BaseColor",new Vector4(color.r,color.g,color.b,1));AssetDatabase.CreateAsset(material,content+"/Color"+i+".mat");palette[i]=material;
                }
                var resources=AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
                var indirect=new Material(resources.Indirect){name="City indirect"};AssetDatabase.CreateAsset(indirect,content+"/Indirect.mat");
                var ground=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="City shadow receiver"};ground.SetColor("_BaseColor",new Color(.35f,.35f,.35f));AssetDatabase.CreateAsset(ground,content+"/Ground.mat");
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,content+"/DynamicCityRenderer.asset");
                var pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.msaaSampleCount=1;pipeline.renderScale=1;pipeline.shadowDistance=160;
                AssetDatabase.CreateAsset(pipeline,content+"/DynamicCityPipeline.asset");
                var root=new GameObject("Dynamic City");var sample=root.AddComponent<DynamicCityBootstrap>();sample.CubeMesh=mesh;sample.ShaderResources=resources;
                sample.StockPalette=palette;sample.IndirectMaterial=indirect;sample.GroundMaterial=ground;
                if(!EditorSceneManager.SaveScene(owned,path))throw new IOException("Could not save the owned Dynamic City scene.");
                return path;
            }
            finally
            {
                if(owned.IsValid() && owned.isLoaded)EditorSceneManager.CloseScene(owned,true);
                if(host.IsValid() && host.isLoaded)SceneManager.SetActiveScene(host);
            }
        }
    }
}
