﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TShockAPI.Database;

namespace TShockAPI
{
	/// <summary>
	/// A class used to group multiple users' permissions and settings.
	/// </summary>
	public class Group : MongoDB.Entities.Entity
	{
		/// <summary>
		/// Default chat color.
		/// </summary>
		public const string DefaultChatColor = "255,255,255";

		/// <summary>
		/// List of permissions available to the group.
		/// </summary>
		public virtual List<string> Permissions { get; set; } = new List<string>();

		/// <summary>
		/// List of permissions that the group is explicitly barred from.
		/// </summary>
		public virtual List<string> NegatedPermissions { get; set; } = new List<string>();

		/// <summary>
		/// The group's name.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The chat prefix for this group.
		/// </summary>
		public string Prefix { get; set; }

		/// <summary>
		/// The chat suffix for this group.
		/// </summary>
		public string Suffix { get; set; }

		/// <summary>
		/// The name of the parent group, if any.
		/// </summary>
		public string? ParentGroupName { get; set; }

		/// <summary>
		/// The chat color of the group in "R,G,B" format. Each component should be in the range 0-255.
		/// </summary>
		public string ChatColor
		{
			get => $"{R:D3},{G:D3},{B:D3}";
			set
			{
				if (value == null) throw new ArgumentNullException(nameof(value), "ChatColor cannot be null.");

				var parts = value.Split(',');
				if (parts.Length != 3)
					throw new ArgumentException("ChatColor must be in the format \"R,G,B\".", nameof(value));

				if (byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) &&
				    byte.TryParse(parts[2], out var b))
				{
					R = r;
					G = g;
					B = b;
				}
				else
				{
					throw new ArgumentException(
						"Each component of ChatColor must be a valid byte value in the range 0-255.", nameof(value));
				}
			}
		}


		/// <summary>
		/// The permissions of this group and all that it inherits from.
		/// </summary>
		public virtual async Task<List<string>> GetPermissions()
		{
			var cur = this;
			var traversed = new List<Group>();
			var all = new HashSet<string>();
			while (cur != null)
			{
				foreach (var perm in cur.Permissions)
				{
					all.Add(perm);
				}

				foreach (var perm in cur.NegatedPermissions)
				{
					all.Remove(perm);
				}

				if (traversed.Contains(cur))
				{
					throw new Exception("Infinite group parenting ({0})".SFormat(cur.Name));
				}

				traversed.Add(cur);
				cur = await GroupManager.GetGroupByName(cur.ParentGroupName);
			}

			return all.ToList();
		}

		/// <summary>
		/// The group's chat color red byte.
		/// </summary>
		public byte R = 255;

		/// <summary>
		/// The group's chat color green byte.
		/// </summary>
		public byte G = 255;

		/// <summary>
		/// The group's chat color blue byte.
		/// </summary>
		public byte B = 255;

		/// <summary>
		/// The default group attributed to unregistered users.
		/// </summary>
		public static Group? DefaultGroup = null;

		/// <summary>
		/// Initializes a new instance of the group class.
		/// </summary>
		/// <param name="groupname">The name of the group.</param>
		/// <param name="parentgroup">The parent group, if any.</param>
		/// <param name="chatcolor">The chat color, in "RRR,GGG,BBB" format.</param>
		/// <param name="permissions">The list of permissions associated with this group, separated by commas.</param>
		public Group(string groupname, Group? parentgroup = null, string chatcolor = "255,255,255",
			List<string> permissions = null)
		{
			Name = groupname;
			ParentGroupName = parentgroup?.Name;
			ChatColor = chatcolor;
			Permissions = permissions;
		}

		/// <summary>
		/// Checks to see if a group has a specified permission.
		/// </summary>
		/// <param name="permission">The permission to check.</param>
		/// <returns>True if the group has that permission.</returns>
		public virtual async Task<bool> HasPermission(string permission)
		{
			bool negated = false;
			if (String.IsNullOrEmpty(permission) || (await RealHasPermission(permission)))
			{
				return true;
			}

			if (negated)
				return false;

			string[] nodes = permission.Split('.');
			for (int i = nodes.Length - 1; i >= 0; i--)
			{
				nodes[i] = "*";
				if (await RealHasPermission(String.Join(".", nodes, 0, i + 1)))
				{
					return !negated;
				}
			}

			return false;
		}

