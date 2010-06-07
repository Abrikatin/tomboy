using System;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Xml;

using Mono.Unix;
using Mono.Unix.Native;

using Hyena;

using NDesk.DBus;
using org.gnome.SessionManager;

namespace Tomboy
{
	public class GnomeApplication : INativeApplication
	{
		private Gnome.Program program;
		private static string confDir;
		private static string dataDir;
		private static string cacheDir;
		private static ObjectPath session_client_id;
		private const string tomboyDirName = "tomboy";

		static GnomeApplication ()
		{
			dataDir = Path.Combine (XdgBaseDirectorySpec.GetUserDirectory ("XDG_DATA_HOME",
			                                                               Path.Combine (".local", "share")),
			                        tomboyDirName);
			confDir = Path.Combine (XdgBaseDirectorySpec.GetUserDirectory ("XDG_CONFIG_HOME",
			                                                               ".config"),
			                        tomboyDirName);
			cacheDir = Path.Combine (XdgBaseDirectorySpec.GetUserDirectory ("XDG_CACHE_HOME",
			                                                                ".cache"),
			                         tomboyDirName);

			// NOTE: Other directories created on demand
			//       (non-existence is an indicator that migration is needed)
			if (!Directory.Exists (cacheDir))
				Directory.CreateDirectory (cacheDir);
		}

		public void Initialize (string locale_dir,
		                        string display_name,
		                        string process_name,
		                        string [] args)
		{
			try {
				SetProcessName (process_name);
			} catch {} // Ignore exception if fail (not needed to run)

			// Register handler for saving session when logging out of Gnome
			BusG.Init ();
			string startup_id = Environment.GetEnvironmentVariable ("DESKTOP_AUTOSTART_ID");
			if (String.IsNullOrEmpty (startup_id))
				startup_id = display_name;

			try {
				SessionManager session = Bus.Session.GetObject<SessionManager> (Constants.SessionManagerInterfaceName,
				                                                                new ObjectPath (Constants.SessionManagerPath));
				session_client_id = session.RegisterClient (display_name, startup_id);
				
				ClientPrivate client = Bus.Session.GetObject<ClientPrivate> (Constants.SessionManagerInterfaceName,
				                                                             session_client_id);
				client.QueryEndSession += OnQueryEndSession;
				client.EndSession += OnEndSession;
			} catch (Exception e) {
				Logger.Debug ("Failed to register with session manager: {0}", e.Message);
			}

			Gtk.Application.Init ();
			program = new Gnome.Program (display_name,
			                             Defines.VERSION,
			                             Gnome.Modules.UI,
			                             args);
		}

		public void RegisterSessionManagerRestart (string executable_path,
		                string[] args,
		                string[] environment)
		{
			// Nothing to do, we dropped the .desktop file in the autostart
			// folder which should be enough to handle this in Gnome
		}

		public void RegisterSignalHandlers ()
		{
			// Connect to SIGTERM and SIGINT, so we don't lose
			// unsaved notes on exit...
			Stdlib.signal (Signum.SIGTERM, OnExitSignal);
			Stdlib.signal (Signum.SIGINT, OnExitSignal);
		}

		public event EventHandler ExitingEvent;

		public void Exit (int exitcode)
		{
			OnExitSignal (-1);
			System.Environment.Exit (exitcode);
		}

		public void StartMainLoop ()
		{
			program.Run ();
		}

		[DllImport("libc")]
		private static extern int prctl (int option,
			                                 byte [] arg2,
			                                 IntPtr arg3,
			                                 IntPtr arg4,
			                                 IntPtr arg5);

		// From Banshee: Banshee.Base/Utilities.cs
		private void SetProcessName (string name)
		{
			if (prctl (15 /* PR_SET_NAME */,
			                Encoding.ASCII.GetBytes (name + "\0"),
			                IntPtr.Zero,
			                IntPtr.Zero,
			                IntPtr.Zero) != 0)
				throw new ApplicationException (
				        "Error setting process name: " +
				        Mono.Unix.Native.Stdlib.GetLastError ());
		}

		private void OnExitSignal (int signal)
		{
			if (ExitingEvent != null)
				ExitingEvent (null, new EventArgs ());

			if (signal >= 0)
				System.Environment.Exit (0);
		}

		private void OnQueryEndSession (uint flags)
		{
			Logger.Info ("Received end session query");

			// The session might not actually end but it would be nice to start
			// some cleanup actions like saving notes here

			// Let the session manager know its OK to continue
			try {
				ClientPrivate client = Bus.Session.GetObject<ClientPrivate> (Constants.SessionManagerInterfaceName,
				                                                             session_client_id);
				client.EndSessionResponse(true, String.Empty);
			} catch (Exception e) {
				Logger.Debug("Failed to respond to session manager: {0}", e.Message);
			}
		}

		private void OnEndSession (uint flags)
		{
			Logger.Info ("Received end session signal");

			if (ExitingEvent != null)
				ExitingEvent (null, new EventArgs ());

			// Let the session manager know its OK to continue
			// Ideally we would wait for all the exit events to finish
			try {
				ClientPrivate client = Bus.Session.GetObject<ClientPrivate> (Constants.SessionManagerInterfaceName,
				                                                             session_client_id);
				client.EndSessionResponse (true, String.Empty);
			} catch (Exception e) {
				Logger.Debug ("Failed to respond to session manager: {0}", e.Message);
			}
		}
		
		public void OpenUrl (string url, Gdk.Screen screen)
		{
			GtkBeans.Global.ShowUri (screen, url);
		}
		
		public void DisplayHelp (string help_uri, Gdk.Screen screen)
		{
			GtkBeans.Global.ShowUri (screen, help_uri);
		}
		
		public string DataDirectory {
			get { return dataDir; }
		}

		public string ConfigurationDirectory {
			get { return confDir; }
		}

		public string CacheDirectory {
			get { return cacheDir; }
		}

		public string LogDirectory {
			get { return confDir; }
		}

		public string PreOneDotZeroNoteDirectory {
			get {
				return Path.Combine (Environment.GetEnvironmentVariable ("HOME"),
				                     ".tomboy");
			}
		}
	}
}
