﻿// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using HelixToolkit.SharpDX.Core.Model;
using HelixToolkit.SharpDX.Core.Utilities.ImagePacker;
using Newtonsoft.Json;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using xivModdingFramework.Exd.FileTypes;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.Helpers;
using xivModdingFramework.Resources;

namespace xivModdingFramework.Models.FileTypes
{
    /// <summary>
    /// This class deals with Collada .dae files
    /// </summary>
    public class Dae
    {
        private static readonly Dictionary<string, SkeletonData> FullSkel = new Dictionary<string, SkeletonData>();

        private static readonly Dictionary<int, SkeletonData> FullSkelNum = new Dictionary<int, SkeletonData>();

        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;
        private readonly string _pluginTarget, _appVersion;

        private enum ToolType
		{
            TexTools,
            OpenCOLLADA,
            Blender,
		}

        public Dae(DirectoryInfo gameDirectory, XivDataFile dataFile, string pluginTarget = "OpenCollada", string appVersion = "1.0.0")
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
            _pluginTarget = pluginTarget;
            _appVersion = appVersion;
        }

        /// <summary>
        /// Creates a Collada DAE file for a given model
        /// </summary>
        /// <param name="xivModel">The model to create a dae file for</param>
        /// <param name="saveLocation">The location to save the dae file</param>
        public async Task MakeDaeFileFromModel(XivMdl xivModel, string path, string skelName)
        {
            FullSkel.Clear();
            FullSkelNum.Clear();

            var hasBones = true;

            var modelName = Path.GetFileNameWithoutExtension(xivModel.MdlPath);

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var savePath = path;

            // We will only use the LoD with the highest quality, that being LoD 0
            var lod0 = xivModel.LoDList[0];

            if (xivModel.PathData.BoneList.Count < 1)
            {
                hasBones = false;
            }

            var skelDict = new Dictionary<string, SkeletonData>();

            if (hasBones)
            {
                var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                var skeletonFile = cwd + "/Skeletons/" + skelName + ".skel";
                var skeletonData = File.ReadAllLines(skeletonFile);

                // Deserializes the json skeleton file and makes 2 dictionaries with names and numbers as keys
                foreach (var b in skeletonData)
                {
                    if (b == "") continue;
                    var j = JsonConvert.DeserializeObject<SkeletonData>(b);

                    FullSkel.Add(j.BoneName, j);
                    FullSkelNum.Add(j.BoneNumber, j);
                }

                foreach (var s in xivModel.PathData.BoneList)
                {
                    var fixedBone = Regex.Replace(s, "[0-9]+$", string.Empty);

                    if (FullSkel.ContainsKey(fixedBone))
                    {
                        var skel = FullSkel[fixedBone];

                        if (skel.BoneParent == -1 && !skelDict.ContainsKey(skel.BoneName))
                        {
                            skelDict.Add(skel.BoneName, skel);
                        }

                        while (skel.BoneParent != -1)
                        {
                            if (!skelDict.ContainsKey(skel.BoneName))
                            {
                                skelDict.Add(skel.BoneName, skel);
                            }
                            skel = FullSkelNum[skel.BoneParent];

                            if (skel.BoneParent == -1 && !skelDict.ContainsKey(skel.BoneName))
                            {
                                skelDict.Add(skel.BoneName, skel);
                            }
                        }
                    }
                    else
                    {
                        throw new Exception($"The skeleton file {skeletonFile} did not contain bone {s}. Consider updating the skeleton file.");
                    }
                }
            }


            var xmlWriterSettings = new XmlWriterSettings()
            {
                Indent = true,
            };

            await Task.Run(() =>
            {
                using (var xmlWriter = XmlWriter.Create(savePath, xmlWriterSettings))
                {
                    xmlWriter.WriteStartDocument();

                    //<COLLADA>
                    xmlWriter.WriteStartElement("COLLADA", "http://www.collada.org/2005/11/COLLADASchema");
                    xmlWriter.WriteAttributeString("xmlns", "http://www.collada.org/2005/11/COLLADASchema");
                    xmlWriter.WriteAttributeString("version", "1.4.1");
                    xmlWriter.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

                    //Assets
                    XMLassets(xmlWriter);

                    //Images
                    XMLimages(xmlWriter, modelName, lod0.MeshCount, lod0.MeshDataList);

                    //effects
                    XMLeffects(xmlWriter, modelName, lod0.MeshCount, lod0.MeshDataList);

                    //Materials
                    XMLmaterials(xmlWriter, modelName, lod0.MeshCount);

                    //Geometries
                    XMLgeometries(xmlWriter, modelName, lod0.MeshDataList);

                    if (hasBones)
                    {
                        //Controllers
                        XMLcontrollers(xmlWriter, modelName, lod0.MeshDataList, skelDict, xivModel);
                    }

                    //Scenes
                    XMLscenes(xmlWriter, modelName, lod0.MeshDataList, skelDict);

                    xmlWriter.WriteEndElement();
                    //</COLLADA>

                    xmlWriter.WriteEndDocument();

                    xmlWriter.Flush();
                    FullSkel.Clear();
                    FullSkelNum.Clear();
                }
            });
        }

