using System;
using System.IO;
using System.Linq;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Editor.Baking;
using CullingSystemDOTS.Sample;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace CullingSystemDOTS.Delivery.Editor
{
    public static class DeliveryFixtureBuilder
    {
        public static string CreateSample(string folder)
        {
            string path = Path.GetFullPath(folder),assets = Path.GetFullPath(Application.dataPath)+Path.DirectorySeparatorChar;
            if (!folder.StartsWith("Assets/",StringComparison.Ordinal) || !path.StartsWith(assets,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A new folder inside this project's Assets is required.");
            if (Directory.Exists(path) || AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Refusing to overwrite an existing sample folder.");
            var host = SceneManager.GetActiveScene(); Scene geometry = default,main = default; bool success = false;
            try
            {
                CreateFolder(folder);
                var red = Material(folder,"Red",new Color(.9f,.03f,.01f)); var accent = Material(folder,"Accent",new Color(.8f,.2f,.01f));
                var blue = Material(folder,"Blue",new Color(.02f,.08f,.9f)); var gray = Material(folder,"Wall",new Color(.4f,.4f,.4f));
                geometry = CreateOwnedScene(folder+"/Geometry.unity");
                var volume = new GameObject("Two rooms volume").AddComponent<CullingVolumeAuthoring>(); volume.Size = new Vector3(8,4,8);
                var wall = Cube("Partition",Vector3.zero,new Vector3(.1f,4,8),gray);
                Group(volume,0,wall.GetComponent<Renderer>());
                var lodRoot = new GameObject("Left LOD and materials"); lodRoot.transform.position = new Vector3(-2,0,1);
                var first = Cube("LOD0",Vector3.zero,Vector3.one,red); first.transform.SetParent(lodRoot.transform,false);
                var second = Cube("LOD1",Vector3.zero,Vector3.one*.8f,red); second.transform.SetParent(lodRoot.transform,false);
                var source = first.GetComponent<MeshFilter>().sharedMesh; var multi = Object.Instantiate(source); multi.name = "Two material cube";
                int[] triangles = source.triangles; multi.subMeshCount = 2; multi.SetTriangles(triangles.Take(triangles.Length/2).ToArray(),0); multi.SetTriangles(triangles.Skip(triangles.Length/2).ToArray(),1);
                AssetDatabase.CreateAsset(multi,folder+"/TwoMaterials.asset");
                foreach (var cube in new[] {first,second}) { cube.GetComponent<MeshFilter>().sharedMesh = multi; cube.GetComponent<Renderer>().sharedMaterials = new[] {red,accent}; }
                var lod = lodRoot.AddComponent<LODGroup>(); lod.SetLODs(new[] {new LOD(.6f,new[] {first.GetComponent<Renderer>()}),new LOD(.01f,new[] {second.GetComponent<Renderer>()})}); lod.RecalculateBounds();
                Group(volume,1,first.GetComponent<Renderer>(),second.GetComponent<Renderer>()).Kind = CullingGroupKind.LOD;
                var right = Cube("Right room",new Vector3(2,0,1),Vector3.one,blue); Group(volume,2,right.GetComponent<Renderer>());
                Cube("Floor",new Vector3(0,-.7f,0),new Vector3(8,.2f,8),gray);
                string geometryPath = folder+"/Geometry.unity";
                if (!EditorSceneManager.SaveScene(geometry,geometryPath)) throw new IOException("Could not save the new sample geometry scene.");
                var settings = ScriptableObject.CreateInstance<BakeSettingsAsset>(); settings.ExecutionMode = BakeExecutionMode.Cpu; settings.CellSize = new Vector3(4,4,4); settings.Resolution = 32; settings.Far = 20;
                AssetDatabase.CreateAsset(settings,folder+"/BakeSettings.asset");
                using (var controller = new BakeController(volume,settings)) { controller.Start(); controller.Complete(); controller.Publish(folder+"/Bake.asset"); }
                if (!EditorSceneManager.SaveScene(geometry,geometryPath)) throw new IOException("Could not save the newly assigned sample bake.");
                EditorSceneManager.CloseScene(geometry,true); geometry = default;
                main = CreateOwnedScene(folder+"/Basic.unity");
                var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.transform.position = new Vector3(-2,0,-3);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.02f,.02f,.02f); camera.farClipPlane = 30;
                camera.gameObject.AddComponent<CullingCameraAuthoring>(); camera.gameObject.AddComponent<SampleCameraPath>();
                var light = new GameObject("Directional Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.shadows = LightShadows.Hard; light.transform.rotation = Quaternion.Euler(45,-30,0);
                var subscene = new GameObject("Geometry SubScene").AddComponent<SubScene>(); subscene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(geometryPath); subscene.AutoLoadScene = true;
                string mainPath = folder+"/Basic.unity"; if (!EditorSceneManager.SaveScene(main,mainPath)) throw new IOException("Could not save the new sample main scene.");
                success = true; return mainPath;
            }
            finally
            {
                if (main.IsValid()) EditorSceneManager.CloseScene(main,true); if (geometry.IsValid()) EditorSceneManager.CloseScene(geometry,true);
                if (host.IsValid() && host.isLoaded) SceneManager.SetActiveScene(host);
                if (!success && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }
        private static void CreateFolder(string folder)
        {
            string parent = Path.GetDirectoryName(folder).Replace('\\','/'); if (!AssetDatabase.IsValidFolder(parent)) CreateFolder(parent);
            AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
        }
        private static Scene CreateOwnedScene(string path)
        {
            // NewScene(Additive) refuses an untitled host; saving a PreviewScene
            // is also unsupported. Opening a new empty scene asset additively
            // preserves that host, including unsaved user edits.
            using (var stream = new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write)))
                stream.Write("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots: []\n");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            if (!SceneManager.SetActiveScene(scene)) throw new InvalidOperationException("Could not activate the newly created sample scene.");
            return scene;
        }
        private static Material Material(string folder,string name,Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) {name = name,enableInstancing = true}; material.SetColor("_BaseColor",color); AssetDatabase.CreateAsset(material,folder+"/"+name+".mat"); return material;
        }
        private static GameObject Cube(string name,Vector3 position,Vector3 scale,Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        private static CullingGroupAuthoring Group(CullingVolumeAuthoring volume,int index,params Renderer[] renderers)
        {
            var group = new GameObject("Group "+index).AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.GroupIndex = index; group.Renderers = renderers; return group;
        }
    }
}
