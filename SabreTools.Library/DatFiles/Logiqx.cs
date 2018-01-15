﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;

using SabreTools.Library.Data;
using SabreTools.Library.DatItems;
using SabreTools.Library.Tools;

#if MONO
using System.IO;
#else
using Alphaleonis.Win32.Filesystem;

using FileStream = System.IO.FileStream;
using StreamWriter = System.IO.StreamWriter;
#endif
using NaturalSort;

namespace SabreTools.Library.DatFiles
{
	/// <summary>
	/// Represents parsing and writing of a Logiqx-derived DAT
	/// </summary>
	/// TODO: Separate out reading of other DAT types into their own classes
	/// TODO: Add XSD validation for all XML DAT types (maybe?)
	internal class Logiqx : DatFile
	{
		// Private instance variables specific to Logiqx DATs
		bool _depreciated;

		/// <summary>
		/// Constructor designed for casting a base DatFile
		/// </summary>
		/// <param name="datFile">Parent DatFile to copy from</param>
		/// <param name="depreciated">True if the output uses "game", false if the output uses "machine"</param>
		public Logiqx(DatFile datFile, bool depreciated)
			: base(datFile, cloneHeader: false)
		{
			_depreciated = depreciated;
		}

		/// <summary>
		/// Parse a Logiqx XML DAT and return all found games and roms within
		/// </summary>
		/// <param name="filename">Name of the file to be parsed</param>
		/// <param name="sysid">System ID for the DAT</param>
		/// <param name="srcid">Source ID for the DAT</param>
		/// <param name="keep">True if full pathnames are to be kept, false otherwise (default)</param>
		/// <param name="clean">True if game names are sanitized, false otherwise (default)</param>
		/// <param name="remUnicode">True if we should remove non-ASCII characters from output, false otherwise (default)</param>
		/// <remarks>
		/// </remarks>
		public override void ParseFile(
			// Standard Dat parsing
			string filename,
			int sysid,
			int srcid,

			// Miscellaneous
			bool keep,
			bool clean,
			bool remUnicode)
		{
			// Prepare all internal variables
			XmlReader subreader, headreader, flagreader;
			bool superdat = false, empty = true;
			string key = "", date = "";
			long size = -1;
			ItemStatus its = ItemStatus.None;
			List<string> parent = new List<string>();

			Encoding enc = Utilities.GetEncoding(filename);
			XmlReader xtr = Utilities.GetXmlTextReader(filename);

			// If we got a null reader, just return
			if (xtr == null)
			{
				return;
			}

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
							string tempgame = String.Join("\\", parent);
							Rom rom = new Rom("null", tempgame, omitFromScan: Hash.DeepHashes); // TODO: All instances of Hash.DeepHashes should be made into 0x0 eventually

							// Now process and add the rom
							key = ParseAddHelper(rom, clean, remUnicode);
						}

						// Regardless, end the current folder
						int parentcount = parent.Count;
						if (parentcount == 0)
						{
							Globals.Logger.Verbose("Empty parent '{0}' found in '{1}'", String.Join("\\", parent), filename);
							empty = true;
						}

						// If we have an end folder element, remove one item from the parent, if possible
						if (parentcount > 0)
						{
							parent.RemoveAt(parent.Count - 1);
							if (keep && parentcount > 1)
							{
								Type = (String.IsNullOrWhiteSpace(Type) ? "SuperDAT" : Type);
								superdat = true;
							}
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
						// Handle MAME listxml since they're halfway between a SL and a Logiqx XML
						case "mame":
							if (xtr.GetAttribute("build") != null)
							{
								Name = (String.IsNullOrWhiteSpace(Name) ? xtr.GetAttribute("build") : Name);
								Description = (String.IsNullOrWhiteSpace(Description) ? Name : Name);
							}
							xtr.Read();
							break;
						// New software lists have this behavior
						case "softwarelist":
							if (xtr.GetAttribute("name") != null)
							{
								Name = (String.IsNullOrWhiteSpace(Name) ? xtr.GetAttribute("name") : Name);
							}
							if (xtr.GetAttribute("description") != null)
							{
								Description = (String.IsNullOrWhiteSpace(Description) ? xtr.GetAttribute("description") : Description);
							}
							if (ForceMerging == ForceMerging.None)
							{
								ForceMerging = Utilities.GetForceMerging(xtr.GetAttribute("forcemerging"));
							}
							if (ForceNodump == ForceNodump.None)
							{
								ForceNodump = Utilities.GetForceNodump(xtr.GetAttribute("forcenodump"));
							}
							if (ForcePacking == ForcePacking.None)
							{
								ForcePacking = Utilities.GetForcePacking(xtr.GetAttribute("forcepacking"));
							}
							xtr.Read();
							break;
						// Handle M1 DATs since they're 99% the same as a SL DAT
						case "m1":
							Name = (String.IsNullOrWhiteSpace(Name) ? "M1" : Name);
							Description = (String.IsNullOrWhiteSpace(Description) ? "M1" : Description);
							if (xtr.GetAttribute("version") != null)
							{
								Version = (String.IsNullOrWhiteSpace(Version) ? xtr.GetAttribute("version") : Version);
							}
							xtr.Read();
							break;
						// OfflineList has a different header format
						case "configuration":
							headreader = xtr.ReadSubtree();

							// If there's no subtree to the header, skip it
							if (headreader == null)
							{
								xtr.Skip();
								continue;
							}

							// Otherwise, read what we can from the header
							while (!headreader.EOF)
							{
								// We only want elements
								if (headreader.NodeType != XmlNodeType.Element || headreader.Name == "configuration")
								{
									headreader.Read();
									continue;
								}

								// Get all header items (ONLY OVERWRITE IF THERE'S NO DATA)
								string content = "";
								switch (headreader.Name.ToLowerInvariant())
								{
									case "datname":
										content = headreader.ReadElementContentAsString(); ;
										Name = (String.IsNullOrWhiteSpace(Name) ? content : Name);
										superdat = superdat || content.Contains(" - SuperDAT");
										if (keep && superdat)
										{
											Type = (String.IsNullOrWhiteSpace(Type) ? "SuperDAT" : Type);
										}
										break;
									case "datversionurl":
										content = headreader.ReadElementContentAsString(); ;
										Url = (String.IsNullOrWhiteSpace(Name) ? content : Url);
										break;
									default:
										headreader.Read();
										break;
								}
							}

							break;
						// We want to process the entire subtree of the header
						case "header":
							headreader = xtr.ReadSubtree();

							// If there's no subtree to the header, skip it
							if (headreader == null)
							{
								xtr.Skip();
								continue;
							}

							// Otherwise, read what we can from the header
							while (!headreader.EOF)
							{
								// We only want elements
								if (headreader.NodeType != XmlNodeType.Element || headreader.Name == "header")
								{
									headreader.Read();
									continue;
								}

								// Get all header items (ONLY OVERWRITE IF THERE'S NO DATA)
								string content = "";
								switch (headreader.Name)
								{
									case "name":
										content = headreader.ReadElementContentAsString(); ;
										Name = (String.IsNullOrWhiteSpace(Name) ? content : Name);
										superdat = superdat || content.Contains(" - SuperDAT");
										if (keep && superdat)
										{
											Type = (String.IsNullOrWhiteSpace(Type) ? "SuperDAT" : Type);
										}
										break;
									case "description":
										content = headreader.ReadElementContentAsString();
										Description = (String.IsNullOrWhiteSpace(Description) ? content : Description);
										break;
									case "rootdir":
										content = headreader.ReadElementContentAsString();
										RootDir = (String.IsNullOrWhiteSpace(RootDir) ? content : RootDir);
										break;
									case "category":
										content = headreader.ReadElementContentAsString();
										Category = (String.IsNullOrWhiteSpace(Category) ? content : Category);
										break;
									case "version":
										content = headreader.ReadElementContentAsString();
										Version = (String.IsNullOrWhiteSpace(Version) ? content : Version);
										break;
									case "date":
										content = headreader.ReadElementContentAsString();
										Date = (String.IsNullOrWhiteSpace(Date) ? content.Replace(".", "/") : Date);
										break;
									case "author":
										content = headreader.ReadElementContentAsString();
										Author = (String.IsNullOrWhiteSpace(Author) ? content : Author);

										// Special cases for SabreDAT
										Email = (String.IsNullOrWhiteSpace(Email) && !String.IsNullOrWhiteSpace(headreader.GetAttribute("email")) ?
											headreader.GetAttribute("email") : Email);
										Homepage = (String.IsNullOrWhiteSpace(Homepage) && !String.IsNullOrWhiteSpace(headreader.GetAttribute("homepage")) ?
											headreader.GetAttribute("homepage") : Homepage);
										Url = (String.IsNullOrWhiteSpace(Url) && !String.IsNullOrWhiteSpace(headreader.GetAttribute("url")) ?
											headreader.GetAttribute("url") : Url);
										break;
									case "email":
										content = headreader.ReadElementContentAsString();
										Email = (String.IsNullOrWhiteSpace(Email) ? content : Email);
										break;
									case "homepage":
										content = headreader.ReadElementContentAsString();
										Homepage = (String.IsNullOrWhiteSpace(Homepage) ? content : Homepage);
										break;
									case "url":
										content = headreader.ReadElementContentAsString();
										Url = (String.IsNullOrWhiteSpace(Url) ? content : Url);
										break;
									case "comment":
										content = headreader.ReadElementContentAsString();
										Comment = (String.IsNullOrWhiteSpace(Comment) ? content : Comment);
										break;
									case "type":
										content = headreader.ReadElementContentAsString();
										Type = (String.IsNullOrWhiteSpace(Type) ? content : Type);
										superdat = superdat || content.Contains("SuperDAT");
										break;
									case "clrmamepro":
									case "romcenter":
										if (headreader.GetAttribute("header") != null)
										{
											Header = (String.IsNullOrWhiteSpace(Header) ? headreader.GetAttribute("header") : Header);
										}
										if (headreader.GetAttribute("plugin") != null)
										{
											Header = (String.IsNullOrWhiteSpace(Header) ? headreader.GetAttribute("plugin") : Header);
										}
										if (ForceMerging == ForceMerging.None)
										{
											ForceMerging = Utilities.GetForceMerging(headreader.GetAttribute("forcemerging"));
										}
										if (ForceNodump == ForceNodump.None)
										{
											ForceNodump = Utilities.GetForceNodump(headreader.GetAttribute("forcenodump"));
										}
										if (ForcePacking == ForcePacking.None)
										{
											ForcePacking = Utilities.GetForcePacking(headreader.GetAttribute("forcepacking"));
										}
										headreader.Read();
										break;
									case "flags":
										flagreader = xtr.ReadSubtree();

										// If we somehow have a null flag section, skip it
										if (flagreader == null)
										{
											xtr.Skip();
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
														switch (flagreader.GetAttribute("name"))
														{
															case "type":
																Type = (String.IsNullOrWhiteSpace(Type) ? content : Type);
																superdat = superdat || content.Contains("SuperDAT");
																break;
															case "forcemerging":
																if (ForceMerging == ForceMerging.None)
																{
																	ForceMerging = Utilities.GetForceMerging(content);
																}
																break;
															case "forcenodump":
																if (ForceNodump == ForceNodump.None)
																{
																	ForceNodump = Utilities.GetForceNodump(content);
																}
																break;
															case "forcepacking":
																if (ForcePacking == ForcePacking.None)
																{
																	ForcePacking = Utilities.GetForcePacking(content);
																}
																break;
														}
													}
													flagreader.Read();
													break;
												default:
													flagreader.Read();
													break;
											}
										}
										headreader.Skip();
										break;
									default:
										headreader.Read();
										break;
								}
							}

