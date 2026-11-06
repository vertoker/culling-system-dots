using System.Collections.Generic;
using CullingSystemDOTS.Dynamic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Samples.DynamicCity
{
    public enum PieceKind {Wall,Floor,Roof,Door}
    public enum ConstructionOperation {Place,Remove,OpenDoor,CloseDoor}
    public struct ConstructionCommand {public int PieceId;public ConstructionOperation Operation;public ulong Tick;}
    public sealed class DynamicCityPiece
    {
        public int Id {get;internal set;}
        public PieceKind Kind {get;internal set;}
        public bool Present {get;internal set;}
        public Entity Entity {get;internal set;}
        public float4x4 Pose {get;internal set;}
        public Bounds Bounds {get;internal set;}
        internal int Color;
    }
    public sealed class DynamicCityConstruction
    {
        private readonly DynamicCityBootstrap sample;
        private readonly List<DynamicCityPiece> pieces=new List<DynamicCityPiece>();
        public IReadOnlyList<DynamicCityPiece> Pieces=>pieces;
        public ulong LastTick {get;private set;}
        internal DynamicCityConstruction(DynamicCityBootstrap sample){this.sample=sample;}
        internal void Add(PieceKind kind,float4x4 pose,int color)
        {
            float3 extents=.5f*(math.abs(pose.c0.xyz)+math.abs(pose.c1.xyz)+math.abs(pose.c2.xyz));
            var piece=new DynamicCityPiece {Id=pieces.Count+1,Kind=kind,Pose=pose,Color=color,Bounds=new Bounds(pose.c3.xyz,extents*2)};
            pieces.Add(piece);SetPresent(piece,true);
        }
        private void Box(PieceKind kind,float3 center,float3 size,int color)=>Add(kind,float4x4.TRS(center,quaternion.identity,size),color);
        internal void Build(int building,float3 home)
        {
            for(int floor=0;floor<2;floor++)
            {
                float level=floor*3.25f;Box(PieceKind.Floor,home+new float3(0,level,0),new float3(12,.25f,12),building);
                for(int segment=0;segment<4;segment++)
                {
                    float offset=-4.5f+segment*3;
                    Box(PieceKind.Wall,home+new float3(offset,level+1.625f,6),new float3(3,3,.25f),building);
                    Box(PieceKind.Wall,home+new float3(-6,level+1.625f,offset),new float3(.25f,3,3),building);
                    Box(PieceKind.Wall,home+new float3(6,level+1.625f,offset),new float3(.25f,3,3),building);
                    if(segment!=1)Box(PieceKind.Wall,home+new float3(offset,level+1.625f,-6),new float3(3,3,.25f),building);
                    else
                    {
                        Box(PieceKind.Wall,home+new float3(offset-1.05f,level+1.625f,-6),new float3(.9f,3,.25f),building);
                        Box(PieceKind.Wall,home+new float3(offset+1.05f,level+1.625f,-6),new float3(.9f,3,.25f),building);
                        Box(PieceKind.Wall,home+new float3(offset,level+2.825f,-6),new float3(1.2f,.6f,.25f),building);
                        Box(PieceKind.Door,home+new float3(offset,level+1.325f,-6),new float3(1.2f,2.4f,.2f),building+1);
                    }
                }
                Box(PieceKind.Wall,home+new float3(0,level+1.625f,2.5f),new float3(.2f,3,7),building+2);
            }
            Box(PieceKind.Roof,home+new float3(0,6.5f,0),new float3(12,.25f,12),building);
        }
        public bool Apply(in ConstructionCommand command)
        {
            if(command.PieceId<=0 || command.PieceId>pieces.Count || (uint)command.Operation>3 || command.Tick<LastTick)return false;
            var piece=pieces[command.PieceId-1];bool present=command.Operation==ConstructionOperation.Place || command.Operation==ConstructionOperation.CloseDoor;
            if((command.Operation==ConstructionOperation.OpenDoor || command.Operation==ConstructionOperation.CloseDoor) && piece.Kind!=PieceKind.Door)return false;
            SetPresent(piece,present);LastTick=command.Tick;return true;
        }
        private void SetPresent(DynamicCityPiece piece,bool present)
        {
            if(piece.Present==present)return;
            if(present)piece.Entity=sample.CreateRenderEntity(piece.Pose,piece.Color,OcclusionRole.Renderable|OcclusionRole.Occluder|OcclusionRole.Occludee);
            else{sample.RemoveRenderEntity(piece.Entity);piece.Entity=Entity.Null;}
            piece.Present=present;
        }
        public DynamicCityPiece Pick(Ray ray)
        {
            DynamicCityPiece result=null;float nearest=float.MaxValue;
            foreach(var piece in pieces)if(piece.Present && piece.Bounds.IntersectRay(ray,out float distance) && distance<nearest){nearest=distance;result=piece;}
            return result;
        }
    }
}
