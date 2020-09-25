﻿using HelixToolkit.SharpDX.Core.Helper;
using SharpDX;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xivModdingFramework.Cache;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.DataContainers;
using xivModdingFramework.SqPack.DataContainers;
using xivModdingFramework.SqPack.FileTypes;

namespace xivModdingFramework.Materials.FileTypes
{
    /// <summary>
    /// Staining Template File Handler
    /// </summary>
    public static class STM
    {
        public const string GearStainingTemplatePath = "chara/base_material/stainingtemplate.stm";

        public static ushort GetTemplateKeyFromMaterialData(ushort data)
        {
            return (ushort)(data >> 5);
        }

        public static async Task<StainingTemplateFile> GetStainingTemplateFile(bool forceOriginal = false, IndexFile index = null, ModList modlist = null)
        {
            var _dat = new Dat(XivCache.GameInfo.GameDirectory);

            var data = await _dat.GetType2Data(GearStainingTemplatePath, forceOriginal, index, modlist);

            var ret = new StainingTemplateFile(data);
            return ret;
        }

        public static async Task SaveStainingTemplateFile(StainingTemplateFile file, string applicationSource, IndexFile index = null, ModList modlist = null)
        {
            var data = new byte[0];//file.GetBytes();

            var _dat = new Dat(XivCache.GameInfo.GameDirectory);

            var dummyItem = new XivGenericItemModel()
            {
                Name = "Equipment Staining Template",
                SecondaryCategory = "Raw Files"
            };

            await _dat.ImportType2Data(data, GearStainingTemplatePath, applicationSource, null, index, modlist);

        }
    }

    public enum StainingTemplateArrayType
    {
        Singleton,
        OneToOne,
        Indexed
    }

    public class StainingTemplateEntry
    {
        public int Size
        {
            get
            {
                return 0;
            }
        }

        List<Half[]> DiffuseEntries = new List<Half[]>();
        List<Half[]> SpecularEntries = new List<Half[]>();
        List<Half[]> EmissiveEntries = new List<Half[]>();
        List<Half> TileMaterialEntries = new List<Half>();
        List<Half> GlossEntries = new List<Half>();

        public StainingTemplateEntry(byte[] data, int offset)
        {
            var arrayEnds = new List<ushort>();
            var start = offset;


            // This format sucks.
            for (int i = 0; i < 5; i++)
            {
                arrayEnds.Add(BitConverter.ToUInt16(data, offset));
                offset += 2;
            }
            const int headerSize = 10;


            var lastOffset = 0;
            for (int x = 0; x < 5; x++)
            {
                var elementSize = 3;
                if(x > 2)
                {
                    elementSize = 1;
                }

                var arraySize = (arrayEnds[x] - lastOffset) / elementSize;
                var type = StainingTemplateArrayType.OneToOne;

                // Calculate the data type.
                var indexStart = 0;
                if (arraySize == 1)
                {
                    // Single entry used for everything.
                    type = StainingTemplateArrayType.Singleton;
                }
                if(arraySize == 0)
                {
                    // No data.
                    continue;
                }
                else if (arraySize < 128)
                {
                    // Indexed array, where we have [n] # of real entries,
                    // then 128 one-byte index entries referencing those [n] entries.
                    var totalBytes = (arrayEnds[x] - lastOffset) *2;
                    var remBytes = totalBytes - 128;

                    indexStart = start + headerSize + (lastOffset * 2) + remBytes;

                    arraySize = remBytes / 2 / elementSize;
                    type = StainingTemplateArrayType.Indexed;
                }

                var arrayStart = lastOffset;
                var offsetStart = (start + 10 + (arrayStart * 2));

                List<Half[]> halfData  = new List<Half[]>();

                for (int i = 0; i < arraySize; i++)
                {

                    Half[] halfs = new Half[3];

                    var elementStart = offsetStart + (i * 2);

                    var reversed = new byte[] { data[elementStart + 1], data[elementStart] };
                    var test = new Half(BitConverter.ToUInt16(reversed, 0));
                    halfs[0] = new Half(BitConverter.ToUInt16(data, elementStart));
                    if (elementSize > 1)
                    {
                        halfs[1] = new Half(BitConverter.ToUInt16(data, elementStart + 2));
                        halfs[2] = new Half(BitConverter.ToUInt16(data, elementStart + 4));
                    }

                    halfData.Add(halfs);
                }

                if(type == StainingTemplateArrayType.Indexed)
                {
                    var nArray = new List<Half[]>();
                    for (int i = 0; i < 128; i++)
                    {
                        var index = data[indexStart + i];
                        var entry = new Half[3];
                        if (index == 255)
                        {
                            nArray.Add(new Half[] { new Half(), new Half(), new Half() } );
                            continue;
                        }

                        if(index == 0)
                        {
                            nArray.Add(new Half[] { new Half(), new Half(), new Half() });
                            continue;
                        }
                        nArray.Add(halfData[index - 1]);
                    }

                    halfData = nArray;
                }

                if (halfData.Count == 1)
                {
                    for (int i = 0; i < 127; i++)
                    {
                        halfData.Add(halfData[0]);
                    }
                }


                foreach (var arr in halfData)
                {

                    if (x == 0)
                    {
                        DiffuseEntries.Add(arr);
                    }
                    else if (x == 1)
                    {
                        SpecularEntries.Add(arr);
                    }
                    else if (x == 2)
                    {
                        EmissiveEntries.Add(arr);
                    }
                    else if (x == 3)
                    {
                        TileMaterialEntries.Add(arr[0]);
                    }
                    else if (x == 4)
                    {
                        GlossEntries.Add(arr[0]);
                    }
                }

                lastOffset = arrayEnds[x];
            }

            var length = lastOffset;
        }

    }

    public class StainingTemplateFile
    {
        private uint Header;
        private Dictionary<ushort, StainingTemplateEntry> Templates = new Dictionary<ushort, StainingTemplateEntry>();

        public List<ushort> GetKeys()
        {
            return Templates.Keys.ToList();
        }

        public void SetTemplate(ushort key, StainingTemplateEntry entry)
        {
            if (Templates.ContainsKey(key))
            {
                Templates[key] = entry;
            }
            else
            {
                Templates.Add(key, entry);
            }
        }
        public StainingTemplateEntry GetTemplate(ushort key)
        {
            return Templates[key];
        }
        public StainingTemplateFile(byte[] data)
        {
            var Header = BitConverter.ToInt32(data, 0);
            var entryCount = BitConverter.ToUInt16(data, 4);


            Dictionary<ushort, ushort> entries = new Dictionary<ushort, ushort>();
            List<ushort> keys = new List<ushort>();
            List<ushort> values = new List<ushort>();
            List<int> sizes = new List<int>();
            var offset = 8;
            for (int i = 0; i < entryCount; i++)
            {
                var key = BitConverter.ToUInt16(data, offset);
                entries.Add(key, 0);
                keys.Add(key);
                offset += 2;
            }

            var endOfHeader = (8 + (4 * entryCount));

            for (int i = 0; i < entryCount; i++)
            {
                entries[keys[i]] = (ushort) ((BitConverter.ToUInt16(data, offset) * 2) + endOfHeader);
                offset += 2;
            }


            var idx = 0;
            foreach (var kv in entries)
            {
                var entry = new StainingTemplateEntry(data, kv.Value);
                Templates.Add(kv.Key, entry);
                idx++;
            }
        }


    }
}
