using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Xml;

using Mono.Unix;
using Mono.Unix.Native;

namespace Tomboy.Platform
{
	public class GnomeApplication : INativeApplication
	{
		private Gnome.Program program;

		public void Initialize (string locale_dir, 
					       string display_name, 
					       string process_name,  
					       string [] args)
		{
			try {
				SetProcessName (process_name);
			} catch {} // Ignore exception if fail (not needed to run)

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
			if (executable_path == null)
				return;

			// Restart if we are running when the session ends or at crash...
			Gnome.Client client = Gnome.Global.MasterClient ();
			client.RestartStyle = 
				Gnome.RestartStyle.IfRunning | Gnome.RestartStyle.Immediately;
			client.Die += OnSessionManagerDie;

			foreach (string env in environment) {
				string [] split = env.Split (new char [] { '=' }, 2);
				if (split.Length == 2) {
					client.SetEnvironment (split[0], split[1]);
				}
			}

			// Get the args for session restart... 	 
			string [] restart_args = new string [args.Length + 1]; 	 
			restart_args [0] = executable_path;
			args.CopyTo (restart_args, 1);
			client.SetRestartCommand (restart_args.Length, restart_args);
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

		public void QuitMainLoop ()
		{
			Gtk.Main.Quit ();
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

		private void OnSessionManagerDie (object sender, EventArgs args)
		{
			// Don't let the exit signal run, which would cancel
			// session management.
			QuitMainLoop ();
		}

		private void CancelSessionManagerRestart ()
		{
			Gnome.Client client = Gnome.Global.MasterClient ();
			client.RestartStyle = Gnome.RestartStyle.IfRunning;
			client.Flush ();
		}

		private void OnExitSignal (int signal)
		{
			// Don't auto-restart after exit/kill.
			CancelSessionManagerRestart ();

			if (ExitingEvent != null)
				ExitingEvent (null, new EventArgs ());

			if (signal >= 0)
				System.Environment.Exit (0);
		}
	}
}
