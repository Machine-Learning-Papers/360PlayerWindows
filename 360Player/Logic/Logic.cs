﻿using BivrostAnalytics;
using Bivrost.AnalyticsForVR;
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Logger = Bivrost.Log.Logger;

namespace Bivrost.Bivrost360Player
{
	public enum HeadsetMode
	{
		OSVR,

        [Description("Oculus Rift (DK2, CV1)")]
        Oculus,

		[Description("SteamVR (OpenVR, HTC Vive)")]
		OpenVR,

		Disable
	}

	public enum ScreenSelection:int
	{
		Autodetect = 999,
		One = 0,
		Two=1,
		Three=2,
		Four=3,
		Five=4
	}

	public class Logic
	{

		public const string productCode = "360player-windows";


		private Logger logger = new Logger("logic");


		// Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\BivrostPlayer";
		public static string LocalDataDirectory { get; private set; } = "";

		public static Logic Instance { get; protected set; }
		public static void Prepare(string localDataDirectory) {
			if (Instance != null)
				throw new Exception("cannot prepare Logic more than once");
			Instance = new Logic(localDataDirectory);
		}

		public Settings settings;
		public Tracker stats;

		public event Action OnUpdateAvailable = delegate { };

		protected Logic(string localDataDirectory)
		{
			//File.WriteAllText("D:\\abcd.txt", "qweqwe123");
			//File.WriteAllText(Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\" + "aaaaa.wtf", "asbvlajhbvaldbaldsbalsd");
			Logic.LocalDataDirectory = localDataDirectory + "BIVROST\\360Player\\";
			if(!Directory.Exists(Logic.LocalDataDirectory))
			{
				try
				{
					Directory.CreateDirectory(Logic.LocalDataDirectory);
				} catch(Exception exc)
				{
					MessageBox.Show("exc: " + exc.Message + "\npath: " + Logic.LocalDataDirectory);
				}
			}
			


			Instance = this;

			// bind buttons in settings window
			settings = new Settings();
			settings.ResetInstallId = SettingsResetInstallId;
			settings.ResetConfiguration = SettingsResetConfiguration;


			// prepare google analytics handler
			var OsPlatform = Environment.OSVersion.Platform.ToString();
			var OsVersion = Environment.OSVersion.Version.ToString();
			var OsVersionString = Environment.OSVersion.VersionString;
			var x64 = Environment.Is64BitOperatingSystem ? "x64" : "x86";
			var cpu = Environment.ProcessorCount;

			stats = new Tracker()
			{
				TrackingId = "UA-68212464-1",
				DeviceId = settings.InstallId.ToString(),
				UserAgentString = $"BivrostAnalytics/1.0 ({OsPlatform}; {OsVersion}; {OsVersionString}; {x64}; CPU-Cores:{cpu}) {Assembly.GetEntryAssembly().GetName().Name}/{Assembly.GetEntryAssembly().GetName().Version}"
			};

			//==============

			ProtocolHandler.RegisterProtocol();

            lookListener = new LookListener();

			locallyStoredSessions = new LocallyStoredSessionSink();
			lookListener.RegisterSessionSink(locallyStoredSessions);

#if FEATURE_GHOSTVR
			ghostVRConnector = new GhostVRConnector();
			lookListener.RegisterSessionSink(new GhostVRSessionSink(ghostVRConnector));
#endif
		}

        public LookListener lookListener;
#if FEATURE_GHOSTVR
		public GhostVRConnector ghostVRConnector;
#endif
		public LocallyStoredSessionSink locallyStoredSessions;

		~Logic()
		{         
            lookListener?.Dispose();
		}

		

		public void ReloadPlayer()
		{
			System.Diagnostics.Process.Start(System.Reflection.Assembly.GetEntryAssembly().Location);
			Application.Current.Shutdown();
		}

		public void CheckForUpdate()
		{
			Task.Factory.StartNew(() =>
			{
				if(Updater.CheckForUpdate())
				{
					OnUpdateAvailable();
				}                
            });
		}


#region actions in the settings window


		private void SettingsResetInstallId()
		{
			var result = System.Windows.Forms.MessageBox.Show("Do you really want to reset installation ID?", "Installation ID", System.Windows.Forms.MessageBoxButtons.YesNo);
			if (result == System.Windows.Forms.DialogResult.Yes)
			{
				settings.InstallId = Guid.NewGuid();
				settings.Save();
				System.Windows.Forms.Application.Restart();
				System.Windows.Application.Current.Shutdown();
			}
		}


		private void SettingsResetConfiguration()
		{
			var result = System.Windows.Forms.MessageBox.Show("Reset configuration to default?", "Configuration", System.Windows.Forms.MessageBoxButtons.YesNo);
			if (result == System.Windows.Forms.DialogResult.Yes)
			{
				try
				{
					System.IO.File.Delete(settings.SettingsFile);
					System.Windows.Forms.Application.Restart();
					System.Windows.Application.Current.Shutdown();
				}
				catch (Exception exc) { logger.Error(exc, "Error resetting configuration"); }
			}
		}
#endregion


        internal void ValidateSettings()
        {
			foreach (SettingVerificationAttribute attr in typeof(Settings).GetCustomAttributes(false))
			{
				attr.Normalize();
			}
			foreach (SettingVerificationAttribute attr in typeof(Settings).GetCustomAttributes(false))
			{
				if (!attr.Valid) logger.Error($"Settings not valid? {attr.GetType().Name}");
			}
		}





#region notifications

		private static Logger notificationLogger = new Logger("notification");

		/// <summary>
		/// Display a notification visible as a popup
		/// Does nothing if ShellViewMovel.NotificationCenter is not yet set up.
		/// </summary>
		/// <param name="msg">the message</param>
		/// <param name="memberName">(automatically added) source code trace information</param>
		/// <param name="sourceFilePath">(automatically added) source code trace information</param>
		/// <param name="sourceLineNumber">(automatically added) source code trace information</param>
		public static void Notify(
			string msg,
			[System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
			[System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
			[System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0
		)
		{
			notificationLogger.Info($"{msg}", memberName, sourceFilePath, sourceLineNumber);
			Caliburn.Micro.Execute.OnUIThreadAsync(() => ShellViewModel.Instance?.NotificationCenter?.PushNotification(new NotificationViewModel(msg)));
		}
		/// <summary>
		/// Display a notification visible as a popup with a link
		/// Does nothing if ShellViewMovel.NotificationCenter is not yet set up.
		/// </summary>
		/// <param name="msg">the message</param>
		/// <param name="uri">url of a link</param>
		/// <param name="linkLabel">title of a link</param>
		/// <param name="memberName">(automatically added) source code trace information</param>
		/// <param name="sourceFilePath">(automatically added) source code trace information</param>
		/// <param name="sourceLineNumber">(automatically added) source code trace information</param>
		public static void NotifyWithLink(
			string msg,
			string uri,
			string linkLabel,
			[System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
			[System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
			[System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0
		)
		{
			notificationLogger.Info($"{msg}, {linkLabel}->{uri}", memberName, sourceFilePath, sourceLineNumber);
			var notification = new NotificationViewModel(msg, () => System.Diagnostics.Process.Start(uri), linkLabel);
			Caliburn.Micro.Execute.OnUIThreadAsync(
				() => ShellViewModel.Instance?.NotificationCenter?.PushNotification(notification)
			);
		}
#endregion


	}
}
