﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

using SabreTools.Library.Data;
using SabreTools.Library.DatItems;
using SabreTools.Library.Tools;
using NaturalSort;

namespace SabreTools.Library.DatFiles
{
    /// <summary>
    /// Represents parsing and writing of an SabreDat XML DAT
    /// </summary>
    internal class SabreDat : DatFile
    {
        /// <summary>
        /// Constructor designed for casting a base DatFile
        /// </summary>
        /// <param name="datFile">Parent DatFile to copy from</param>
        public SabreDat(DatFile datFile)
            : base(datFile)
        {
        }

        /// <summary>
        /// Parse an SabreDat XML DAT and return all found directories and files within
        /// </summary>
        /// <param name="filename">Name of the file to be parsed</param>
        /// <param name="indexId">Index ID for the DAT</param>
        /// <param name="keep">True if full pathnames are to be kept, false otherwise (default)</param>
        protected override void ParseFile(
            // Standard Dat parsing
            string filename,
            int indexId,

            // Miscellaneous
            bool keep)
        {
            // Prepare all internal variables
            bool empty = true;
            string key;
            List<string> parent = new List<string>();

            XmlReader xtr = filename.GetXmlTextReader();

            // If we got a null reader, just return
            if (xtr == null)
                return;

            // Otherwise, read the file to the end
            try
            {
                xtr.MoveToContent();
                while (!xtr.EOF)
                {
                    // If we're ending a folder or game, take care of possibly empty games and removing from the parent
                    if (xtr.NodeType == XmlNodeType.EndElement && (xtr.Name == "directory" || xtr.Name == "dir"))
                    {
                        // If we didn't find any items in the folder, make sure to add the blank rom
                        if (empty)
                        {
                            string tempgame = string.Join("\\", parent);
                            Rom rom = new Rom("null", tempgame);

                            // Now process and add the rom
                            key = ParseAddHelper(rom);
                        }

                        // Regardless, end the current folder
                        int parentcount = parent.Count;
                        if (parentcount == 0)
                        {
                            Globals.Logger.Verbose($"Empty parent '{string.Join("\\", parent)}' found in '{filename}'");
                            empty = true;
                        }

                        // If we have an end folder element, remove one item from the parent, if possible
                        if (parentcount > 0)
                        {
                            parent.RemoveAt(parent.Count - 1);
                            if (keep && parentcount > 1)
                                Header.Type = (string.IsNullOrWhiteSpace(Header.Type) ? "SuperDAT" : Header.Type);
                        }
                    }

                    // We only want elements
                    if (xtr.NodeType != XmlNodeType.Element)
                    {
                        xtr.Read();
                        continue;
                    }

                    switch (xtr.Name)
                    {
                        // We want to process the entire subtree of the header
                        case "header":
                            ReadHeader(xtr.ReadSubtree(), keep);

                            // Skip the header node now that we've processed it
                            xtr.Skip();
                            break;

                        case "dir":
                        case "directory":
                            empty = ReadDirectory(xtr.ReadSubtree(), parent, filename, indexId, keep);

                            // Skip the directory node now that we've processed it
                            xtr.Read();
                            break;
                        default:
                            xtr.Read();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Globals.Logger.Warning($"Exception found while parsing '{filename}': {ex}");

                // For XML errors, just skip the affected node
                xtr?.Read();
            }

            xtr.Dispose();
        }

        /// <summary>
        /// Read header information
        /// </summary>
        /// <param name="reader">XmlReader to use to parse the header</param>
        /// <param name="keep">True if full pathnames are to be kept, false otherwise (default)</param>
        private void ReadHeader(XmlReader reader, bool keep)
        {
            bool superdat = false;

            // If there's no subtree to the header, skip it
            if (reader == null)
                return;

            // Otherwise, read what we can from the header
            while (!reader.EOF)
            {
                // We only want elements
                if (reader.NodeType != XmlNodeType.Element || reader.Name == "header")
                {
                    reader.Read();
                    continue;
                }

                // Get all header items (ONLY OVERWRITE IF THERE'S NO DATA)
                string content;
                switch (reader.Name)
                {
                    case "name":
                        content = reader.ReadElementContentAsString(); ;
                        Header.Name = (string.IsNullOrWhiteSpace(Header.Name) ? content : Header.Name);
                        superdat = superdat || content.Contains(" - SuperDAT");
                        if (keep && superdat)
                        {
                            Header.Type = (string.IsNullOrWhiteSpace(Header.Type) ? "SuperDAT" : Header.Type);
                        }
                        break;

                    case "description":
                        content = reader.ReadElementContentAsString();
                        Header.Description = (string.IsNullOrWhiteSpace(Header.Description) ? content : Header.Description);
                        break;

                    case "rootdir":
                        content = reader.ReadElementContentAsString();
                        Header.RootDir = (string.IsNullOrWhiteSpace(Header.RootDir) ? content : Header.RootDir);
                        break;

                    case "category":
                        content = reader.ReadElementContentAsString();
                        Header.Category = (string.IsNullOrWhiteSpace(Header.Category) ? content : Header.Category);
                        break;

                    case "version":
                        content = reader.ReadElementContentAsString();
                        Header.Version = (string.IsNullOrWhiteSpace(Header.Version) ? content : Header.Version);
                        break;

                    case "date":
                        content = reader.ReadElementContentAsString();
                        Header.Date = (string.IsNullOrWhiteSpace(Header.Date) ? content.Replace(".", "/") : Header.Date);
                        break;

                    case "author":
                        content = reader.ReadElementContentAsString();
                        Header.Author = (string.IsNullOrWhiteSpace(Header.Author) ? content : Header.Author);
                        Header.Email = (string.IsNullOrWhiteSpace(Header.Email) ? reader.GetAttribute("email") : Header.Email);
                        Header.Homepage = (string.IsNullOrWhiteSpace(Header.Homepage) ? reader.GetAttribute("homepage") : Header.Homepage);
                        Header.Url = (string.IsNullOrWhiteSpace(Header.Url) ? reader.GetAttribute("url") : Header.Url);
                        break;

                    case "comment":
                        content = reader.ReadElementContentAsString();
                        Header.Comment = (string.IsNullOrWhiteSpace(Header.Comment) ? content : Header.Comment);
                        break;

                    case "flags":
                        ReadFlags(reader.ReadSubtree(), superdat);

                        // Skip the flags node now that we've processed it
                        reader.Skip();
                        break;

                    default:
                        reader.Read();
                        break;
                }
            }
        }

        /// <summary>
        /// Read directory information
        /// </summary>
        /// <param name="reader">XmlReader to use to parse the header</param>
        /// <param name="filename">Name of the file to be parsed</param>
        /// <param name="indexId">Index ID for the DAT</param>
        /// <param name="keep">True if full pathnames are to be kept, false otherwise (default)</param>
        private bool ReadDirectory(XmlReader reader,
            List<string> parent,

            // Standard Dat parsing
            string filename,
            int indexId,

            // Miscellaneous
            bool keep)
        {
            // Prepare all internal variables
            XmlReader flagreader;
            bool empty = true;
            string key = string.Empty, date = string.Empty;
            long size = -1;
            ItemStatus its = ItemStatus.None;

            // If there's no subtree to the header, skip it
            if (reader == null)
                return empty;

            string foldername = (reader.GetAttribute("name") ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(foldername))
                parent.Add(foldername);

            // Otherwise, read what we can from the directory
            while (!reader.EOF)
            {
                // If we're ending a folder or game, take care of possibly empty games and removing from the parent
                if (reader.NodeType == XmlNodeType.EndElement && (reader.Name == "directory" || reader.Name == "dir"))
                {
                    // If we didn't find any items in the folder, make sure to add the blank rom
                    if (empty)
                    {
                        string tempgame = string.Join("\\", parent);
                        Rom rom = new Rom("null", tempgame);

                        // Now process and add the rom
                        key = ParseAddHelper(rom);
                    }

                    // Regardless, end the current folder
                    int parentcount = parent.Count;
                    if (parentcount == 0)
                    {
                        Globals.Logger.Verbose($"Empty parent '{string.Join("\\", parent)}' found in '{filename}'");
                        empty = true;
                    }

                    // If we have an end folder element, remove one item from the parent, if possible
                    if (parentcount > 0)
                    {
                        parent.RemoveAt(parent.Count - 1);
                        if (keep && parentcount > 1)
                            Header.Type = (string.IsNullOrWhiteSpace(Header.Type) ? "SuperDAT" : Header.Type);
                    }
                }

                // We only want elements
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                // Get all directory items
                string content = string.Empty;
                switch (reader.Name)
                {
                    // Directories can contain directories
                    case "dir":
                    case "directory":
                        ReadDirectory(reader.ReadSubtree(), parent, filename, indexId, keep);

                        // Skip the directory node now that we've processed it
                        reader.Read();
                        break;

                    case "file":
                        empty = false;

                        // If the rom is itemStatus, flag it
                        its = ItemStatus.None;
                        flagreader = reader.ReadSubtree();

                        // If the subtree is empty, skip it
                        if (flagreader == null)
                        {
                            reader.Skip();
                            continue;
                        }

                        while (!flagreader.EOF)
                        {
                            // We only want elements
                            if (flagreader.NodeType != XmlNodeType.Element || flagreader.Name == "flags")
                            {
                                flagreader.Read();
                                continue;
                            }

                            switch (flagreader.Name)
                            {
                                case "flag":
                                    if (flagreader.GetAttribute("name") != null && flagreader.GetAttribute("value") != null)
                                    {
                                        content = flagreader.GetAttribute("value");
                                        its = flagreader.GetAttribute("name").AsItemStatus();
                                    }
                                    break;
                            }

                            flagreader.Read();
                        }

                        // If the rom has a Date attached, read it in and then sanitize it
                        date = Sanitizer.CleanDate(reader.GetAttribute("date"));

                        // Take care of hex-sized files
                        size = Sanitizer.CleanSize(reader.GetAttribute("size"));

                        Machine dir = new Machine
                        {
                            // Get the name of the game from the parent
                            Name = string.Join("\\", parent),
                            Description = string.Join("\\", parent),
                        };

                        DatItem datItem;
                        switch (reader.GetAttribute("type").ToLowerInvariant())
                        {
                            case "archive":
                                datItem = new Archive
                                {
                                    Name = reader.GetAttribute("name"),

                                    IndexId = indexId,
                                    IndexSource = filename,
                                };
                                break;

                            case "biosset":
                                datItem = new BiosSet
                                {
                                    Name = reader.GetAttribute("name"),
                                    Description = reader.GetAttribute("description"),
                                    Default = reader.GetAttribute("default").AsYesNo(),

                                    IndexId = indexId,
                                    IndexSource = filename,
                                };
                                break;

                            case "disk":
                                datItem = new Disk
                                {
                                    Name = reader.GetAttribute("name"),
                                    MD5 = reader.GetAttribute("md5"),
#if NET_FRAMEWORK
                                    RIPEMD160 = reader.GetAttribute("ripemd160"),
#endif
                                    SHA1 = reader.GetAttribute("sha1"),
                                    SHA256 = reader.GetAttribute("sha256"),
                                    SHA384 =reader.GetAttribute("sha384"),
                                    SHA512 = reader.GetAttribute("sha512"),
                                    ItemStatus = its,

                                    IndexId = indexId,
                                    IndexSource = filename,
                                };
                                break;

                            case "release":
                                datItem = new Release
                                {
                                    Name = reader.GetAttribute("name"),
                                    Region = reader.GetAttribute("region"),
                                    Language = reader.GetAttribute("language"),
                                    Date = reader.GetAttribute("date"),
                                    Default = reader.GetAttribute("default").AsYesNo(),

                                    IndexId = indexId,
                                    IndexSource = filename,
                                };
                                break;

                            case "rom":
                                datItem = new Rom
                                {
                                    Name = reader.GetAttribute("name"),
                                    Size = size,
                                    CRC = reader.GetAttribute("crc"),
                                    MD5 = reader.GetAttribute("md5"),
#if NET_FRAMEWORK
                                    RIPEMD160 = reader.GetAttribute("ripemd160"),
#endif
                                    SHA1 = reader.GetAttribute("sha1"),
                                    SHA256 = reader.GetAttribute("sha256"),
                                    SHA384 = reader.GetAttribute("sha384"),
                                    SHA512 = reader.GetAttribute("sha512"),
                                    ItemStatus = its,
                                    Date = date,

                                    IndexId = indexId,
                                    IndexSource = filename,
                                };
                                break;

                            case "sample":
                                datItem = new Sample
                                {
                                    Name = reader.GetAttribute("name"),

                                    IndexId = indexId,
                                    IndexSource = filename,
                                };
                                break;

                            default:
                                // By default, create a new Blank, just in case
                                datItem = new Blank();
                                break;
                        }

                        datItem?.CopyMachineInformation(dir);

                        // Now process and add the rom
                        key = ParseAddHelper(datItem);

                        reader.Read();
                        break;
                }
            }

            return empty;
        }

        /// <summary>
        /// Read flags information
        /// </summary>
        /// <param name="reader">XmlReader to use to parse the header</param>
        /// <param name="superdat">True if superdat has already been set externally, false otherwise</param>
        private void ReadFlags(XmlReader reader, bool superdat)
        {
            // Prepare all internal variables
            string content;

            // If we somehow have a null flag section, skip it
            if (reader == null)
                return;

            while (!reader.EOF)
            {
                // We only want elements
                if (reader.NodeType != XmlNodeType.Element || reader.Name == "flags")
                {
                    reader.Read();
                    continue;
                }

                switch (reader.Name)
                {
                    case "flag":
                        if (reader.GetAttribute("name") != null && reader.GetAttribute("value") != null)
                        {
                            content = reader.GetAttribute("value");
                            switch (reader.GetAttribute("name").ToLowerInvariant())
                            {
                                case "type":
                                    Header.Type = (string.IsNullOrWhiteSpace(Header.Type) ? content : Header.Type);
                                    superdat = superdat || content.Contains("SuperDAT");
                                    break;

                                case "forcemerging":
                                    if (Header.ForceMerging == ForceMerging.None)
                                        Header.ForceMerging = content.AsForceMerging();

                                    break;

                                case "forcenodump":
                                    if (Header.ForceNodump == ForceNodump.None)
                                        Header.ForceNodump = content.AsForceNodump();

                                    break;

                                case "forcepacking":
                                    if (Header.ForcePacking == ForcePacking.None)
                                        Header.ForcePacking = content.AsForcePacking();

                                    break;
                            }
                        }

                        reader.Read();
                        break;

                    default:
                        reader.Read();
                        break;
                }
            }
        }

        /// <summary>
        /// Create and open an output file for writing direct from a dictionary
        /// </summary>
        /// <param name="outfile">Name of the file to write to</param>
        /// <param name="ignoreblanks">True if blank roms should be skipped on output, false otherwise (default)</param>
        /// <returns>True if the DAT was written correctly, false otherwise</returns>
        /// TODO: Fix writing out files that have a path in the name
        public override bool WriteToFile(string outfile, bool ignoreblanks = false)
        {
            try
            {
                Globals.Logger.User($"Opening file for writing: {outfile}");
                FileStream fs = FileExtensions.TryCreate(outfile);

                // If we get back null for some reason, just log and return
                if (fs == null)
                {
                    Globals.Logger.Warning($"File '{outfile}' could not be created for writing! Please check to see if the file is writable");
                    return false;
                }

                XmlTextWriter xtw = new XmlTextWriter(fs, new UTF8Encoding(false))
                {
                    Formatting = Formatting.Indented,
                    IndentChar = '\t',
                    Indentation = 1
                };

                // Write out the header
                WriteHeader(xtw);

                // Write out each of the machines and roms
                int depth = 2, last = -1;
                string lastgame = null;
                List<string> splitpath = new List<string>();

                // Use a sorted list of games to output
                foreach (string key in Items.SortedKeys)
                {
                    List<DatItem> roms = Items[key];

                    // Resolve the names in the block
                    roms = DatItem.ResolveNames(roms);

                    for (int index = 0; index < roms.Count; index++)
                    {
                        DatItem rom = roms[index];

                        // There are apparently times when a null rom can skip by, skip them
                        if (rom.Name == null || rom.MachineName == null)
                        {
                            Globals.Logger.Warning("Null rom found!");
                            continue;
                        }

                        List<string> newsplit = rom.MachineName.Split('\\').ToList();

                        // If we have a different game and we're not at the start of the list, output the end of last item
                        if (lastgame != null && lastgame.ToLowerInvariant() != rom.MachineName.ToLowerInvariant())
                            depth = WriteEndGame(xtw, splitpath, newsplit, depth, out last);

                        // If we have a new game, output the beginning of the new item
                        if (lastgame == null || lastgame.ToLowerInvariant() != rom.MachineName.ToLowerInvariant())
                            depth = WriteStartGame(xtw, rom, newsplit, depth, last);

                        // If we have a "null" game (created by DATFromDir or something similar), log it to file
                        if (rom.ItemType == ItemType.Rom
                            && ((Rom)rom).Size == -1
                            && ((Rom)rom).CRC == "null")
                        {
                            Globals.Logger.Verbose($"Empty folder found: {rom.MachineName}");

                            splitpath = newsplit;
                            lastgame = rom.MachineName;
                            continue;
                        }

                        // Now, output the rom data
                        WriteDatItem(xtw, rom, depth, ignoreblanks);

                        // Set the new data to compare against
                        splitpath = newsplit;
                        lastgame = rom.MachineName;
                    }
                }

                // Write the file footer out
                WriteFooter(xtw, depth);

                Globals.Logger.Verbose("File written!" + Environment.NewLine);
                xtw.Dispose();
                fs.Dispose();
            }
            catch (Exception ex)
            {
                Globals.Logger.Error(ex.ToString());
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write out DAT header using the supplied StreamWriter
        /// </summary>
        /// <param name="xtw">XmlTextWriter to output to</param>
        /// <returns>True if the data was written, false on error</returns>
        private bool WriteHeader(XmlTextWriter xtw)
        {
            try
            {
                xtw.WriteStartDocument();
                xtw.WriteDocType("sabredat", null, "newdat.xsd", null);

                xtw.WriteStartElement("datafile");

                xtw.WriteStartElement("header");

                xtw.WriteElementString("name", Header.Name);
                xtw.WriteElementString("description", Header.Description);
                if (!string.IsNullOrWhiteSpace(Header.RootDir))
                    xtw.WriteElementString("rootdir", Header.RootDir);
                if (!string.IsNullOrWhiteSpace(Header.Category))
                    xtw.WriteElementString("category", Header.Category);
                xtw.WriteElementString("version", Header.Version);
                if (!string.IsNullOrWhiteSpace(Header.Date))
                    xtw.WriteElementString("date", Header.Date);
                xtw.WriteElementString("author", Header.Author);
                if (!string.IsNullOrWhiteSpace(Header.Comment))
                    xtw.WriteElementString("comment", Header.Comment);
                if (!string.IsNullOrWhiteSpace(Header.Type)
                    || Header.ForcePacking != ForcePacking.None
                    || Header.ForceMerging != ForceMerging.None
                    || Header.ForceNodump != ForceNodump.None)
                {
                    xtw.WriteStartElement("flags");

                    if (!string.IsNullOrWhiteSpace(Header.Type))
                    {
                        xtw.WriteStartElement("flag");
                        xtw.WriteAttributeString("name", "type");
                        xtw.WriteAttributeString("value", Header.Type);
                        xtw.WriteEndElement();
                    }

                    switch (Header.ForcePacking)
                    {
                        case ForcePacking.Unzip:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcepacking");
                            xtw.WriteAttributeString("value", "unzip");
                            xtw.WriteEndElement();
                            break;
                        case ForcePacking.Zip:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcepacking");
                            xtw.WriteAttributeString("value", "zip");
                            xtw.WriteEndElement();
                            break;
                    }

                    switch (Header.ForceMerging)
                    {
                        case ForceMerging.Full:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcemerging");
                            xtw.WriteAttributeString("value", "full");
                            xtw.WriteEndElement();
                            break;
                        case ForceMerging.Split:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcemerging");
                            xtw.WriteAttributeString("value", "split");
                            xtw.WriteEndElement();
                            break;
                        case ForceMerging.Merged:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcemerging");
                            xtw.WriteAttributeString("value", "merged");
                            xtw.WriteEndElement();
                            break;
                        case ForceMerging.NonMerged:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcemerging");
                            xtw.WriteAttributeString("value", "nonmerged");
                            xtw.WriteEndElement();
                            break;
                    }

                    switch (Header.ForceNodump)
                    {
                        case ForceNodump.Ignore:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcenodump");
                            xtw.WriteAttributeString("value", "ignore");
                            xtw.WriteEndElement();
                            break;
                        case ForceNodump.Obsolete:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcenodump");
                            xtw.WriteAttributeString("value", "obsolete");
                            xtw.WriteEndElement();
                            break;
                        case ForceNodump.Required:
                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "forcenodump");
                            xtw.WriteAttributeString("value", "required");
                            xtw.WriteEndElement();
                            break;
                    }

                    // End flags
                    xtw.WriteEndElement();
                }

                // End header
                xtw.WriteEndElement();

                xtw.WriteStartElement("data");

                xtw.Flush();
            }
            catch (Exception ex)
            {
                Globals.Logger.Error(ex.ToString());
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write out Game start using the supplied StreamWriter
        /// </summary>
        /// <param name="xtw">XmlTextWriter to output to</param>
        /// <param name="datItem">DatItem object to be output</param>
        /// <param name="newsplit">Split path representing the parent game (SabreDAT only)</param>
        /// <param name="depth">Current depth to output file at (SabreDAT only)</param>
        /// <param name="last">Last known depth to cycle back from (SabreDAT only)</param>
        /// <returns>The new depth of the tag</returns>
        private int WriteStartGame(XmlTextWriter xtw, DatItem datItem, List<string> newsplit, int depth, int last)
        {
            try
            {
                // No game should start with a path separator
                datItem.MachineName = datItem.MachineName.TrimStart(Path.DirectorySeparatorChar);

                // Build the state based on excluded fields
                for (int i = (last == -1 ? 0 : last); i < newsplit.Count; i++)
                {
                    xtw.WriteStartElement("directory");
                    xtw.WriteAttributeString("name", !Header.ExcludeFields[(int)Field.MachineName] ? newsplit[i] : string.Empty);
                    xtw.WriteAttributeString("description", !Header.ExcludeFields[(int)Field.MachineName] ? newsplit[i] : string.Empty);
                }

                depth = depth - (last == -1 ? 0 : last) + newsplit.Count;

                xtw.Flush();
            }
            catch (Exception ex)
            {
                Globals.Logger.Error(ex.ToString());
                return depth;
            }

            return depth;
        }

        /// <summary>
        /// Write out Game start using the supplied StreamWriter
        /// </summary>
        /// <param name="xtw">XmlTextWriter to output to</param>
        /// <param name="splitpath">Split path representing last kwown parent game (SabreDAT only)</param>
        /// <param name="newsplit">Split path representing the parent game (SabreDAT only)</param>
        /// <param name="depth">Current depth to output file at (SabreDAT only)</param>
        /// <param name="last">Last known depth to cycle back from (SabreDAT only)</param>
        /// <returns>The new depth of the tag</returns>
        private int WriteEndGame(XmlTextWriter xtw, List<string> splitpath, List<string> newsplit, int depth, out int last)
        {
            last = 0;

            try
            {
                if (splitpath != null)
                {
                    for (int i = 0; i < newsplit.Count && i < splitpath.Count; i++)
                    {
                        // Always keep track of the last seen item
                        last = i;

                        // If we find a difference, break
                        if (newsplit[i] != splitpath[i])
                            break;
                    }

                    // Now that we have the last known position, take down all open folders
                    for (int i = depth - 1; i > last + 1; i--)
                    {
                        // End directory
                        xtw.WriteEndElement();
                    }

                    // Reset the current depth
                    depth = 2 + last;
                }

                xtw.Flush();
            }
            catch (Exception ex)
            {
                Globals.Logger.Error(ex.ToString());
                return depth;
            }

            return depth;
        }

        /// <summary>
        /// Write out DatItem using the supplied StreamWriter
        /// </summary>
        /// <param name="xtw">XmlTextWriter to output to</param>
        /// <param name="datItem">DatItem object to be output</param>
        /// <param name="depth">Current depth to output file at (SabreDAT only)</param>
        /// <param name="ignoreblanks">True if blank roms should be skipped on output, false otherwise (default)</param>
        /// <returns>True if the data was written, false on error</returns>
        private bool WriteDatItem(XmlTextWriter xtw, DatItem datItem, int depth, bool ignoreblanks = false)
        {
            // If we are in ignore blanks mode AND we have a blank (0-size) rom, skip
            if (ignoreblanks && (datItem.ItemType == ItemType.Rom && ((datItem as Rom).Size == 0 || (datItem as Rom).Size == -1)))
                return true;

            try
            {
                // Pre-process the item name
                ProcessItemName(datItem, true);

                // Build the state based on excluded fields
                switch (datItem.ItemType)
                {
                    case ItemType.Archive:
                        xtw.WriteStartElement("file");
                        xtw.WriteAttributeString("type", "archive");
                        xtw.WriteAttributeString("name", datItem.GetField(Field.Name, Header.ExcludeFields));
                        xtw.WriteEndElement();
                        break;

                    case ItemType.BiosSet:
                        var biosSet = datItem as BiosSet;
                        xtw.WriteStartElement("file");
                        xtw.WriteAttributeString("type", "biosset");
                        xtw.WriteAttributeString("name", biosSet.GetField(Field.Name, Header.ExcludeFields));
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.BiosDescription, Header.ExcludeFields)))
                            xtw.WriteAttributeString("description", biosSet.Description);
                        if (!Header.ExcludeFields[(int)Field.Default] && biosSet.Default != null)
                            xtw.WriteAttributeString("default", biosSet.Default.ToString().ToLowerInvariant());
                        xtw.WriteEndElement();
                        break;

                    case ItemType.Disk:
                        var disk = datItem as Disk;
                        xtw.WriteStartElement("file");
                        xtw.WriteAttributeString("type", "disk");
                        xtw.WriteAttributeString("name", disk.GetField(Field.Name, Header.ExcludeFields));
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.MD5, Header.ExcludeFields)))
                            xtw.WriteAttributeString("md5", disk.MD5.ToLowerInvariant());
#if NET_FRAMEWORK
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.RIPEMD160, Header.ExcludeFields)))
                            xtw.WriteAttributeString("ripemd160", disk.RIPEMD160.ToLowerInvariant());
#endif
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA1, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha1", disk.SHA1.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA256, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha256", disk.SHA256.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA384, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha384", disk.SHA384.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA512, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha512", disk.SHA512.ToLowerInvariant());
                        if (!Header.ExcludeFields[(int)Field.Status] && disk.ItemStatus != ItemStatus.None)
                        {
                            xtw.WriteStartElement("flags");

                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "status");
                            xtw.WriteAttributeString("value", disk.ItemStatus.ToString().ToLowerInvariant());
                            xtw.WriteEndElement();

                            // End flags
                            xtw.WriteEndElement();
                        }
                        xtw.WriteEndElement();
                        break;

                    case ItemType.Release:
                        var release = datItem as Release;
                        xtw.WriteStartElement("file");
                        xtw.WriteAttributeString("type", "release");
                        xtw.WriteAttributeString("name", release.GetField(Field.Name, Header.ExcludeFields));
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.Region, Header.ExcludeFields)))
                            xtw.WriteAttributeString("region", release.Region);
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.Language, Header.ExcludeFields)))
                            xtw.WriteAttributeString("language", release.Language);
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.Date, Header.ExcludeFields)))
                            xtw.WriteAttributeString("date", release.Date);
                        if (!Header.ExcludeFields[(int)Field.Default] && release.Default != null)
                            xtw.WriteAttributeString("default", release.Default.ToString().ToLowerInvariant());
                        xtw.WriteEndElement();
                        break;

                    case ItemType.Rom:
                        var rom = datItem as Rom;
                        xtw.WriteStartElement("file");
                        xtw.WriteAttributeString("type", "rom");
                        xtw.WriteAttributeString("name", rom.GetField(Field.Name, Header.ExcludeFields));
                        if (!Header.ExcludeFields[(int)Field.Size] && rom.Size != -1)
                            xtw.WriteAttributeString("size", rom.Size.ToString());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.CRC, Header.ExcludeFields)))
                            xtw.WriteAttributeString("crc", rom.CRC.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.MD5, Header.ExcludeFields)))
                            xtw.WriteAttributeString("md5", rom.MD5.ToLowerInvariant());
