﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using Terraria.IO;
using TerrariaApi.Server;

namespace EthnessaAPI
{
	/// <summary>
	/// Intended to be a thread safe way to save the world
	/// </summary>
	class SaveManager : IDisposable
	{
		// Singleton
		private static readonly SaveManager instance = new SaveManager();
		private SaveManager()
		{
			_saveThread = new Thread(SaveWorker);
			_saveThread.Name = "Ethnessa SaveManager Worker";
			_saveThread.Start();
		}
		public static SaveManager Instance { get { return instance; } }

		// Producer Consumer
		private EventWaitHandle _wh = new AutoResetEvent(false);
		private Object _saveLock = new Object();
		private Queue<SaveTask> _saveQueue = new Queue<SaveTask>();
		private Thread _saveThread;
		private int saveQueueCount { get { lock (_saveLock) return _saveQueue.Count; } }

		/// <summary>
		/// SaveWorld event handler which notifies users that the server may lag
		/// </summary>
		public void OnSaveWorld(WorldSaveEventArgs args)
		{
			if (ServerBase.Config.Settings.AnnounceSave)
			{
				// Protect against internal errors causing save failures
				// These can be caused by an unexpected error such as a bad or out of date plugin
				try
				{
					ServerBase.Utils.Broadcast(GetString("Saving world..."), Color.Yellow);
				}
				catch (Exception ex)
				{
					ServerBase.Log.Error("World saved notification failed");
					ServerBase.Log.Error(ex.ToString());
				}
			}


		}

		/// <summary>
		/// Saves the map data
		/// </summary>
		/// <param name="wait">wait for all pending saves to finish (default: true)</param>
		/// <param name="resetTime">reset the last save time counter (default: false)</param>
		/// <param name="direct">use the realsaveWorld method instead of saveWorld event (default: false)</param>
		public void SaveWorld(bool wait = true, bool resetTime = false, bool direct = false)
		{
			EnqueueTask(new SaveTask(resetTime, direct));
			if (!wait)
				return;

			// Wait for all outstanding saves to complete
			int count = saveQueueCount;
			while (0 != count)
			{
				Thread.Sleep(50);
				count = saveQueueCount;
			}
		}

		/// <summary>
		/// Processes any outstanding saves, shutsdown the save thread and returns
		/// </summary>
		public void Dispose()
		{
			EnqueueTask(null);
			_saveThread.Join();
			_wh.Close();
		}

		private void EnqueueTask(SaveTask task)
		{
			lock (_saveLock)
			{
				_saveQueue.Enqueue(task);
			}
			_wh.Set();
		}

		private void SaveWorker()
		{
			while (true)
			{
				lock (_saveLock)
				{
					// NOTE: lock for the entire process so wait works in SaveWorld
					if (_saveQueue.Count > 0)
					{
						SaveTask task = _saveQueue.Dequeue();
						if (null == task)
							return;
						else
						{
							// Ensure that save handler errors don't bubble up and cause a recursive call
							// These can be caused by an unexpected error such as a bad or out of date plugin
							try
							{
								if (task.Direct)
								{
									OnSaveWorld(new WorldSaveEventArgs());
									WorldFile.SaveWorld(task.ResetTime);
								}
								else
									WorldFile.SaveWorld(task.ResetTime);

								if (ServerBase.Config.Settings.AnnounceSave)
									ServerBase.Utils.Broadcast(GetString("World saved."), Color.Yellow);

								ServerBase.Log.Info(GetString("World saved at ({0})", Main.worldPathName));
							}
							catch (Exception e)
							{
								ServerBase.Log.Error("World saved failed");
								ServerBase.Log.Error(e.ToString());
							}
						}
					}
				}
				_wh.WaitOne();
			}
		}

		class SaveTask
		{
			public bool ResetTime { get; set; }
			public bool Direct { get; set; }
			public SaveTask(bool resetTime, bool direct)
			{
				this.ResetTime = resetTime;
				this.Direct = direct;
			}

			public override string ToString()
			{
				return GetString("resetTime {0}, direct {1}", ResetTime, Direct);
			}
		}
	}
}
