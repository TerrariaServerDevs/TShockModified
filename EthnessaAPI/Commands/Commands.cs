﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EthnessaAPI.ServerCommands;
using Terraria;
using EthnessaAPI.Database;
using EthnessaAPI.Models;

namespace EthnessaAPI
{
	public delegate void CommandDelegate(CommandArgs args);

	// We want to make this disaster a bit more readable and easy to maintain in the future
	public static class Commands
	{
		public static List<Command> ServerCommands = new List<Command>();

		/// <summary>
		/// The command specifier, defaults to "/"
		/// </summary>
		public static string Specifier => string.IsNullOrWhiteSpace(ServerBase.Config.Settings.CommandSpecifier)
			? "/"
			: ServerBase.Config.Settings.CommandSpecifier;

		/// <summary>
		/// The silent command specifier, defaults to "."
		/// </summary>
		public static string SilentSpecifier =>
			string.IsNullOrWhiteSpace(ServerBase.Config.Settings.CommandSilentSpecifier)
				? "."
				: ServerBase.Config.Settings.CommandSilentSpecifier;

		private delegate void AddChatCommand(string permission, CommandDelegate command, params string[] names);

		/// <summary>
		/// Initializes EthnessaAPI's commands.
		/// </summary>
		public static void InitializeCommands()
		{
			ServerCommands = new List<Command>()
			{
				new UserCommand(),
				new StopCommand(),
				new GroupCommand(),
				new HelpCommand(),
				new UserInfoCommand(),
				new LoginCommand(),
				new LogoutCommand(),
				new RegisterCommand(),
				new ChangePasswordCommand(),
				new AccountInfoCommand(),
				new ConfigCommand(),
				new UuidCommand(),
				new SetSpawnCommand(),
				new SpawnCommand(),
				new KickCommand(),
				new BanCommand(),
				new ListBansCommand(),
				new UnbanCommand(),
				new MuteCommand(),
				new UnmuteCommand(),
				new ListMutesCommand(),
				new TimeCommand(),
				new SpawnMobCommand(),
				new ButcherCommand(),
				new TagCommand(),
				new PrefixCommand(),
				new BroadcastCommand(),
				new NicknameCommand(),
				new KillCommand()
			};
		}

		/// <summary>
		/// Executes a command as a player.
		/// </summary>
		/// <param name="player"></param>
		/// <param name="text"></param>
		/// <returns>Was the player able to run the command?</returns>
		public static bool HandleCommand(ServerPlayer player, string text)
		{
			string cmdText = text.Remove(0, 1);
			string cmdPrefix = text[0].ToString();
			bool silent = cmdPrefix == SilentSpecifier;

			int index = -1;
			for (int i = 0; i < cmdText.Length; i++)
			{
				if (IsWhiteSpace(cmdText[i]))
				{
					index = i;
					break;
				}
			}

			string cmdName;
			if (index == 0) // Space after the command specifier should not be supported
			{
				player.SendErrorMessage(GetString(
					"You entered a space after {0} instead of a command. Type {0}help for a list of valid commands.",
					Specifier));
				return true;
			}
			else if (index < 0)
				cmdName = cmdText.ToLower();
			else
				cmdName = cmdText.Substring(0, index).ToLower();

			List<string> args;
			if (index < 0)
				args = new List<string>();
			else
				args = ParseParameters(cmdText.Substring(index));

			IEnumerable<Command> cmds = ServerCommands.FindAll(c => c.HasAlias(cmdName));

			if (Hooks.PlayerHooks.OnPlayerCommand(player, cmdName, cmdText, args, ref cmds, cmdPrefix))
				return true;

			if (!cmds.Any())
			{
				if (player.AwaitingResponse.ContainsKey(cmdName))
				{
					Action<CommandArgs> call = player.AwaitingResponse[cmdName];
					player.AwaitingResponse.Remove(cmdName);
					call(new CommandArgs(cmdText, player, args));
					return true;
				}

				player.SendErrorMessage(GetString("Invalid command entered. Type {0}help for a list of valid commands.",
					Specifier));
				return true;
			}

			foreach (Command cmd in cmds)
			{
				if (!cmd.CanRun(player))
				{
					if (cmd.DoLog)
						ServerBase.Utils.SendLogs(
							GetString("{0} tried to execute {1}{2}.", player.Name, Specifier, cmdText),
							Color.PaleVioletRed, player);
					else
						ServerBase.Utils.SendLogs(
							GetString("{0} tried to execute (args omitted) {1}{2}.", player.Name, Specifier, cmdName),
							Color.PaleVioletRed, player);
					player.SendErrorMessage(GetString("You do not have access to this command."));
					if (player.HasPermission(Permissions.su))
					{
						player.SendInfoMessage(GetString("You can use '{0}sudo {0}{1}' to override this check.",
							Specifier, cmdText));
					}
				}
				else if (!cmd.AllowServer && !player.RealPlayer)
				{
					player.SendErrorMessage(GetString("You must use this command in-game."));
				}
				else
				{
					if (cmd.DoLog)
						ServerBase.Utils.SendLogs(
							GetString("{0} executed: {1}{2}.", player.Name, silent ? SilentSpecifier : Specifier,
								cmdText), Color.PaleVioletRed, player);
					else
						ServerBase.Utils.SendLogs(
							GetString("{0} executed (args omitted): {1}{2}.", player.Name,
								silent ? SilentSpecifier : Specifier, cmdName), Color.PaleVioletRed, player);
					cmd.Run(cmdText, silent, player, args);
				}
			}

			return true;
		}

		/// <summary>
		/// Parses a string of parameters into a list. Handles quotes.
		/// </summary>
		/// <param name="str"></param>
		/// <returns></returns>
		private static List<string> ParseParameters(string str)
		{
			var ret = new List<string>();
			var sb = new StringBuilder();
			bool instr = false;
			for (int i = 0; i < str.Length; i++)
			{
				char c = str[i];

				if (c == '\\' && ++i < str.Length)
				{
					if (str[i] != '"' && str[i] != ' ' && str[i] != '\\')
						sb.Append('\\');
					sb.Append(str[i]);
				}
				else if (c == '"')
				{
					instr = !instr;
					if (!instr)
					{
						ret.Add(sb.ToString());
						sb.Clear();
					}
					else if (sb.Length > 0)
					{
						ret.Add(sb.ToString());
						sb.Clear();
					}
				}
				else if (IsWhiteSpace(c) && !instr)
				{
					if (sb.Length > 0)
					{
						ret.Add(sb.ToString());
						sb.Clear();
					}
				}
				else
					sb.Append(c);
			}

			if (sb.Length > 0)
				ret.Add(sb.ToString());

			return ret;
		}

		private static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\n';
	}
}
