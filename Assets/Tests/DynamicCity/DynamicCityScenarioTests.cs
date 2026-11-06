using System.Collections;
using System.Linq;
using CullingSystemDOTS.Dynamic.Gpu;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Samples.DynamicCity;
using NUnit.Framework;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.DynamicCity.Tests
{
    public class DynamicCityScenarioTests
    {
        [UnityTest] public IEnumerator PieceEditsAndHiddenAgentsRemainIndependentOfRendering()
        {
            var go=new GameObject("Owned dynamic construction scenario");var sample=go.AddComponent<DynamicCityBootstrap>();sample.AutoStart=false;
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.SetActive(false);
            #if UNITY_EDITOR
            var resources=UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
            #else
            OcclusionShaderResources resources=null;
            #endif
            var stock=new Material(Shader.Find("Universal Render Pipeline/Unlit")){enableInstancing=true};var indirect=new Material(resources.Indirect);
            try
            {
                sample.CubeMesh=cube.GetComponent<MeshFilter>().sharedMesh;sample.ShaderResources=resources;sample.StockPalette=new[]{stock};
                sample.IndirectMaterial=indirect;sample.AgentCount=32;sample.BuildingCount=4;sample.Layer=31;sample.Initialize();
                Assert.That(sample.AutomaticEdits,Is.False,"Ordinary demo startup must preserve the city until construction is explicitly enabled.");
                Assert.That(sample.View.GetComponent<DynamicCityCamera>().Tour,Is.False,"The initial view must not automatically enter geometry.");
                var manager=sample.World.EntityManager;
                using(var interior=manager.CreateEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<DynamicOcclusionBinding>()},None=new[]{ComponentType.ReadOnly<DynamicCityAgent>()}}))
                    Assert.That(interior.CalculateEntityCount(),Is.GreaterThan(sample.Construction.Pieces.Count),"Buildings need independently rendered interior fixtures.");
                foreach(var home in sample.Agents.Select(e=>manager.GetComponentData<DynamicCityAgent>(e)).GroupBy(a=>a.Home))
                {Assert.That(home.Any(a=>a.Upper!=0),Is.True,"Each building must have upper-floor agents.");Assert.That(home.Any(a=>a.Upper==0),Is.True,"Each building must have ground-floor agents.");}
                Assert.That(sample.Construction.Pieces.Count,Is.GreaterThan(16),"The city must consist of independently editable pieces.");
                var wall=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Wall);
                var floor=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Floor);
                var door=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Door);
                Assert.That(sample.Construction.Apply(new ConstructionCommand{PieceId=wall.Id,Operation=ConstructionOperation.Remove,Tick=1}),Is.True);
                Assert.That(wall.Present,Is.False);
                Assert.That(sample.Construction.Apply(new ConstructionCommand{PieceId=wall.Id,Operation=ConstructionOperation.Place,Tick=2}),Is.True);
                Assert.That(wall.Present,Is.True);
                Assert.That(sample.Construction.Apply(new ConstructionCommand{PieceId=door.Id,Operation=ConstructionOperation.OpenDoor,Tick=3}),Is.True);
                Assert.That(door.Present,Is.False);
                Assert.That(sample.Construction.Apply(new ConstructionCommand{PieceId=floor.Id,Operation=ConstructionOperation.Remove,Tick=4}),Is.True);
                Assert.That(floor.Present,Is.False);
                var agent=sample.Agents[0];var tick=manager.GetComponentData<DynamicCityAgent>(agent).Tick;
                var before=manager.GetComponentData<LocalToWorld>(agent).Value;
                sample.SetFeatureEnabled(false);sample.Advance(.25f);
                Assert.That(manager.GetComponentData<DynamicCityAgent>(agent).Tick,Is.GreaterThan(tick));
                Assert.That(manager.GetComponentData<LocalToWorld>(agent).Value.Equals(before),Is.False,"Disabling graphics cannot pause indoor/street movement.");
                Assert.That(manager.IsEnabled(agent),Is.True);
                sample.SetFeatureEnabled(true);
                for(int i=0;i<4;i++)yield return null;
            }
            finally {sample.Shutdown();Object.Destroy(go);Object.Destroy(stock);Object.Destroy(indirect);Object.Destroy(cube);}
        }
    }
}