							// Skip the header node now that we've processed it
							xtr.Skip();
							break;
						case "machine":
						case "game":
						case "software":
							string temptype = xtr.Name, publisher = "", partname = "", partinterface = "", areaname = "";
							bool? supported = null;
							long? areasize = null;
							List<Tuple<string, string>> infos = new List<Tuple<string, string>>();
							List<Tuple<string, string>> features = new List<Tuple<string, string>>();
							bool containsItems = false;

							// We want to process the entire subtree of the game
							subreader = xtr.ReadSubtree();

							// Safeguard for interesting case of "software" without anything except roms
							bool software = false;

							// If we have an empty machine, skip it
							if (subreader == null)
							{
								xtr.Skip();
								continue;
							}

							// Otherwise, add what is possible
							subreader.MoveToContent();

							// Create a new machine
							MachineType machineType = MachineType.NULL;
							if (Utilities.GetYesNo(xtr.GetAttribute("isbios")) == true)
							{
								machineType |= MachineType.Bios;
							}
							if (Utilities.GetYesNo(xtr.GetAttribute("isdevice")) == true)
							{
								machineType |= MachineType.Device;
							}
							if (Utilities.GetYesNo(xtr.GetAttribute("ismechanical")) == true)
							{
								machineType |= MachineType.Mechanical;
							}

							Machine machine = new Machine
							{
								Name = xtr.GetAttribute("name"),
								Description = xtr.GetAttribute("name"),

								RomOf = xtr.GetAttribute("romof") ?? "",
								CloneOf = xtr.GetAttribute("cloneof") ?? "",
								SampleOf = xtr.GetAttribute("sampleof") ?? "",

								Devices = new List<string>(),
								MachineType = (machineType == MachineType.NULL ? MachineType.None : machineType),
							};

							// Get the supported value from the reader
							if (subreader.GetAttribute("supported") != null)
							{
								supported = Utilities.GetYesNo(subreader.GetAttribute("supported"));
							}

							// Get the runnable value from the reader
							if (subreader.GetAttribute("runnable") != null)
							{
								machine.Runnable = Utilities.GetYesNo(subreader.GetAttribute("runnable"));
							}

							if (superdat && !keep)
							{
								string tempout = Regex.Match(machine.Name, @".*?\\(.*)").Groups[1].Value;
								if (!String.IsNullOrWhiteSpace(tempout))
								{
									machine.Name = tempout;
								}
							}
							// Get the name of the game from the parent
							else if (superdat && keep && parent.Count > 0)
							{
								machine.Name = String.Join("\\", parent) + "\\" + machine.Name;
							}

							// Special offline list parts
							string ext = "";
							string releaseNumber = "";

