using UnityEngine;

namespace CullingSystemDOTS.Samples.DynamicCity
{
    public sealed class DynamicCityDiagnostics : MonoBehaviour
    {
        public DynamicCityBootstrap Sample;
        private DynamicCityPiece removed;
        private void OnGUI()
        {
            if(Sample==null || !Sample.ShowHud)return;
            GUILayout.BeginArea(new Rect(12,12,390,540),GUI.skin.box);
            GUILayout.Label("Dynamic geometric occlusion — experimental demo");
            if(!Sample.Initialized){GUILayout.Label(Sample.Fault??"Starting...");GUILayout.EndArea();return;}
            var presentation=Sample.Presentation;
            GUILayout.Label("Agents: "+Sample.Agents.Length+" | Pieces: "+Sample.Construction.Pieces.Count);
            GUILayout.Label("Interior fixtures: "+Sample.InteriorDetails.Length);
            GUILayout.Label("Ready indirect instances: "+presentation.ReadyInstances+" | Slots: "+presentation.InFlightSlots);
            GUILayout.Label("Upload: "+presentation.UploadedBytes+" bytes | Accounted: "+(presentation.AccountedResourceBytes/1048576f).ToString("F1")+" MiB");
            if(presentation.LastFault!=null)GUILayout.Label("Ordinary rendering: "+presentation.LastFault);
            if(GUILayout.Button(Sample.FeatureEnabled?"Use ordinary rendering":"Enable dynamic occlusion"))Sample.SetFeatureEnabled(!Sample.FeatureEnabled);
            Sample.AutomaticEdits=GUILayout.Toggle(Sample.AutomaticEdits,"Seeded automatic construction/demolition");
            bool paused=GUILayout.Toggle(Sample.AgentsPaused,"Pause agents");if(paused!=Sample.AgentsPaused)Sample.PauseSimulation(paused);
            var controller=Sample.View.GetComponent<DynamicCityCamera>();controller.Tour=GUILayout.Toggle(controller.Tour,"Guided first-person tour");
            GUILayout.Label("WASD, Shift, Space/Ctrl; RMB look. T toggles tour.");
            GUILayout.Label("Free camera; no game physics or collision response.");
            var home=(Vector3)DynamicCityBootstrap.Home(0);
            if(GUILayout.Button("Street view"))controller.SetPose(home+new Vector3(-1.5f,1.7f,-18),home+new Vector3(0,1.7f,0));
            if(GUILayout.Button("Visit ground-floor workshop"))controller.SetPose(home+new Vector3(2,1.7f,-3.5f),home+new Vector3(4,1.4f,3));
            if(GUILayout.Button("Visit upper-floor workshop"))controller.SetPose(home+new Vector3(2,4.95f,-3.5f),home+new Vector3(4,4.65f,3));
            var selected=Sample.Construction.Pick(Sample.View.ViewportPointToRay(new Vector3(.5f,.5f)));
            GUILayout.Label(selected==null?"Aim at a building piece.":"Aimed piece: "+selected.Id+" ("+selected.Kind+")");
            if(GUILayout.Button("Remove aimed piece") && selected!=null)
            {removed=selected;Sample.Construction.Apply(new ConstructionCommand{PieceId=selected.Id,Operation=ConstructionOperation.Remove,Tick=Sample.Construction.LastTick+1});}
            if(GUILayout.Button("Restore last removed piece") && removed!=null)
                Sample.Construction.Apply(new ConstructionCommand{PieceId=removed.Id,Operation=ConstructionOperation.Place,Tick=Sample.Construction.LastTick+1});
            if(GUILayout.Button("Open every door"))foreach(var piece in Sample.Construction.Pieces)if(piece.Kind==PieceKind.Door)
                Sample.Construction.Apply(new ConstructionCommand{PieceId=piece.Id,Operation=ConstructionOperation.OpenDoor,Tick=Sample.Construction.LastTick+1});
            if(GUILayout.Button("Restore every building piece"))foreach(var piece in Sample.Construction.Pieces)
                Sample.Construction.Apply(new ConstructionCommand{PieceId=piece.Id,Operation=ConstructionOperation.Place,Tick=Sample.Construction.LastTick+1});
            GUILayout.EndArea();
        }
    }
}