		private async Task<bool> RealHasPermission(string permission)
		{
			if (string.IsNullOrEmpty(permission))
				return true;

			var cur = this;
			var traversed = new List<Group>();
			while (cur != null)
			{
				if (cur.NegatedPermissions.Contains(permission))
				{
					return false;
				}

				if (cur.Permissions.Contains(permission))
					return true;
				if (traversed.Contains(cur))
				{
					throw new InvalidOperationException("Infinite group parenting ({0})".SFormat(cur.Name));
				}

				traversed.Add(cur);
				cur = await GroupManager.GetGroupByName(cur?.ParentGroupName);
			}

			return false;
		}

		/// <summary>
		/// Adds a permission to the list of negated permissions.
		/// </summary>
		/// <param name="permission">The permission to negate.</param>
		public void NegatePermission(string permission)
		{
			// Avoid duplicates
			if (!NegatedPermissions.Contains(permission))
			{
				NegatedPermissions.Add(permission);
				Permissions.Remove(permission); // Ensure we don't have conflicting definitions for a permissions
			}
		}

		/// <summary>
		/// Adds a permission to the list of permissions.
		/// </summary>
		/// <param name="permission">The permission to add.</param>
		public void AddPermission(string permission)
		{
			if (permission.StartsWith("!"))
			{
				NegatePermission(permission.Substring(1));
				return;
			}

			// Avoid duplicates
			if (!Permissions.Contains(permission))
			{
				Permissions.Add(permission);
				NegatedPermissions.Remove(permission); // Ensure we don't have conflicting definitions for a permissions
			}
		}

		/// <summary>
		/// Clears the permission list and sets it to the list provided,
		/// will parse "!permission" and add it to the negated permissions.
		/// </summary>
		/// <param name="permission">The new list of permissions to associate with the group.</param>
		public void SetPermission(List<string> permission)
		{
			Permissions.Clear();
			NegatedPermissions.Clear();
			permission.ForEach(p => AddPermission(p));
		}

		/// <summary>
		/// Will remove a permission from the respective list,
		/// where "!permission" will remove a negated permission.
		/// </summary>
		/// <param name="permission"></param>
		public void RemovePermission(string permission)
		{
			if (permission.StartsWith("!"))
			{
				NegatedPermissions.Remove(permission.Substring(1));
				return;
			}

			Permissions.Remove(permission);
		}

		/// <summary>
		/// Assigns all fields of this instance to another.
		/// </summary>
		/// <param name="otherGroup">The other instance.</param>
		public void AssignTo(Group otherGroup)
		{
			otherGroup.Name = Name;
			otherGroup.ParentGroupName = ParentGroupName;
			otherGroup.Prefix = Prefix;
			otherGroup.Suffix = Suffix;
			otherGroup.R = R;
			otherGroup.G = G;
			otherGroup.B = B;
			otherGroup.Permissions = Permissions;
		}

		public override string ToString()
		{
			return this.Name;
		}
	}

	/// <summary>
	/// This class is the SuperAdminGroup, which has access to everything.
	/// </summary>
	public class SuperAdminGroup : Group
	{
		/// <summary>
		/// The super admin class has every permission, represented by '*'.
		/// </summary>
		public override List<string> Permissions { get; set; } = new() { "*" };

		/// <summary>
		/// Initializes a new instance of the SuperAdminGroup class with the configured parameters.
		/// Those can be changed in the config file.
		/// </summary>
		public SuperAdminGroup()
			: base("superadmin")
		{
			R = (byte)TShock.Config.Settings.SuperAdminChatRGB[0];
			G = (byte)TShock.Config.Settings.SuperAdminChatRGB[1];
			B = (byte)TShock.Config.Settings.SuperAdminChatRGB[2];
			Prefix = TShock.Config.Settings.SuperAdminChatPrefix;
			Suffix = TShock.Config.Settings.SuperAdminChatSuffix;
		}

		/// <summary>
		/// Override to allow access to everything.
		/// </summary>
		/// <param name="permission">The permission</param>
		/// <returns>True</returns>
		public override async Task<bool> HasPermission(string permission)
		{
			return await Task.FromResult(true);
		}
	}
}