							while (software || !subreader.EOF)
							{
								software = false;

								// We only want elements
								if (subreader.NodeType != XmlNodeType.Element)
								{
									if (subreader.NodeType == XmlNodeType.EndElement && subreader.Name == "part")
									{
										partname = "";
										partinterface = "";
										features = new List<Tuple<string, string>>();
									}
									if (subreader.NodeType == XmlNodeType.EndElement && (subreader.Name == "dataarea" || subreader.Name == "diskarea"))
									{
										areaname = "";
										areasize = null;
									}

									subreader.Read();
									continue;
								}

								// Get the roms from the machine
								switch (subreader.Name)
								{
									// For OfflineList only
									case "title":
										machine.Name = subreader.ReadElementContentAsString();
										break;
									case "releaseNumber":
										releaseNumber = subreader.ReadElementContentAsString();
										break;
									case "romSize":
										if (!Int64.TryParse(subreader.ReadElementContentAsString(), out size))
										{
											size = -1;
										}
										break;
									case "romCRC":
										empty = false;
										containsItems = true;

										ext = (subreader.GetAttribute("extension") ?? "");

										DatItem olrom = new Rom
										{
											Name = releaseNumber + " - " + machine.Name + ext,
											Size = size,
											CRC = subreader.ReadElementContentAsString(),
											ItemStatus = ItemStatus.None,
										};

										olrom.CopyMachineInformation(machine);

										// Now process and add the rom
										key = ParseAddHelper(olrom, clean, remUnicode);
										break;

									// For Software List and MAME listxml only
									case "device_ref":
										string device = subreader.GetAttribute("name");
										if (!machine.Devices.Contains(device))
										{
											machine.Devices.Add(device);
										}

										subreader.Read();
										break;
									case "slotoption":
										string slotoption = subreader.GetAttribute("devname");
										if (!machine.Devices.Contains(slotoption))
										{
											machine.Devices.Add(slotoption);
										}

										subreader.Read();
										break;
									case "publisher":
										publisher = subreader.ReadElementContentAsString();
										break;
									case "info":
										infos.Add(Tuple.Create(subreader.GetAttribute("name"), subreader.GetAttribute("value")));
										subreader.Read();
										break;
									case "part":
										partname = subreader.GetAttribute("name");
										partinterface = subreader.GetAttribute("interface");
										subreader.Read();
										break;
									case "feature":
										features.Add(Tuple.Create(subreader.GetAttribute("name"), subreader.GetAttribute("value")));
										subreader.Read();
										break;
									case "dataarea":
									case "diskarea":
										areaname = subreader.GetAttribute("name");
										long areasizetemp = -1;
										if (Int64.TryParse(subreader.GetAttribute("size"), out areasizetemp))
										{
											areasize = areasizetemp;
										}
										subreader.Read();
										break;

									// For Logiqx, SabreDAT, and Software List
									case "description":
										machine.Description = subreader.ReadElementContentAsString();
										break;
									case "year":
										machine.Year = subreader.ReadElementContentAsString();
										break;
									case "manufacturer":
										machine.Manufacturer = subreader.ReadElementContentAsString();
										break;
									case "release":
										empty = false;
										containsItems = true;

										bool? defaultrel = null;
										if (subreader.GetAttribute("default") != null)
										{
											defaultrel = Utilities.GetYesNo(subreader.GetAttribute("default"));
										}

										DatItem relrom = new Release
										{
											Name = subreader.GetAttribute("name"),
											Region = subreader.GetAttribute("region"),
											Language = subreader.GetAttribute("language"),
											Date = date,
											Default = defaultrel,

											Supported = supported,
											Publisher = publisher,
											Infos = infos,
											PartName = partname,
											PartInterface = partinterface,
											Features = features,
											AreaName = areaname,
											AreaSize = areasize,
										};

										relrom.CopyMachineInformation(machine);

										// Now process and add the rom
										key = ParseAddHelper(relrom, clean, remUnicode);

										subreader.Read();
										break;
									case "biosset":
										empty = false;
										containsItems = true;

										bool? defaultbios = null;
										if (subreader.GetAttribute("default") != null)
										{
											defaultbios = Utilities.GetYesNo(subreader.GetAttribute("default"));
										}

										DatItem biosrom = new BiosSet
										{
											Name = subreader.GetAttribute("name"),
											Description = subreader.GetAttribute("description"),
											Default = defaultbios,

											Supported = supported,
											Publisher = publisher,
											Infos = infos,
											PartName = partname,
											PartInterface = partinterface,
											Features = features,
											AreaName = areaname,
											AreaSize = areasize,

											SystemID = sysid,
											System = filename,
											SourceID = srcid,
										};

										biosrom.CopyMachineInformation(machine);

										// Now process and add the rom
										key = ParseAddHelper(biosrom, clean, remUnicode);

										subreader.Read();
										break;
									case "archive":
										empty = false;
										containsItems = true;

										DatItem archiverom = new Archive
										{
											Name = subreader.GetAttribute("name"),

											Supported = supported,
											Publisher = publisher,
											Infos = infos,
											PartName = partname,
											PartInterface = partinterface,
											Features = features,
											AreaName = areaname,
											AreaSize = areasize,

											SystemID = sysid,
											System = filename,
											SourceID = srcid,
										};

										archiverom.CopyMachineInformation(machine);

										// Now process and add the rom
										key = ParseAddHelper(archiverom, clean, remUnicode);

										subreader.Read();
										break;
									case "sample":
										empty = false;
										containsItems = true;

										DatItem samplerom = new Sample
										{
											Name = subreader.GetAttribute("name"),

											Supported = supported,
											Publisher = publisher,
											Infos = infos,
											PartName = partname,
											PartInterface = partinterface,
											Features = features,
											AreaName = areaname,
											AreaSize = areasize,

											SystemID = sysid,
											System = filename,
											SourceID = srcid,
										};

										samplerom.CopyMachineInformation(machine);

										// Now process and add the rom
										key = ParseAddHelper(samplerom, clean, remUnicode);

										subreader.Read();
										break;
									case "rom":
									case "disk":
										empty = false;
										containsItems = true;

										// If the rom has a merge tag, add it
										string merge = subreader.GetAttribute("merge");

										// If the rom has a status, flag it
										its = Utilities.GetItemStatus(subreader.GetAttribute("status"));
										if (its == ItemStatus.None)
										{
											its = Utilities.GetItemStatus(subreader.GetAttribute("flags"));
										}

										// If the rom has a Date attached, read it in and then sanitize it
										date = "";
										if (subreader.GetAttribute("date") != null)
										{
											DateTime dateTime = DateTime.Now;
											if (DateTime.TryParse(subreader.GetAttribute("date"), out dateTime))
											{
												date = dateTime.ToString();
											}
											else
											{
												date = subreader.GetAttribute("date");
											}
										}

										// Take care of hex-sized files
										size = -1;
										if (subreader.GetAttribute("size") != null && subreader.GetAttribute("size").Contains("0x"))
										{
											size = Convert.ToInt64(subreader.GetAttribute("size"), 16);
										}
										else if (subreader.GetAttribute("size") != null)
										{
											Int64.TryParse(subreader.GetAttribute("size"), out size);
										}

										// If the rom is continue or ignore, add the size to the previous rom
										if (subreader.GetAttribute("loadflag") == "continue" || subreader.GetAttribute("loadflag") == "ignore")
										{
											int index = this[key].Count() - 1;
											DatItem lastrom = this[key][index];
											if (lastrom.Type == ItemType.Rom)
											{
												((Rom)lastrom).Size += size;
											}
											this[key].RemoveAt(index);
											this[key].Add(lastrom);
											subreader.Read();
											continue;
										}

										// If we're in clean mode, sanitize the game name
										if (clean)
										{
											machine.Name = Utilities.CleanGameName(machine.Name.Split(Path.DirectorySeparatorChar));
										}

										DatItem inrom;
										switch (subreader.Name.ToLowerInvariant())
										{
											case "disk":
												inrom = new Disk
												{
													Name = subreader.GetAttribute("name"),
													MD5 = subreader.GetAttribute("md5")?.ToLowerInvariant(),
													SHA1 = subreader.GetAttribute("sha1")?.ToLowerInvariant(),
													SHA256 = subreader.GetAttribute("sha256")?.ToLowerInvariant(),
													SHA384 = subreader.GetAttribute("sha384")?.ToLowerInvariant(),
													SHA512 = subreader.GetAttribute("sha512")?.ToLowerInvariant(),
													MergeTag = merge,
													ItemStatus = its,

													Supported = supported,
													Publisher = publisher,
													Infos = infos,
													PartName = partname,
													PartInterface = partinterface,
													Features = features,
													AreaName = areaname,
													AreaSize = areasize,

													SystemID = sysid,
													System = filename,
													SourceID = srcid,
												};
												break;
											case "rom":
											default:
												inrom = new Rom
												{
													Name = subreader.GetAttribute("name"),
													Size = size,
													CRC = subreader.GetAttribute("crc"),
													MD5 = subreader.GetAttribute("md5")?.ToLowerInvariant(),
													SHA1 = subreader.GetAttribute("sha1")?.ToLowerInvariant(),
													SHA256 = subreader.GetAttribute("sha256")?.ToLowerInvariant(),
													SHA384 = subreader.GetAttribute("sha384")?.ToLowerInvariant(),
													SHA512 = subreader.GetAttribute("sha512")?.ToLowerInvariant(),
													ItemStatus = its,
													MergeTag = merge,
													Date = date,

													Supported = supported,
													Publisher = publisher,
													Infos = infos,
													PartName = partname,
													PartInterface = partinterface,
													Features = features,
													AreaName = areaname,
													AreaSize = areasize,

													SystemID = sysid,
													System = filename,
													SourceID = srcid,
												};
												break;
										}

										inrom.CopyMachineInformation(machine);

										// Now process and add the rom
										key = ParseAddHelper(inrom, clean, remUnicode);

										subreader.Read();
										break;
									default:
										subreader.Read();
										break;
								}
							}

