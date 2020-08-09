﻿using System.Collections.Generic;
using xivModdingFramework.Animations.Enums;
using xivModdingFramework.Animations.Helpers;

namespace xivModdingFramework.Animations.DataContainers
{
    public class PapModel
    {
        public PapHeader Header { get; set; }

        public List<PapAnimationHeader> AnimationHeaders { get; set; } = new List<PapAnimationHeader>();

        public byte[] HavokData { get; set; }

        public Dictionary<int, PapParameter> PapParameters { get; set; } = new Dictionary<int, PapParameter>();

        public class PapHeader
        {
            public short Unknown1 { get; set; }
            public short Unknown2 { get; set; }
            public short AnimationCount { get; set; }
            public short Unknown3 { get; set; }
            public short Unknown4 { get; set; }
            public short HeaderSize { get; set; }
            public short Unknown5 { get; set; }
        }

        public class PapAnimationHeader
        {
            public string AnimationName { get; set; }
            public short Unknown1 { get; set; }
            public short Unknown2 { get; set; }
            public short AnimationIndex { get; set; }
            public short Unknown3 { get; set; }
            public short Unknown4 { get; set; }
        }

        public class PapParameter
        {
            public int ParameterLength { get; set; }
            public int ParameterPropertyCount { get; set; }
            public List<PapParameterProperty> PapParameterProperties { get; set; } = new List<PapParameterProperty>();
            public List<short> PropertyIndexList { get; set; } = new List<short>();
        }

        public class PapParameterProperty
        {
            public PapPropertyType Type { get; set; }
            public int PropertyLength { get; set; }

            public object Property { get; set; }

            public List<object> PropertyList { get; set; } = new List<object>();
        }

        public class PapHavokData
        {
            public float Duration { get; set; }
            public int TransformTracks { get; set; }
            public int FrameCount { get; set; }
            public int BlockCount { get; set; }
            public int FramesPerBlock { get; set; }
            public int MaskAndQuantSize { get; set; }
            public float BlockDuration { get; set; }
            public float BlockInverseDuration { get; set; }
            public float FrameDuration { get; set; }
            public List<int> BlockOffsets { get; set; } = new List<int>();
            public List<int> FloatBlockOffsets { get; set; } = new List<int>();
            public List<int> TransformTrackToBoneIndices { get; set; } = new List<int>();
            public List<SplineCompressedAnimation.TransformTrack[]> AnimationTracks { get; set; }
        }

        public class PapTMDH
        {
            public int Index { get; set; }
            public short FrameCount { get; set; }
            public short Unknown1 { get; set; }
        }


        public class PapTMAL
        {
            public short Unknown1 { get; set; }
            public short Unknown2 { get; set; }
        }

        public class PapTMAC
        {
            public int Index { get; set; }
            public int Unknown1 { get; set; }
            public int Unknown2 { get; set; }
            public int PapTMTRCount { get; set; }
        }

        public class PapTMTR
        {
            public int Index { get; set; }
            public List<int> AnimationIndices { get; set; } = new List<int>();
            public int AnimationCount { get; set; }
            public int Unknown1 { get; set; }
        }

        public class PapC9
        {
            public int Index { get; set; }
            public int FrameCount { get; set; }
            public int Unknown1 { get; set; }
            public string AnimationName { get; set; }
        }

        public class PapC10
        {
            public int Index { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown1 { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown2 { get; set; }
            public int Unknown3 { get; set; }
            public short Unknown4 { get; set; }
            /// <summary>
            /// Something Count?
            /// </summary>
            public short Unknown5 { get; set; }
            public int Unknown6 { get; set; }
            public int Unknown7 { get; set; }
            /// <summary>
            /// Index to something?
            /// </summary>
            public short Unknown8 { get; set; }

            public string AnimationName { get; set; }
            public short Unknown9 { get; set; }
            public int Unknown10 { get; set; }
        }

        public class PapC42
        {
            public int Index { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown1 { get; set; }
            /// <summary>
            /// Maybe Time or Frames?
            /// </summary>
            public short Unknown2 { get; set; }
            public short Unknown3 { get; set; }
            public int Unknown4 { get; set; }
            public int Unknown5 { get; set; }
            public int Unknown6 { get; set; }
        }
    }
}