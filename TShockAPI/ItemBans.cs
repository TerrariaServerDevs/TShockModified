/*
TShock, a server mod for Terraria
Copyright (C) 2011-2018 Pryaxis & TShock Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;
using TShockAPI.Database;
using TShockAPI.Net;
using Terraria;
using Microsoft.Xna.Framework;
using TShockAPI.Localization;
using static TShockAPI.GetDataHandlers;
using TerrariaApi.Server;
using Terraria.ObjectData;
using Terraria.DataStructures;
using Terraria.Localization;
using System.Data;
using System.Threading.Tasks;

namespace TShockAPI
{
	/// <summary>The TShock item ban subsystem. It handles keeping things out of people's inventories.</summary>
	public sealed class ItemBans
	{
		/// <summary>The last time the second update process was run. Used to throttle task execution.</summary>
		private DateTime LastTimelyRun = DateTime.UtcNow;

		/// <summary>A reference to the TShock plugin so we can register events.</summary>
		private TShock Plugin;

		/// <summary>Creates an ItemBan system given a plugin to register events to and a database.</summary>
		/// <param name="plugin">The executing plugin.</param>
		/// <param name="database">The database the item ban information is stored in.</param>
		/// <returns>A new item ban system.</returns>
		internal ItemBans(TShock plugin)
		{
			Plugin = plugin;

			ServerApi.Hooks.GameUpdate.Register(plugin, OnGameUpdate);
			GetDataHandlers.PlayerUpdate += OnPlayerUpdate;
			GetDataHandlers.ChestItemChange += OnChestItemChange;
			GetDataHandlers.TileEdit += OnTileEdit;
		}

		/// <summary>Called on the game update loop (the XNA tickrate).</summary>
		/// <param name="args">The standard event arguments.</param>
		internal async Task OnGameUpdate(EventArgs args)
		{
			if ((DateTime.UtcNow - LastTimelyRun).TotalSeconds >= 1)
			{
				await OnSecondlyUpdate(args);
			}
		}

		/// <summary>Called by OnGameUpdate once per second to execute tasks regularly but not too often.</summary>
		/// <param name="args">The standard event arguments.</param>
		internal async Task OnSecondlyUpdate(EventArgs args)
		{
			DisableFlags disableFlags = TShock.Config.Settings.DisableSecondUpdateLogs ? DisableFlags.WriteToConsole : DisableFlags.WriteToLogAndConsole;

			foreach (ServerPlayer player in TShock.Players)
			{
				if (player == null || !player.Active)
				{
					continue;
				}

				// Untaint now, re-taint if they fail the check.
				UnTaint(player);

				// No matter the player type, we do a check when a player is holding an item that's banned.
				if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(player.TPlayer.inventory[player.TPlayer.selectedItem].netID), player))
				{
					string itemName = player.TPlayer.inventory[player.TPlayer.selectedItem].Name;
					player.Disable(GetString($"holding banned item: {itemName}"), disableFlags);
					SendCorrectiveMessage(player, itemName);
				}

				// If SSC isn't enabled OR if SSC is enabled and the player is logged in
				// In a case like this, we do the full check too.
				if (!Main.ServerSideCharacter || (Main.ServerSideCharacter && player.IsLoggedIn))
				{
					// The Terraria inventory is composed of a multicultural set of arrays
					// with various different contents and beliefs

					// Armor ban checks
					foreach (Item item in player.TPlayer.armor)
					{
						if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}

					// Dye ban checks
					foreach (Item item in player.TPlayer.dye)
					{
						if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}

					// Misc equip ban checks
					foreach (Item item in player.TPlayer.miscEquips)
					{
						if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}

					// Misc dye ban checks
					foreach (Item item in player.TPlayer.miscDyes)
					{
						if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), player))
						{
							Taint(player);
							SendCorrectiveMessage(player, item.Name);
						}
					}
				}
			}

			// Set the update time to now, so that we know when to execute next.
			// We do this at the end so that the task can't re-execute faster than we expected.
			// (If we did this at the start of the method, the method execution would count towards the timer.)
			LastTimelyRun = DateTime.UtcNow;
		}

		internal async Task OnPlayerUpdate(PlayerUpdateEventArgs args)
		{
			DisableFlags disableFlags = TShock.Config.Settings.DisableSecondUpdateLogs ? DisableFlags.WriteToConsole : DisableFlags.WriteToLogAndConsole;
			bool useItem = args.Control.IsUsingItem;
			ServerPlayer player = args.Player;
			string itemName = player.TPlayer.inventory[args.SelectedItem].Name;

			if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(player.TPlayer.inventory[args.SelectedItem].netID), args.Player))
			{
				player.TPlayer.controlUseItem = false;
				player.Disable(GetString($"holding banned item: {itemName}"), disableFlags);

				SendCorrectiveMessage(player, itemName);

				player.TPlayer.Update(player.TPlayer.whoAmI);
				NetMessage.SendData((int)PacketTypes.PlayerUpdate, -1, player.Index, NetworkText.Empty, player.Index);

				args.Handled = true;
				return;
			}

			args.Handled = false;
			return;
		}

		internal async Task OnChestItemChange(ChestItemEventArgs args)
		{
			Item item = new Item();
			item.netDefaults(args.Type);


			if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(item.type), args.Player))
			{
				SendCorrectiveMessage(args.Player, item.Name);
				args.Handled = true;
				return;
			}

			args.Handled = false;
			return;
		}

		internal async Task OnTileEdit(TileEditEventArgs args)
		{
			if (args.Action == EditAction.PlaceTile || args.Action == EditAction.PlaceWall)
			{
				if (args.Player.TPlayer.autoActuator && await ItemBanManager.ItemIsBanned("Actuator", args.Player))
				{
					args.Player.SendTileSquareCentered(args.X, args.Y, 1);
					args.Player.SendErrorMessage(GetString("You do not have permission to place actuators."));
					args.Handled = true;
					return;
				}

				if (await ItemBanManager.ItemIsBanned(EnglishLanguage.GetItemNameById(args.Player.SelectedItem.netID), args.Player))
				{
					args.Player.SendTileSquareCentered(args.X, args.Y, 4);
					args.Handled = true;
					return;
				}
			}
		}

		private void UnTaint(ServerPlayer player)
		{
			player.IsDisabledForBannedWearable = false;
		}

		private void Taint(ServerPlayer player)
		{
			// Arbitrarily does things to the player
			player.SetBuff(BuffID.Frozen, 330, true);
			player.SetBuff(BuffID.Stoned, 330, true);
			player.SetBuff(BuffID.Webbed, 330, true);

			// Marks them as a target for future disables
			player.IsDisabledForBannedWearable = true;
		}

		private void SendCorrectiveMessage(ServerPlayer player, string itemName)
		{
			player.SendErrorMessage(GetString("{0} is banned! Remove it!", itemName));
		}
	}
}