							// If no items were found for this machine, add a Blank placeholder
							if (!containsItems)
							{
								Blank blank = new Blank()
								{
									Supported = supported,
									Publisher = publisher,
									Infos = infos,
									PartName = partname,
									PartInterface = partinterface,
									Features = features,
									AreaName = areaname,
									AreaSize = areasize,

									SystemID = sysid,
									System = filename,
									SourceID = srcid,
								};

								blank.CopyMachineInformation(machine);

								// Now process and add the rom
								key = ParseAddHelper(blank, clean, remUnicode);
							}

							xtr.Skip();
							break;
						case "dir":
						case "directory":
							// Set SuperDAT flag for all SabreDAT inputs, regardless of depth
							superdat = true;
							if (keep)
							{
								Type = (String.IsNullOrWhiteSpace(Type) ? "SuperDAT" : Type);
							}

							string foldername = (xtr.GetAttribute("name") ?? "");
							if (!String.IsNullOrWhiteSpace(foldername))
							{
								parent.Add(foldername);
							}

							xtr.Read();
							break;
						case "file":
							empty = false;
							containsItems = true;

							// If the rom is itemStatus, flag it
							its = ItemStatus.None;
							flagreader = xtr.ReadSubtree();

							// If the subtree is empty, skip it
							if (flagreader == null)
							{
								xtr.Skip();
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
									case "status":
										if (flagreader.GetAttribute("name") != null && flagreader.GetAttribute("value") != null)
										{
											string content = flagreader.GetAttribute("value");
											its = Utilities.GetItemStatus(flagreader.GetAttribute("name"));
										}
										break;
								}

