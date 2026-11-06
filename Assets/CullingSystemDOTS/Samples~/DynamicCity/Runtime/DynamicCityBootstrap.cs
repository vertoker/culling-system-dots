using System;
using CullingSystemDOTS.Dynamic;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Dynamic.Gpu;
using CullingSystemDOTS.Dynamic.Urp;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Samples.DynamicCity
{
    public sealed class DynamicCityBootstrap : MonoBehaviour
    {
        public OcclusionShaderResources ShaderResources;
        public Material IndirectMaterial;
        public Material[] StockPalette;
        public Mesh CubeMesh;
        public bool AutoStart=true;
        public int AgentCount=512,BuildingCount=16,Layer;
        public bool IncludeRotatedFixture=true,IncludeInteriorDetails=true,CreateView=true,AgentsPaused,AutomaticEdits,ShowHud=true;
        public OcclusionSettings Settings=new OcclusionSettings{MaxUploadBytesPerFrame=16*1024*1024,MaxResourceBytes=256L*1024*1024};
        public int Seed=12345;
        public Material GroundMaterial;
        public Camera View {get;private set;}
        public World World {get;private set;}
        public DynamicCityConstruction Construction {get;private set;}
        public Entity[] Agents {get;private set;}
        public Entity[] InteriorDetails {get;private set;}
        public DynamicOcclusionPresentation Presentation {get;private set;}
        public DynamicOcclusionSystem Culling {get;private set;}
        public bool Initialized => World!=null && World.IsCreated;
        public string Fault {get;private set;}
        private GeometryHandle geometry;
        private Entity[] prototypes;
        private DynamicCityAgents agentSystem;
        private bool feature=true;
        private float editClock;
        private uint random;
        private readonly System.Collections.Generic.List<GameObject> ownedObjects=new System.Collections.Generic.List<GameObject>();
        private void Start(){if(AutoStart && !Initialized)try{Initialize();}catch(Exception exception){Fault=exception.Message;Shutdown();}}
        public void Initialize()
        {
            if(Initialized)throw new InvalidOperationException("This sample already owns a World.");
            if(ShaderResources==null || IndirectMaterial==null || CubeMesh==null || StockPalette==null || StockPalette.Length==0)
                throw new InvalidOperationException("Assign the sample's serialized mesh, palette, indirect material and shader resources.");
            if(AgentCount<0 || BuildingCount<0 || BuildingCount>16 || Layer<0 || Layer>31)throw new ArgumentOutOfRangeException(nameof(AgentCount));
            random=(uint)math.max(1,Seed);
            World=new World("Dynamic City sample",WorldFlags.Game);
            try
            {
                var systems=DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(World,systems);
                Presentation=DynamicOcclusionPresentation.Install(World,ShaderResources,Settings);
                Culling=Presentation.System;geometry=Culling.Registry.RegisterGeometry(CubeMesh);
                agentSystem=World.GetOrCreateSystemManaged<DynamicCityAgents>();
                var simulation=World.GetOrCreateSystemManaged<SimulationSystemGroup>();simulation.AddSystemToUpdateList(agentSystem);simulation.SortSystems();
                prototypes=new Entity[StockPalette.Length];var manager=World.EntityManager;
                for(int i=0;i<prototypes.Length;i++)
                {
                    var entity=manager.CreateEntity();
                    RenderMeshUtility.AddComponents(entity,manager,new RenderMeshDescription(ShadowCastingMode.On,receiveShadows:false,layer:Layer),
                        new RenderMeshArray(new[]{StockPalette[i]},new[]{CubeMesh}),MaterialMeshInfo.FromRenderMeshArrayIndices(0,0));
                    manager.AddComponent<Prefab>(entity);prototypes[i]=entity;
                }
                Construction=new DynamicCityConstruction(this);
                for(int i=0;i<BuildingCount;i++)Construction.Build(i,Home(i));
                if(IncludeRotatedFixture)Construction.Add(PieceKind.Wall,float4x4.TRS(new float3(-30.3f,1.5f,-42.7f),quaternion.RotateY(.61f),new float3(5,3,.25f)),0);
                InteriorDetails=IncludeInteriorDetails?DynamicCityInteriors.Build(this):Array.Empty<Entity>();
                Agents=new Entity[AgentCount];
                int homes=math.max(1,BuildingCount),perHome=AgentCount==0?1:1+(AgentCount-1)/homes;
                for(int i=0;i<AgentCount;i++)
                {
                    int local=i/homes;var home=Home(i%homes);
                    var data=new DynamicCityAgent {Home=home,Phase=local*6f/perHome,Upper=(byte)(local%4==0?1:0)};
                    var matrix=DynamicCityAgents.Pose(data);
                    var entity=CreateRenderEntity(matrix,(local*3+5)%StockPalette.Length,OcclusionRole.Renderable|OcclusionRole.Occludee);
                    manager.AddComponentData(entity,data);Agents[i]=entity;
                }
                if(CreateView)
                {
                    var cameraObject=new GameObject("Dynamic City first-person camera");ownedObjects.Add(cameraObject);cameraObject.transform.SetParent(transform,false);
                    View=cameraObject.AddComponent<Camera>();View.nearClipPlane=.05f;View.farClipPlane=256;View.fieldOfView=70;View.allowMSAA=false;
                    View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.035f,.045f,.065f);View.cullingMask=1<<Layer;
                    View.transform.position=(Vector3)Home(0)+new Vector3(-1.5f,1.7f,-18);View.transform.LookAt((Vector3)Home(0)+new Vector3(0,1.7f,0));
                    var controller=cameraObject.AddComponent<DynamicCityCamera>();controller.Sample=this;Presentation.RegisterView(View);
                    var diagnostics=GetComponent<DynamicCityDiagnostics>();if(diagnostics==null)diagnostics=gameObject.AddComponent<DynamicCityDiagnostics>();diagnostics.Sample=this;
                }
                if(GroundMaterial!=null)
                {
                    var ground=new GameObject("Dynamic City shadow receiver");ownedObjects.Add(ground);ground.transform.SetParent(transform,false);ground.layer=Layer;
                    ground.AddComponent<MeshFilter>().sharedMesh=CubeMesh;ground.AddComponent<MeshRenderer>().sharedMaterial=GroundMaterial;
                    ground.transform.position=new Vector3(0,-.3f,0);ground.transform.localScale=new Vector3(128,.25f,128);
                    var sunObject=new GameObject("Dynamic City sun");ownedObjects.Add(sunObject);sunObject.transform.SetParent(transform,false);
                    var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;sun.shadows=LightShadows.Hard;sun.cullingMask=1<<Layer;sun.transform.rotation=Quaternion.Euler(45,-35,0);
                }
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(World);
                SetFeatureEnabled(feature);
            }
            catch{Shutdown();throw;}
        }
        public static float3 Home(int index)=>new float3((index%4-1.5f)*28,0,(index/4-1.5f)*28);
        internal Entity CreateRenderEntity(float4x4 matrix,int color,OcclusionRole roles)
        {
            var manager=World.EntityManager;int palette=color%prototypes.Length;var entity=manager.Instantiate(prototypes[palette]);
            try
            {
                manager.SetComponentData(entity,new LocalToWorld{Value=matrix});
                float4 tint=StockPalette[palette].HasProperty("_BaseColor")?(float4)StockPalette[palette].GetVector("_BaseColor"):new float4(1);
                Culling.Bindings.Bind(entity,new OcclusionInstanceDescriptor {Geometry=geometry,Material=IndirectMaterial,Layer=Layer,Shadows=ShadowCastingMode.On,
                    State=new OcclusionInstanceState{Roles=roles,Opaque=true,Color=tint}});return entity;
            }
            catch{manager.DestroyEntity(entity);throw;}
        }
        internal void RemoveRenderEntity(Entity entity)
        {Culling.Bindings.Unbind(entity);if(World.EntityManager.Exists(entity))World.EntityManager.DestroyEntity(entity);}
        public void Advance(float delta){if(Initialized && !AgentsPaused)agentSystem.Advance(delta);}
        public void PauseSimulation(bool pause)
        {AgentsPaused=pause;if(Initialized){agentSystem.Paused=pause;if(pause)World.EntityManager.CompleteAllTrackedJobs();}}
        public void SetFeatureEnabled(bool value)
        {
            feature=value;if(!Initialized)return;
            Presentation.Enabled=value;Culling.Enabled=value;
        }
        public bool FeatureEnabled=>feature;
        private void Update()
        {
            if(!Initialized)return;agentSystem.Paused=AgentsPaused;
            if(!AutomaticEdits || Construction.Pieces.Count==0)return;
            editClock+=Time.deltaTime;if(editClock<.5f)return;editClock-=.5f;
            random^=random<<13;random^=random>>17;random^=random<<5;
            var piece=Construction.Pieces[(int)(random%(uint)Construction.Pieces.Count)];
            Construction.Apply(new ConstructionCommand{PieceId=piece.Id,Operation=piece.Present?ConstructionOperation.Remove:ConstructionOperation.Place,Tick=Construction.LastTick+1});
        }
        public void Shutdown()
        {
            if(World==null)return;
            if(World.IsCreated){ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(World);Presentation?.Dispose();World.Dispose();}
            World=null;Presentation=null;Culling=null;
            foreach(var item in ownedObjects)if(item!=null){item.SetActive(false);Destroy(item);}ownedObjects.Clear();View=null;
        }
        private void OnDestroy(){Shutdown();}
    }
}
