﻿using System;

using SabreTools.Library.Data;

namespace SabreTools.Library.Items
{
	/// <summary>
	/// Represents a generic file within a set
	/// </summary>
	public class Rom : DatItem
	{
		#region Private instance variables

		// Rom information
		private long _size;
		private string _crc;
		private string _md5;
		private string _sha1;
		private string _sha256;
		private string _sha384;
		private string _sha512;
		private string _date;
		private ItemStatus _itemStatus;

		#endregion

		#region Publicly facing variables

		// Rom information
		public long Size
		{
			get { return _size; }
			set { _size = value; }
		}
		public string CRC
		{
			get { return _crc; }
			set { _crc = value; }
		}
		public string MD5
		{
			get { return _md5; }
			set { _md5 = value; }
		}
		public string SHA1
		{
			get { return _sha1; }
			set { _sha1 = value; }
		}
		public string SHA256
		{
			get { return _sha256; }
			set { _sha256 = value; }
		}
		public string SHA384
		{
			get { return _sha384; }
			set { _sha384 = value; }
		}
		public string SHA512
		{
			get { return _sha512; }
			set { _sha512 = value; }
		}
		public string Date
		{
			get { return _date; }
			set { _date = value; }
		}
		public ItemStatus ItemStatus
		{
			get { return _itemStatus; }
			set { _itemStatus = value; }
		}

		#endregion

		#region Constructors

		/// <summary>
		/// Create a default, empty Rom object
		/// </summary>
		public Rom()
		{
			_name = "";
			_itemType = ItemType.Rom;
			_dupeType = 0x00;
			_itemStatus = ItemStatus.None;
			_date = "";
		}

		/// <summary>
		/// Create a "blank" Rom object
		/// </summary>
		/// <param name="name"></param>
		/// <param name="machineName"></param>
		/// <param name="omitFromScan"></param>
		/// <remarks>TODO: All instances of Hash.DeepHashes should be made into 0x0 eventually</remarks>
		public Rom(string name, string machineName, Hash omitFromScan = Hash.DeepHashes)
		{
			_name = name;
			_itemType = ItemType.Rom;
			_size = -1;
			if ((omitFromScan & Hash.CRC) == 0)
			{
				_crc = "null";
			}
			if ((omitFromScan & Hash.MD5) == 0)
			{
				_md5 = "null";
			}
			if ((omitFromScan & Hash.SHA1) == 0)
			{
				_sha1 = "null";
			}
			if ((omitFromScan & Hash.SHA256) == 0)
			{
				_sha256 = "null";
			}
			if ((omitFromScan & Hash.SHA384) == 0)
			{
				_sha384 = "null";
			}
			if ((omitFromScan & Hash.SHA512) == 0)
			{
				_sha512 = "null";
			}
			_itemStatus = ItemStatus.None;

			_machine = new Machine
			{
				Name = machineName,
				Description = machineName,
			};
		}

		#endregion

		#region Cloning Methods

		public override object Clone()
		{
			return new Rom()
			{
				Name = this.Name,
				Type = this.Type,
				Dupe = this.Dupe,

				Supported = this.Supported,
				Publisher = this.Publisher,
				Infos = this.Infos,
				PartName = this.PartName,
				PartInterface = this.PartInterface,
				Features = this.Features,
				AreaName = this.AreaName,
				AreaSize = this.AreaSize,

				MachineName = this.MachineName,
				Comment = this.Comment,
				MachineDescription = this.MachineDescription,
				Year = this.Year,
				Manufacturer = this.Manufacturer,
				RomOf = this.RomOf,
				CloneOf = this.CloneOf,
				SampleOf = this.SampleOf,
				SourceFile = this.SourceFile,
				Runnable = this.Runnable,
				Board = this.Board,
				RebuildTo = this.RebuildTo,
				Devices = this.Devices,
				MachineType = this.MachineType,

				SystemID = this.SystemID,
				System = this.System,
				SourceID = this.SourceID,
				Source = this.Source,

				Size = this.Size,
				CRC = this.CRC,
				MD5 = this.MD5,
				SHA1 = this.SHA1,
				SHA256 = this.SHA256,
				SHA384 = this.SHA384,
				SHA512 = this.SHA512,
				ItemStatus = this.ItemStatus,
				Date = this.Date,
			};
		}

		#endregion

		#region Comparision Methods

		public override bool Equals(DatItem other)
		{
			bool dupefound = false;

			// If we don't have a rom, return false
			if (_itemType != other.Type)
			{
				return dupefound;
			}

			// Otherwise, treat it as a rom
			Rom newOther = (Rom)other;

			// If either is a nodump, it's never a match
			if (_itemStatus == ItemStatus.Nodump || newOther.ItemStatus == ItemStatus.Nodump)
			{
				return dupefound;
			}

			// If we can determine that the roms have no non-empty hashes in common, we return false
			if ((String.IsNullOrEmpty(_crc) || String.IsNullOrEmpty(newOther.CRC))
				&& (String.IsNullOrEmpty(_md5) || String.IsNullOrEmpty(newOther.MD5))
				&& (String.IsNullOrEmpty(_sha1) || String.IsNullOrEmpty(newOther.SHA1))
				&& (String.IsNullOrEmpty(_sha256) || String.IsNullOrEmpty(newOther.SHA256))
				&& (String.IsNullOrEmpty(_sha384) || String.IsNullOrEmpty(newOther.SHA384))
				&& (String.IsNullOrEmpty(_sha512) || String.IsNullOrEmpty(newOther.SHA512)))
			{
				dupefound = false;
			}
			else if ((this.Size == newOther.Size)
				&& ((String.IsNullOrEmpty(_crc) || String.IsNullOrEmpty(newOther.CRC)) || _crc == newOther.CRC)
				&& ((String.IsNullOrEmpty(_md5) || String.IsNullOrEmpty(newOther.MD5)) || _md5 == newOther.MD5)
				&& ((String.IsNullOrEmpty(_sha1) || String.IsNullOrEmpty(newOther.SHA1)) || _sha1 == newOther.SHA1)
				&& ((String.IsNullOrEmpty(_sha256) || String.IsNullOrEmpty(newOther.SHA256)) || _sha256 == newOther.SHA256)
				&& ((String.IsNullOrEmpty(_sha384) || String.IsNullOrEmpty(newOther.SHA384)) || _sha384 == newOther.SHA384)
				&& ((String.IsNullOrEmpty(_sha512) || String.IsNullOrEmpty(newOther.SHA512)) || _sha512 == newOther.SHA512))
			{
				dupefound = true;
			}

			return dupefound;
		}

		#endregion
	}
}