								flagreader.Read();
							}

							// If the rom has a Date attached, read it in and then sanitize it
							date = "";
							if (xtr.GetAttribute("date") != null)
							{
								date = DateTime.Parse(xtr.GetAttribute("date")).ToString();
							}

							// Take care of hex-sized files
							size = -1;
							if (xtr.GetAttribute("size") != null && xtr.GetAttribute("size").Contains("0x"))
							{
								size = Convert.ToInt64(xtr.GetAttribute("size"), 16);
							}
							else if (xtr.GetAttribute("size") != null)
							{
								Int64.TryParse(xtr.GetAttribute("size"), out size);
							}

							// If the rom is continue or ignore, add the size to the previous rom
							if (xtr.GetAttribute("loadflag") == "continue" || xtr.GetAttribute("loadflag") == "ignore")
							{
								int index = this[key].Count() - 1;
								DatItem lastrom = this[key][index];
								if (lastrom.Type == ItemType.Rom)
								{
									((Rom)lastrom).Size += size;
								}
								this[key].RemoveAt(index);
								this[key].Add(lastrom);
								continue;
							}

							Machine dir = new Machine();

							// Get the name of the game from the parent
							dir.Name = String.Join("\\", parent);
							dir.Description = dir.Name;

							// If we aren't keeping names, trim out the path
							if (!keep || !superdat)
							{
								string tempout = Regex.Match(dir.Name, @".*?\\(.*)").Groups[1].Value;
								if (!String.IsNullOrWhiteSpace(tempout))
								{
									dir.Name = tempout;
								}
							}

							DatItem rom;
							switch (xtr.GetAttribute("type").ToLowerInvariant())
							{
								case "disk":
									rom = new Disk
									{
										Name = xtr.GetAttribute("name"),
										MD5 = xtr.GetAttribute("md5")?.ToLowerInvariant(),
										SHA1 = xtr.GetAttribute("sha1")?.ToLowerInvariant(),
										SHA256 = xtr.GetAttribute("sha256")?.ToLowerInvariant(),
										SHA384 = xtr.GetAttribute("sha384")?.ToLowerInvariant(),
										SHA512 = xtr.GetAttribute("sha512")?.ToLowerInvariant(),
										ItemStatus = its,

										SystemID = sysid,
										System = filename,
										SourceID = srcid,
									};
									break;
								case "rom":
								default:
									rom = new Rom
									{
										Name = xtr.GetAttribute("name"),
										Size = size,
										CRC = xtr.GetAttribute("crc")?.ToLowerInvariant(),
										MD5 = xtr.GetAttribute("md5")?.ToLowerInvariant(),
										SHA1 = xtr.GetAttribute("sha1")?.ToLowerInvariant(),
										SHA256 = xtr.GetAttribute("sha256")?.ToLowerInvariant(),
										SHA384 = xtr.GetAttribute("sha384")?.ToLowerInvariant(),
										SHA512 = xtr.GetAttribute("sha512")?.ToLowerInvariant(),
										ItemStatus = its,
										Date = date,

										SystemID = sysid,
										System = filename,
										SourceID = srcid,
									};
									break;
							}

							rom.CopyMachineInformation(dir);

							// Now process and add the rom
							key = ParseAddHelper(rom, clean, remUnicode);

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
				Globals.Logger.Warning("Exception found while parsing '{0}': {1}", filename, ex);

				// For XML errors, just skip the affected node
				xtr?.Read();
			}

			xtr.Dispose();
		}

		/// <summary>
		/// Parse a Logiqx XML DAT and return all found games and roms within
		/// </summary>
		/// <param name="filename">Name of the file to be parsed</param>
		/// <param name="sysid">System ID for the DAT</param>
		/// <param name="srcid">Source ID for the DAT</param>
		/// <param name="keep">True if full pathnames are to be kept, false otherwise (default)</param>
		/// <param name="clean">True if game names are sanitized, false otherwise (default)</param>
		/// <param name="remUnicode">True if we should remove non-ASCII characters from output, false otherwise (default)</param>
		public void ParseFileStripped(
			// Standard Dat parsing
			string filename,
			int sysid,
			int srcid,

			// Miscellaneous
			bool keep,
			bool clean,
			bool remUnicode)
		{
			// Prepare all internal variables
			Encoding enc = Utilities.GetEncoding(filename);
			XmlReader xtr = Utilities.GetXmlTextReader(filename);

			// If we got a null reader, just return
			if (xtr == null)
			{
				return;
			}

			// Otherwise, read the file to the end
			try
			{
				xtr.MoveToContent();
				while (!xtr.EOF)
				{
					// We only want elements
					if (xtr.NodeType != XmlNodeType.Element)
					{
						xtr.Read();
						continue;
					}

					switch (xtr.Name)
					{
						// The datafile tag can have some attributes
						case "datafile":
							// string build = xtr.GetAttribute("build");
							// string debug = xtr.GetAttribute("debug"); // (yes|no) "no"
							xtr.Read();
							break;
						// We want to process the entire subtree of the header
						case "header":
							ReadHeader(xtr.ReadSubtree(), keep);

							// Skip the header node now that we've processed it
							xtr.Skip();
							break;
						// We want to process the entire subtree of the game
						case "machine": // New-style Logiqx
						case "game": // Old-style Logiqx
							ReadMachine(xtr.ReadSubtree(), filename, sysid, srcid, keep, clean, remUnicode);

							// Skip the machine now that we've processed it
							xtr.Skip();
							break;
						default:
							xtr.Read();
							break;
					}
				}
			}
			catch (Exception ex)
			{
				Globals.Logger.Warning("Exception found while parsing '{0}': {1}", filename, ex);

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
			{
				return;
			}

			// Otherwise, add what is possible
			reader.MoveToContent();

			while (!reader.EOF)
			{
				// We only want elements
				if (reader.NodeType != XmlNodeType.Element || reader.Name == "header")
				{
					reader.Read();
					continue;
				}

				// Get all header items (ONLY OVERWRITE IF THERE'S NO DATA)
				string content = "";
				switch (reader.Name)
				{
					case "name":
						content = reader.ReadElementContentAsString(); ;
						Name = (String.IsNullOrWhiteSpace(Name) ? content : Name);
						superdat = superdat || content.Contains(" - SuperDAT");
						if (keep && superdat)
						{
							Type = (String.IsNullOrWhiteSpace(Type) ? "SuperDAT" : Type);
						}
						break;
					case "description":
						content = reader.ReadElementContentAsString();
						Description = (String.IsNullOrWhiteSpace(Description) ? content : Description);
						break;
					case "rootdir": // This is exclusive to TruRip XML
						content = reader.ReadElementContentAsString();
						RootDir = (String.IsNullOrWhiteSpace(RootDir) ? content : RootDir);
						break;
					case "category":
						content = reader.ReadElementContentAsString();
						Category = (String.IsNullOrWhiteSpace(Category) ? content : Category);
						break;
					case "version":
						content = reader.ReadElementContentAsString();
						Version = (String.IsNullOrWhiteSpace(Version) ? content : Version);
						break;
					case "date":
						content = reader.ReadElementContentAsString();
						Date = (String.IsNullOrWhiteSpace(Date) ? content.Replace(".", "/") : Date);
						break;
					case "author":
						content = reader.ReadElementContentAsString();
						Author = (String.IsNullOrWhiteSpace(Author) ? content : Author);
						break;
					case "email":
						content = reader.ReadElementContentAsString();
						Email = (String.IsNullOrWhiteSpace(Email) ? content : Email);
						break;
					case "homepage":
						content = reader.ReadElementContentAsString();
						Homepage = (String.IsNullOrWhiteSpace(Homepage) ? content : Homepage);
						break;
					case "url":
						content = reader.ReadElementContentAsString();
						Url = (String.IsNullOrWhiteSpace(Url) ? content : Url);
						break;
					case "comment":
						content = reader.ReadElementContentAsString();
						Comment = (String.IsNullOrWhiteSpace(Comment) ? content : Comment);
						break;
					case "type": // This is exclusive to TruRip XML
						content = reader.ReadElementContentAsString();
						Type = (String.IsNullOrWhiteSpace(Type) ? content : Type);
						superdat = superdat || content.Contains("SuperDAT");
						break;
					case "clrmamepro":
						if (String.IsNullOrWhiteSpace(Header))
						{
							Header = reader.GetAttribute("header");
						}
						if (ForceMerging == ForceMerging.None)
						{
							ForceMerging = Utilities.GetForceMerging(reader.GetAttribute("forcemerging"));
						}
						if (ForceNodump == ForceNodump.None)
						{
							ForceNodump = Utilities.GetForceNodump(reader.GetAttribute("forcenodump"));
						}
						if (ForcePacking == ForcePacking.None)
						{
							ForcePacking = Utilities.GetForcePacking(reader.GetAttribute("forcepacking"));
						}
						break;
					case "romcenter":
						if (reader.GetAttribute("plugin") != null)
						{
							// CDATA
						}
						if (reader.GetAttribute("rommode") != null)
						{
							// (merged|split|unmerged) "split"
						}
						if (reader.GetAttribute("biosmode") != null)
						{
							// merged|split|unmerged) "split"
						}
						if (reader.GetAttribute("samplemode") != null)
						{
							// (merged|unmerged) "merged"
						}
						if (reader.GetAttribute("lockrommode") != null)
						{
							// (yes|no) "no"
						}
						if (reader.GetAttribute("lockbiosmode") != null)
						{
							// (yes|no) "no"
						}
						if (reader.GetAttribute("locksamplemode") != null)
						{
							// (yes|no) "no"
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
		/// Read game/machine information
		/// </summary>
		/// <param name="filename">Name of the file to be parsed</param>
		/// <param name="sysid">System ID for the DAT</param>
		/// <param name="srcid">Source ID for the DAT</param>
		/// <param name="keep">True if full pathnames are to be kept, false otherwise (default)</param>
		/// <param name="clean">True if game names are sanitized, false otherwise (default)</param>
		/// <param name="remUnicode">True if we should remove non-ASCII characters from output, false otherwise (default)</param>
		private void ReadMachine(
			XmlReader reader,

			// Standard Dat parsing
			string filename,
			int sysid,
			int srcid,

			// Miscellaneous
			bool keep,
			bool clean,
			bool remUnicode)
		{
			// If we have an empty machine, skip it
			if (reader == null)
			{
				return;
			}

			// Otherwise, add what is possible
			reader.MoveToContent();

			string key = "";
			string temptype = reader.Name;
			bool containsItems = false;
			List<string> parent = new List<string>();

			// Create a new machine
			MachineType machineType = MachineType.NULL;
			if (Utilities.GetYesNo(reader.GetAttribute("isbios")) == true)
			{
				machineType |= MachineType.Bios;
			}
			if (Utilities.GetYesNo(reader.GetAttribute("isdevice")) == true)
			{
				machineType |= MachineType.Device;
			}
			if (Utilities.GetYesNo(reader.GetAttribute("ismechanical")) == true)
			{
				machineType |= MachineType.Mechanical;
			}

			Machine machine = new Machine
			{
				Name = reader.GetAttribute("name"),
				Description = reader.GetAttribute("name"),
				SourceFile = reader.GetAttribute("sourcefile"),
				Board = reader.GetAttribute("board"),
				RebuildTo = reader.GetAttribute("rebuildto"),

				Comment = "",

				CloneOf = reader.GetAttribute("cloneof") ?? "",
				RomOf = reader.GetAttribute("romof") ?? "",
				SampleOf = reader.GetAttribute("sampleof") ?? "",

				MachineType = (machineType == MachineType.NULL ? MachineType.None : machineType),
			};

			if (Type == "SuperDAT" && !keep)
			{
				string tempout = Regex.Match(machine.Name, @".*?\\(.*)").Groups[1].Value;
				if (!String.IsNullOrWhiteSpace(tempout))
				{
					machine.Name = tempout;
				}
			}
			// Get the name of the game from the parent
			else if (Type == "SuperDAT" && keep && parent.Count > 0)
			{
				machine.Name = String.Join("\\", parent) + "\\" + machine.Name;
			}

			while (!reader.EOF)
			{
				// We only want elements
				if (reader.NodeType != XmlNodeType.Element)
				{
					reader.Read();
					continue;
				}

				// Get the roms from the machine
				switch (reader.Name)
				{
					case "comment": // There can be multiple comments by spec
						machine.Comment += reader.ReadElementContentAsString();
						break;
					case "description":
						machine.Description = reader.ReadElementContentAsString();
						break;
					case "year":
						machine.Year = reader.ReadElementContentAsString();
						break;
					case "manufacturer":
						machine.Manufacturer = reader.ReadElementContentAsString();
						break;
					case "trurip": // This is special metadata unique to TruRip
						ReadTruRip(reader.ReadSubtree(), machine);

						// Skip the trurip node now that we've processed it
						reader.Skip();
						break;
					case "release":
						containsItems = true;

						DatItem release = new Release
						{
							Name = reader.GetAttribute("name"),
							Region = reader.GetAttribute("region"),
							Language = reader.GetAttribute("language"),
							Date = reader.GetAttribute("date"),
							Default = Utilities.GetYesNo(reader.GetAttribute("default")),
						};

						release.CopyMachineInformation(machine);

						// Now process and add the rom
						key = ParseAddHelper(release, clean, remUnicode);

						reader.Read();
						break;
					case "biosset":
						containsItems = true;

						DatItem biosset = new BiosSet
						{
							Name = reader.GetAttribute("name"),
							Description = reader.GetAttribute("description"),
							Default = Utilities.GetYesNo(reader.GetAttribute("default")),

							SystemID = sysid,
							System = filename,
							SourceID = srcid,
						};

						biosset.CopyMachineInformation(machine);

						// Now process and add the rom
						key = ParseAddHelper(biosset, clean, remUnicode);

						reader.Read();
						break;
					case "rom":
						containsItems = true;

						DatItem rom = new Rom
						{
							Name = reader.GetAttribute("name"),
							Size = Utilities.GetSize(reader.GetAttribute("size")),
							CRC = reader.GetAttribute("crc")?.ToLowerInvariant(),
							MD5 = reader.GetAttribute("md5")?.ToLowerInvariant(),
							SHA1 = reader.GetAttribute("sha1")?.ToLowerInvariant(),
							SHA256 = reader.GetAttribute("sha256")?.ToLowerInvariant(),
							SHA384 = reader.GetAttribute("sha384")?.ToLowerInvariant(),
							SHA512 = reader.GetAttribute("sha512")?.ToLowerInvariant(),
							MergeTag = reader.GetAttribute("merge"),
							ItemStatus = Utilities.GetItemStatus(reader.GetAttribute("status")),
							Date = Utilities.GetDate(reader.GetAttribute("date")),

							SystemID = sysid,
							System = filename,
							SourceID = srcid,
						};

						rom.CopyMachineInformation(machine);

						// Now process and add the rom
						key = ParseAddHelper(rom, clean, remUnicode);

						reader.Read();
						break;
					case "disk":
						containsItems = true;

						DatItem disk = new Disk
						{
							Name = reader.GetAttribute("name"),
							MD5 = reader.GetAttribute("md5")?.ToLowerInvariant(),
							SHA1 = reader.GetAttribute("sha1")?.ToLowerInvariant(),
							SHA256 = reader.GetAttribute("sha256")?.ToLowerInvariant(),
							SHA384 = reader.GetAttribute("sha384")?.ToLowerInvariant(),
							SHA512 = reader.GetAttribute("sha512")?.ToLowerInvariant(),
							MergeTag = reader.GetAttribute("merge"),
							ItemStatus = Utilities.GetItemStatus(reader.GetAttribute("status")),

							SystemID = sysid,
							System = filename,
							SourceID = srcid,
						};

						disk.CopyMachineInformation(machine);

						// Now process and add the rom
						key = ParseAddHelper(disk, clean, remUnicode);

						reader.Read();
						break;
					case "sample":
						containsItems = true;

						DatItem samplerom = new Sample
						{
							Name = reader.GetAttribute("name"),

							SystemID = sysid,
							System = filename,
							SourceID = srcid,
						};

						samplerom.CopyMachineInformation(machine);

						// Now process and add the rom
						key = ParseAddHelper(samplerom, clean, remUnicode);

						reader.Read();
						break;
					case "archive":
						containsItems = true;

						DatItem archiverom = new Archive
						{
							Name = reader.GetAttribute("name"),

							SystemID = sysid,
							System = filename,
							SourceID = srcid,
						};

						archiverom.CopyMachineInformation(machine);

						// Now process and add the rom
						key = ParseAddHelper(archiverom, clean, remUnicode);

						reader.Read();
						break;
					default:
						reader.Read();
						break;
				}
			}

			// If no items were found for this machine, add a Blank placeholder
			if (!containsItems)
			{
				Blank blank = new Blank()
				{
					SystemID = sysid,
					System = filename,
					SourceID = srcid,
				};

				blank.CopyMachineInformation(machine);

				// Now process and add the rom
				key = ParseAddHelper(blank, clean, remUnicode);
			}
		}

		/// <summary>
		/// Read TruRip information
		/// </summary>
		/// <param name="keep">True if full pathnames are to be kept, false otherwise (default)</param>
		/// <param name="machine">Machine information to pass to contained items</param>
		private void ReadTruRip(XmlReader reader, Machine machine)
		{
			// If we have an empty trurip, skip it
			if (reader == null)
			{
				return;
			}

			// Otherwise, add what is possible
			reader.MoveToContent();

			while (!reader.EOF)
			{
				// We only want elements
				if (reader.NodeType != XmlNodeType.Element)
				{
					reader.Read();
					continue;
				}

				// Get the information from the trurip
				string content = "";
				switch (reader.Name)
				{
					case "titleid":
						content = reader.ReadElementContentAsString();
						// string titleid = content;
						break;
					case "publisher":
						machine.Publisher = reader.ReadElementContentAsString();
						break;
					case "developer": // Manufacturer is as close as this gets
						machine.Manufacturer = reader.ReadElementContentAsString();
						break;
					case "year":
						machine.Year = reader.ReadElementContentAsString();
						break;
					case "genre":
						content = reader.ReadElementContentAsString();
						// string genre = content;
						break;
					case "subgenre":
						content = reader.ReadElementContentAsString();
						// string subgenre = content;
						break;
					case "ratings":
						content = reader.ReadElementContentAsString();
						// string ratings = content;
						break;
					case "score":
						content = reader.ReadElementContentAsString();
						// string score = content;
						break;
					case "players":
						content = reader.ReadElementContentAsString();
						// string players = content;
						break;
					case "enabled":
						content = reader.ReadElementContentAsString();
						// string enabled = content;
						break;
					case "crc":
						content = reader.ReadElementContentAsString();
						// string crc = Utilities.GetYesNo(content);
						break;
					case "source":
						content = reader.ReadElementContentAsString();
						// string source = content;
						break;
					case "cloneof":
						machine.CloneOf = reader.ReadElementContentAsString();
						break;
					case "relatedto":
						content = reader.ReadElementContentAsString();
						// string relatedto = content;
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
		public override bool WriteToFile(string outfile, bool ignoreblanks = false)
		{
			try
			{
				Globals.Logger.User("Opening file for writing: {0}", outfile);
				FileStream fs = Utilities.TryCreate(outfile);

				// If we get back null for some reason, just log and return
				if (fs == null)
				{
					Globals.Logger.Warning("File '{0}' could not be created for writing! Please check to see if the file is writable", outfile);
					return false;
				}

				StreamWriter sw = new StreamWriter(fs, new UTF8Encoding(false));

				// Write out the header
				WriteHeader(sw);

				// Write out each of the machines and roms
				string lastgame = null;

				// Get a properly sorted set of keys
				List<string> keys = Keys;
				keys.Sort(new NaturalComparer());

				foreach (string key in keys)
				{
					List<DatItem> roms = this[key];

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

						// If we have a different game and we're not at the start of the list, output the end of last item
						if (lastgame != null && lastgame.ToLowerInvariant() != rom.MachineName.ToLowerInvariant())
						{
							WriteEndGame(sw);
						}

						// If we have a new game, output the beginning of the new item
						if (lastgame == null || lastgame.ToLowerInvariant() != rom.MachineName.ToLowerInvariant())
						{
							WriteStartGame(sw, rom);
						}

						// If we have a "null" game (created by DATFromDir or something similar), log it to file
						if (rom.Type == ItemType.Rom
							&& ((Rom)rom).Size == -1
							&& ((Rom)rom).CRC == "null")
						{
							Globals.Logger.Verbose("Empty folder found: {0}", rom.MachineName);

							rom.Name = (rom.Name == "null" ? "-" : rom.Name);
							((Rom)rom).Size = Constants.SizeZero;
							((Rom)rom).CRC = ((Rom)rom).CRC == "null" ? Constants.CRCZero : null;
							((Rom)rom).MD5 = ((Rom)rom).MD5 == "null" ? Constants.MD5Zero : null;
							((Rom)rom).SHA1 = ((Rom)rom).SHA1 == "null" ? Constants.SHA1Zero : null;
							((Rom)rom).SHA256 = ((Rom)rom).SHA256 == "null" ? Constants.SHA256Zero : null;
							((Rom)rom).SHA384 = ((Rom)rom).SHA384 == "null" ? Constants.SHA384Zero : null;
							((Rom)rom).SHA512 = ((Rom)rom).SHA512 == "null" ? Constants.SHA512Zero : null;
						}

						// Now, output the rom data
						WriteDatItem(sw, rom, ignoreblanks);

						// Set the new data to compare against
						lastgame = rom.MachineName;
					}
				}

				// Write the file footer out
				WriteFooter(sw);

				Globals.Logger.Verbose("File written!" + Environment.NewLine);
				sw.Dispose();
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
		/// <param name="sw">StreamWriter to output to</param>
		/// <returns>True if the data was written, false on error</returns>
		private bool WriteHeader(StreamWriter sw)
		{
			try
			{
				string header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
							"<!DOCTYPE datafile PUBLIC \"-//Logiqx//DTD ROM Management Datafile//EN\" \"http://www.logiqx.com/Dats/datafile.dtd\">\n\n" +
							"<datafile>\n" +
							"\t<header>\n" +
							"\t\t<name>" + HttpUtility.HtmlEncode(Name) + "</name>\n" +
							"\t\t<description>" + HttpUtility.HtmlEncode(Description) + "</description>\n" +
							(!String.IsNullOrWhiteSpace(RootDir) ? "\t\t<rootdir>" + HttpUtility.HtmlEncode(RootDir) + "</rootdir>\n" : "") +
							(!String.IsNullOrWhiteSpace(Category) ? "\t\t<category>" + HttpUtility.HtmlEncode(Category) + "</category>\n" : "") +
							"\t\t<version>" + HttpUtility.HtmlEncode(Version) + "</version>\n" +
							(!String.IsNullOrWhiteSpace(Date) ? "\t\t<date>" + HttpUtility.HtmlEncode(Date) + "</date>\n" : "") +
							"\t\t<author>" + HttpUtility.HtmlEncode(Author) + "</author>\n" +
							(!String.IsNullOrWhiteSpace(Email) ? "\t\t<email>" + HttpUtility.HtmlEncode(Email) + "</email>\n" : "") +
							(!String.IsNullOrWhiteSpace(Homepage) ? "\t\t<homepage>" + HttpUtility.HtmlEncode(Homepage) + "</homepage>\n" : "") +
							(!String.IsNullOrWhiteSpace(Url) ? "\t\t<url>" + HttpUtility.HtmlEncode(Url) + "</url>\n" : "") +
							(!String.IsNullOrWhiteSpace(Comment) ? "\t\t<comment>" + HttpUtility.HtmlEncode(Comment) + "</comment>\n" : "") +
							(!String.IsNullOrWhiteSpace(Type) ? "\t\t<type>" + HttpUtility.HtmlEncode(Type) + "</type>\n" : "") +
							(ForcePacking != ForcePacking.None || ForceMerging != ForceMerging.None || ForceNodump != ForceNodump.None ?
								"\t\t<clrmamepro" +
									(ForcePacking == ForcePacking.Unzip ? " forcepacking=\"unzip\"" : "") +
									(ForcePacking == ForcePacking.Zip ? " forcepacking=\"zip\"" : "") +
									(ForceMerging == ForceMerging.Full ? " forcemerging=\"full\"" : "") +
									(ForceMerging == ForceMerging.Split ? " forcemerging=\"split\"" : "") +
									(ForceMerging == ForceMerging.Merged ? " forcemerging=\"merged\"" : "") +
									(ForceMerging == ForceMerging.NonMerged ? " forcemerging=\"nonmerged\"" : "") +
									(ForceNodump == ForceNodump.Ignore ? " forcenodump=\"ignore\"" : "") +
									(ForceNodump == ForceNodump.Obsolete ? " forcenodump=\"obsolete\"" : "") +
									(ForceNodump == ForceNodump.Required ? " forcenodump=\"required\"" : "") +
									" />\n"
							: "") +
							"\t</header>\n";

				// Write the header out
				sw.Write(header);
				sw.Flush();
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
		/// <param name="sw">StreamWriter to output to</param>
		/// <param name="rom">DatItem object to be output</param>
		/// <returns>True if the data was written, false on error</returns>
		private bool WriteStartGame(StreamWriter sw, DatItem rom)
		{
			try
			{
				// No game should start with a path separator
				if (rom.MachineName.StartsWith(Path.DirectorySeparatorChar.ToString()))
				{
					rom.MachineName = rom.MachineName.Substring(1);
				}

				string state = "\t<" + (_depreciated ? "game" : "machine") + " name=\"" + HttpUtility.HtmlEncode(rom.MachineName) + "\"" +
								(ExcludeOf ? "" :
									((rom.MachineType & MachineType.Bios) != 0 ? " isbios=\"yes\"" : "") +
									((rom.MachineType & MachineType.Device) != 0 ? " isdevice=\"yes\"" : "") +
									((rom.MachineType & MachineType.Mechanical) != 0 ? " ismechanical=\"yes\"" : "") +
									(rom.Runnable == true ? " runnable=\"yes\"" : "") +
									(String.IsNullOrWhiteSpace(rom.CloneOf) || (rom.MachineName.ToLowerInvariant() == rom.CloneOf.ToLowerInvariant())
										? ""
										: " cloneof=\"" + HttpUtility.HtmlEncode(rom.CloneOf) + "\"") +
									(String.IsNullOrWhiteSpace(rom.RomOf) || (rom.MachineName.ToLowerInvariant() == rom.RomOf.ToLowerInvariant())
										? ""
										: " romof=\"" + HttpUtility.HtmlEncode(rom.RomOf) + "\"") +
									(String.IsNullOrWhiteSpace(rom.SampleOf) || (rom.MachineName.ToLowerInvariant() == rom.SampleOf.ToLowerInvariant())
										? ""
										: " sampleof=\"" + HttpUtility.HtmlEncode(rom.SampleOf) + "\"")
								) +
								">\n" +
							(String.IsNullOrWhiteSpace(rom.Comment) ? "" : "\t\t<comment>" + HttpUtility.HtmlEncode(rom.Comment) + "</comment>\n") +
							"\t\t<description>" + HttpUtility.HtmlEncode((String.IsNullOrWhiteSpace(rom.MachineDescription) ? rom.MachineName : rom.MachineDescription)) + "</description>\n" +
							(String.IsNullOrWhiteSpace(rom.Year) ? "" : "\t\t<year>" + HttpUtility.HtmlEncode(rom.Year) + "</year>\n") +
							(String.IsNullOrWhiteSpace(rom.Manufacturer) ? "" : "\t\t<manufacturer>" + HttpUtility.HtmlEncode(rom.Manufacturer) + "</manufacturer>\n");

				sw.Write(state);
				sw.Flush();
			}
			catch (Exception ex)
			{
				Globals.Logger.Error(ex.ToString());
				return false;
			}

			return true;
		}

		/// <summary>
		/// Write out Game end using the supplied StreamWriter
		/// </summary>
		/// <param name="sw">StreamWriter to output to</param>
		/// <returns>True if the data was written, false on error</returns>
		private bool WriteEndGame(StreamWriter sw)
		{
			try
			{
				string state = "\t</" + (_depreciated ? "game" : "machine") + ">\n";

				sw.Write(state);
				sw.Flush();
			}
			catch (Exception ex)
			{
				Globals.Logger.Error(ex.ToString());
				return false;
			}

			return true;
		}

		/// <summary>
		/// Write out DatItem using the supplied StreamWriter
		/// </summary>
		/// <param name="sw">StreamWriter to output to</param>
		/// <param name="rom">DatItem object to be output</param>
		/// <param name="ignoreblanks">True if blank roms should be skipped on output, false otherwise (default)</param>
		/// <returns>True if the data was written, false on error</returns>
		private bool WriteDatItem(StreamWriter sw, DatItem rom, bool ignoreblanks = false)
		{
			// If we are in ignore blanks mode AND we have a blank (0-size) rom, skip
			if (ignoreblanks
				&& (rom.Type == ItemType.Rom
				&& (((Rom)rom).Size == 0 || ((Rom)rom).Size == -1)))
			{
				return true;
			}

			try
			{
				string state = "";
				switch (rom.Type)
				{
					case ItemType.Archive:
						state += "\t\t<archive name=\"" + HttpUtility.HtmlEncode(rom.Name) + "\""
							+ "/>\n";
						break;
					case ItemType.BiosSet:
						state += "\t\t<biosset name=\"" + HttpUtility.HtmlEncode(rom.Name) + "\""
							+ (!String.IsNullOrWhiteSpace(((BiosSet)rom).Description) ? " description=\"" + HttpUtility.HtmlEncode(((BiosSet)rom).Description) + "\"" : "")
							+ (((BiosSet)rom).Default != null
								? ((BiosSet)rom).Default.ToString().ToLowerInvariant()
								: "")
							+ "/>\n";
						break;
					case ItemType.Disk:
						state += "\t\t<disk name=\"" + HttpUtility.HtmlEncode(rom.Name) + "\""
							+ (!String.IsNullOrWhiteSpace(((Disk)rom).MD5) ? " md5=\"" + ((Disk)rom).MD5.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Disk)rom).SHA1) ? " sha1=\"" + ((Disk)rom).SHA1.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Disk)rom).SHA256) ? " sha256=\"" + ((Disk)rom).SHA256.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Disk)rom).SHA384) ? " sha384=\"" + ((Disk)rom).SHA384.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Disk)rom).SHA512) ? " sha512=\"" + ((Disk)rom).SHA512.ToLowerInvariant() + "\"" : "")
							+ (((Disk)rom).ItemStatus != ItemStatus.None ? " status=\"" + ((Disk)rom).ItemStatus.ToString().ToLowerInvariant() + "\"" : "")
							+ "/>\n";
						break;
					case ItemType.Release:
						state += "\t\t<release name=\"" + HttpUtility.HtmlEncode(rom.Name) + "\""
							+ (!String.IsNullOrWhiteSpace(((Release)rom).Region) ? " region=\"" + HttpUtility.HtmlEncode(((Release)rom).Region) + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Release)rom).Language) ? " language=\"" + HttpUtility.HtmlEncode(((Release)rom).Language) + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Release)rom).Date) ? " date=\"" + HttpUtility.HtmlEncode(((Release)rom).Date) + "\"" : "")
							+ (((Release)rom).Default != null
								? ((Release)rom).Default.ToString().ToLowerInvariant()
								: "")
							+ "/>\n";
						break;
					case ItemType.Rom:
						state += "\t\t<rom name=\"" + HttpUtility.HtmlEncode(rom.Name) + "\""
							+ (((Rom)rom).Size != -1 ? " size=\"" + ((Rom)rom).Size + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Rom)rom).CRC) ? " crc=\"" + ((Rom)rom).CRC.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Rom)rom).MD5) ? " md5=\"" + ((Rom)rom).MD5.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Rom)rom).SHA1) ? " sha1=\"" + ((Rom)rom).SHA1.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Rom)rom).SHA256) ? " sha256=\"" + ((Rom)rom).SHA256.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Rom)rom).SHA384) ? " sha384=\"" + ((Rom)rom).SHA384.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Rom)rom).SHA512) ? " sha512=\"" + ((Rom)rom).SHA512.ToLowerInvariant() + "\"" : "")
							+ (!String.IsNullOrWhiteSpace(((Rom)rom).Date) ? " date=\"" + ((Rom)rom).Date + "\"" : "")
							+ (((Rom)rom).ItemStatus != ItemStatus.None ? " status=\"" + ((Rom)rom).ItemStatus.ToString().ToLowerInvariant() + "\"" : "")
							+ "/>\n";
						break;
					case ItemType.Sample:
						state += "\t\t<sample name=\"" + HttpUtility.HtmlEncode(rom.Name) + "\""
							+ "/>\n";
						break;
				}

				sw.Write(state);
				sw.Flush();
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
		/// <param name="sw">StreamWriter to output to</param>
		/// <returns>True if the data was written, false on error</returns>
		private bool WriteFooter(StreamWriter sw)
		{
			try
			{
				string footer = "\t</machine>\n</datafile>\n";

				// Write the footer out
				sw.Write(footer);
				sw.Flush();
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