#if NET_FRAMEWORK
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.RIPEMD160, Header.ExcludeFields)))
                            xtw.WriteAttributeString("ripemd160", rom.RIPEMD160.ToLowerInvariant());
#endif
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA1, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha1", rom.SHA1.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA256, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha256", rom.SHA256.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA384, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha384", rom.SHA384.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.SHA512, Header.ExcludeFields)))
                            xtw.WriteAttributeString("sha512", rom.SHA512.ToLowerInvariant());
                        if (!string.IsNullOrWhiteSpace(datItem.GetField(Field.Date, Header.ExcludeFields)))
                            xtw.WriteAttributeString("date", rom.Date);
                        if (!Header.ExcludeFields[(int)Field.Status] && rom.ItemStatus != ItemStatus.None)
                        {
                            xtw.WriteStartElement("flags");

                            xtw.WriteStartElement("flag");
                            xtw.WriteAttributeString("name", "status");
                            xtw.WriteAttributeString("value", rom.ItemStatus.ToString().ToLowerInvariant());
                            xtw.WriteEndElement();

                            // End flags
                            xtw.WriteEndElement();
                        }
                        xtw.WriteEndElement();
                        break;

                    case ItemType.Sample:
                        xtw.WriteStartElement("file");
                        xtw.WriteAttributeString("type", "sample");
                        xtw.WriteAttributeString("name", datItem.GetField(Field.Name, Header.ExcludeFields));
                        xtw.WriteEndElement();
                        break;
                }

                xtw.Flush();
            }
            catch (Exception ex)
            {
                Globals.Logger.Error(ex.ToString());
                return false;
            }

            return true;
        }

        /// <summary>
        /// Write out DAT footer using the supplied StreamWriter
        /// </summary>
        /// <param name="xtw">XmlTextWriter to output to</param>
        /// <param name="depth">Current depth to output file at (SabreDAT only)</param>
        /// <returns>True if the data was written, false on error</returns>
        private bool WriteFooter(XmlTextWriter xtw, int depth)
        {
            try
            {
                for (int i = depth - 1; i >= 2; i--)
                {
                    // End directory
                    xtw.WriteEndElement();
                }

                // End data
                xtw.WriteEndElement();

                // End datafile
                xtw.WriteEndElement();

                xtw.Flush();
            }
            catch (Exception ex)
            {
                Globals.Logger.Error(ex.ToString());
                return false;
            }

            return true;
        }
    }
}