        /// <summary>
        /// Reads the collada file
        /// </summary>
        /// <param name="xivMdl">The XivMdl data</param>
        /// <param name="daeLocation">The location of the dae file</param>
        /// <returns>A dictionary containing (Mesh Number, (Mesh Part Number, Collada Data)</returns>
        public TTModel ReadColladaFile(DirectoryInfo daeLocation, Action<bool, string> loggingFunction)
        {
            var boneJointDict = new Dictionary<string, string>();
            var boneStringList = new List<string>();
            var uniqueBoneNames = new HashSet<string>();

            // List of mesh names by Mesh Num -> Part Num
            var meshNames = new List<List<string>>();

            // A dictionary containing <Mesh Number, <Mesh Part Number, Collada Data>
            var meshPartDataDictionary = new SortedDictionary<int, SortedDictionary<int, ColladaData>>();

            TTModel ttModel = new TTModel();
            ttModel.Source = daeLocation.FullName;

            // Reading Control Data to get bones that are used in model
            using (var reader = XmlReader.Create(daeLocation.FullName))
            {
                string[] bones = null;
                while (reader.Read())
                {
                    if (!reader.IsStartElement()) continue;
                    if (!reader.Name.Equals("controller")) continue;

                    while (reader.Read())
                    {
                        if (reader.IsStartElement())
                        {
                            if (reader.Name.Contains("Name_array"))
                            {
                                bones = (string[])reader.ReadElementContentAs(typeof(string[]), null);
                            }

                            if (reader.Name.Equals("v"))
                            {
                                var uniqueJointIndices = new HashSet<int>();
                                var vData = (int[])reader.ReadElementContentAs(typeof(int[]), null);

                                for (var a = 0; a < vData.Length; a += 2)
                                {
                                    uniqueJointIndices.Add(vData[a]);
                                }

                                if (bones != null)
                                {
                                    foreach (var uniqueJointIndex in uniqueJointIndices)
                                    {
                                        var bone = bones[uniqueJointIndex];
                                        var boneName = bone;

                                        if (!bone.Contains("joint"))
                                        {
                                            boneName = Regex.Replace(bone, "[0-9]+$", string.Empty);
                                        }

                                        uniqueBoneNames.Add(boneName);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Reading Bone Data
            using (var reader = XmlReader.Create(daeLocation.FullName))
            {
                while (reader.Read())
                {
                    if (!reader.IsStartElement()) continue;
                    if (!reader.Name.Equals("visual_scene")) continue;

                    while (reader.Read())
                    {
                        if (reader.IsStartElement())
                        {
                            if (reader.Name.Contains("node"))
                            {
                                var sid = reader["sid"];
                                if (sid != null)
                                {
                                    var name = reader["name"];
                                    if (name==null) continue;

                                    var boneString = Regex.Replace(name, "[0-9]+$", string.Empty);

                                    if (!uniqueBoneNames.Contains(sid))
                                    {
                                        continue;
                                    }

                                    // Throw an exception if there is a duplicate bone
                                    if (boneStringList.Contains(sid))
                                    {
                                        throw new Exception($"Model cannot contain duplicate bones. Duplicate found: {sid}");
                                    }

                                    if (!boneString.Substring(0, 2).Contains("n_") && !name.Substring(0, 2).Contains("j_")) continue;

                                    if (!boneStringList.Contains(boneString))
                                    {
                                        boneJointDict.Add(sid, boneString);
                                    }
                                }
                                else
                                {
                                    var name = reader["name"];
                                    if (name==null) continue;

                                    var boneString = Regex.Replace(name, "[0-9]+$", string.Empty);

                                    if (!uniqueBoneNames.Contains(boneString))
                                    {
                                        continue;
                                    }

                                    if (!boneString.Substring(0, 2).Contains("n_") && !name.Substring(0, 2).Contains("j_")) continue;

                                    if (!uniqueBoneNames.Contains(name))
                                    {
                                        boneJointDict.Add(name, name);
                                    }
                                }
                            }
                        }
                    }
                    break;
                }
            }
            var boneDict = new Dictionary<string, int>();
            var meshNameDict = new Dictionary<string, string>();

            // Default values for element names
            var texc   = "-map0-array";
            var texc2  = "-map1-array";
            var valpha = "-map2-array";
            var vcol   = "-col0-array";
            var pos    = "-positions-array";
            var norm   = "-normals-array";
            var biNorm = "-texbinormals";
            var tang   = "-textangents";

            var indexStride = 4;
            var textureCoordinateStride = 2;
            var vertexColorStride = 3;
            ToolType toolType = ToolType.TexTools;

            using (var reader = XmlReader.Create(daeLocation.FullName))
            {
                while (reader.Read())
                {
                    var cData = new ColladaData();

                    if (reader.IsStartElement())
                    {
                        // Set element name values based on authoring tool
                        if (reader.Name.Contains("authoring_tool"))
                        {
                            var tool = reader.ReadElementContentAsString();

                            if (tool.Contains("OpenCOLLADA"))
                            {
                                vcol   = "-map0-array";
                                texc   = "-map1-array";
                                texc2  = "-map2-array";
                                valpha = "-map3-array";
                                biNorm = "-map1-texbinormals";
                                tang   = "-map1-textangents";
                                textureCoordinateStride = 3;
                                indexStride = 6;
                                toolType = ToolType.OpenCOLLADA;
                            }
                            else if (tool.Contains("Blender"))
                            {
                                texc    = "-map-0-array";
                                texc2   = "-map-1-array";
                                valpha  = "-map-2-array";
                                vcol    = "-col-0-array";
                                pos     = "-mesh-positions-array";
                                norm    = "-mesh-normals-array";
                                biNorm  = "-mesh-bitangents-array";
                                tang    = "-mesh-tangents-array";
                                indexStride = 1;
                                toolType = ToolType.Blender;
                            }
                            else if (!tool.Contains("TexTools"))
                            {
                                throw new FormatException($"The Authoring Tool being used is unsupported.  Tool:{tool}\n" +
                                    $"TexTools requires the use of 3ds Max OpenCOLLADA or Blender.");
                            }
                        }

                        // Read Geometry
                        if (reader.Name.Equals("geometry"))
                        {
                            var atr = reader["name"];
                            var id = reader["id"];

                            if (meshNameDict.ContainsKey(id))
                            {
                                throw new Exception($"Meshes cannot have duplicate names. Duplicate: {id}");
                            }

                            if (atr.Contains("Mesh"))
                            {
                                atr = atr.Replace("Mesh", string.Empty);
                            }

                            meshNameDict.Add(id, atr);

                            var meshNum = 0;

                            try
                            {
                                meshNum = int.Parse(atr.Substring(atr.LastIndexOf("_", StringComparison.Ordinal) + 1, 1));

                                // Determines whether the mesh has parts and gets the mesh number
                                if (atr.Contains("."))
                                {
                                    meshNum = int.Parse(atr.Substring(atr.LastIndexOf("_", StringComparison.Ordinal) + 1,
                                        atr.LastIndexOf(".", StringComparison.Ordinal) -
                                        (atr.LastIndexOf("_", StringComparison.Ordinal) + 1)));
                                }
                            }
                            catch
                            {
                                throw new Exception($"Could not read mesh number of mesh: {atr}\n\n" +
                                                    $"Please make sure your mesh name ends with the mesh/part number.");
                            }

                            while (reader.Read())
                            {
                                if (reader.IsStartElement())
                                {
                                    if (reader.Name.Contains("float_array"))
                                    {
                                        var currentId = reader["id"].ToLower();
                                        // Positions 
                                        if (currentId.EndsWith(pos))
                                        {
                                            cData.Positions.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                        // Normals
                                        else if (currentId.EndsWith(norm) && cData.Positions.Count > 0)
                                        {
                                            cData.Normals.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                        // Vertex Colors                                        
                                        else if(currentId.EndsWith(vcol) && cData.Positions.Count > 0)
                                        {
                                            cData.VertexColors.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                        //Texture Coordinates
                                        else if (currentId.EndsWith(texc) && cData.Positions.Count > 0)
                                        {
                                            cData.TextureCoordinates0.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                        //Texture Coordinates2
                                        else if (currentId.EndsWith(texc2) && cData.Positions.Count > 0)
                                        {
                                            cData.TextureCoordinates1.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                        // Vertex Alphas
                                        else if (currentId.EndsWith(valpha) && cData.Positions.Count > 0)
                                        {
                                            cData.VertexAlphas.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                        //Tangents
                                        else if (currentId.EndsWith(tang) && cData.Positions.Count > 0)
                                        {
                                            cData.Tangents.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                        //BiNormals
                                        else if (currentId.EndsWith(biNorm) && cData.Positions.Count > 0)
                                        {
                                            cData.BiNormals.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                    }
                                    
                                    var vertexIndexDict = new Dictionary<string, int>();

                                    var inputOffset = 0;
                                    if (reader.Name.Equals("triangles"))
                                    {
                                        while (reader.Read())
                                        {
                                            if (reader.IsStartElement())
                                            {
                                                if (reader.Name.Contains("input"))
                                                {
                                                    string semantic = reader["semantic"].ToLower();
                                                    string source = reader["source"].ToLower();

                                                    inputOffset = int.Parse(reader["offset"]);

                                                    if (semantic.Equals("vertex"))
                                                    {
                                                        vertexIndexDict.Add("position", inputOffset);
                                                    }
                                                    else if (semantic.Equals("normal"))
                                                    {
                                                        vertexIndexDict.Add("normal", inputOffset);
                                                    }
                               
                                                    else if (semantic.Equals("textangent") && (source.Contains("map0") || source.Contains("map1")))
                                                    {
                                                        vertexIndexDict.Add("tangent", inputOffset);
                                                    }
                                                    else if (semantic.Equals("texbinormal") && (source.Contains("map0") ||source.Contains("map1")))
                                                    {
                                                        vertexIndexDict.Add("biNormal", inputOffset);
                                                    }


                                                    if (toolType == ToolType.TexTools)
                                                    {
                                                        if (semantic.Equals("texcoord") && source.Contains("map0"))
                                                        {
                                                            vertexIndexDict.Add("textureCoordinate", inputOffset);
                                                        }
                                                        else if (semantic.Equals("texcoord") && source.Contains("map1"))
                                                        {
                                                            vertexIndexDict.Add("textureCoordinate1", inputOffset);
                                                        }
                                                        else if (semantic.Equals("texcoord") && source.Contains("map2"))
                                                        {
                                                            vertexIndexDict.Add("vertexAlpha", inputOffset);
                                                        }
                                                        else if (semantic.Equals("color"))
                                                        {
                                                            vertexIndexDict.Add("vertexColor", inputOffset);
                                                        }
                                                    }
                                                    else if (toolType == ToolType.Blender)
													{
                                                        if (semantic.Equals("texcoord") && source.Contains("map-0"))
                                                        {
                                                            vertexIndexDict.Add("textureCoordinate", inputOffset);
                                                        }
                                                        else if (semantic.Equals("texcoord") && source.Contains("map-1"))
                                                        {
                                                            vertexIndexDict.Add("textureCoordinate1", inputOffset);
                                                        }
                                                        else if (semantic.Equals("texcoord") && source.Contains("map-2"))
                                                        {
                                                            vertexIndexDict.Add("vertexAlpha", inputOffset);
                                                        }
                                                        else if (semantic.Equals("color") && !vertexIndexDict.ContainsKey("vertexColor"))
                                                        {
                                                            vertexIndexDict.Add("vertexColor", inputOffset);
                                                        }
                                                    }
                                                    else if (toolType == ToolType.OpenCOLLADA)
													{
                                                        if (semantic.Equals("texcoord") && (source.Contains("map1") || source.Contains("uv0")))
                                                        {
                                                            vertexIndexDict.Add("textureCoordinate", inputOffset);
                                                        }
                                                        else if (semantic.Equals("texcoord") && (source.Contains("map2") || source.Contains("uv1")))
                                                        {
                                                            vertexIndexDict.Add("textureCoordinate1", inputOffset);
                                                        }
                                                        else if (semantic.Equals("texcoord") && (source.Contains("map3") || source.Contains("uv3")))
                                                        {
                                                            vertexIndexDict.Add("vertexAlpha", inputOffset);
                                                        }
                                                        else if (semantic.Equals("color"))
                                                        {
                                                            vertexIndexDict.Add("vertexColor", inputOffset);
                                                        }
                                                    }
                                                    else
													{
                                                        throw new Exception($"Tool not supported: {toolType}");
                                                    }
                                                }
                                                else
                                                {
                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    // Indices
                                    if (reader.Name.Equals("p"))
                                    {

                                        cData.Indices.AddRange((int[])reader.ReadElementContentAs(typeof(int[]), null));

                                        indexStride = inputOffset + 1;

                                        // Reads the indices for each data point and places them in a list
                                        for (var i = 0; i < cData.Indices.Count; i += indexStride)
                                        {
                                            cData.PositionIndices.Add(cData.Indices[i + vertexIndexDict["position"]]);
                                            cData.NormalIndices.Add(cData.Indices[i + vertexIndexDict["normal"]]);
                                            cData.TextureCoordinate0Indices.Add(cData.Indices[i + vertexIndexDict["textureCoordinate"]]);

                                            if (cData.TextureCoordinates1.Count > 0)
                                            {
                                                cData.TextureCoordinate1Indices.Add(cData.Indices[i + vertexIndexDict["textureCoordinate1"]]);
                                            }

                                            if (cData.BiNormals.Count > 0)
                                            {
                                                cData.BiNormalIndices.Add(cData.Indices[i + vertexIndexDict["biNormal"]]);
                                            }

                                            if (cData.VertexColors.Count > 0)
                                            {
                                                cData.VertexColorIndices.Add(cData.Indices[i + vertexIndexDict["vertexColor"]]);
                                            }

                                            if (cData.VertexAlphas.Count > 0)
                                            {
                                                cData.VertexAlphaIndices.Add(cData.Indices[i + vertexIndexDict["vertexAlpha"]]);
                                            }

                                        }
                                        break;
                                    }
                                }
                            }
                            
                            cData.IndexStride = indexStride;
                            cData.TextureCoordinateStride = textureCoordinateStride;
                            cData.VertexColorStride = vertexColorStride;

                            if(!meshPartDataDictionary.ContainsKey(meshNum))
                            {
                                meshPartDataDictionary.Add(meshNum, new SortedDictionary<int, ColladaData>());
                            }

                            while(meshNames.Count <= meshNum)
                            {
                                meshNames.Add(new List<string>());
                            }

                            // If the current attribute is a mesh part
                            if (atr.Contains("."))
                            {
                                var meshPartNum = 0;
                                try
                                {
                                    // Get part number
                                    meshPartNum = int.Parse(atr.Substring(atr.LastIndexOf(".") + 1));

                                    while(meshNames[meshNum].Count <= meshPartNum)
                                    {
                                        meshNames[meshNum].Add("Unknown");
                                    }

                                    meshNames[meshNum][meshPartNum] = atr;
                                }
                                catch
                                {
                                    throw new Exception($"Could not read mesh number of mesh: {atr}\n\n" +
                                                    $"Please make sure your mesh name ends with the mesh/part number.");
                                }

                                if (meshPartDataDictionary[meshNum].ContainsKey(meshPartNum))
                                {
                                    throw new Exception($"There cannot be any duplicate meshes.  Duplicate: {atr}");
                                }

                                meshPartDataDictionary[meshNum].Add(meshPartNum, cData);
                            }
                            else
                            {
                                if(meshNames[meshNum].Count == 0)
                                {
                                    meshNames[meshNum].Add(atr);
                                } else
                                {
                                    meshNames[meshNum][0] = atr;
                                }

                                if (meshPartDataDictionary[meshNum].ContainsKey(0))
                                {
                                    throw new Exception($"There cannot be any duplicate meshes.  Duplicate: {atr}");
                                }

                                meshPartDataDictionary[meshNum].Add(0, cData);
                            }
                        }

                        // Read Controller
                        else if (reader.Name.Equals("controller"))
                        {
                            var atr = reader["id"];

                            if (atr.Contains("Controller"))
                            {
                                atr = atr.Replace("Controller", "-Controller");
                            }

                            ColladaData colladaData;

                            // If the collada file was saved in blender
                            if (toolType == ToolType.Blender)
                            {
                                while (reader.Read())
                                {
                                    if (reader.IsStartElement())
                                    {
                                        if (reader.Name.Equals("skin"))
                                        {
                                            var skinSource = reader["source"];
                                            atr = meshNameDict[skinSource.Substring(1, skinSource.Length - 1)];
                                            break;
                                        }
                                    }
                                }
                            }

                            var meshNum = int.Parse(atr.Substring(atr.LastIndexOf("_") + 1, 1));

                            // If it is a mesh part get mesh number
                            if (atr.Contains("."))
                            {
                                meshNum = int.Parse(atr.Substring(atr.LastIndexOf("_") + 1, atr.LastIndexOf(".") - (atr.LastIndexOf("_") + 1)));
                            }

                            var partDataDictionary = meshPartDataDictionary[meshNum];

                            // If it is a mesh part get part number, and get the Collada Data associated with it
                            if (atr.Contains("."))
                            {
                                if (toolType == ToolType.Blender)
                                {
                                    int meshPartNum = int.Parse(atr.Substring((atr.LastIndexOf(".") + 1), atr.Length - (atr.LastIndexOf(".") + 1)));
                                    colladaData = partDataDictionary[meshPartNum];
                                }
                                else
                                {
                                    int meshPartNum = int.Parse(atr.Substring((atr.LastIndexOf(".") + 1), atr.LastIndexOf("-") - (atr.LastIndexOf(".") + 1)));
                                    colladaData = partDataDictionary[meshPartNum];
                                }
                            }
                            else
                            {
                                colladaData = partDataDictionary[0];
                            }

                            while (reader.Read())
                            {
                                if (reader.IsStartElement())
                                {
                                    // Bone Strings 
                                    if (reader.Name.Contains("Name_array"))
                                    {
                                        colladaData.Bones = (string[])reader.ReadElementContentAs(typeof(string[]), null);

                                        var boneNum = 0;
                                        foreach (var bone in boneDict.Keys)
                                        {
                                            colladaData.BoneNumDictionary.Add(bone, boneNum);
                                            boneNum++;
                                        }

                                        foreach (var boneName in boneJointDict.Values)
                                        {
                                            if (!colladaData.BoneNumDictionary.ContainsKey(boneName))
                                            {
                                                colladaData.BoneNumDictionary.Add(boneName, boneNum);
                                                boneNum++;
                                            }
                                        }
                                    }

                                    if (reader.Name.Contains("float_array"))
                                    {
                                        // Bone Weights
                                        if (reader["id"].ToLower().Contains("weights-array"))
                                        {
                                            colladaData.BoneWeights.AddRange((float[])reader.ReadElementContentAs(typeof(float[]), null));
                                        }
                                    }

                                    // Bone(v) Counts per vertex
                                    else if (reader.Name.Equals("vcount"))
                                    {
                                        colladaData.Vcounts.AddRange((int[])reader.ReadElementContentAs(typeof(int[]), null));
                                    }

                                    // Bone Indices
                                    else if (reader.Name.Equals("v"))
                                    {
                                        var tempbIndex = (int[])reader.ReadElementContentAs(typeof(int[]), null);
                                        
                                        // Some saved dae files have swapped bone names, so we correct them
                                        for (var a = 0; a < tempbIndex.Length; a += 2)
                                        {
                                            var boneIndex = tempbIndex[a];
                                            var blendName = colladaData.Bones[boneIndex];

                                            var blendBoneName = boneJointDict[blendName];

                                            // Fix for bones with numbers at the end of the string
                                            var bString = Regex.Replace(blendBoneName, "[0-9]+$", string.Empty);

                                            if (!colladaData.MeshBoneNames.Contains(bString))
                                            {
                                                colladaData.MeshBoneNames.Add(bString);
                                            }

                                            colladaData.BoneIndices.Add(colladaData.BoneNumDictionary[bString]);

                                            colladaData.BoneIndices.Add(tempbIndex[a + 1]);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var dict = meshPartDataDictionary.ToDictionary(x => x.Key, x => x.Value.ToDictionary(y => y.Key, y => y.Value));

            try
            {
                foreach (var kv in meshPartDataDictionary)
                {
                    var group = new TTMeshGroup();
                    ttModel.MeshGroups.Add(group);
                    var parts = group.Parts;

                    var meshNum = kv.Key;
                    foreach (var kv2 in kv.Value)
                    {
                        var partNum = kv2.Key;
                        var part = new TTMeshPart();
                        parts.Add(part);
                        var cData = kv2.Value;

                        part.Name = meshNames[meshNum][partNum];

                        // Here we need to rip through the old ColladaData format,
                        // And rebuild the Vertex and Index lists into SE standard style.
                        // That means each vertex is fully qualified with each datapoint.

                        // The cleanest way to do this is to break down the list of triangle indicies
                        // into their own completely qualified datapoints, then rebuild then vertex list
                        // by effectively welding together identical indices.

                        var numVerts = cData.Positions.Count / 3;
                        var numIndices = cData.PositionIndices.Count;

                        // Keep us from having to constantly resize this.
                        part.Vertices.Capacity = numIndices;
                        part.TriangleIndices.Capacity = numIndices;

                        // Build the offset list for sanity to avoid expensive recalculations.
                        var weightEntryOffsets = new List<int>();
                        var offset = 0;
                        if (cData.Vcounts.Count != 0)
                        {
                            for (int i = 0; i < numVerts; i++)
                            {
                                weightEntryOffsets.Add(offset);
                                offset += (cData.Vcounts[i]) * 2;
                            }
                        }

                        // list of [vertex Id] => All the triangle indexes that reference it.
                        var verticesToIndexes = new List<List<int>>(new List<int>[numVerts]);
                        for (int i = 0; i < numVerts; i++)
                        {
                            // There's probably a better way to do the constructor to make this handled automatically, but whatever.
                            verticesToIndexes[i] = new List<int>();
                        }

                        // Collect all the tri indexes by vertex.
                        for (int i = 0; i < numIndices; i++)
                        {
                            var vertexId = cData.PositionIndices[i];
                            verticesToIndexes[vertexId].Add(i);
                        }

                        // Pre-fill the array so we can reference it by index.
                        part.TriangleIndices = new List<int>(new int[numIndices]);

                        // For every vert
                        for (int vi = 0; vi < numVerts; vi++)
                        {
                            var sharedVerts = new List<TTVertex>();

                            // And every Triangle Index used by that vert.
                            for (int ti = 0; ti < verticesToIndexes[vi].Count; ti++)
                            {
                                // Build a vertex using the data at this index.
                                var i = verticesToIndexes[vi][ti];
                                TTVertex vert = new TTVertex();

                                var start = (cData.PositionIndices[i]) * 3;
                                vert.Position = new Vector3(cData.Positions[start], cData.Positions[start + 1], cData.Positions[start + 2]);

                                // Make sure each data point actually exists, or use default.
                                if (cData.NormalIndices.Count > i)
                                {
                                    start = (cData.NormalIndices[i]) * 3;
                                    vert.Normal = new Vector3(cData.Normals[start], cData.Normals[start + 1], cData.Normals[start + 2]);
                                }

                                byte r = 255;
                                byte g = 255;
                                byte b = 255;
                                byte a = 255;

                                // Make sure each data point actually exists, or use default.
                                if (cData.VertexColorIndices.Count > i)
                                {
                                    start = (cData.VertexColorIndices[i]) * 3;
                                    r = (byte)(Math.Round(cData.VertexColors[start] * 255));
                                    g = (byte)(Math.Round(cData.VertexColors[start + 1] * 255));
                                    b = (byte)(Math.Round(cData.VertexColors[start + 2] * 255));
                                }


                                // Make sure each data point actually exists, or use default.
                                if (cData.TextureCoordinate0Indices.Count > i)
                                {
                                    start = (cData.TextureCoordinate0Indices[i]) * textureCoordinateStride;
                                    vert.UV1 = new Vector2(cData.TextureCoordinates0[start], cData.TextureCoordinates0[start + 1]);
                                }

                                // Make sure each data point actually exists, or use default.
                                if (cData.TextureCoordinate1Indices.Count > i)
                                {
                                    start = (cData.TextureCoordinate1Indices[i]) * textureCoordinateStride;
                                    vert.UV2 = new Vector2(cData.TextureCoordinates1[start], cData.TextureCoordinates1[start + 1]);
                                }


                                // Make sure each data point actually exists, or use default.
                                if (cData.VertexAlphaIndices.Count > i)
                                {
                                    start = (cData.VertexAlphaIndices[i]) * textureCoordinateStride;
                                    a = (byte)(Math.Round(cData.VertexAlphas[start] * 255));
                                }
                                vert.VertexColor = new byte[] { r, g, b, a };

                                if (cData.Vcounts.Count != 0)
                                {
                                    // Weights in DAE files are extremely annoying to manipulate, but here goes.
                                    var vertexId = cData.PositionIndices[i];
                                    var weightCount = cData.Vcounts[vertexId];
                                    for (var wi = 0; wi < weightCount; wi++)
                                    {
                                        start = weightEntryOffsets[vertexId] + (wi * 2);

                                        // The Weight Indexes are listed as [JointId WeightId] in pairs.
                                        var jointId = cData.BoneIndices[start];
                                        var weightId = cData.BoneIndices[start + 1];

                                        // Get the original name and weight.
                                        var boneName = cData.BoneNumDictionary.First(x => x.Value == jointId).Key;
                                        var weight = cData.BoneWeights[weightId];

                                        if (wi > 3)
                                        {
                                            // FFXIV only supports up to 4 bones per vertex, so here we have to decide which weight to cut off.
                                            var leastIdx = -1;
                                            var least = 99.0f;
                                            for(int id = 0; id < 4; id++)
                                            {
                                                var w = vert.Weights[id];
                                                if (w < least || leastIdx < 0)
                                                {
                                                    least = w;
                                                    leastIdx = id;
                                                }
                                            }

                                            // If this is a more major bone weight than our current least, replace it.
                                            if(weight > least)
                                            {
                                                wi = leastIdx;
                                            }
                                            else
                                            {
                                                break;
                                            }
                                        }


                                        // Add the bones to the group bone set at needed.
                                        if (!group.Bones.Contains(boneName))
                                        {
                                            group.Bones.Add(boneName);
                                        }


                                        // Convert them to the new id and byte format.
                                        byte newBoneId = (byte)group.Bones.IndexOf(boneName);
                                        byte xivWeight = (byte)(Math.Round(weight * 255));

                                        // Save them.
                                        vert.BoneIds[wi] = newBoneId;
                                        vert.Weights[wi] = xivWeight;
                                    }
                                }

                                // Now test to see if this vert is unique, or needs to be added.
                                var vertToUse = -1;
                                for(int si = 0; si < sharedVerts.Count; si++)
                                {
                                    if(sharedVerts[si] == vert)
                                    {
                                        vertToUse = si;
                                        break;
                                    }
                                }

                                if(vertToUse == -1)
                                {
                                    // This vertex is unique, and needs to be added to the array.
                                    sharedVerts.Add(vert);
                                    var lastIndex = sharedVerts.Count - 1;
                                    part.TriangleIndices[i] = (part.Vertices.Count + lastIndex);
                                } else
                                {
                                    // Vert already exists, just use the reference one.
                                    part.TriangleIndices[i] =(part.Vertices.Count + vertToUse);
                                }
                            }

                            part.Vertices.AddRange(sharedVerts);
                        }
                    }
                }
            } catch(Exception ex)
            {
                // Convenient throw to breakpoint on when debugging.
                throw ex;
            }

            ModelModifiers.MakeImportReady(ttModel);

            return ttModel;
        }

        /// <summary>
        /// A quick collada reader to obtain mesh and part numbers
        /// </summary>
        /// <param name="daeLocation">The location of the dae file</param>
        /// <returns>A dictionary containing the mesh number and its parts</returns>
        public (Dictionary<int, List<int>> MeshPartDictionary, List<string> BoneList) QuickColladaReader(DirectoryInfo daeLocation, XivMdl xivMdl)
        {
            var meshNameDict = new Dictionary<string, string>();
            var boneStringList = new List<string>();

            // A dictionary containing <Mesh Number, List<Mesh Parts>>
            var meshPartDictionary = new Dictionary<int, List<int>>();

            var uniqueBoneNames = new HashSet<string>(xivMdl.PathData.BoneList);

            // Reading Control Data to get bones that are used in model
            using (var reader = XmlReader.Create(daeLocation.FullName))
            {
                string[] bones = null;
                while (reader.Read())
                {
                    if (!reader.IsStartElement()) continue;
                    if (!reader.Name.Equals("controller")) continue;

                    while (reader.Read())
                    {
                        if (reader.IsStartElement())
                        {
                            if (reader.Name.Contains("Name_array"))
                            {
                                bones = (string[]) reader.ReadElementContentAs(typeof(string[]), null);
                            }

                            if (reader.Name.Equals("v"))
                            {
                                var uniqueJointIndices = new HashSet<int>();
                                var vData = (int[]) reader.ReadElementContentAs(typeof(int[]), null);

                                for (var a = 0; a < vData.Length; a += 2)
                                {
                                    uniqueJointIndices.Add(vData[a]);
                                }

                                if (bones != null)
                                {
                                    foreach (var uniqueJointIndex in uniqueJointIndices)
                                    {
                                        var bone = bones[uniqueJointIndex];
                                        var boneName = bone;

                                        if (!bone.Contains("joint"))
                                        {
                                            boneName = Regex.Replace(bone, "[0-9]+$", string.Empty);
                                        }

                                        uniqueBoneNames.Add(boneName);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Reading Bone Data
            using (var reader = XmlReader.Create(daeLocation.FullName))
            {
                while (reader.Read())
                {
                    if (!reader.IsStartElement()) continue;
                    if (!reader.Name.Equals("visual_scene")) continue;

                    while (reader.Read())
                    {
                        if (reader.IsStartElement())
                        {
                            if (reader.Name.Contains("node"))
                            {
                                var sid = reader["sid"];
                                if (sid != null)
                                {
                                    var name = reader["name"];
                                    if (name==null) continue;

                                    var boneString = Regex.Replace(name, "[0-9]+$", string.Empty);

                                    if (!uniqueBoneNames.Contains(sid))
                                    {
                                        continue;
                                    }

                                    // Throw an exception if there is a duplicate bone
                                    if (boneStringList.Contains(sid))
                                    {
                                        throw new Exception($"Model cannot contain duplicate bones. Duplicate found: {sid}");
                                    }

                                    if (!boneString.Substring(0, 2).Contains("n_") && !name.Substring(0, 2).Contains("j_")) continue;

                                    if (!boneStringList.Contains(boneString))
                                    {
                                        boneStringList.Add(boneString);
                                    }
                                }
                                else
                                {
                                    var name = reader["name"];
                                    if (name==null) continue;

                                    var boneString = Regex.Replace(name, "[0-9]+$", string.Empty);

                                    if (!uniqueBoneNames.Contains(boneString))
                                    {
                                        continue;
                                    }

                                    if (!boneString.Substring(0, 2).Contains("n_") && !name.Substring(0, 2).Contains("j_")) continue;

                                    if (!uniqueBoneNames.Contains(name))
                                    {
                                        boneStringList.Add(name);
                                    }
                                }
                            }
                        }
                    }
                    break;
                }
            }

            using (var reader = XmlReader.Create(daeLocation.FullName))
            {
                while (reader.Read())
                {
                    if (reader.IsStartElement())
                    {
                        // Read Geometry
                        if (reader.Name.Equals("geometry"))
                        {
                            var atr = reader["name"];
                            var id = reader["id"];

                            if (atr.Contains("Mesh"))
                            {
                                atr = atr.Replace("Mesh", string.Empty);
                            }

                            if (meshNameDict.ContainsKey(id))
                            {
                                throw new Exception($"Meshes cannot have duplicate names. Duplicate: {id}\n\n" +
                                                    $"Meshes must have unique numbers, make sure to rename them if any were added");
                            }

                            meshNameDict.Add(id, atr);

                            var meshNum = 0;

                            try
                            {
                                meshNum = int.Parse(atr.Substring(atr.LastIndexOf("_", StringComparison.Ordinal) + 1, 1));

                                // Determines whether the mesh has parts and gets the mesh number
                                if (atr.Contains("."))
                                {
                                    meshNum = int.Parse(atr.Substring(atr.LastIndexOf("_", StringComparison.Ordinal) + 1,
                                        atr.LastIndexOf(".", StringComparison.Ordinal) -
                                        (atr.LastIndexOf("_", StringComparison.Ordinal) + 1)));
                                }
                            }
                            catch
                            {
                                throw new Exception($"Could not read mesh number of mesh: {atr}\n\n" +
                                                    $"Please make sure your mesh name ends with the mesh/part number.");
                            }
                        
                            // If the current attribute is a mesh part
                            if (atr.Contains("."))
                            {
                                var meshPartNum = 0;
                                try
                                {
                                    // Get part number
                                    var numStr = atr.Substring(atr.LastIndexOf(".") + 1);
                                    numStr = numStr.EndsWith("Mesh", StringComparison.OrdinalIgnoreCase) ? numStr.Remove(numStr.Length - 4):numStr;
                                    meshPartNum = int.Parse(numStr);
                                }
                                catch
                                {
                                    throw new Exception($"Could not read mesh number of mesh: {atr}\n\n" +
                                                        $"Please make sure your mesh name ends with the mesh/part number.");
                                }                            

                                if (meshPartDictionary.ContainsKey(meshNum))
                                {
                                    if (meshPartDictionary[meshNum].Contains(meshPartNum))
                                    {
                                        throw new Exception($"There cannot be any duplicate meshes.  Duplicate: {atr}\n\n" +
                                                            $"Meshes must have unique numbers, make sure to rename them if any were added");
                                    }

                                    meshPartDictionary[meshNum].Add(meshPartNum);
                                }
                                else
                                {
                                    meshPartDictionary.Add(meshNum, new List<int> { meshPartNum });
                                }
                            }
                            else
                            {
                                if (meshPartDictionary.ContainsKey(meshNum))
                                {
                                    meshPartDictionary[meshNum].Add(0);
                                }
                                else
                                {
                                    meshPartDictionary.Add(meshNum, new List<int>{0});
                                }

                            }
                        }
                    }
                }
            }

            if (!meshPartDictionary.ContainsKey(0) || (meshPartDictionary.Keys.Max() + 1) != meshPartDictionary.Keys.Count)
            {
                var meshes = "";
                foreach (var key in meshPartDictionary.Keys)
                {
                    meshes += $"{key} ";
                }

                throw new Exception($"The DAE file does not contain consecutive mesh numbers.\nModels must have consecutive mesh numbers starting at 0.\n\nMesh Numbers Found: {meshes}");
            }

            return (meshPartDictionary, boneStringList);
        }

        /// <summary>
        /// Writes the XML assets to the xml writer
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        private void XMLassets(XmlWriter xmlWriter)
        {
            //<asset>
            xmlWriter.WriteStartElement("asset");

            //<contributor>
            xmlWriter.WriteStartElement("contributor");
            //<authoring_tool>
            xmlWriter.WriteStartElement("authoring_tool");
            xmlWriter.WriteString("FFXIV TexTools");
            xmlWriter.WriteEndElement();
            //</authoring_tool>
            //<tool_settings>
            xmlWriter.WriteStartElement("tool_settings");
            xmlWriter.WriteString(_pluginTarget);
            xmlWriter.WriteEndElement();
            //</tool_settings>
            //<tool_version>
            xmlWriter.WriteStartElement("tool_version");
            xmlWriter.WriteString(_appVersion);
            xmlWriter.WriteEndElement();
            //</tool_version>
            xmlWriter.WriteEndElement();
            //</contributor>

            //<created>
            xmlWriter.WriteStartElement("created");
            xmlWriter.WriteString(DateTime.Now.ToLongDateString());
            xmlWriter.WriteEndElement();
            //</created>

            //<unit>
            xmlWriter.WriteStartElement("unit");
            xmlWriter.WriteAttributeString("name", "meter");
            xmlWriter.WriteAttributeString("meter", "1"); // FFXIV uses Meters as the internal measure.
            xmlWriter.WriteEndElement();
            //</unit>

            //<up_axis>
            xmlWriter.WriteStartElement("up_axis");
            xmlWriter.WriteString("Y_UP");
            xmlWriter.WriteEndElement();
            //</up_axis>

            xmlWriter.WriteEndElement();
            //</asset>
        }

        /// <summary>
        /// Writes the xml images to be used in the collada file
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        /// <param name="modelName">The name of the model</param>
        /// <param name="meshCount">The amount of meshes</param>
        /// <param name="meshData">The list of mesh data for the model</param>
        private static void XMLimages(XmlWriter xmlWriter, string modelName, int meshCount, IReadOnlyList<MeshData> meshData)
        {
            //<library_images>
            xmlWriter.WriteStartElement("library_images");

            for (var i = 0; i < meshCount; i++)
            {
                //<image>
                xmlWriter.WriteStartElement("image");
                xmlWriter.WriteAttributeString("id", modelName + "_" + i + "_Diffuse_bmp");
                xmlWriter.WriteAttributeString("name", modelName + "_" + i + "_Diffuse_bmp");
                //<init_from>
                xmlWriter.WriteStartElement("init_from");
                xmlWriter.WriteString(modelName + "_" + meshData[i].MeshInfo.MaterialIndex.ToString() + "_Diffuse.bmp");
                xmlWriter.WriteEndElement();
                //</init_from>
                xmlWriter.WriteEndElement();
                //</image>
                //<image>
                xmlWriter.WriteStartElement("image");
                xmlWriter.WriteAttributeString("id", modelName + "_" + i + "_Normal_bmp");
                xmlWriter.WriteAttributeString("name", modelName + "_" + i + "_Normal_bmp");
                //<init_from>
                xmlWriter.WriteStartElement("init_from");
                xmlWriter.WriteString(modelName + "_" + meshData[i].MeshInfo.MaterialIndex.ToString() + "_Normal.bmp");
                xmlWriter.WriteEndElement();
                //</init_from>
                xmlWriter.WriteEndElement();
                //</image>

                // we don't include specular or alpha if the mesh has a body material
                if (meshData[i].IsBody) continue;

                //<image>
                xmlWriter.WriteStartElement("image");
                xmlWriter.WriteAttributeString("id", modelName + "_" + i + "_Specular_bmp");
                xmlWriter.WriteAttributeString("name", modelName + "_" + i + "_Specular_bmp");
                //<init_from>
                xmlWriter.WriteStartElement("init_from");
                xmlWriter.WriteString(modelName + "_" + meshData[i].MeshInfo.MaterialIndex.ToString() + "_Specular.bmp");
                xmlWriter.WriteEndElement();
                //</init_from>
                xmlWriter.WriteEndElement();
                //</image>
                //<image>
                xmlWriter.WriteStartElement("image");
                xmlWriter.WriteAttributeString("id", modelName + "_" + i + "_Alpha_bmp");
                xmlWriter.WriteAttributeString("name", modelName + "_" + i + "_Alpha_bmp");
                //<init_from>
                xmlWriter.WriteStartElement("init_from");
                xmlWriter.WriteString(modelName + "_" + meshData[i].MeshInfo.MaterialIndex.ToString() + "_Alpha.bmp");
                xmlWriter.WriteEndElement();
                //</init_from>
                xmlWriter.WriteEndElement();
                //</image>
            }

            xmlWriter.WriteEndElement();
            //</library_images>
        }

        /// <summary>
        /// Writes the xml effects to be used in the collada file
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        /// <param name="modelName">The name of the model</param>
        /// <param name="meshCount">The number of meshes</param>
        /// <param name="meshData">The list of mesh data for the model</param>
        private void XMLeffects(XmlWriter xmlWriter, string modelName, int meshCount, IReadOnlyList<MeshData> meshData)
        {
            //<library_effects>
            xmlWriter.WriteStartElement("library_effects");

            for (int i = 0; i < meshCount; i++)
            {
                //<effect>
                xmlWriter.WriteStartElement("effect");
                xmlWriter.WriteAttributeString("id", modelName + "_" + i);
                //<profile_COMMON>
                xmlWriter.WriteStartElement("profile_COMMON");
                //<newparam>
                xmlWriter.WriteStartElement("newparam");
                xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Diffuse_bmp-surface");
                //<surface>
                xmlWriter.WriteStartElement("surface");
                xmlWriter.WriteAttributeString("type", "2D");
                //<init_from>
                xmlWriter.WriteStartElement("init_from");
                xmlWriter.WriteString(modelName + "_" + i + "_Diffuse_bmp");
                xmlWriter.WriteEndElement();
                //</init_from>
                xmlWriter.WriteEndElement();
                //</surface>
                xmlWriter.WriteEndElement();
                //</newparam>
                //<newparam>
                xmlWriter.WriteStartElement("newparam");
                xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Diffuse_bmp-sampler");
                //<sampler2D>
                xmlWriter.WriteStartElement("sampler2D");
                //<source>
                xmlWriter.WriteStartElement("source");
                xmlWriter.WriteString(modelName + "_" + i + "_Diffuse_bmp-surface");
                xmlWriter.WriteEndElement();
                //</source>
                xmlWriter.WriteEndElement();
                //</sampler2D>
                xmlWriter.WriteEndElement();
                //</newparam>

                if (!meshData[i].IsBody)
                {
                    //<newparam>
                    xmlWriter.WriteStartElement("newparam");
                    xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Specular_bmp-surface");
                    //<surface>
                    xmlWriter.WriteStartElement("surface");
                    xmlWriter.WriteAttributeString("type", "2D");
                    //<init_from>
                    xmlWriter.WriteStartElement("init_from");
                    xmlWriter.WriteString(modelName + "_" + i + "_Specular_bmp");
                    xmlWriter.WriteEndElement();
                    //</init_from>
                    xmlWriter.WriteEndElement();
                    //</surface>
                    xmlWriter.WriteEndElement();
                    //</newparam>
                    //<newparam>
                    xmlWriter.WriteStartElement("newparam");
                    xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Specular_bmp-sampler");
                    //<sampler2D>
                    xmlWriter.WriteStartElement("sampler2D");
                    //<source>
                    xmlWriter.WriteStartElement("source");
                    xmlWriter.WriteString(modelName + "_" + i + "_Specular_bmp-surface");
                    xmlWriter.WriteEndElement();
                    //</source>
                    xmlWriter.WriteEndElement();
                    //</sampler2D>
                    xmlWriter.WriteEndElement();
                    //</newparam>
                }

                //<newparam>
                xmlWriter.WriteStartElement("newparam");
                xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Normal_bmp-surface");
                //<surface>
                xmlWriter.WriteStartElement("surface");
                xmlWriter.WriteAttributeString("type", "2D");
                //<init_from>
                xmlWriter.WriteStartElement("init_from");
                xmlWriter.WriteString(modelName + "_" + i + "_Normal_bmp");
                xmlWriter.WriteEndElement();
                //</init_from>
                xmlWriter.WriteEndElement();
                //</surface>
                xmlWriter.WriteEndElement();
                //</newparam>
                //<newparam>
                xmlWriter.WriteStartElement("newparam");
                xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Normal_bmp-sampler");
                //<sampler2D>
                xmlWriter.WriteStartElement("sampler2D");
                //<source>
                xmlWriter.WriteStartElement("source");
                xmlWriter.WriteString(modelName + "_" + i + "_Normal_bmp-surface");
                xmlWriter.WriteEndElement();
                //</source>
                xmlWriter.WriteEndElement();
                //</sampler2D>
                xmlWriter.WriteEndElement();
                //</newparam>

                if (!meshData[i].IsBody)
                {
                    //<newparam>
                    xmlWriter.WriteStartElement("newparam");
                    xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Alpha_bmp-surface");
                    //<surface>
                    xmlWriter.WriteStartElement("surface");
                    xmlWriter.WriteAttributeString("type", "2D");
                    //<init_from>
                    xmlWriter.WriteStartElement("init_from");
                    xmlWriter.WriteString(modelName + "_" + i + "_Alpha_bmp");
                    xmlWriter.WriteEndElement();
                    //</init_from>
                    xmlWriter.WriteEndElement();
                    //</surface>
                    xmlWriter.WriteEndElement();
                    //</newparam>
                    //<newparam>
                    xmlWriter.WriteStartElement("newparam");
                    xmlWriter.WriteAttributeString("sid", modelName + "_" + i + "_Alpha_bmp-sampler");
                    //<sampler2D>
                    xmlWriter.WriteStartElement("sampler2D");
                    //<source>
                    xmlWriter.WriteStartElement("source");
                    xmlWriter.WriteString(modelName + "_" + i + "_Alpha_bmp-surface");
                    xmlWriter.WriteEndElement();
                    //</source>
                    xmlWriter.WriteEndElement();
                    //</sampler2D>
                    xmlWriter.WriteEndElement();
                    //</newparam>
                }

                //<technique>
                xmlWriter.WriteStartElement("technique");
                xmlWriter.WriteAttributeString("sid", "common");
                //<phong>
                xmlWriter.WriteStartElement("phong");
                //<diffuse>
                xmlWriter.WriteStartElement("diffuse");
                //<texture>
                xmlWriter.WriteStartElement("texture");
                xmlWriter.WriteAttributeString("texture", modelName + "_" + i + "_Diffuse_bmp-sampler");
                xmlWriter.WriteAttributeString("texcoord", "geom-" + modelName + "_" + i + "-map1");
                xmlWriter.WriteEndElement();
                //</texture>
                xmlWriter.WriteEndElement();
                //</diffuse>

                if (!meshData[i].IsBody)
                {
                    //<specular>
                    xmlWriter.WriteStartElement("specular");
                    //<texture>
                    xmlWriter.WriteStartElement("texture");
                    xmlWriter.WriteAttributeString("texture", modelName + "_" + i + "_Specular_bmp-sampler");
                    xmlWriter.WriteAttributeString("texcoord", "geom-" + modelName + "_" + i + "-map1");
                    xmlWriter.WriteEndElement();
                    //</texture>
                    xmlWriter.WriteEndElement();
                    //</specular>

                    //<transparent>
                    xmlWriter.WriteStartElement("transparent");
                    if (_pluginTarget.Equals(XivStrings.AutodeskCollada))
                    {
                        xmlWriter.WriteAttributeString("opaque", "RGB_ZERO");
                    }
                    else
                    {
                        xmlWriter.WriteAttributeString("opaque", "A_ONE");
                    }

                    //<texture>
                    xmlWriter.WriteStartElement("texture");
                    xmlWriter.WriteAttributeString("texture", modelName + "_" + i + "_Alpha_bmp-sampler");
                    xmlWriter.WriteAttributeString("texcoord", "geom-" + modelName + "_" + i + "-map1");
                    xmlWriter.WriteEndElement();
                    //</texture>
                    xmlWriter.WriteEndElement();
                    //</transparent>
                }

                xmlWriter.WriteEndElement();
                //</phong>

                //<extra>
                xmlWriter.WriteStartElement("extra");
                //<technique>
                xmlWriter.WriteStartElement("technique");
                xmlWriter.WriteAttributeString("profile", "OpenCOLLADA3dsMax");

                if (!meshData[i].IsBody)
                {
                    //<specularLevel>
                    xmlWriter.WriteStartElement("specularLevel");
                    //<texture>
                    xmlWriter.WriteStartElement("texture");
                    xmlWriter.WriteAttributeString("texture", modelName + "_" + i + "_Specular_bmp-sampler");
                    xmlWriter.WriteAttributeString("texcoord", "geom-" + modelName + "_" + i + "-map1");
                    xmlWriter.WriteEndElement();
                    //</texture>
                    xmlWriter.WriteEndElement();
                    //</specularLevel>
                }

                //<bump>
                xmlWriter.WriteStartElement("bump");
                xmlWriter.WriteAttributeString("bumptype", "HEIGHTFIELD");
                //<texture>
                xmlWriter.WriteStartElement("texture");
                xmlWriter.WriteAttributeString("texture", modelName + "_" + i + "_Normal_bmp-sampler");
                xmlWriter.WriteAttributeString("texcoord", "geom-" + modelName + "_" + i + "-map1");
                xmlWriter.WriteEndElement();
                //</texture>
                xmlWriter.WriteEndElement();
                //</bump>
                xmlWriter.WriteEndElement();
                //</technique>
                xmlWriter.WriteEndElement();
                //</extra>
                xmlWriter.WriteEndElement();
                //</technique>
                xmlWriter.WriteEndElement();
                //</profile_COMMON>
                xmlWriter.WriteEndElement();
                //</effect>
            }

            xmlWriter.WriteEndElement();
            //</library_effects>
        }

        /// <summary>
        /// Writes the xml materials to be used in the collada file
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        /// <param name="modelName">The model name</param>
        /// <param name="meshCount">The number of meshes in the model</param>
        private static void XMLmaterials(XmlWriter xmlWriter, string modelName, int meshCount)
        {
            //<library_materials>
            xmlWriter.WriteStartElement("library_materials");
            for (var i = 0; i < meshCount; i++)
            {
                //<material>
                xmlWriter.WriteStartElement("material");
                xmlWriter.WriteAttributeString("id", modelName + "_" + i + "-material");
                xmlWriter.WriteAttributeString("name", modelName + "_" + i);
                //<instance_effect>
                xmlWriter.WriteStartElement("instance_effect");
                xmlWriter.WriteAttributeString("url", "#" + modelName + "_" + i);
                xmlWriter.WriteEndElement();
                //</instance_effect>
                xmlWriter.WriteEndElement();
                //</material>
            }

            xmlWriter.WriteEndElement();
            //</library_materials>
        }


        /// <summary>
        /// Writes the xml geometries to be used in the collada file
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        /// <param name="modelName">The model name</param>
        /// <param name="meshList">The list of meshes in the model</param>
        private void XMLgeometries(XmlWriter xmlWriter, string modelName, IReadOnlyList<MeshData> meshList)
        {
            //<library_geometries>
            xmlWriter.WriteStartElement("library_geometries");

            for (var i = 0; i < meshList.Count; i++)
            {
                // Only write geometry data if there are positions in the list
                if (meshList[i].VertexData.Positions.Count <= 0) continue;

                var prevIndexCount = 0;
                var totalVertices = 0;
                var meshPartCount = meshList[i].MeshPartList.Count;
                var noMeshParts = false;

                if (meshPartCount == 0)
                {
                    meshPartCount = 1;
                    noMeshParts = true;
                }

                for (var j = 0; j < meshPartCount; j++)
                {
                    var indexCount = 0;
                    if (noMeshParts)
                    {
                        indexCount = meshList[i].MeshInfo.IndexCount;
                    }
                    else
                    {
                        indexCount = meshList[i].MeshPartList[j].IndexCount;
                    }

                    // Only write geometry data if there are indices for the positions
                    if (indexCount <= 0) continue;

                    var indexList = new List<int>();
                    var indexHashSet = new HashSet<int>();

                    indexList = new List<int>(meshList[i].VertexData.Indices.GetRange(prevIndexCount, indexCount));

                    foreach (var index in indexList)
                    {
                        if (index > totalVertices)
                        {
                            indexHashSet.Add(index);
                        }
                    }

                    var totalCount = indexHashSet.Count + 1;

                    var partString = "." + j;

                    if (j == 0)
                    {
                        partString = "";
                    }

                    //<geometry>
                    xmlWriter.WriteStartElement("geometry");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString);
                    xmlWriter.WriteAttributeString("name", modelName + "_" + i + partString);
                    //<mesh>
                    xmlWriter.WriteStartElement("mesh");

                    /*
                     * --------------------
                     * Positions
                     * --------------------
                     */

                    //<source>
                    xmlWriter.WriteStartElement("source");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-positions");
                    //<float_array>
                    xmlWriter.WriteStartElement("float_array");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-positions-array");
                    xmlWriter.WriteAttributeString("count", (totalCount * 3).ToString());

                    var positions = meshList[i].VertexData.Positions.GetRange(totalVertices, totalCount);

                    foreach (var v in positions)
                    {
                        xmlWriter.WriteString((v.X).ToString() + " " + (v.Y).ToString() + " " + (v.Z).ToString() + " ");
                    }

                    xmlWriter.WriteEndElement();
                    //</float_array>

                    //<technique_common>
                    xmlWriter.WriteStartElement("technique_common");
                    //<accessor>
                    xmlWriter.WriteStartElement("accessor");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-positions-array");
                    xmlWriter.WriteAttributeString("count", totalCount.ToString());
                    xmlWriter.WriteAttributeString("stride", "3");

                    //<param>
                    xmlWriter.WriteStartElement("param");
                    xmlWriter.WriteAttributeString("name", "X");
                    xmlWriter.WriteAttributeString("type", "float");
                    xmlWriter.WriteEndElement();
                    //</param>

                    //<param>
                    xmlWriter.WriteStartElement("param");
                    xmlWriter.WriteAttributeString("name", "Y");
                    xmlWriter.WriteAttributeString("type", "float");
                    xmlWriter.WriteEndElement();
                    //</param>

                    //<param>
                    xmlWriter.WriteStartElement("param");
                    xmlWriter.WriteAttributeString("name", "Z");
                    xmlWriter.WriteAttributeString("type", "float");
                    xmlWriter.WriteEndElement();
                    //</param>

                    xmlWriter.WriteEndElement();
                    //</accessor>
                    xmlWriter.WriteEndElement();
                    //</technique_common>
                    xmlWriter.WriteEndElement();
                    //</source>

                    /*
                     * --------------------
                     * Normals
                     * --------------------
                     */
                    if (meshList[i].VertexData.Normals != null && meshList[i].VertexData.Normals.Count > 0)
                    {
                        //<source>
                        xmlWriter.WriteStartElement("source");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-normals");
                        //<float_array>
                        xmlWriter.WriteStartElement("float_array");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-normals-array");
                        xmlWriter.WriteAttributeString("count", (totalCount * 3).ToString());

                        var normals = meshList[i].VertexData.Normals.GetRange(totalVertices, totalCount);

                        foreach (var n in normals)
                        {
                            xmlWriter.WriteString(n.X.ToString() + " " + n.Y.ToString() + " " + n.Z.ToString() + " ");
                        }

                        xmlWriter.WriteEndElement();
                        //</float_array>

                        //<technique_common>
                        xmlWriter.WriteStartElement("technique_common");
                        //<accessor>
                        xmlWriter.WriteStartElement("accessor");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-normals-array");
                        xmlWriter.WriteAttributeString("count", (totalCount.ToString()));
                        xmlWriter.WriteAttributeString("stride", "3");

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "X");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "Y");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "Z");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        xmlWriter.WriteEndElement();
                        //</accessor>
                        xmlWriter.WriteEndElement();
                        //</technique_common>
                        xmlWriter.WriteEndElement();
                        //</source>
                    }

                    /*
                     * --------------------
                     * Vertex Colors
                     * --------------------
                     */

                    if (meshList[i].VertexData.Colors != null && meshList[i].VertexData.Colors.Count > 0)
                    {
                        var vertexColors = meshList[i].VertexData.Colors.GetRange(totalVertices, totalCount);

                        // Indexcount <= 3 because the Autodesk importer crashes when there is only a single tri for whatever reason
                        if (!(indexCount <= 3 && _pluginTarget.Equals(XivStrings.AutodeskCollada)))
                        {
                            //<source>
                            xmlWriter.WriteStartElement("source");
                            xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-col0");
                            //<float_array>
                            xmlWriter.WriteStartElement("float_array");
                            xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-col0-array");
                            xmlWriter.WriteAttributeString("count", (totalCount * 3).ToString());

                            foreach (var vc in vertexColors)
                            {
                                var red = vc.R / 255.0f;
                                var green = vc.G / 255.0f;
                                var blue = vc.B / 255.0f;
                                //float a = vc.A / 255.0f;

                                xmlWriter.WriteString(red.ToString() + " " + green.ToString() + " " + blue.ToString() + " ");
                            }

                            xmlWriter.WriteEndElement();
                            //</float_array>

                            //<technique_common>
                            xmlWriter.WriteStartElement("technique_common");
                            //<accessor>
                            xmlWriter.WriteStartElement("accessor");
                            xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-col0-array");
                            xmlWriter.WriteAttributeString("count", totalCount.ToString());
                            xmlWriter.WriteAttributeString("stride", "3");

                            //<param>
                            xmlWriter.WriteStartElement("param");
                            xmlWriter.WriteAttributeString("name", "R");
                            xmlWriter.WriteAttributeString("type", "float");
                            xmlWriter.WriteEndElement();
                            //</param>

                            //<param>
                            xmlWriter.WriteStartElement("param");
                            xmlWriter.WriteAttributeString("name", "G");
                            xmlWriter.WriteAttributeString("type", "float");
                            xmlWriter.WriteEndElement();
                            //</param>

                            //<param>
                            xmlWriter.WriteStartElement("param");
                            xmlWriter.WriteAttributeString("name", "B");
                            xmlWriter.WriteAttributeString("type", "float");
                            xmlWriter.WriteEndElement();
                            //</param>

                            //<param>
                            /*xmlWriter.WriteStartElement("param");
                            xmlWriter.WriteAttributeString("name", "A");
                            xmlWriter.WriteAttributeString("type", "float");
                            xmlWriter.WriteEndElement();*/
                            //</param>

                            xmlWriter.WriteEndElement();
                            //</accessor>
                            xmlWriter.WriteEndElement();
                            //</technique_common>
                            xmlWriter.WriteEndElement();
                            //</source>
                        }
                    }


                    /*
                     * --------------------
                     * Primary Texture Coordinates
                     * --------------------
                     */

                    if (meshList[i].VertexData.TextureCoordinates0 != null && meshList[i].VertexData.TextureCoordinates0.Count > 0)
                    {
                        //<source>
                        xmlWriter.WriteStartElement("source");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map0");
                        //<float_array>
                        xmlWriter.WriteStartElement("float_array");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map0-array");
                        xmlWriter.WriteAttributeString("count", (totalCount * 2).ToString());

                        var texCoords = meshList[i].VertexData.TextureCoordinates0.GetRange(totalVertices, totalCount);

                        foreach (var tc in texCoords)
                        {
                            xmlWriter.WriteString(tc.X.ToString() + " " + (tc.Y * -1).ToString() + " ");
                        }

                        xmlWriter.WriteEndElement();
                        //</float_array>

                        //<technique_common>
                        xmlWriter.WriteStartElement("technique_common");
                        //<accessor>
                        xmlWriter.WriteStartElement("accessor");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map0-array");
                        xmlWriter.WriteAttributeString("count", totalCount.ToString());
                        xmlWriter.WriteAttributeString("stride", "2");

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "S");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "T");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        xmlWriter.WriteEndElement();
                        //</accessor>
                        xmlWriter.WriteEndElement();
                        //</technique_common>
                        xmlWriter.WriteEndElement();
                        //</source>
                    }

                    /*
                     * --------------------
                     * Secondary Texture Coordinates
                     * --------------------
                     */

                    if (meshList[i].VertexData.TextureCoordinates1 != null && meshList[i].VertexData.TextureCoordinates1.Count > 0)
                    {
                        //<source>
                        xmlWriter.WriteStartElement("source");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map1");
                        //<float_array>
                        xmlWriter.WriteStartElement("float_array");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map1-array");
                        xmlWriter.WriteAttributeString("count", (totalCount * 2).ToString());

                        var texCoords2 = meshList[i].VertexData.TextureCoordinates1.GetRange(totalVertices, totalCount);

                        foreach (var tc in texCoords2)
                        {
                            xmlWriter.WriteString(tc.X.ToString() + " " + (tc.Y * -1).ToString() + " ");
                        }

                        xmlWriter.WriteEndElement();
                        //</float_array>

                        //<technique_common>
                        xmlWriter.WriteStartElement("technique_common");
                        //<accessor>
                        xmlWriter.WriteStartElement("accessor");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map1-array");
                        xmlWriter.WriteAttributeString("count", totalCount.ToString());
                        xmlWriter.WriteAttributeString("stride", "2");

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "S");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "T");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        xmlWriter.WriteEndElement();
                        //</accessor>
                        xmlWriter.WriteEndElement();
                        //</technique_common>
                        xmlWriter.WriteEndElement();
                        //</source>
                    }

                    /*
                     * --------------------
                     * Vertex Alpha - UV Work-Around
                     * --------------------
                     */

                    if (meshList[i].VertexData.Colors != null && meshList[i].VertexData.Colors.Count > 0)
                    {
                        // Indexcount <= 3 because the Autodesk importer crashes when there is only a single tri for whatever reason
                        if (!(indexCount <= 3 && _pluginTarget.Equals(XivStrings.AutodeskCollada)))
                        {
                            //<source>
                            xmlWriter.WriteStartElement("source");
                            xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map2");
                            //<float_array>
                            xmlWriter.WriteStartElement("float_array");
                            xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map2-array");
                            xmlWriter.WriteAttributeString("count", (totalCount * 2).ToString());

                            var vertexColors = meshList[i].VertexData.Colors.GetRange(totalVertices, totalCount);

                            foreach (var vc in vertexColors)
                            {
                                var a = vc.A / 255.0f;

                                // Use the UV S channel for the Alpha data, since OpenCollada doesn't play with it nicely.
                                xmlWriter.WriteString(a.ToString() + " 0 ");
                            }

                            xmlWriter.WriteEndElement();
                            //</float_array>

                            //<technique_common>
                            xmlWriter.WriteStartElement("technique_common");
                            //<accessor>
                            xmlWriter.WriteStartElement("accessor");
                            xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map2-array");
                            xmlWriter.WriteAttributeString("count", totalCount.ToString());
                            xmlWriter.WriteAttributeString("stride", "2");

                            //<param>
                            xmlWriter.WriteStartElement("param");
                            xmlWriter.WriteAttributeString("name", "S");
                            xmlWriter.WriteAttributeString("type", "float");
                            xmlWriter.WriteEndElement();
                            //</param>

                            //<param>
                            xmlWriter.WriteStartElement("param");
                            xmlWriter.WriteAttributeString("name", "T");
                            xmlWriter.WriteAttributeString("type", "float");
                            xmlWriter.WriteEndElement();
                            //</param>

                            xmlWriter.WriteEndElement();
                            //</accessor>
                            xmlWriter.WriteEndElement();
                            //</technique_common>
                            xmlWriter.WriteEndElement();
                            //</source>
                        }
                    }


                    /*
                     * --------------------
                     * Tangents
                     * --------------------
                     */

                    if (meshList[i].VertexData.Tangents != null && meshList[i].VertexData.Tangents.Count > 0)
                    {
                        //<source>
                        xmlWriter.WriteStartElement("source");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map0-textangents");
                        //<float_array>
                        xmlWriter.WriteStartElement("float_array");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map0-textangents-array");
                        xmlWriter.WriteAttributeString("count", (totalCount * 3).ToString());

                        var tangents = meshList[i].VertexData.Tangents.GetRange(totalVertices, totalCount);

                        foreach (var tan in tangents)
                        {
                            xmlWriter.WriteString(tan.X.ToString() + " " + tan.Y.ToString() + " " + tan.Z.ToString() + " ");
                        }

                        xmlWriter.WriteEndElement();
                        //</float_array>

                        //<technique_common>
                        xmlWriter.WriteStartElement("technique_common");
                        //<accessor>
                        xmlWriter.WriteStartElement("accessor");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map0-textangents-array");
                        xmlWriter.WriteAttributeString("count", totalCount.ToString());
                        xmlWriter.WriteAttributeString("stride", "3");

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "X");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "Y");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "Z");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        xmlWriter.WriteEndElement();
                        //</accessor>
                        xmlWriter.WriteEndElement();
                        //</technique_common>
                        xmlWriter.WriteEndElement();
                        //</source>
                    }

                    /*
                     * --------------------
                     * BiNormals
                     * --------------------
                     */

                    if (meshList[i].VertexData.BiNormals != null && meshList[i].VertexData.BiNormals.Count > 0)
                    {
                        //<source>
                        xmlWriter.WriteStartElement("source");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map0-texbinormals");
                        //<float_array>
                        xmlWriter.WriteStartElement("float_array");
                        xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-map0-texbinormals-array");
                        xmlWriter.WriteAttributeString("count", (totalCount * 3).ToString());

                        var biNormals = meshList[i].VertexData.BiNormals.GetRange(totalVertices, totalCount);

                        foreach (var bn in biNormals)
                        {
                            xmlWriter.WriteString(bn.X.ToString() + " " + bn.Y.ToString() + " " + bn.Z.ToString() + " ");
                        }

                        xmlWriter.WriteEndElement();
                        //</float_array>

                        //<technique_common>
                        xmlWriter.WriteStartElement("technique_common");
                        //<accessor>
                        xmlWriter.WriteStartElement("accessor");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map0-texbinormals-array");
                        xmlWriter.WriteAttributeString("count", totalCount.ToString());
                        xmlWriter.WriteAttributeString("stride", "3");

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "X");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "Y");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        //<param>
                        xmlWriter.WriteStartElement("param");
                        xmlWriter.WriteAttributeString("name", "Z");
                        xmlWriter.WriteAttributeString("type", "float");
                        xmlWriter.WriteEndElement();
                        //</param>

                        xmlWriter.WriteEndElement();
                        //</accessor>
                        xmlWriter.WriteEndElement();
                        //</technique_common>
                        xmlWriter.WriteEndElement();
                        //</source>

                    }


                    //<vertices>
                    xmlWriter.WriteStartElement("vertices");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-vertices");
                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "POSITION");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-positions");
                    xmlWriter.WriteEndElement();
                    //</input>
                    xmlWriter.WriteEndElement();
                    //</vertices>


                    //<triangles>
                    xmlWriter.WriteStartElement("triangles");
                    xmlWriter.WriteAttributeString("material", modelName + "_" + i);
                    xmlWriter.WriteAttributeString("count", (indexCount / 3).ToString());
                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "VERTEX");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-vertices");
                    xmlWriter.WriteAttributeString("offset", "0");
                    xmlWriter.WriteEndElement();
                    //</input>

                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "NORMAL");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-normals");
                    xmlWriter.WriteAttributeString("offset", "1");
                    xmlWriter.WriteEndElement();
                    //</input>

                    // Indexcount <= 3 because the Autodesk importer crashes when there is only a single tri for whatever reason
                    if (!(indexCount <= 3 && _pluginTarget.Equals(XivStrings.AutodeskCollada)))
                    {
                        if (meshList[i].VertexData.Colors.Count > 0)
                        {
                            //<input>
                            xmlWriter.WriteStartElement("input");
                            xmlWriter.WriteAttributeString("semantic", "COLOR");
                            xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-col0");
                            xmlWriter.WriteAttributeString("offset", "2");
                            xmlWriter.WriteAttributeString("set", "0");
                            xmlWriter.WriteEndElement();
                            //</input>
                        }
                    }

                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "TEXCOORD");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map0");
                    xmlWriter.WriteAttributeString("offset", "2");

                    if (_pluginTarget.Equals(XivStrings.AutodeskCollada))
                    {
                        xmlWriter.WriteAttributeString("set", "0");
                    }
                    else
                    {
                        xmlWriter.WriteAttributeString("set", "1");
                    }

                    xmlWriter.WriteEndElement();
                    //</input>

                    var hasTexCoord1 = false;
                    if (meshList[i].VertexData.TextureCoordinates1 != null &&
                        meshList[i].VertexData.TextureCoordinates1.Count > 0)
                    {

                        //<input>
                        xmlWriter.WriteStartElement("input");
                        xmlWriter.WriteAttributeString("semantic", "TEXCOORD");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map1");
                        xmlWriter.WriteAttributeString("offset", "2");

                        if (_pluginTarget.Equals(XivStrings.AutodeskCollada))
                        {
                            xmlWriter.WriteAttributeString("set", "1");
                        }
                        else
                        {
                            xmlWriter.WriteAttributeString("set", "2");
                        }
                        xmlWriter.WriteEndElement();
                        //</input>

                        hasTexCoord1 = true;
                    }

                    if (!(indexCount <= 3 && _pluginTarget.Equals(XivStrings.AutodeskCollada)))
                    {
                        if (meshList[i].VertexData.Colors.Count > 0)
                        {
                            //<input>
                            xmlWriter.WriteStartElement("input");
                            xmlWriter.WriteAttributeString("semantic", "TEXCOORD");
                            xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map2");
                            xmlWriter.WriteAttributeString("offset", "2");
                            if (_pluginTarget == XivStrings.AutodeskCollada)
                            {
                                xmlWriter.WriteAttributeString("set", hasTexCoord1 ? "2" : "1");
                            }
                            else
                            {
                                xmlWriter.WriteAttributeString("set", hasTexCoord1 ? "3" : "2");
                            }
                            xmlWriter.WriteEndElement();
                            //</input>
                        }
                    }

                    var pCount = 3;

                    if (meshList[i].VertexData.Tangents != null && meshList[i].VertexData.Tangents.Count > 0)
                    {
                        //<input>
                        xmlWriter.WriteStartElement("input");
                        xmlWriter.WriteAttributeString("semantic", "TEXTANGENT");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map0-textangents");
                        xmlWriter.WriteAttributeString("offset", "3");
                        xmlWriter.WriteAttributeString("set", "1");
                        xmlWriter.WriteEndElement();
                        //</input>

                        pCount++;
                    }

                    if (meshList[i].VertexData.BiNormals != null && meshList[i].VertexData.BiNormals.Count > 0)
                    {
                        //<input>
                        xmlWriter.WriteStartElement("input");
                        xmlWriter.WriteAttributeString("semantic", "TEXBINORMAL");
                        xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-map0-texbinormals");
                        xmlWriter.WriteAttributeString("offset", "3");
                        xmlWriter.WriteAttributeString("set", "1");
                        xmlWriter.WriteEndElement();
                        //</input>

                        pCount++;
                    }

                    //<p>
                    xmlWriter.WriteStartElement("p");
                    foreach (var ind in indexList)
                    {
                        int p = ind - totalVertices;

                        if (p >= 0)
                        {
                            if (pCount == 3)
                            {
                                xmlWriter.WriteString(p + " " + p + " " + p + " ");
                            }
                            else
                            {
                                xmlWriter.WriteString(p + " " + p + " " + p + " " + p + " ");
                            }
                        }
                    }
                    xmlWriter.WriteEndElement();
                    //</p>

                    xmlWriter.WriteEndElement();
                    //</triangles>
                    xmlWriter.WriteEndElement();
                    //</mesh>
                    xmlWriter.WriteEndElement();
                    //</geometry>

                    prevIndexCount += indexCount;
                    totalVertices += totalCount;
                }

            }

            xmlWriter.WriteEndElement();
            //</library_geometries>
        }

        /// <summary>
        /// Writes the xml controllers to be used in the collada file
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        /// <param name="modelName">The name of the model</param>
        /// <param name="meshDataList">The list of mesh data</param>
        /// <param name="skelDict">The dictionary of skeleton data</param>
        /// <param name="modelData">The model data</param>
        private static void XMLcontrollers(XmlWriter xmlWriter, string modelName, IReadOnlyList<MeshData> meshDataList, IReadOnlyDictionary<string, SkeletonData> skelDict, XivMdl modelData)
        {

            //<library_controllers>
            xmlWriter.WriteStartElement("library_controllers");
            for (var i = 0; i < meshDataList.Count; i++)
            {
                // only write controller data if bone weights exist for this mesh
                if (meshDataList[i].VertexData.BoneWeights.Count <= 0) continue;

                var prevIndexCount = 0;

                for (var j = 0; j < meshDataList[i].MeshPartList.Count; j++)
                {
                    var indexCount = meshDataList[i].MeshPartList[j].IndexCount;

                    // only write controller data if there are indices present for this mesh
                    if (indexCount <= 0) continue;

                    var indexList = meshDataList[i].VertexData.Indices.GetRange(prevIndexCount, indexCount);

                    var indexHashSet = new HashSet<int>();

                    foreach (var index in indexList)
                    {
                        indexHashSet.Add(index);
                    }

                    var indexHashCount = indexHashSet.Count;

                    var vCounts = new List<int>();
                    var bwList = new List<float>();
                    var biList = new List<int>();

                    foreach (var index in indexHashSet)
                    {
                        var bw = meshDataList[i].VertexData.BoneWeights[index];
                        var bi = meshDataList[i].VertexData.BoneIndices[index];

                        var count = 0;
                        for (var a = 0; a < 4; a++)
                        {
                            if (!(bw[a] > 0)) continue;

                            bwList.Add(bw[a]);
                            biList.Add(bi[a]);
                            count++;
                        }

                        vCounts.Add(count);
                    }

                    var partString = "." + j;

                    if (j == 0)
                    {
                        partString = "";
                    }

                    //<controller>
                    xmlWriter.WriteStartElement("controller");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-skin1");
                    //<skin>
                    xmlWriter.WriteStartElement("skin");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString);
                    //<bind_shape_matrix>
                    xmlWriter.WriteStartElement("bind_shape_matrix");
                    xmlWriter.WriteString("1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1");
                    xmlWriter.WriteEndElement();
                    //</bind_shape_matrix>

                    //<source>
                    xmlWriter.WriteStartElement("source");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-skin1-joints");
                    //<Name_array>
                    xmlWriter.WriteStartElement("Name_array");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-skin1-joints-array");
                    xmlWriter.WriteAttributeString("count", modelData.PathData.BoneList.Count.ToString());

                    foreach (var b in modelData.PathData.BoneList)
                    {
                        xmlWriter.WriteString(b + " ");
                    }
                    xmlWriter.WriteEndElement();
                    //</Name_array>

                    //<technique_common>
                    xmlWriter.WriteStartElement("technique_common");
                    //<accessor>
                    xmlWriter.WriteStartElement("accessor");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-skin1-joints-array");
                    xmlWriter.WriteAttributeString("count", modelData.PathData.BoneList.Count.ToString());
                    xmlWriter.WriteAttributeString("stride", "1");
                    //<param>
                    xmlWriter.WriteStartElement("param");
                    xmlWriter.WriteAttributeString("name", "JOINT");
                    xmlWriter.WriteAttributeString("type", "name");
                    xmlWriter.WriteEndElement();
                    //</param>
                    xmlWriter.WriteEndElement();
                    //</accessor>
                    xmlWriter.WriteEndElement();
                    //</technique_common>
                    xmlWriter.WriteEndElement();
                    //</source>


                    //<source>
                    xmlWriter.WriteStartElement("source");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-skin1-bind_poses");
                    //<Name_array>
                    xmlWriter.WriteStartElement("float_array");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-skin1-bind_poses-array");
                    xmlWriter.WriteAttributeString("count", (16 * modelData.PathData.BoneList.Count).ToString());

                    for (var m = 0; m < modelData.PathData.BoneList.Count; m++)
                    {
                        try
                        {
                            var matrix = new Matrix(skelDict[modelData.PathData.BoneList[m]].InversePoseMatrix);
                            //matrix = Matrix.Identity;

                            xmlWriter.WriteString(matrix.Column1.X + " " + matrix.Column1.Y + " " + matrix.Column1.Z + " " + (matrix.Column1.W) + " ");
                            xmlWriter.WriteString(matrix.Column2.X + " " + matrix.Column2.Y + " " + matrix.Column2.Z + " " + (matrix.Column2.W) + " ");
                            xmlWriter.WriteString(matrix.Column3.X + " " + matrix.Column3.Y + " " + matrix.Column3.Z + " " + (matrix.Column3.W) + " ");
                            xmlWriter.WriteString(matrix.Column4.X + " " + matrix.Column4.Y + " " + matrix.Column4.Z + " " + (matrix.Column4.W) + " ");
                        }
                        catch
                        {
                            Debug.WriteLine("Error at " + modelData.PathData.BoneList[m]);
                        }

                    }
                    xmlWriter.WriteEndElement();
                    //</Name_array>

                    //<technique_common>
                    xmlWriter.WriteStartElement("technique_common");
                    //<accessor>
                    xmlWriter.WriteStartElement("accessor");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-skin1-bind_poses-array");
                    xmlWriter.WriteAttributeString("count", modelData.PathData.BoneList.Count.ToString());
                    xmlWriter.WriteAttributeString("stride", "16");
                    //<param>
                    xmlWriter.WriteStartElement("param");
                    xmlWriter.WriteAttributeString("name", "TRANSFORM");
                    xmlWriter.WriteAttributeString("type", "float4x4");
                    xmlWriter.WriteEndElement();
                    //</param>
                    xmlWriter.WriteEndElement();
                    //</accessor>
                    xmlWriter.WriteEndElement();
                    //</technique_common>
                    xmlWriter.WriteEndElement();
                    //</source>


                    //<source>
                    xmlWriter.WriteStartElement("source");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-skin1-weights");
                    //<Name_array>
                    xmlWriter.WriteStartElement("float_array");
                    xmlWriter.WriteAttributeString("id", "geom-" + modelName + "_" + i + partString + "-skin1-weights-array");
                    xmlWriter.WriteAttributeString("count", bwList.Count.ToString());

                    foreach (var bw in bwList)
                    {
                        xmlWriter.WriteString(bw + " ");

                    }

                    xmlWriter.WriteEndElement();
                    //</Name_array>

                    //<technique_common>
                    xmlWriter.WriteStartElement("technique_common");
                    //<accessor>
                    xmlWriter.WriteStartElement("accessor");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-skin1-weights-array");
                    xmlWriter.WriteAttributeString("count", bwList.Count.ToString());
                    xmlWriter.WriteAttributeString("stride", "1");
                    //<param>
                    xmlWriter.WriteStartElement("param");
                    xmlWriter.WriteAttributeString("name", "WEIGHT");
                    xmlWriter.WriteAttributeString("type", "float");
                    xmlWriter.WriteEndElement();
                    //</param>
                    xmlWriter.WriteEndElement();
                    //</accessor>
                    xmlWriter.WriteEndElement();
                    //</technique_common>
                    xmlWriter.WriteEndElement();
                    //</source>

                    //<joints>
                    xmlWriter.WriteStartElement("joints");
                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "JOINT");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-skin1-joints");
                    xmlWriter.WriteEndElement();
                    //</input>

                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "INV_BIND_MATRIX");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-skin1-bind_poses");
                    xmlWriter.WriteEndElement();
                    //</input>
                    xmlWriter.WriteEndElement();
                    //</joints>

                    //<vertex_weights>
                    xmlWriter.WriteStartElement("vertex_weights");
                    xmlWriter.WriteAttributeString("count", bwList.Count.ToString());
                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "JOINT");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-skin1-joints");
                    xmlWriter.WriteAttributeString("offset", "0");
                    xmlWriter.WriteEndElement();
                    //</input>
                    //<input>
                    xmlWriter.WriteStartElement("input");
                    xmlWriter.WriteAttributeString("semantic", "WEIGHT");
                    xmlWriter.WriteAttributeString("source", "#geom-" + modelName + "_" + i + partString + "-skin1-weights");
                    xmlWriter.WriteAttributeString("offset", "1");
                    xmlWriter.WriteEndElement();
                    //</input>
                    //<vcount>
                    xmlWriter.WriteStartElement("vcount");

                    foreach (var vc in vCounts)
                    {
                        xmlWriter.WriteString(vc + " ");
                    }

                    xmlWriter.WriteEndElement();
                    //</vcount>

                    var bs = meshDataList[i].MeshInfo.BoneSetIndex;
                    var boneSet = modelData.MeshBoneSets[bs].BoneIndices;

                    //<v>
                    xmlWriter.WriteStartElement("v");
                    var blin = 0;

                    foreach (var bi in biList)
                    {
                        xmlWriter.WriteString(boneSet[bi] + " " + blin + " ");
                        blin++;
                    }

                    xmlWriter.WriteEndElement();
                    //</v>
                    xmlWriter.WriteEndElement();
                    //</vertex_weights>

                    xmlWriter.WriteEndElement();
                    //</skin>
                    xmlWriter.WriteEndElement();
                    //</controller>

                    prevIndexCount += indexCount;
                }

            }

            xmlWriter.WriteEndElement();
            //</library_controllers>
        }

        /// <summary>
        /// Writes the xml scenes to be used in the collada file
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        /// <param name="modelName">The name of the model</param>
        /// <param name="meshDataList">The list of mesh data</param>
        /// <param name="skelDict">The dictionary containing skeleton data</param>
        private static void XMLscenes(XmlWriter xmlWriter, string modelName, IReadOnlyList<MeshData> meshDataList, Dictionary<string, SkeletonData> skelDict)
        {
            //<library_visual_scenes>
            xmlWriter.WriteStartElement("library_visual_scenes");
            //<visual_scene>
            xmlWriter.WriteStartElement("visual_scene");
            xmlWriter.WriteAttributeString("id", "Scene");

            var boneParents = new List<string>();

            if (skelDict.Count > 0)
            {
                try
                {
                    var firstBone = skelDict["n_root"];
                    WriteBones(xmlWriter, firstBone, skelDict);
                    boneParents.Add("n_root");
                }
                catch
                {
                    var firstBone = skelDict["j_kao"];
                    WriteBones(xmlWriter, firstBone, skelDict);
                    boneParents.Add("j_kao");
                }
            }

            for (var i = 0; i < meshDataList.Count; i++)
            {
                if(meshDataList[i].VertexData.Positions.Count <= 0) continue;

                //<node>
                xmlWriter.WriteStartElement("node");
                xmlWriter.WriteAttributeString("id", "node-Group_" + i);
                xmlWriter.WriteAttributeString("name", "Group_" + i);

                var meshPartCount = meshDataList[i].MeshPartList.Count;

                // Determine the last non-dummy mesh part
                var lastNonDummy = meshPartCount;

                for (var j = 0; j < meshPartCount; j++)
                {
                    if (meshDataList[i].MeshPartList[j].IndexCount != 0)
                    {
                        lastNonDummy = j;
                    }
                }

                // Only add nodes up until the last non-dummy mesh to not clutter the DAE with dummies
                for (var k = 0; k <= lastNonDummy; k++)
                {
                    var partString = "." + k;

                    if (k == 0)
                    {
                        partString = "";
                    }

                    //<node>
                    xmlWriter.WriteStartElement("node");
                    xmlWriter.WriteAttributeString("id", "node-" + modelName + "_" + i + partString);
                    xmlWriter.WriteAttributeString("name", modelName + "_" + i + partString);

                    if (skelDict.Count > 0)
                    {
                        //<instance_controller>
                        xmlWriter.WriteStartElement("instance_controller");
                        xmlWriter.WriteAttributeString("url", "#geom-" + modelName + "_" + i + partString + "-skin1");

                        foreach (var b in boneParents)
                        {
                            //<skeleton> 
                            xmlWriter.WriteStartElement("skeleton");
                            xmlWriter.WriteString("#node-" + b);
                            xmlWriter.WriteEndElement();
                            //</skeleton> 
                        }

                        //<bind_material>
                        xmlWriter.WriteStartElement("bind_material");
                        //<technique_common>
                        xmlWriter.WriteStartElement("technique_common");
                        //<instance_material>
                        xmlWriter.WriteStartElement("instance_material");
                        xmlWriter.WriteAttributeString("symbol", modelName + "_" + i);
                        xmlWriter.WriteAttributeString("target", "#" + modelName + "_" + i + "-material");
                        //<bind_vertex_input>
                        xmlWriter.WriteStartElement("bind_vertex_input");
                        xmlWriter.WriteAttributeString("semantic", "geom-" + modelName + "_" + i + "-map1");
                        xmlWriter.WriteAttributeString("input_semantic", "TEXCOORD");
                        xmlWriter.WriteAttributeString("input_set", "0");
                        xmlWriter.WriteEndElement();
                        //</bind_vertex_input>   
                        xmlWriter.WriteEndElement();
                        //</instance_material>       
                        xmlWriter.WriteEndElement();
                        //</technique_common>            
                        xmlWriter.WriteEndElement();
                        //</bind_material>   
                        xmlWriter.WriteEndElement();
                        //</instance_controller>
                    }
                    else
                    {
                        //<instance_geometry>
                        xmlWriter.WriteStartElement("instance_geometry");
                        xmlWriter.WriteAttributeString("url", "#geom-" + modelName + "_" + i + partString);
                        //<bind_material>
                        xmlWriter.WriteStartElement("bind_material");
                        //<technique_common>
                        xmlWriter.WriteStartElement("technique_common");
                        //<instance_material>
                        xmlWriter.WriteStartElement("instance_material");
                        xmlWriter.WriteAttributeString("symbol", modelName + "_" + i);
                        xmlWriter.WriteAttributeString("target", "#" + modelName + "_" + i + "-material");
                        //<bind_vertex_input>
                        xmlWriter.WriteStartElement("bind_vertex_input");
                        xmlWriter.WriteAttributeString("semantic", "geom-" + modelName + "_" + i + "-map1");
                        xmlWriter.WriteAttributeString("input_semantic", "TEXCOORD");
                        xmlWriter.WriteAttributeString("input_set", "0");
                        xmlWriter.WriteEndElement();
                        //</bind_vertex_input>   
                        xmlWriter.WriteEndElement();
                        //</instance_material>       
                        xmlWriter.WriteEndElement();
                        //</technique_common>            
                        xmlWriter.WriteEndElement();
                        //</bind_material>   
                        xmlWriter.WriteEndElement();
                        //</instance_geometry>
                    }


                    xmlWriter.WriteEndElement();
                    //</node>
                }

                #region testing
                //if (modelData.ExtraData.totalExtraCounts.ContainsKey(i))
                //{
                //    //<node>
                //    xmlWriter.WriteStartElement("node");
                //    xmlWriter.WriteAttributeString("id", "node-extra_" + modelName + "_" + i);
                //    xmlWriter.WriteAttributeString("name", "extra_" + modelName + "_" + i);

                //    //<instance_controller>
                //    xmlWriter.WriteStartElement("instance_controller");
                //    xmlWriter.WriteAttributeString("url", "#geom-extra_" + modelName + "_" + i + "-skin1");

                //    foreach (var b in boneParents)
                //    {
                //        //<skeleton> 
                //        xmlWriter.WriteStartElement("skeleton");
                //        xmlWriter.WriteString("#node-" + b);
                //        xmlWriter.WriteEndElement();
                //        //</skeleton> 
                //    }

                //    //<bind_material>
                //    xmlWriter.WriteStartElement("bind_material");
                //    //<technique_common>
                //    xmlWriter.WriteStartElement("technique_common");
                //    //<instance_material>
                //    xmlWriter.WriteStartElement("instance_material");
                //    xmlWriter.WriteAttributeString("symbol", modelName + "_" + i);
                //    xmlWriter.WriteAttributeString("target", "#" + modelName + "_" + i + "-material");
                //    //<bind_vertex_input>
                //    xmlWriter.WriteStartElement("bind_vertex_input");
                //    xmlWriter.WriteAttributeString("semantic", "geom-" + modelName + "_" + i + "-map1");
                //    xmlWriter.WriteAttributeString("input_semantic", "TEXCOORD");
                //    xmlWriter.WriteAttributeString("input_set", "0");
                //    xmlWriter.WriteEndElement();
                //    //</bind_vertex_input>   
                //    xmlWriter.WriteEndElement();
                //    //</instance_material>       
                //    xmlWriter.WriteEndElement();
                //    //</technique_common>            
                //    xmlWriter.WriteEndElement();
                //    //</bind_material>   
                //    xmlWriter.WriteEndElement();
                //    //</instance_controller>
                //    xmlWriter.WriteEndElement();
                //    //</node>
                //}
                #endregion testing

                xmlWriter.WriteEndElement();
                //</node>
            }

            xmlWriter.WriteEndElement();
            //</visual_scene>
            xmlWriter.WriteEndElement();
            //</library_visual_scenes>

            //<scene>
            xmlWriter.WriteStartElement("scene");
            //<instance_visual_scenes>
            xmlWriter.WriteStartElement("instance_visual_scene");
            xmlWriter.WriteAttributeString("url", "#Scene");
            xmlWriter.WriteEndElement();
            //</instance_visual_scenes>
            xmlWriter.WriteEndElement();
            //</scene>
        }

        /// <summary>
        /// Writes the bone data
        /// </summary>
        /// <param name="xmlWriter">The xml writer being used</param>
        /// <param name="skeleton">The skeleton data</param>
        /// <param name="boneDictionary">The dictionary containing skeleton data</param>
        private static void WriteBones(XmlWriter xmlWriter, SkeletonData skeleton, Dictionary<string, SkeletonData> boneDictionary)
        {
            //<node>
            xmlWriter.WriteStartElement("node");
            xmlWriter.WriteAttributeString("id", "node-" + skeleton.BoneName);
            xmlWriter.WriteAttributeString("name", skeleton.BoneName);
            xmlWriter.WriteAttributeString("sid", skeleton.BoneName);
            xmlWriter.WriteAttributeString("type", "JOINT");

            //<matrix>
            xmlWriter.WriteStartElement("matrix");
            xmlWriter.WriteAttributeString("sid", "matrix");

            Matrix matrix = new Matrix(boneDictionary[skeleton.BoneName].PoseMatrix);

            
            xmlWriter.WriteString(matrix.Column1.X + " " + matrix.Column1.Y + " " + matrix.Column1.Z + " " + (matrix.Column1.W ) + " ");
            xmlWriter.WriteString(matrix.Column2.X + " " + matrix.Column2.Y + " " + matrix.Column2.Z + " " + (matrix.Column2.W ) + " ");
            xmlWriter.WriteString(matrix.Column3.X + " " + matrix.Column3.Y + " " + matrix.Column3.Z + " " + (matrix.Column3.W ) + " ");
            xmlWriter.WriteString(matrix.Column4.X + " " + matrix.Column4.Y + " " + matrix.Column4.Z + " " + (matrix.Column4.W ) + " ");

            xmlWriter.WriteEndElement();
            //</matrix>

            foreach (var sk in boneDictionary.Values)
            {
                if (sk.BoneParent == skeleton.BoneNumber)
                {
                    WriteBones(xmlWriter, sk, boneDictionary);
                }
            }

            xmlWriter.WriteEndElement();
            //</node>
        }
    }
}